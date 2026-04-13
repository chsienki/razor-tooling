// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.AspNetCore.Razor.PooledObjects;

namespace Microsoft.AspNetCore.Razor.Language;

/// <summary>
/// Rewrites <see cref="MarkupElementIntermediateNode"/> nodes in the intermediate tree
/// to <see cref="TagHelperIntermediateNode"/> when they match tag helper bindings.
/// </summary>
internal static class TagHelperIntermediateNodeRewriter
{
    public static void Rewrite(
        DocumentIntermediateNode document,
        RazorSourceDocument source,
        TagHelperBinder binder,
        RazorParserOptions options,
        TagHelperCollection.Builder? usedDescriptors = null,
        CancellationToken cancellationToken = default)
    {
        var rewriter = new Rewriter(source, binder, options, usedDescriptors, cancellationToken);
        rewriter.Visit(document);
    }

    private sealed class Rewriter : IntermediateNodeWalker
    {
        // Null characters are invalid markup for HTML attribute values.
        private const char InvalidAttributeValueMarker = '\0';

        private readonly RazorSourceDocument _source;
        private readonly TagHelperBinder _binder;
        private readonly RazorParserOptions _options;
        private readonly TagHelperCollection.Builder? _usedDescriptors;
        private readonly CancellationToken _cancellationToken;
        private readonly Stack<TagTracker> _trackerStack = new();

        public Rewriter(
            RazorSourceDocument source,
            TagHelperBinder binder,
            RazorParserOptions options,
            TagHelperCollection.Builder? usedDescriptors,
            CancellationToken cancellationToken)
        {
            _source = source;
            _binder = binder;
            _options = options;
            _usedDescriptors = usedDescriptors;
            _cancellationToken = cancellationToken;
        }

        private TagTracker? CurrentTracker => _trackerStack.Count > 0 ? _trackerStack.Peek() : null;
        private string? CurrentParentTagName => CurrentTracker?.TagName;
        private bool CurrentParentIsTagHelper => CurrentTracker?.IsTagHelper ?? false;

        private TagHelperTracker? CurrentTagHelperTracker
        {
            get
            {
                foreach (var tracker in _trackerStack)
                {
                    if (tracker.IsTagHelper)
                    {
                        return tracker as TagHelperTracker;
                    }
                }
                return null;
            }
        }

        public override void VisitTagHelper(TagHelperIntermediateNode node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            // For TagHelperIntermediateNode nodes created by the first lowering phase (legacy cshtml files),
            // we need to:
            // 1. Extract attribute information from any HTML content in the body that represents start tag attributes
            // 2. Create proper TagHelperPropertyIntermediateNode for bound attributes
            // 3. Clean up the body content to remove the attribute text

            // Find the body node
            TagHelperBodyIntermediateNode? bodyNode = null;
            foreach (var child in node.Children)
            {
                if (child is TagHelperBodyIntermediateNode body)
                {
                    bodyNode = body;
                    break;
                }
            }

            if (bodyNode != null)
            {
                // Find and process any HtmlContentIntermediateNode that contains attribute-like content
                var childrenToRemove = new List<IntermediateNode>();
                foreach (var child in bodyNode.Children)
                {
                    if (child is HtmlContentIntermediateNode htmlContent)
                    {
                        // Extract content and check if it looks like attribute text
                        string fullContent = "";
                        foreach (var token in htmlContent.Children)
                        {
                            if (token is IntermediateToken intermediateToken)
                            {
                                fullContent += intermediateToken.Content;
                            }
                        }

                        if (fullContent.Contains('='))
                        {
                            // This content contains attributes - parse and create proper attribute nodes
                            ParseAndAddAttributes(node, fullContent, htmlContent.Source);
                            childrenToRemove.Add(child);
                        }
                    }
                }

                foreach (var child in childrenToRemove)
                {
                    bodyNode.Children.Remove(child);
                }

                // Visit body children for nested elements
                foreach (var child in bodyNode.Children)
                {
                    Visit(child);
                }
            }
        }

