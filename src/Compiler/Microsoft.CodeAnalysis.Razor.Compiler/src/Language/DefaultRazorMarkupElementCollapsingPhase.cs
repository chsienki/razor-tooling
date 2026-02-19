// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.AspNetCore.Razor.PooledObjects;

namespace Microsoft.AspNetCore.Razor.Language;

/// <summary>
/// Phase that runs after tag helper rewriting to collapse remaining <see cref="MarkupElementIntermediateNode"/>
/// instances (that were not converted to tag helpers) back into <see cref="HtmlContentIntermediateNode"/>.
/// This is necessary for legacy (cshtml) files where HTML is rendered as text.
/// </summary>
internal sealed class DefaultRazorMarkupElementCollapsingPhase : RazorEnginePhaseBase
{
    protected override RazorCodeDocument ExecuteCore(RazorCodeDocument codeDocument, CancellationToken cancellationToken)
    {
        var documentNode = codeDocument.GetRequiredDocumentNode();
        ThrowForMissingDocumentDependency(documentNode);

        // Only collapse for legacy (non-component) files
        if (codeDocument.FileKind.IsComponent())
        {
            return codeDocument;
        }

        var rewriter = new MarkupElementCollapsingRewriter(cancellationToken);
        rewriter.Visit(documentNode);

        return codeDocument;
    }

    private sealed class MarkupElementCollapsingRewriter : IntermediateNodeWalker
    {
        private readonly CancellationToken _cancellationToken;

        public MarkupElementCollapsingRewriter(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public override void VisitMarkupElement(MarkupElementIntermediateNode node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            // First, visit children to collapse any nested markup elements
            base.VisitMarkupElement(node);

            // Now collapse this markup element into HtmlContent nodes
            if (Parent != null)
            {
                var index = Parent.Children.IndexOf(node);
                if (index >= 0)
                {
                    // Remove the MarkupElementIntermediateNode
                    Parent.Children.RemoveAt(index);

                    // Insert the collapsed HTML content in its place
                    var insertIndex = index;

                    // Add opening tag as HtmlContent
                    var tagName = node.TagName;
                    if (!string.IsNullOrEmpty(tagName))
                    {
                        var openingTag = CreateOpeningTagContent(node);
                        Parent.Children.Insert(insertIndex++, openingTag);
                    }

                    // Move body children (excluding any HtmlContentIntermediateNode that contains attribute-like text)
                    // First, extract any attributes from HTML content that was incorrectly placed in the body
                    var extraAttributes = new List<(string Name, string Value)>();
                    foreach (var child in node.Body)
                    {
                        if (child is HtmlContentIntermediateNode htmlContent)
                        {
                            string fullContent = "";
                            foreach (var token in htmlContent.Children)
                            {
                                if (token is IntermediateToken intermediateToken)
                                {
                                    fullContent += intermediateToken.Content;
                                }
                            }
                            if (fullContent.Contains("="))
                            {
                                // Parse attributes from this content
                                ParseAttributesFromContent(fullContent, extraAttributes);
                            }
                        }
                    }

                    // If we found attributes in the body, recreate the opening tag with them
                    if (extraAttributes.Count > 0)
                    {
                        var newOpeningTag = CreateOpeningTagContentWithExtraAttributes(node, extraAttributes);
                        Parent.Children[index] = newOpeningTag;
                    }

                    // Now move body children, filtering out attribute-like content
                    foreach (var child in node.Body)
                    {
                        // Skip HtmlContentIntermediateNode that contains attribute-like content (text with '=')
                        // This can happen when the body incorrectly includes start tag attribute text
                        if (child is HtmlContentIntermediateNode htmlContent)
                        {
                            var hasAttributeLikeContent = false;
                            foreach (var token in htmlContent.Children)
                            {
                                if (token is IntermediateToken intermediateToken &&
                                    intermediateToken.Content.Contains("="))
                                {
                                    hasAttributeLikeContent = true;
                                    break;
                                }
                            }
                            if (hasAttributeLikeContent)
                            {
                                continue;
                            }
                        }
                        Parent.Children.Insert(insertIndex++, child);
                    }

                    // Add closing tag as HtmlContent (if not void/self-closing)
                    if (!string.IsNullOrEmpty(tagName) && !IsVoidElement(tagName) && HasBodyContent(node))
                    {
                        var closingTag = CreateClosingTagContent(node);
                        Parent.Children.Insert(insertIndex++, closingTag);
                    }
                }
            }
        }

        private static HtmlContentIntermediateNode CreateOpeningTagContent(MarkupElementIntermediateNode node)
        {
            using var _ = StringBuilderPool.GetPooledObject(out var builder);

            builder.Append('<');
            builder.Append(node.TagName);

            // Add attributes
            foreach (var attribute in node.Attributes)
            {
                builder.Append(' ');
                builder.Append(attribute.AttributeName);

                // Check if it's a minimized attribute
                if (!string.IsNullOrEmpty(attribute.Suffix))
                {
                    builder.Append("=\"");
                    // Get attribute value from children
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
                            }
                        }
                    }
                    builder.Append('"');
                }
            }

