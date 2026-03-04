// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Collections.Generic;
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
        var documentNode = codeDocument.GetDocumentNode();
        if (documentNode is null)
        {
            return codeDocument;
        }

        var syntaxTree = codeDocument.GetSyntaxTree();
        var isComponent = codeDocument.FileKind.IsComponent();
        // When AllowComponentFileKind is true, the lowering phase used ComponentFileKindVisitor
        // which creates MarkupElementIntermediateNode that doesn't need flattening.
        // When false (e.g. Version_2_1), LegacyFileKindVisitor was used even for .razor files,
        // and those MarkupElementIntermediateNodes need flattening.
        var usedComponentVisitor = isComponent && syntaxTree?.Options.AllowComponentFileKind == true;

        TagHelperBinder binder = null;
        string prefix = null;

        var tagHelperContext = codeDocument.GetTagHelperContext();
        if (tagHelperContext is not null && tagHelperContext.TagHelpers is not [])
        {
            binder = tagHelperContext.GetBinder();
            prefix = tagHelperContext.Prefix;
        }
        else if (usedComponentVisitor)
        {
            // Component files lowered with ComponentFileKindVisitor and no tag helpers — nothing to do
            return codeDocument;
        }

        var rewriter = new TagHelperRewriter(binder, prefix, usedComponentVisitor);
        rewriter.Visit(documentNode);
        rewriter.FlattenDeferredNodes();

        return codeDocument;
    }

    private class TagHelperRewriter : IntermediateNodeWalker
    {
        private readonly TagHelperBinder _binder;
        private readonly string _prefix;
        private readonly bool _isComponent;
        private readonly List<(MarkupElementIntermediateNode node, IntermediateNode parent)> _nodesToFlatten = new();

        public TagHelperRewriter(TagHelperBinder binder, string prefix, bool isComponent)
        {
            _binder = binder;
            _prefix = prefix;
            _isComponent = isComponent;
        }

        public void FlattenDeferredNodes()
        {
            if (_nodesToFlatten.Count == 0)
            {
                return;
            }

            // Build a set of all nodes to flatten so we can detect nesting
            var nodesToFlattenSet = new HashSet<MarkupElementIntermediateNode>();
            foreach (var (node, _) in _nodesToFlatten)
            {
                nodesToFlattenSet.Add(node);
            }

            // Process in DFS order (children before parents in the list).
            // Skip nodes whose parent is also in the set — their content will be
            // recursively collected when the parent is flattened.
            foreach (var (node, parent) in _nodesToFlatten)
            {
                if (parent is MarkupElementIntermediateNode parentElement && nodesToFlattenSet.Contains(parentElement))
                {
                    continue;
                }

                FlattenMarkupElement(node, parent, nodesToFlattenSet);
            }
        }

        public override void VisitMarkupElement(MarkupElementIntermediateNode node)
        {
            // First, visit children so we process nested elements bottom-up
            base.VisitDefault(node);

            // Orphan end tags (e.g., </body> without a matching start tag) should be flattened
            if (node.FlatStartTag.Count == 0 && node.FlatEndTag.Count > 0)
            {
                if (!_isComponent)
                {
                    _nodesToFlatten.Add((node, Parent));
                }

                return;
            }

            var tagName = node.TagName;
            if (string.IsNullOrEmpty(tagName))
            {
                if (!_isComponent)
                {
                    _nodesToFlatten.Add((node, Parent));
                }

                return;
            }

            // If no binder (legacy file with no tag helpers), just flatten
            if (_binder is null)
            {
                _nodesToFlatten.Add((node, Parent));
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
                    _nodesToFlatten.Add((node, Parent));
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
        /// Handles nested MarkupElements by recursively collecting their content.
        /// </summary>
        private static void FlattenMarkupElement(MarkupElementIntermediateNode node, IntermediateNode parent, HashSet<MarkupElementIntermediateNode> nodesToFlattenSet)
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

            // Build the replacement using recursive content collection
            var replacements = new List<IntermediateNode>();
            CollectFlattenedContent(node, replacements, nodesToFlattenSet);

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
        /// Recursively collects the flattened content from a MarkupElementIntermediateNode.
        /// For nested MarkupElements that are also in the flatten set, recurses into them
        /// instead of adding them as-is (which would leave MarkupElementIntermediateNode in the tree).
        /// </summary>
        private static void CollectFlattenedContent(MarkupElementIntermediateNode node, List<IntermediateNode> result, HashSet<MarkupElementIntermediateNode> nodesToFlattenSet)
        {
            // Flat start tag tokens
            foreach (var child in node.FlatStartTag)
            {
                result.Add(child);
            }

            // Body children — recurse into nested MarkupElements that need flattening
            foreach (var child in node.Body)
            {
                if (child is MarkupElementIntermediateNode nestedElement && nodesToFlattenSet.Contains(nestedElement))
                {
                    CollectFlattenedContent(nestedElement, result, nodesToFlattenSet);
                }
                else
                {
                    result.Add(child);
                }
            }

            // Flat end tag tokens
            foreach (var child in node.FlatEndTag)
            {
                result.Add(child);
            }
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
                children[endIdx - 1] is HtmlContentIntermediateNode prevAtEnd &&
                AreSpansContiguous(prevAtEnd, nextHtml))
            {
                MergeHtmlNodes(prevAtEnd, nextHtml);
                children.RemoveAt(endIdx);
            }

            // Merge consecutive HtmlContent within the inserted range (right to left)
            for (var i = start + count - 1; i > start; i--)
            {
                if (i < children.Count && i - 1 >= 0 &&
                    children[i] is HtmlContentIntermediateNode right &&
                    children[i - 1] is HtmlContentIntermediateNode left &&
                    AreSpansContiguous(left, right))
                {
                    MergeHtmlNodes(left, right);
                    children.RemoveAt(i);
                }
            }

            // Merge with the node before the insertion point
            if (start > 0 && start < children.Count &&
                children[start] is HtmlContentIntermediateNode insertedHtml &&
                children[start - 1] is HtmlContentIntermediateNode beforeHtml &&
                AreSpansContiguous(beforeHtml, insertedHtml))
            {
                MergeHtmlNodes(beforeHtml, insertedHtml);
                children.RemoveAt(start);
            }
        }

        /// <summary>
        /// Checks whether two HtmlContent nodes have contiguous source spans,
        /// matching the merge logic in DefaultRazorIntermediateNodeLoweringPhase.VisitHtmlContent.
        /// </summary>
        private static bool AreSpansContiguous(HtmlContentIntermediateNode left, HtmlContentIntermediateNode right)
        {
            if (left.Source is null && right.Source is null)
            {
                return true;
            }

            if (left.Source is SourceSpan leftSpan && right.Source is SourceSpan rightSpan)
            {
                return leftSpan.FilePath == rightSpan.FilePath &&
                       leftSpan.AbsoluteIndex + leftSpan.Length == rightSpan.AbsoluteIndex;
            }

            return false;
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

            // Use TagMode from the MarkupElementIntermediateNode (set during lowering),
            // but override based on TagStructure from the tag helper descriptors.
            var tagMode = GetTagMode(element, binding);

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
                    ProcessAttribute(tagHelperNode, htmlAttr, tagHelpers, renderedBoundAttributeNames, _isComponent);
                }
            }

            return tagHelperNode;
        }

        /// <summary>
        /// Determines the TagMode for a tag helper node, mirroring the logic in TagHelperBlockRewriter.GetTagMode.
        /// Checks TagStructure.WithoutEndTag from descriptors, which overrides the syntax-based TagMode.
        /// </summary>
        private static TagMode GetTagMode(MarkupElementIntermediateNode element, TagHelperBinding binding)
        {
            if (element.TagMode == TagMode.SelfClosing)
            {
                return TagMode.SelfClosing;
            }

            foreach (var boundRulesInfo in binding.AllBoundRules)
            {
                foreach (var rule in boundRulesInfo.Rules)
                {
                    if (rule.TagStructure == TagStructure.WithoutEndTag)
                    {
                        return TagMode.StartTagOnly;
                    }
                }
            }

            return element.TagMode;
        }

        private static void ProcessAttribute(
            TagHelperIntermediateNode tagHelperNode,
            HtmlAttributeIntermediateNode htmlAttr,
            TagHelperCollection tagHelpers,
            HashSet<string> renderedBoundAttributeNames,
            bool isComponent)
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

                // For components, convert HtmlAttributeValue → HtmlContent to match the format
                // produced by ComponentFileKindVisitor.VisitMarkupTagHelperAttribute.
                // For legacy, preserve children as-is (LegacyFileKindVisitor keeps HtmlAttributeValue).
                foreach (var valueChild in htmlAttr.Children)
                {
                    if (isComponent && valueChild is HtmlAttributeValueIntermediateNode htmlAttrValue)
                    {
                        var htmlContent = new HtmlContentIntermediateNode() { Source = htmlAttrValue.Source };
                        foreach (var inner in htmlAttrValue.Children)
                        {
                            htmlContent.Children.Add(inner);
                        }
                        addHtmlAttribute.Children.Add(htmlContent);
                    }
                    else
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