        private void ParseAndAddAttributes(TagHelperIntermediateNode tagHelperNode, string attributeContent, SourceSpan? contentSource)
        {
            // Parse simple attribute assignments like: name="value" or name='value' or name=value
            // The content might look like: ` mail="example"` or `attr1="val1" attr2="val2"`

            var content = attributeContent.Trim();
            var trimOffset = attributeContent.Length - attributeContent.TrimStart().Length;
            var index = 0;

            while (index < content.Length)
            {
                // Skip whitespace
                while (index < content.Length && char.IsWhiteSpace(content[index]))
                {
                    index++;
                }

                if (index >= content.Length)
                    break;

                // Read attribute name
                var nameStart = index;
                while (index < content.Length && content[index] != '=' && !char.IsWhiteSpace(content[index]))
                {
                    index++;
                }
                var attributeName = content[nameStart..index];

                if (string.IsNullOrEmpty(attributeName))
                    break;

                // Skip whitespace and '='
                while (index < content.Length && (char.IsWhiteSpace(content[index]) || content[index] == '='))
                {
                    index++;
                }

                // Read attribute value
                string attributeValue = "";
                var valueStart = index;
                if (index < content.Length)
                {
                    var quoteChar = content[index];
                    if (quoteChar == '"' || quoteChar == '\'')
                    {
                        index++; // Skip opening quote
                        valueStart = index;
                        while (index < content.Length && content[index] != quoteChar)
                        {
                            index++;
                        }
                        attributeValue = content[valueStart..index];
                        if (index < content.Length)
                            index++; // Skip closing quote
                    }
                    else
                    {
                        // Unquoted value
                        valueStart = index;
                        while (index < content.Length && !char.IsWhiteSpace(content[index]))
                        {
                            index++;
                        }
                        attributeValue = content[valueStart..index];
                    }
                }

                // Create TagHelperPropertyIntermediateNode for bound attributes
                using var matches = new PooledArrayBuilder<TagHelperAttributeMatch>();
                TagHelperMatchingConventions.GetAttributeMatches(tagHelperNode.TagHelpers, attributeName, ref matches.AsRef());

                if (matches.Any())
                {
                    foreach (var match in matches)
                    {
                        var propertyNode = new TagHelperPropertyIntermediateNode(match)
                        {
                            AttributeName = attributeName,
                            AttributeStructure = AttributeStructure.DoubleQuotes,
                            Source = null,
                        };

                        // Add the attribute value as a C# string literal token
                        var csharpExpression = new CSharpExpressionIntermediateNode();
                        // Wrap the value in quotes to make it a string literal
                        var stringLiteralValue = $"\"{attributeValue.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
                        csharpExpression.Children.Add(IntermediateNodeFactory.CSharpToken(stringLiteralValue, source: null));
                        propertyNode.Children.Add(csharpExpression);

                        tagHelperNode.Children.Add(propertyNode);
                    }
                }
                else
                {
                    // Unbound HTML attribute
                    var htmlAttributeNode = new TagHelperHtmlAttributeIntermediateNode()
                    {
                        AttributeName = attributeName,
                        AttributeStructure = AttributeStructure.DoubleQuotes,
                    };

                    // Source and Prefix were originally set by VisitMarkupLiteralAttributeValue in the
                    // lowering phase from the syntax tree. Since we're re-parsing from string content,
                    // derive the source span from the HtmlContentIntermediateNode that contained this text.
                    var valueSource = contentSource is { } cs
                        ? cs.Slice(trimOffset + valueStart, attributeValue.Length)
                        : (SourceSpan?)null;
                    var valueNode = new HtmlAttributeValueIntermediateNode()
                    {
                        Prefix = string.Empty,
                        Source = valueSource,
                    };
                    valueNode.Children.Add(IntermediateNodeFactory.HtmlToken(attributeValue, source: valueSource));
                    htmlAttributeNode.Children.Add(valueNode);

                    tagHelperNode.Children.Add(htmlAttributeNode);
                }
            }
        }

