// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.AspNetCore.Razor.PooledObjects;

namespace Microsoft.AspNetCore.Razor.Language;

/// <summary>
/// An engine phase that runs after lowering and transforms MarkupElementIntermediateNode
/// nodes that match tag helpers into TagHelperIntermediateNode subtrees.
/// </summary>
internal class TagHelperIntermediateNodeRewritePhase : RazorEnginePhaseBase
{
    protected override RazorCodeDocument ExecuteCore(RazorCodeDocument codeDocument, CancellationToken cancellationToken)
    {
        var tagHelperContext = codeDocument.GetTagHelperContext();
        if (tagHelperContext is null || tagHelperContext.TagHelpers is [])
        {
            return codeDocument;
        }

        var documentNode = codeDocument.GetDocumentNode();
        if (documentNode is null)
        {
            return codeDocument;
        }

        var binder = tagHelperContext.GetBinder();
        var prefix = tagHelperContext.Prefix;

        var rewriter = new TagHelperRewriter(binder, prefix, codeDocument.FileKind.IsComponent());
        rewriter.Visit(documentNode);

        return codeDocument;
    }

    private class TagHelperRewriter : IntermediateNodeWalker
    {
        private readonly TagHelperBinder _binder;
        private readonly string _prefix;
        private readonly bool _isComponent;

        public TagHelperRewriter(TagHelperBinder binder, string prefix, bool isComponent)
        {
            _binder = binder;
            _prefix = prefix;
            _isComponent = isComponent;
        }

        public override void VisitMarkupElement(MarkupElementIntermediateNode node)
        {
            // First, visit children so we process nested elements bottom-up
            base.VisitDefault(node);

            var tagName = node.TagName;
            if (string.IsNullOrEmpty(tagName))
            {
                return;
            }

            // Collect attributes as key-value pairs for the binder
            using var attributePairs = new PooledArrayBuilder<KeyValuePair<string, string>>();
            foreach (var child in node.Children)
            {
                if (child is HtmlAttributeIntermediateNode htmlAttr)
                {
                    attributePairs.Add(new KeyValuePair<string, string>(htmlAttr.AttributeName, string.Empty));
                }
            }

            // Check the parent to see if it's a tag helper (for parent tag name matching)
            string parentTagName = null;
            var parentIsTagHelper = false;
            foreach (var ancestor in Ancestors)
            {
                if (ancestor is MarkupElementIntermediateNode parentElement)
                {
                    parentTagName = parentElement.TagName;
                    break;
                }
                else if (ancestor is TagHelperIntermediateNode parentTagHelper)
                {
                    parentTagName = parentTagHelper.TagName;
                    parentIsTagHelper = true;
                    break;
                }
            }

            var binding = _binder.GetBinding(tagName, attributePairs.ToImmutable(), parentTagName, parentIsTagHelper);
            if (binding is null)
            {
                return;
            }

            // This is a tag helper match! Transform the node.
            var tagHelperNode = CreateTagHelperNode(node, binding);

            // Replace the MarkupElementIntermediateNode with the TagHelperIntermediateNode
            if (Parent is not null)
            {
                var reference = new IntermediateNodeReference(node, Parent);
                reference.Replace(tagHelperNode);
            }
        }

        private TagHelperIntermediateNode CreateTagHelperNode(MarkupElementIntermediateNode element, TagHelperBinding binding)
        {
            var tagName = binding.TagName;

            // Strip prefix for legacy (non-component) files
            if (!_isComponent && _prefix != null && tagName.StartsWith(_prefix, System.StringComparison.Ordinal))
            {
                tagName = tagName.Substring(_prefix.Length);
            }

            // Use TagMode from the MarkupElementIntermediateNode (set during lowering)
            var tagMode = element.TagMode;

            var tagHelperNode = new TagHelperIntermediateNode()
            {
                TagName = tagName,
                TagMode = tagMode,
                Source = element.Source,
                TagHelpers = binding.TagHelpers,
                StartTagSpan = _isComponent ? element.Source : null,
            };

            // Add body node first (matches the order in the existing lowering)
            var bodyNode = new TagHelperBodyIntermediateNode();
            foreach (var child in element.Body)
            {
                bodyNode.Children.Add(child);
            }
            tagHelperNode.Children.Add(bodyNode);

            // Process attributes
            var tagHelpers = binding.TagHelpers;
            var renderedBoundAttributeNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var child in element.Children)
            {
                if (child is HtmlAttributeIntermediateNode htmlAttr)
                {
                    ProcessAttribute(tagHelperNode, htmlAttr, tagHelpers, renderedBoundAttributeNames);
                }
            }

            return tagHelperNode;
        }

        private static void ProcessAttribute(
            TagHelperIntermediateNode tagHelperNode,
            HtmlAttributeIntermediateNode htmlAttr,
            TagHelperCollection tagHelpers,
            HashSet<string> renderedBoundAttributeNames)
        {
            var attributeName = htmlAttr.AttributeName;

            using var matches = new PooledArrayBuilder<TagHelperAttributeMatch>();
            TagHelperMatchingConventions.GetAttributeMatches(tagHelpers, attributeName, ref matches.AsRef());

            if (matches.Count > 0 && renderedBoundAttributeNames.Add(attributeName))
            {
                foreach (var match in matches)
                {
                    var attributeStructure = DetermineAttributeStructure(htmlAttr);

                    var setTagHelperProperty = new TagHelperPropertyIntermediateNode(match)
                    {
                        AttributeName = attributeName,
                        AttributeStructure = attributeStructure,
                        Source = DeterminePropertySource(htmlAttr),
                    };

                    // Copy attribute value children
                    foreach (var valueChild in htmlAttr.Children)
                    {
                        setTagHelperProperty.Children.Add(valueChild);
                    }

                    tagHelperNode.Children.Add(setTagHelperProperty);
                }
            }
            else
            {
                var attributeStructure = DetermineAttributeStructure(htmlAttr);

                var addHtmlAttribute = new TagHelperHtmlAttributeIntermediateNode()
                {
                    AttributeName = attributeName,
                    AttributeStructure = attributeStructure,
                };

                // Copy attribute value children
                foreach (var valueChild in htmlAttr.Children)
                {
                    addHtmlAttribute.Children.Add(valueChild);
                }

                tagHelperNode.Children.Add(addHtmlAttribute);
            }
        }

        private static AttributeStructure DetermineAttributeStructure(HtmlAttributeIntermediateNode htmlAttr)
        {
            var prefix = htmlAttr.Prefix ?? string.Empty;

            if (prefix.EndsWith("\"", System.StringComparison.Ordinal))
            {
                return AttributeStructure.DoubleQuotes;
            }
            else if (prefix.EndsWith("'", System.StringComparison.Ordinal))
            {
                return AttributeStructure.SingleQuotes;
            }
            else if (prefix.EndsWith("=", System.StringComparison.Ordinal))
            {
                return AttributeStructure.NoQuotes;
            }
            else
            {
                return AttributeStructure.Minimized;
            }
        }

        private static SourceSpan? DeterminePropertySource(HtmlAttributeIntermediateNode htmlAttr)
        {
            // For the property source, we want the source of the attribute value, not the whole attribute.
            // Look for the first child with a source span.
            foreach (var child in htmlAttr.Children)
            {
                if (child.Source is not null)
                {
                    return child.Source;
                }
            }

            return null;
        }
    }
}