            // Close the opening tag
            if (IsVoidElement(node.TagName) || !HasBodyContent(node))
            {
                builder.Append(" />");
            }
            else
            {
                builder.Append('>');
            }

            var content = builder.ToString();
            var htmlNode = new HtmlContentIntermediateNode()
            {
                Source = node.Source,
            };
            htmlNode.Children.Add(IntermediateNodeFactory.HtmlToken(content, node.Source));
            return htmlNode;
        }

        private static HtmlContentIntermediateNode CreateClosingTagContent(MarkupElementIntermediateNode node)
        {
            var content = $"</{node.TagName}>";
            var htmlNode = new HtmlContentIntermediateNode()
            {
                Source = null,
            };
            htmlNode.Children.Add(IntermediateNodeFactory.HtmlToken(content, null));
            return htmlNode;
        }

        private static bool HasBodyContent(MarkupElementIntermediateNode node)
        {
            foreach (var _ in node.Body)
            {
                return true;
            }
            return false;
        }

        private static bool IsVoidElement(string tagName)
        {
            return Legacy.ParserHelpers.VoidElements.Contains(tagName);
        }

        private static void ParseAttributesFromContent(string content, List<(string Name, string Value)> attributes)
        {
            // Parse simple attribute assignments like: name="value" or name='value' or name=value
            var trimmed = content.Trim();
            var index = 0;

            while (index < trimmed.Length)
            {
                // Skip whitespace
                while (index < trimmed.Length && char.IsWhiteSpace(trimmed[index]))
                {
                    index++;
                }

                if (index >= trimmed.Length)
                    break;

                // Read attribute name
                var nameStart = index;
                while (index < trimmed.Length && trimmed[index] != '=' && !char.IsWhiteSpace(trimmed[index]))
                {
                    index++;
                }
                var attributeName = trimmed[nameStart..index];

                if (string.IsNullOrEmpty(attributeName))
                    break;

                // Skip whitespace and '='
                while (index < trimmed.Length && (char.IsWhiteSpace(trimmed[index]) || trimmed[index] == '='))
                {
                    index++;
                }

                // Read attribute value
                string attributeValue = "";
                if (index < trimmed.Length)
                {
                    var quoteChar = trimmed[index];
                    if (quoteChar == '"' || quoteChar == '\'')
                    {
                        index++; // Skip opening quote
                        var valueStart = index;
                        while (index < trimmed.Length && trimmed[index] != quoteChar)
                        {
                            index++;
                        }
                        attributeValue = trimmed[valueStart..index];
                        if (index < trimmed.Length)
                            index++; // Skip closing quote
                    }
                    else
                    {
                        // Unquoted value
                        var valueStart = index;
                        while (index < trimmed.Length && !char.IsWhiteSpace(trimmed[index]))
                        {
                            index++;
                        }
                        attributeValue = trimmed[valueStart..index];
                    }
                }

                attributes.Add((attributeName, attributeValue));
            }
        }

        private static HtmlContentIntermediateNode CreateOpeningTagContentWithExtraAttributes(
            MarkupElementIntermediateNode node, 
            List<(string Name, string Value)> extraAttributes)
        {
            using var _ = StringBuilderPool.GetPooledObject(out var builder);

            builder.Append('<');
            builder.Append(node.TagName);

            // Add existing attributes from node.Attributes
            foreach (var attribute in node.Attributes)
            {
                builder.Append(' ');
                builder.Append(attribute.AttributeName);

                if (!string.IsNullOrEmpty(attribute.Suffix))
                {
                    builder.Append("=\"");
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
                            }
                        }
                    }
                    builder.Append('"');
                }
            }

            // Add extra attributes parsed from body content
            foreach (var (name, value) in extraAttributes)
            {
                builder.Append(' ');
                builder.Append(name);
                builder.Append("=\"");
                builder.Append(value);
                builder.Append('"');
            }

            // Close the opening tag
            if (IsVoidElement(node.TagName) || !HasBodyContent(node))
            {
                builder.Append(" />");
            }
            else
            {
                builder.Append('>');
            }

            var content = builder.ToString();
            var htmlNode = new HtmlContentIntermediateNode()
            {
                Source = node.Source,
            };
            htmlNode.Children.Add(IntermediateNodeFactory.HtmlToken(content, node.Source));
            return htmlNode;
        }
    }
}