        public override void VisitMarkupElement(MarkupElementIntermediateNode node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var tagName = node.TagName;
            if (string.IsNullOrEmpty(tagName) || tagName.StartsWith("!", StringComparison.Ordinal))
            {
                // Can't be a tag helper, visit children and return
                _trackerStack.Push(new TagTracker(tagName ?? string.Empty, IsTagHelper: false));
                base.VisitMarkupElement(node);
                _trackerStack.Pop();
                return;
            }

            // Check if this element matches a tag helper
            var elementAttributes = GetAttributeNameValuePairs(node);
            var tagHelperBinding = _binder.GetBinding(
                tagName,
                elementAttributes,
                CurrentParentTagName,
                CurrentParentIsTagHelper);

            if (tagHelperBinding == null)
            {
                // Not a tag helper, but we need to track it for parent context
                var tracker = CurrentTagHelperTracker;
                var tagNameScope = tracker?.TagName ?? string.Empty;

                if (string.Equals(tagNameScope, tagName, StringComparison.OrdinalIgnoreCase))
                {
                    tracker!.OpenMatchingTags++;
                }

                _trackerStack.Push(new TagTracker(tagName, IsTagHelper: false));
                base.VisitMarkupElement(node);
                _trackerStack.Pop();
                return;
            }

            // This is a tag helper - record usage
            _usedDescriptors?.AddRange(tagHelperBinding.TagHelpers);

            // Determine tag mode
            var tagMode = DetermineTagMode(node, tagHelperBinding);

            // Create the TagHelperIntermediateNode
            var tagHelperNode = new TagHelperIntermediateNode()
            {
                TagName = tagName,
                TagMode = tagMode,
                Source = node.Source,
                TagHelpers = tagHelperBinding.TagHelpers,
                StartTagSpan = node.Source
            };

            // Create body node and move children (excluding attributes and markup blocks that may contain start tag content)
            var bodyNode = new TagHelperBodyIntermediateNode();
            foreach (var child in node.Body)
            {
                // Skip MarkupBlockIntermediateNode which may contain start tag content including attributes
                if (child is MarkupBlockIntermediateNode)
                {
                    continue;
                }
                bodyNode.Children.Add(child);
            }
            tagHelperNode.Children.Add(bodyNode);

            // Process attributes - convert HtmlAttributeIntermediateNode to appropriate tag helper attribute nodes
            ProcessAttributes(node, tagHelperNode, tagHelperBinding);

            // Replace this node in the parent's children
            if (Parent != null)
            {
                var index = Parent.Children.IndexOf(node);
                if (index >= 0)
                {
                    Parent.Children[index] = tagHelperNode;
                }
            }

            // Track this tag helper and visit body children
            var tagHelperInfo = new TagHelperInfo(tagName, tagMode, tagHelperBinding);
            _trackerStack.Push(new TagHelperTracker(_binder.TagNamePrefix, tagHelperInfo));

            // Visit the body children (now in the bodyNode)
            foreach (var child in bodyNode.Children)
            {
                Visit(child);
            }

            _trackerStack.Pop();
        }

        private static TagMode DetermineTagMode(MarkupElementIntermediateNode node, TagHelperBinding binding)
        {
            // Check if any bound rules require a specific tag structure
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

            // Check if the element has body content
            var hasBody = false;
            foreach (var child in node.Body)
            {
                hasBody = true;
                break;
            }

            if (!hasBody)
            {
                // No body - could be self-closing or start-tag only
                // Check if it's a void element
                if (Legacy.ParserHelpers.VoidElements.Contains(node.TagName))
                {
                    return TagMode.StartTagOnly;
                }

                // For elements without body, default to SelfClosing if no end tag is expected
                return TagMode.SelfClosing;
            }

            return TagMode.StartTagAndEndTag;
        }

        private void ProcessAttributes(
            MarkupElementIntermediateNode sourceNode,
            TagHelperIntermediateNode targetNode,
            TagHelperBinding binding)
        {
            var renderedBoundAttributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var attribute in sourceNode.Attributes)
            {
                var attributeName = attribute.AttributeName;
                if (string.IsNullOrEmpty(attributeName))
                {
                    continue;
                }

                using var matches = new PooledArrayBuilder<TagHelperAttributeMatch>();
                TagHelperMatchingConventions.GetAttributeMatches(binding.TagHelpers, attributeName, ref matches.AsRef());

                if (matches.Any() && renderedBoundAttributeNames.Add(attributeName))
                {
                    // Bound attribute
                    foreach (var match in matches)
                    {
                        var propertyNode = new TagHelperPropertyIntermediateNode(match)
                        {
                            AttributeName = attributeName,
                            AttributeStructure = GetAttributeStructure(attribute),
                            Source = attribute.Source,
                            OriginalAttributeSpan = attribute.Source, // Preserve original span for code generation
                        };

                        // For bound attributes, we need to convert HTML attribute values to C# expressions
                        // The HTML attribute contains the value as HTML text, but component parameters need C# tokens
                        TransformAttributeValueToCSharp(attribute, propertyNode);

                        targetNode.Children.Add(propertyNode);
                    }
                }
                else
                {
                    // Unbound HTML attribute
                    var htmlAttributeNode = new TagHelperHtmlAttributeIntermediateNode()
                    {
                        AttributeName = attributeName,
                        AttributeStructure = GetAttributeStructure(attribute),
                    };

                    // Copy attribute value children
                    foreach (var child in attribute.Children)
                    {
                        htmlAttributeNode.Children.Add(child);
                    }

                    targetNode.Children.Add(htmlAttributeNode);
                }
            }
        }

