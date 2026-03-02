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
                if (!_isComponent)
                {
                    FlattenMarkupElement(node, Parent);
                }

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
                // For legacy files, flatten non-matched elements back to HtmlContent
                if (!_isComponent)
                {
                    FlattenMarkupElement(node, Parent);
                }

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

        /// <summary>
        /// For legacy files, non-tag-helper elements need to be flattened back to HtmlContent
        /// since the legacy pipeline doesn't produce MarkupElementIntermediateNode for regular elements.
        /// Uses flat tag tokens captured during lowering (same representation as legacy pipeline).
        /// </summary>
        private static void FlattenMarkupElement(MarkupElementIntermediateNode node, IntermediateNode parent)
        {
            if (parent is null)
            {
                return;
            }

            var parentChildren = parent.Children;
            var index = parentChildren.IndexOf(node);
            if (index < 0)
            {
                return;
            }

            // Remove the MarkupElement node
            parentChildren.RemoveAt(index);

            // Build the replacement: flat start tag tokens, body, flat end tag tokens
            using var replacements = new PooledArrayBuilder<IntermediateNode>();

            // Flat start tag tokens (captured from legacy pipeline during lowering)
            foreach (var child in node.FlatStartTag)
            {
                replacements.Add(child);
            }

            // Body children (preserve as-is)
            foreach (var child in node.Body)
            {
                replacements.Add(child);
            }

            // Flat end tag tokens (captured from legacy pipeline during lowering)
            foreach (var child in node.FlatEndTag)
            {
                replacements.Add(child);
            }

            // Insert replacement nodes
            var insertCount = replacements.Count;
            for (var i = 0; i < insertCount; i++)
            {
                parentChildren.Insert(index + i, replacements[i]);
            }

            // Merge adjacent HtmlContent nodes around the insertion point
            MergeAdjacentHtmlContent(parentChildren, index, insertCount);
        }

        /// <summary>
        /// Merges adjacent HtmlContentIntermediateNode nodes in the range [start-1, start+count].
        /// </summary>
        private static void MergeAdjacentHtmlContent(IntermediateNodeCollection children, int start, int count)
        {
            // Merge from the end of the inserted range forward
            var endIdx = start + count;
            if (endIdx < children.Count && endIdx > 0 &&
                children[endIdx] is HtmlContentIntermediateNode nextHtml &&
                children[endIdx - 1] is HtmlContentIntermediateNode prevAtEnd)
            {
                MergeHtmlNodes(prevAtEnd, nextHtml);
                children.RemoveAt(endIdx);
            }

            // Merge consecutive HtmlContent within the inserted range (right to left)
            for (var i = start + count - 1; i > start; i--)
            {
                if (i < children.Count && i - 1 >= 0 &&
                    children[i] is HtmlContentIntermediateNode right &&
                    children[i - 1] is HtmlContentIntermediateNode left)
                {
                    MergeHtmlNodes(left, right);
                    children.RemoveAt(i);
                }
            }

            // Merge with the node before the insertion point
            if (start > 0 && start < children.Count &&
                children[start] is HtmlContentIntermediateNode insertedHtml &&
                children[start - 1] is HtmlContentIntermediateNode beforeHtml)
            {
                MergeHtmlNodes(beforeHtml, insertedHtml);
                children.RemoveAt(start);
            }
        }

        /// <summary>
        /// Merges the children and source span of 'source' into 'target'.
        /// </summary>
        private static void MergeHtmlNodes(HtmlContentIntermediateNode target, HtmlContentIntermediateNode source)
        {
            foreach (var child in source.Children)
            {
                target.Children.Add(child);
            }

            if (target.Source is SourceSpan targetSpan && source.Source is SourceSpan sourceSpan)
            {
                var newLength = (sourceSpan.AbsoluteIndex + sourceSpan.Length) - targetSpan.AbsoluteIndex;
                target.Source = new SourceSpan(
                    targetSpan.FilePath,
                    targetSpan.AbsoluteIndex,
                    targetSpan.LineIndex,
                    targetSpan.CharacterIndex,
                    newLength,
                    targetSpan.LineCount,
                    targetSpan.EndCharacterIndex);
            }
            else if (target.Source is null && source.Source is not null)
            {
                target.Source = source.Source;
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

                    if (match.Attribute.IsDirectiveAttribute)
                    {
                        // Directive attributes (@ref, @key, @onclick, @attributes, etc.)
                        ProcessDirectiveAttribute(tagHelperNode, htmlAttr, match, attributeStructure);
                    }
                    else
                    {
                        // Regular bound attributes
                        ProcessBoundAttribute(tagHelperNode, htmlAttr, match, attributeStructure);
                    }
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

                // For directive attributes (@ref, @key, etc.) that are unmatched (e.g., ClassifyAttributesOnly),
                // the current pipeline converts HtmlAttributeValue → HtmlContent because VisitAttributeValue
                // creates HtmlContent for pure literal values. Regular unmatched attributes keep as-is.
                var isDirectiveAttribute = attributeName.StartsWith("@", System.StringComparison.Ordinal);
                if (isDirectiveAttribute)
                {
                    CopyAttributeValueChildren(htmlAttr, addHtmlAttribute);
                }
                else
                {
                    foreach (var valueChild in htmlAttr.Children)
                    {
                        addHtmlAttribute.Children.Add(valueChild);
                    }
                }

                tagHelperNode.Children.Add(addHtmlAttribute);
            }
        }

        /// <summary>
        /// Copies attribute value children from an HtmlAttributeIntermediateNode to a target node,
        /// converting HTML-attribute-specific intermediate nodes to tag-helper-compatible ones.
        /// HtmlAttributeValue → HtmlContent, CSharpExpressionAttributeValue → CSharpExpression.
        /// </summary>
        private static void CopyAttributeValueChildren(HtmlAttributeIntermediateNode htmlAttr, IntermediateNode target)
        {
            foreach (var valueChild in htmlAttr.Children)
            {
                if (valueChild is CSharpExpressionAttributeValueIntermediateNode exprAttrValue)
                {
                    // Convert CSharpExpressionAttributeValue to CSharpExpression
                    var csharpExpr = new CSharpExpressionIntermediateNode();
                    foreach (var inner in exprAttrValue.Children)
                    {
                        csharpExpr.Source = inner.Source;
                        csharpExpr.Children.Add(inner);
                    }
                    target.Children.Add(csharpExpr);
                }
                else if (valueChild is HtmlAttributeValueIntermediateNode htmlAttrValue)
                {
                    // Convert HtmlAttributeValue to HtmlContent
                    var htmlContent = new HtmlContentIntermediateNode() { Source = htmlAttrValue.Source };
                    foreach (var inner in htmlAttrValue.Children)
                    {
                        htmlContent.Children.Add(inner);
                    }
                    target.Children.Add(htmlContent);
                }
                else
                {
                    target.Children.Add(valueChild);
                }
            }
        }

        private static void ProcessBoundAttribute(
            TagHelperIntermediateNode tagHelperNode,
            HtmlAttributeIntermediateNode htmlAttr,
            TagHelperAttributeMatch match,
            AttributeStructure attributeStructure)
        {
            var setTagHelperProperty = new TagHelperPropertyIntermediateNode(match)
            {
                AttributeName = htmlAttr.AttributeName,
                AttributeStructure = attributeStructure,
                Source = DeterminePropertySource(htmlAttr),
            };

            CopyAttributeValueChildren(htmlAttr, setTagHelperProperty);
            tagHelperNode.Children.Add(setTagHelperProperty);
        }

        private static void ProcessDirectiveAttribute(
            TagHelperIntermediateNode tagHelperNode,
            HtmlAttributeIntermediateNode htmlAttr,
            TagHelperAttributeMatch match,
            AttributeStructure attributeStructure)
        {
            var attributeName = htmlAttr.AttributeName;

            // Directive attribute names start with '@' — strip it for the AttributeName property
            var strippedName = attributeName.StartsWith("@", System.StringComparison.Ordinal)
                ? attributeName.Substring(1)
                : attributeName;

            // Check for parameter syntax (e.g., @onclick:preventDefault → parameter "preventDefault")
            var colonIndex = strippedName.IndexOf(':');
            var hasParameter = colonIndex >= 0;
            var nameWithoutParameter = hasParameter ? strippedName.Substring(0, colonIndex) : strippedName;

            IntermediateNode attributeNode;

            if (match.IsParameterMatch && hasParameter)
            {
                attributeNode = new TagHelperDirectiveAttributeParameterIntermediateNode(match)
                {
                    AttributeName = strippedName,
                    AttributeNameWithoutParameter = nameWithoutParameter,
                    OriginalAttributeName = attributeName,
                    AttributeStructure = attributeStructure,
                    Source = DeterminePropertySource(htmlAttr),
                    OriginalAttributeSpan = htmlAttr.Source,
                };
            }
            else
            {
                attributeNode = new TagHelperDirectiveAttributeIntermediateNode(match)
                {
                    AttributeName = strippedName,
                    OriginalAttributeName = attributeName,
                    AttributeStructure = attributeStructure,
                    Source = DeterminePropertySource(htmlAttr),
                    OriginalAttributeSpan = htmlAttr.Source,
                };
            }

            CopyAttributeValueChildren(htmlAttr, attributeNode);
            tagHelperNode.Children.Add(attributeNode);
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
                // NoQuotes is normalized to DoubleQuotes for tag helper attributes.
                // This matches the behavior of the syntax tree rewrite (TagHelperBlockRewriter).
                return AttributeStructure.DoubleQuotes;
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