        private static void TransformAttributeValueToCSharp(HtmlAttributeIntermediateNode sourceAttribute, TagHelperPropertyIntermediateNode targetNode)
        {
            // Check if the attribute already has C# expression children (from dynamic expressions like @value)
            foreach (var child in sourceAttribute.Children)
            {
                if (child is CSharpExpressionAttributeValueIntermediateNode ||
                    child is CSharpCodeAttributeValueIntermediateNode ||
                    child is CSharpExpressionIntermediateNode)
                {
                    // Already has C# content, copy as-is
                    targetNode.Children.Add(child);
                    continue;
                }

                if (child is HtmlAttributeValueIntermediateNode htmlValueNode)
                {
                    // Transform HTML value to C# expression
                    // Extract the text content and create a C# token
                    var valueContent = GetHtmlAttributeValueContent(htmlValueNode);
                    if (!string.IsNullOrEmpty(valueContent))
                    {
                        // Create a C# expression with the value
                        var csharpExpression = new CSharpExpressionIntermediateNode()
                        {
                            Source = htmlValueNode.Source,
                        };
                        csharpExpression.Children.Add(IntermediateNodeFactory.CSharpToken(valueContent, htmlValueNode.Source));
                        targetNode.Children.Add(csharpExpression);
                    }
                }
                else
                {
                    // Copy other child types as-is
                    targetNode.Children.Add(child);
                }
            }
        }

        private static string GetHtmlAttributeValueContent(HtmlAttributeValueIntermediateNode valueNode)
        {
            using var _ = StringBuilderPool.GetPooledObject(out var builder);

            foreach (var child in valueNode.Children)
            {
                if (child is IntermediateToken token)
                {
                    builder.Append(token.Content);
                }
            }

            return builder.ToString();
        }

        private static AttributeStructure GetAttributeStructure(HtmlAttributeIntermediateNode attribute)
        {
            if (string.IsNullOrEmpty(attribute.Prefix) && string.IsNullOrEmpty(attribute.Suffix))
            {
                return AttributeStructure.Minimized;
            }
            if (attribute.Prefix?.Contains('"') == true || attribute.Suffix?.Contains('"') == true)
            {
                return AttributeStructure.DoubleQuotes;
            }
            if (attribute.Prefix?.Contains('\'') == true || attribute.Suffix?.Contains('\'') == true)
            {
                return AttributeStructure.SingleQuotes;
            }
            return AttributeStructure.NoQuotes;
        }

        private static ImmutableArray<KeyValuePair<string, string>> GetAttributeNameValuePairs(MarkupElementIntermediateNode node)
        {
            using var attributes = new PooledArrayBuilder<KeyValuePair<string, string>>();

            foreach (var attribute in node.Attributes)
            {
                var name = attribute.AttributeName;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // Try to get the attribute value from children
                var value = GetAttributeValue(attribute);
                attributes.Add(new KeyValuePair<string, string>(name, value));
            }

            return attributes.ToImmutableAndClear();
        }

        private static string GetAttributeValue(HtmlAttributeIntermediateNode attribute)
        {
            using var _ = StringBuilderPool.GetPooledObject(out var builder);

            foreach (var child in attribute.Children)
            {
                if (child is HtmlAttributeValueIntermediateNode valueNode)
                {
                    foreach (var token in valueNode.Children)
                    {
                        if (token is IntermediateToken intermediateToken)
                        {
                            builder.Append(intermediateToken.Content);
                        }
                        else
                        {
                            builder.Append(InvalidAttributeValueMarker);
                        }
                    }
                }
                else if (child is CSharpExpressionAttributeValueIntermediateNode ||
                         child is CSharpCodeAttributeValueIntermediateNode)
                {
                    builder.Append(InvalidAttributeValueMarker);
                }
            }

            return builder.ToString();
        }

        private record TagTracker(string TagName, bool IsTagHelper);

        private record TagHelperTracker : TagTracker
        {
            public uint OpenMatchingTags;

            private readonly string? _tagNamePrefix;
            private readonly TagHelperBinding _binding;
            private readonly Lazy<(ImmutableArray<string> Names, HashSet<string> NameSet)> _lazyAllowedChildren;

            public ImmutableArray<string> AllowedChildren => _lazyAllowedChildren.Value.Names;

            public TagHelperTracker(string? tagNamePrefix, TagHelperInfo info)
                : base(info.TagName, IsTagHelper: true)
            {
                _tagNamePrefix = tagNamePrefix;
                _binding = info.BindingResult;
                _lazyAllowedChildren = new(CreateAllowedChildren);
            }

            private (ImmutableArray<string>, HashSet<string>) CreateAllowedChildren()
            {
                var distinctSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var result = new PooledArrayBuilder<string>();

                foreach (var tagHelper in _binding.TagHelpers)
                {
                    foreach (var allowedChildTag in tagHelper.AllowedChildTags)
                    {
                        var name = allowedChildTag.Name;
                        if (distinctSet.Add(name))
                        {
                            result.Add(name);
                        }
                    }
                }

                return (result.ToImmutableAndClear(), distinctSet);
            }
        }
    }
}
