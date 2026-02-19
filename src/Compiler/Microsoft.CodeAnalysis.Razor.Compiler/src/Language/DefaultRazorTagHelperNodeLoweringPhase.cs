//// Licensed to the .NET Foundation under one or more agreements.
//// The .NET Foundation licenses this file to you under the MIT license.

//#nullable disable

//using System;
//using System.Buffers;
//using System.Collections.Generic;
//using System.Collections.Immutable;
//using System.Threading;
//using Microsoft.AspNetCore.Razor.Language.Components;
//using Microsoft.AspNetCore.Razor.Language.Intermediate;
//using Microsoft.AspNetCore.Razor.Language.Legacy;
//using Microsoft.AspNetCore.Razor.Language.Syntax;
//using Microsoft.AspNetCore.Razor.PooledObjects;

//namespace Microsoft.AspNetCore.Razor.Language;

//internal class DefaultRazorTagHelperNodeLoweringPhase : RazorEnginePhaseBase
//{
//    protected override RazorCodeDocument ExecuteCore(RazorCodeDocument codeDocument, CancellationToken cancellationToken)
//    {
//        var syntaxTree = codeDocument.GetSyntaxTree();
//        ThrowForMissingDocumentDependency(syntaxTree);

//        var documentNode = codeDocument.GetRequiredDocumentNode();
//        ThrowForMissingDocumentDependency(documentNode);

//        // Build a map of source span -> MarkupElementIntermediateNode for efficient lookup
//        // These nodes were created by the first lowering phase before tag helper rewriting
//        var markupElementNodes = BuildMarkupElementNodeMap(documentNode);

//        // This might not have been set if there are no tag helpers.
//        var tagHelperContext = codeDocument.GetTagHelperContext();

//        // Create the appropriate visitor based on file kind
//        TagHelperLoweringVisitor visitor;
//        if (codeDocument.FileKind.IsComponentImport() &&
//            syntaxTree.Options.AllowComponentFileKind)
//        {
//            // Component imports don't process TagHelper attributes - they just report diagnostics
//            // which is already handled by the first phase
//            return codeDocument;
//        }
//        else if (codeDocument.FileKind.IsComponent() &&
//            syntaxTree.Options.AllowComponentFileKind)
//        {
//            visitor = new ComponentTagHelperLoweringVisitor(markupElementNodes, syntaxTree.Options)
//            {
//                SourceDocument = syntaxTree.Source,
//            };
//        }
//        else
//        {
//            visitor = new LegacyTagHelperLoweringVisitor(markupElementNodes, tagHelperContext?.Prefix, syntaxTree.Options)
//            {
//                SourceDocument = syntaxTree.Source,
//            };
//        }

//        visitor.Visit(syntaxTree.Root);

//        return codeDocument;
//    }

//    private readonly record struct MarkupElementNodeReference(IntermediateNode Node, IntermediateNode Parent);

//    private static Dictionary<SourceSpan, MarkupElementNodeReference> BuildMarkupElementNodeMap(DocumentIntermediateNode document)
//    {
//        var map = new Dictionary<SourceSpan, MarkupElementNodeReference>();
//        CollectMarkupElements(document, null, map);
//        return map;
//    }

//    private static void CollectMarkupElements(IntermediateNode node, IntermediateNode parent, Dictionary<SourceSpan, MarkupElementNodeReference> map)
//    {
//        //if (node is MarkupElementIntermediateNode markupElement && markupElement.Source is SourceSpan source)
//        if(node.Source is SourceSpan source)
//        {
//            map[source] = new MarkupElementNodeReference(node, parent);
//        }

//        foreach (var child in node.Children)
//        {
//            CollectMarkupElements(child, node, map);
//        }
//    }

//    private abstract class TagHelperLoweringVisitor : SyntaxWalker
//    {
//        protected readonly Dictionary<SourceSpan, MarkupElementNodeReference> _markupElementNodes;
//        protected readonly HashSet<string> _renderedBoundAttributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
//        protected readonly RazorParserOptions _options;
//        protected IntermediateNodeBuilder _builder;
//        protected TagHelperIntermediateNode _currentTagHelperNode;

//        protected TagHelperLoweringVisitor(
//            Dictionary<SourceSpan, MarkupElementNodeReference> markupElementNodes,
//            RazorParserOptions options)
//        {
//            _markupElementNodes = markupElementNodes;
//            _options = options;
//        }

//        public RazorSourceDocument SourceDocument { get; set; }

//        protected SourceSpan? BuildSourceSpanFromNode(SyntaxNode node)
//        {
//            if (node == null)
//            {
//                return null;
//            }

//            return node.GetSourceSpan(SourceDocument);
//        }

//        protected TagHelperIntermediateNode FindOrCreateTagHelperNode(MarkupTagHelperElementSyntax element)
//        {
//            var source = BuildSourceSpanFromNode(element);
//            if (source is SourceSpan span && _markupElementNodes.TryGetValue(span, out var nodeRef))
//            {
//                // Found the MarkupElementIntermediateNode - create a TagHelperIntermediateNode to replace it
//                var info = element.TagHelperInfo;
//                var tagHelperNode = new TagHelperIntermediateNode()
//                {
//                    TagName = info.TagName,
//                    TagMode = info.TagMode,
//                    Source = span,
//                    TagHelpers = info.BindingResult.TagHelpers,
//                    StartTagSpan = element.StartTag?.Name.GetSourceSpan(SourceDocument)
//                };

//                // Create body node and move children from the markup element
//                var bodyNode = new TagHelperBodyIntermediateNode();
//                foreach (var child in nodeRef.Node.Children)
//                {
//                    bodyNode.Children.Add(child);
//                }
//                tagHelperNode.Children.Add(bodyNode);

//                // Replace the MarkupElementIntermediateNode with the TagHelperIntermediateNode in the parent
//                if (nodeRef.Parent != null)
//                {
//                    var index = nodeRef.Parent.Children.IndexOf(nodeRef.Node);
//                    if (index >= 0)
//                    {
//                        nodeRef.Parent.Children[index] = tagHelperNode;
//                    }
//                }

//                return tagHelperNode;
//            }

//            return null;
//        }

//        /// <summary>
//        ///  Simple helper struct to simplify calling code that needs to skip elements
//        ///  without resorting to LINQ.
//        /// </summary>
//        protected readonly struct ChildNodesHelper(ChildSyntaxList list, int start = 0)
//        {
//            public int Count { get; } = Math.Max(list.Count - start, 0);

//            public SyntaxNodeOrToken this[int index] => list[start + index];

//            public ChildNodesHelper Skip(int count)
//            {
//                return new ChildNodesHelper(list, start + count);
//            }

//            public SyntaxNodeOrToken FirstOrDefault() => Count > 0 ? this[0] : default;

//            public bool TryCast<TNode>(out ImmutableArray<TNode> result)
//            {
//                // Note that this intentionally returns true for empty lists.
//                // This behavior matches the expectations of code that previously called
//                // ".All(x => x is TNode)" followed by ".Cast<TNode>()" via LINQ.
//                // Because "All" would return true for empty lists, this method
//                // needs to do the same.

//                using var builder = new PooledArrayBuilder<TNode>(Count);

//                for (var i = start; i < list.Count; i++)
//                {
//                    if (list[i].AsNode() is not TNode node)
//                    {
//                        result = default;
//                        return false;
//                    }

//                    builder.Add(node);
//                }

//                result = builder.ToImmutableAndClear();
//                return true;
//            }
//        }

//        protected static SyntaxTokenList MergeTokenLists(
//            SyntaxTokenList? literal1,
//            SyntaxTokenList? literal2)
//        {
//            using var _ = ArrayPool<SyntaxTokenList>.Shared.GetPooledArraySpan(2, out var tokenLists);
//            var tokenListsCount = 0;
//            var count = 0;

//            if (literal1 is { } tokens1)
//            {
//                tokenLists[tokenListsCount++] = tokens1;
//                count += tokens1.Count;
//            }

//            if (literal2 is { } tokens2)
//            {
//                tokenLists[tokenListsCount++] = tokens2;
//                count += tokens2.Count;
//            }

//            if (count == 0)
//            {
//                return default;
//            }

//            using var builder = new PooledArrayBuilder<SyntaxToken>(count);

//            foreach (var tokenList in tokenLists[..tokenListsCount])
//            {
//                builder.AddRange(tokenList);
//            }

//            return builder.ToList();
//        }

//        protected static MarkupTextLiteralSyntax MergeAttributeValue(MarkupLiteralAttributeValueSyntax node)
//        {
//            var valueTokens = MergeTokenLists(
//                node.Prefix?.LiteralTokens,
//                node.Value?.LiteralTokens);

//            var rewritten = node.Prefix?.Update(valueTokens) ?? node.Value?.Update(valueTokens);

//            rewritten = (MarkupTextLiteralSyntax)rewritten?.Green.CreateRed(node, node.Position);

//            if (rewritten.EditHandler is { } originalEditHandler)
//            {
//                rewritten = rewritten.Update(rewritten.LiteralTokens, MarkupChunkGenerator.Instance, originalEditHandler);
//            }

//            return rewritten;
//        }
//    }

//    // Handles TagHelper attributes for Legacy (cshtml) files
//    private class LegacyTagHelperLoweringVisitor : TagHelperLoweringVisitor
//    {
//        private readonly string _tagHelperPrefix;

//        public LegacyTagHelperLoweringVisitor(
//            Dictionary<SourceSpan, MarkupElementNodeReference> markupElementNodes,
//            string tagHelperPrefix,
//            RazorParserOptions options)
//            : base(markupElementNodes, options)
//        {
//            _tagHelperPrefix = tagHelperPrefix;
//        }

//        public override void VisitMarkupTagHelperElement(MarkupTagHelperElementSyntax node)
//        {
//            _currentTagHelperNode = FindOrCreateTagHelperNode(node);
//            if (_currentTagHelperNode == null)
//            {
//                // The element wasn't found in the intermediate tree, skip it
//                base.VisitMarkupTagHelperElement(node);
//                return;
//            }

//            Visit(node.StartTag);

//            // Clear for next element
//            _renderedBoundAttributeNames.Clear();
//            _currentTagHelperNode = null;

//            // Visit body for any nested TagHelper elements
//            foreach (var item in node.Body)
//            {
//                Visit(item);
//            }
//        }

//        public override void VisitMarkupTagHelperStartTag(MarkupTagHelperStartTagSyntax node)
//        {
//            if (_currentTagHelperNode == null)
//            {
//                return;
//            }

//            foreach (var child in node.Attributes)
//            {
//                if (child is MarkupTagHelperAttributeSyntax || child is MarkupMinimizedTagHelperAttributeSyntax)
//                {
//                    Visit(child);
//                }
//            }
//        }

//        public override void VisitMarkupMinimizedTagHelperAttribute(MarkupMinimizedTagHelperAttributeSyntax node)
//        {
//            if (_currentTagHelperNode == null)
//            {
//                return;
//            }

//            if (!_options.AllowMinimizedBooleanTagHelperAttributes)
//            {
//                // Minimized attributes are not valid for non-boolean bound attributes. TagHelperBlockRewriter
//                // has already logged an error if it was a non-boolean bound attribute; so we can skip.
//                return;
//            }

//            var element = node.FirstAncestorOrSelf<MarkupTagHelperElementSyntax>();
//            var tagHelpers = element.TagHelperInfo.BindingResult.TagHelpers;
//            var attributeName = node.Name.GetContent();

//            using var matches = new PooledArrayBuilder<TagHelperAttributeMatch>();
//            TagHelperMatchingConventions.GetAttributeMatches(tagHelpers, attributeName, ref matches.AsRef());

//            if (matches.Any() && _renderedBoundAttributeNames.Add(attributeName))
//            {
//                foreach (var match in matches)
//                {
//                    if (!match.ExpectsBooleanValue)
//                    {
//                        // We do not allow minimized non-boolean bound attributes.
//                        return;
//                    }

//                    var setTagHelperProperty = new TagHelperPropertyIntermediateNode(match)
//                    {
//                        AttributeName = attributeName,
//                        AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure,
//                        Source = null,
//                        OriginalAttributeSpan = BuildSourceSpanFromNode(node.Name)
//                    };

//                    _currentTagHelperNode.Children.Add(setTagHelperProperty);
//                }
//            }
//            else
//            {
//                var addHtmlAttribute = new TagHelperHtmlAttributeIntermediateNode()
//                {
//                    AttributeName = attributeName,
//                    AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure
//                };

//                _currentTagHelperNode.Children.Add(addHtmlAttribute);
//            }
//        }

//        public override void VisitMarkupTagHelperAttribute(MarkupTagHelperAttributeSyntax node)
//        {
//            if (_currentTagHelperNode == null)
//            {
//                return;
//            }

//            var element = node.FirstAncestorOrSelf<MarkupTagHelperElementSyntax>();
//            var tagHelpers = element.TagHelperInfo.BindingResult.TagHelpers;
//            var attributeName = node.Name.GetContent();
//            var attributeValueNode = node.Value;

//            using var matches = new PooledArrayBuilder<TagHelperAttributeMatch>();
//            TagHelperMatchingConventions.GetAttributeMatches(tagHelpers, attributeName, ref matches.AsRef());

//            if (matches.Any() && _renderedBoundAttributeNames.Add(attributeName))
//            {
//                foreach (var match in matches)
//                {
//                    var setTagHelperProperty = new TagHelperPropertyIntermediateNode(match)
//                    {
//                        AttributeName = attributeName,
//                        AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure,
//                        Source = BuildSourceSpanFromNode(attributeValueNode),
//                        OriginalAttributeSpan = BuildSourceSpanFromNode(node.Name)
//                    };

//                    _builder = IntermediateNodeBuilder.Create(setTagHelperProperty);
//                    VisitTagHelperAttributeValue(attributeValueNode);
//                    _builder = null;
//                    _currentTagHelperNode.Children.Add(setTagHelperProperty);
//                }
//            }
//            else
//            {
//                var addHtmlAttribute = new TagHelperHtmlAttributeIntermediateNode()
//                {
//                    AttributeName = attributeName,
//                    AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure
//                };

//                _builder = IntermediateNodeBuilder.Create(addHtmlAttribute);
//                VisitTagHelperAttributeValue(attributeValueNode);
//                _builder = null;
//                _currentTagHelperNode.Children.Add(addHtmlAttribute);
//            }
//        }

//        private void VisitTagHelperAttributeValue(SyntaxNode node)
//        {
//            if (node == null)
//            {
//                return;
//            }

//            var children = new ChildNodesHelper(node.ChildNodesAndTokens());
//            var position = node.Position;
//            if (children.FirstOrDefault().AsNode() is MarkupBlockSyntax { Children: [MarkupTextLiteralSyntax, MarkupEphemeralTextLiteralSyntax] } markupBlock)
//            {
//                // This is a special case when we have an attribute like attr="@@foo".
//                // In this case, we want the foo to be written out as HtmlContent and not HtmlAttributeValue.
//                Visit(markupBlock);
//                children = children.Skip(1);
//                position = children.Count > 0 ? children[0].Position : position;
//            }

//            if (children.TryCast<MarkupLiteralAttributeValueSyntax>(out var attributeLiteralArray))
//            {
//                using PooledArrayBuilder<SyntaxToken> builder = [];

//                foreach (var literal in attributeLiteralArray)
//                {
//                    var mergedValue = MergeAttributeValue(literal);
//                    builder.AddRange(mergedValue.LiteralTokens);
//                }

//                var rewritten = SyntaxFactory.MarkupTextLiteral(builder.ToList()).Green.CreateRed(node.Parent, position);
//                Visit(rewritten);
//            }
//            else if (children.TryCast<MarkupTextLiteralSyntax>(out var markupLiteralArray))
//            {
//                using PooledArrayBuilder<SyntaxToken> builder = [];

//                foreach (var literal in markupLiteralArray)
//                {
//                    builder.AddRange(literal.LiteralTokens);
//                }

//                var rewritten = SyntaxFactory.MarkupTextLiteral(builder.ToList()).Green.CreateRed(node.Parent, position);
//                Visit(rewritten);
//            }
//            else if (children.TryCast<CSharpExpressionLiteralSyntax>(out var expressionLiteralArray))
//            {
//                using PooledArrayBuilder<SyntaxToken> builder = [];

//                SpanEditHandler editHandler = null;
//                ISpanChunkGenerator generator = null;
//                foreach (var literal in expressionLiteralArray)
//                {
//                    generator = literal.ChunkGenerator;
//                    editHandler = literal.EditHandler;
//                    builder.AddRange(literal.LiteralTokens);
//                }

//                var rewritten = SyntaxFactory.CSharpExpressionLiteral(builder.ToList(), generator, editHandler).Green.CreateRed(node.Parent, position);
//                Visit(rewritten);
//            }
//            else
//            {
//                Visit(node);
//            }
//        }

//        // Example
//        // <input checked="hello-world `@false`"/>
//        //  Prefix= (space)
//        //  Children will contain a token for @false.
//        public override void VisitMarkupDynamicAttributeValue(MarkupDynamicAttributeValueSyntax node)
//        {
//            if (_builder == null)
//            {
//                return;
//            }

//            var containsExpression = false;

//            // Don't go into sub block. They may contain expressions but we only care about the top level.
//            var descendantNodes = node.DescendantNodes(static n => n.Parent is not CSharpCodeBlockSyntax);

//            foreach (var child in descendantNodes)
//            {
//                if (child is CSharpImplicitExpressionSyntax || child is CSharpExplicitExpressionSyntax)
//                {
//                    containsExpression = true;
//                }
//            }

//            if (containsExpression)
//            {
//                _builder.Push(new CSharpExpressionAttributeValueIntermediateNode()
//                {
//                    Prefix = node.Prefix?.GetContent() ?? string.Empty,
//                    Source = BuildSourceSpanFromNode(node),
//                });
//            }
//            else
//            {
//                _builder.Push(new CSharpCodeAttributeValueIntermediateNode()
//                {
//                    Prefix = node.Prefix?.GetContent() ?? string.Empty,
//                    Source = BuildSourceSpanFromNode(node),
//                });
//            }

//            Visit(node.Value);

//            _builder.Pop();
//        }

//        public override void VisitMarkupLiteralAttributeValue(MarkupLiteralAttributeValueSyntax node)
//        {
//            if (_builder == null)
//            {
//                return;
//            }

//            _builder.Push(new HtmlAttributeValueIntermediateNode()
//            {
//                Prefix = node.Prefix?.GetContent() ?? string.Empty,
//                Source = BuildSourceSpanFromNode(node),
//            });

//            _builder.Add(IntermediateNodeFactory.HtmlToken(
//                arg: node,
//                contentFactory: static node => node.Value?.GetContent() ?? string.Empty,
//                source: BuildSourceSpanFromNode(node.Value)));

//            _builder.Pop();
//        }

//        public override void VisitMarkupTextLiteral(MarkupTextLiteralSyntax node)
//        {
//            if (_builder == null)
//            {
//                return;
//            }

//            if (node.ChunkGenerator == SpanChunkGenerator.Null)
//            {
//                return;
//            }

//            if (node.LiteralTokens is [{ Kind: SyntaxKind.Marker, Content.Length: 0 }])
//            {
//                // We don't want to create IR nodes for marker tokens.
//                return;
//            }

//            _builder.Add(IntermediateNodeFactory.HtmlToken(
//                arg: node,
//                contentFactory: static node => node.GetContent(),
//                source: BuildSourceSpanFromNode(node)));
//        }

//        public override void VisitCSharpExpressionLiteral(CSharpExpressionLiteralSyntax node)
//        {
//            if (_builder == null)
//            {
//                return;
//            }

//            if (_builder.Current is TagHelperHtmlAttributeIntermediateNode)
//            {
//                // If we are top level in a tag helper HTML attribute, we want to be rendered as markup.
//                // This case happens for duplicate non-string bound attributes. They would be initially be categorized as
//                // CSharp but since they are duplicate, they should just be markup.
//                var markupLiteral = SyntaxFactory.MarkupTextLiteral(node.LiteralTokens).Green.CreateRed(node.Parent, node.Position);
//                Visit(markupLiteral);
//                return;
//            }

//            _builder.Add(IntermediateNodeFactory.CSharpToken(
//                arg: node,
//                contentFactory: static node => node.GetContent(),
//                source: BuildSourceSpanFromNode(node)));

//            base.VisitCSharpExpressionLiteral(node);
//        }

//        public override void VisitCSharpStatementLiteral(CSharpStatementLiteralSyntax node)
//        {
//            if (_builder == null)
//            {
//                return;
//            }

//            if (node.ChunkGenerator is null or StatementChunkGenerator)
//            {
//                var isAttributeValue = _builder.Current is CSharpCodeAttributeValueIntermediateNode;

//                if (!isAttributeValue)
//                {
//                    var statementNode = new CSharpCodeIntermediateNode()
//                    {
//                        Source = BuildSourceSpanFromNode(node)
//                    };
//                    _builder.Push(statementNode);
//                }

//                _builder.Add(IntermediateNodeFactory.CSharpToken(
//                    arg: node,
//                    contentFactory: static node => node.GetContent(),
//                    source: BuildSourceSpanFromNode(node)));

//                if (!isAttributeValue)
//                {
//                    _builder.Pop();
//                }
//            }

//            base.VisitCSharpStatementLiteral(node);
//        }
//    }

//    // Handles TagHelper attributes for Component (razor) files
//    private class ComponentTagHelperLoweringVisitor : TagHelperLoweringVisitor
//    {
//        public ComponentTagHelperLoweringVisitor(
//            Dictionary<SourceSpan, MarkupElementNodeReference> markupElementNodes,
//            RazorParserOptions options)
//            : base(markupElementNodes, options)
//        {
//        }

//        public override void VisitMarkupTagHelperElement(MarkupTagHelperElementSyntax node)
//        {
//            _currentTagHelperNode = FindOrCreateTagHelperNode(node);
//            if (_currentTagHelperNode == null)
//            {
//                // The element wasn't found in the intermediate tree, skip it
//                base.VisitMarkupTagHelperElement(node);
//                return;
//            }

//            Visit(node.StartTag);

//            // Clear for next element
//            _renderedBoundAttributeNames.Clear();
//            _currentTagHelperNode = null;

//            // Visit body for any nested TagHelper elements
//            foreach (var item in node.Body)
//            {
//                Visit(item);
//            }
//        }

//        public override void VisitMarkupTagHelperStartTag(MarkupTagHelperStartTagSyntax node)
//        {
//            if (_currentTagHelperNode == null)
//            {
//                return;
//            }

//            foreach (var child in node.Attributes)
//            {
//                if (child is MarkupTagHelperAttributeSyntax ||
//                    child is MarkupMinimizedTagHelperAttributeSyntax ||
//                    child is MarkupTagHelperDirectiveAttributeSyntax ||
//                    child is MarkupMinimizedTagHelperDirectiveAttributeSyntax)
//                {
//                    Visit(child);
//                }
//            }
//        }

//        public override void VisitMarkupMinimizedTagHelperAttribute(MarkupMinimizedTagHelperAttributeSyntax node)
//        {
//            if (_currentTagHelperNode == null)
//            {
//                return;
//            }

//            if (!_options.AllowMinimizedBooleanTagHelperAttributes)
//            {
//                // Minimized attributes are not valid for non-boolean bound attributes. TagHelperBlockRewriter
//                // has already logged an error if it was a non-boolean bound attribute; so we can skip.
//                return;
//            }

//            var element = node.FirstAncestorOrSelf<MarkupTagHelperElementSyntax>();
//            var tagHelpers = element.TagHelperInfo.BindingResult.TagHelpers;
//            var attributeName = node.Name.GetContent();

//            using var matches = new PooledArrayBuilder<TagHelperAttributeMatch>();
//            TagHelperMatchingConventions.GetAttributeMatches(tagHelpers, attributeName, ref matches.AsRef());

//            if (matches.Any() && _renderedBoundAttributeNames.Add(attributeName))
//            {
//                foreach (var match in matches)
//                {
//                    if (!match.ExpectsBooleanValue)
//                    {
//                        // We do not allow minimized non-boolean bound attributes.
//                        return;
//                    }

//                    var setTagHelperProperty = new TagHelperPropertyIntermediateNode(match)
//                    {
//                        AttributeName = attributeName,
//                        AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure,
//                        Source = null,
//                        OriginalAttributeSpan = BuildSourceSpanFromNode(node.Name)
//                    };

//                    _currentTagHelperNode.Children.Add(setTagHelperProperty);
//                }
//            }
//            else
//            {
//                var addHtmlAttribute = new TagHelperHtmlAttributeIntermediateNode()
//                {
//                    AttributeName = attributeName,
//                    AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure
//                };

//                _currentTagHelperNode.Children.Add(addHtmlAttribute);
//            }
//        }

//        public override void VisitMarkupMinimizedTagHelperDirectiveAttribute(MarkupMinimizedTagHelperDirectiveAttributeSyntax node)
//        {
//            if (_currentTagHelperNode == null)
//            {
//                return;
//            }

//            if (!_options.AllowMinimizedBooleanTagHelperAttributes)
//            {
//                // Minimized attributes are not valid for non-boolean bound attributes. TagHelperBlockRewriter
//                // has already logged an error if it was a non-boolean bound attribute; so we can skip.
//                return;
//            }

//            var element = node.FirstAncestorOrSelf<MarkupTagHelperElementSyntax>();
//            var tagHelpers = element.TagHelperInfo.BindingResult.TagHelpers;
//            var attributeName = node.FullName;

//            using var matches = new PooledArrayBuilder<TagHelperAttributeMatch>();
//            TagHelperMatchingConventions.GetAttributeMatches(tagHelpers, attributeName, ref matches.AsRef());

//            if (matches.Any() && _renderedBoundAttributeNames.Add(attributeName))
//            {
//                var directiveAttributeName = new DirectiveAttributeName(attributeName);

//                foreach (var match in matches)
//                {
//                    if (!match.ExpectsBooleanValue)
//                    {
//                        // We do not allow minimized non-boolean bound attributes.
//                        return;
//                    }

//                    IntermediateNode attributeNode = match.IsParameterMatch && directiveAttributeName.HasParameter
//                        ? new TagHelperDirectiveAttributeParameterIntermediateNode(match)
//                        {
//                            AttributeName = directiveAttributeName.Text,
//                            AttributeNameWithoutParameter = directiveAttributeName.TextWithoutParameter,
//                            OriginalAttributeName = attributeName,
//                            AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure,
//                            Source = null
//                        }
//                        : new TagHelperDirectiveAttributeIntermediateNode(match)
//                        {
//                            AttributeName = directiveAttributeName.Text,
//                            OriginalAttributeName = attributeName,
//                            AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure,
//                            Source = null,
//                        };

//                    _currentTagHelperNode.Children.Add(attributeNode);
//                }
//            }
//            else
//            {
//                var addHtmlAttribute = new TagHelperHtmlAttributeIntermediateNode()
//                {
//                    AttributeName = attributeName,
//                    AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure
//                };

//                _currentTagHelperNode.Children.Add(addHtmlAttribute);
//            }
//        }

//        public override void VisitMarkupTagHelperAttribute(MarkupTagHelperAttributeSyntax node)
//        {
//            if (_currentTagHelperNode == null)
//            {
//                return;
//            }

//            var element = node.FirstAncestorOrSelf<MarkupTagHelperElementSyntax>();
//            var tagHelpers = element.TagHelperInfo.BindingResult.TagHelpers;
//            var attributeName = node.Name.GetContent();
//            var attributeValueNode = node.Value;

//            using var matches = new PooledArrayBuilder<TagHelperAttributeMatch>();
//            TagHelperMatchingConventions.GetAttributeMatches(tagHelpers, attributeName, ref matches.AsRef());

//            if (matches.Any() && _renderedBoundAttributeNames.Add(attributeName))
//            {
//                foreach (var match in matches)
//                {
//                    var setTagHelperProperty = new TagHelperPropertyIntermediateNode(match)
//                    {
//                        AttributeName = attributeName,
//                        AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure,
//                        Source = BuildSourceSpanFromNode(attributeValueNode),
//                        OriginalAttributeSpan = BuildSourceSpanFromNode(node.Name)
//                    };

//                    _builder = IntermediateNodeBuilder.Create(setTagHelperProperty);
//                    VisitTagHelperAttributeValue(attributeValueNode);
//                    _builder = null;
//                    _currentTagHelperNode.Children.Add(setTagHelperProperty);
//                }
//            }
//            else
//            {
//                var addHtmlAttribute = new TagHelperHtmlAttributeIntermediateNode()
//                {
//                    AttributeName = attributeName,
//                    AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure
//                };

//                _builder = IntermediateNodeBuilder.Create(addHtmlAttribute);
//                VisitTagHelperAttributeValue(attributeValueNode);
//                _builder = null;
//                _currentTagHelperNode.Children.Add(addHtmlAttribute);
//            }
//        }

//        public override void VisitMarkupTagHelperDirectiveAttribute(MarkupTagHelperDirectiveAttributeSyntax node)
//        {
//            if (_currentTagHelperNode == null)
//            {
//                return;
//            }

//            var element = node.FirstAncestorOrSelf<MarkupTagHelperElementSyntax>();
//            var tagHelpers = element.TagHelperInfo.BindingResult.TagHelpers;
//            var attributeName = node.FullName;
//            var attributeValueNode = node.Value;

//            using var matches = new PooledArrayBuilder<TagHelperAttributeMatch>();
//            TagHelperMatchingConventions.GetAttributeMatches(tagHelpers, attributeName, ref matches.AsRef());

//            if (matches.Any() && _renderedBoundAttributeNames.Add(attributeName))
//            {
//                var directiveAttributeName = new DirectiveAttributeName(attributeName);

//                foreach (var match in matches)
//                {
//                    IntermediateNode attributeNode = match.IsParameterMatch && directiveAttributeName.HasParameter
//                        ? new TagHelperDirectiveAttributeParameterIntermediateNode(match)
//                        {
//                            AttributeName = directiveAttributeName.Text,
//                            AttributeNameWithoutParameter = directiveAttributeName.TextWithoutParameter,
//                            OriginalAttributeName = attributeName,
//                            AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure,
//                            Source = BuildSourceSpanFromNode(attributeValueNode),
//                            OriginalAttributeSpan = BuildSourceSpanFromNode(node.Name)
//                        }
//                        : new TagHelperDirectiveAttributeIntermediateNode(match)
//                        {
//                            AttributeName = directiveAttributeName.Text,
//                            OriginalAttributeName = attributeName,
//                            AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure,
//                            Source = BuildSourceSpanFromNode(attributeValueNode),
//                            OriginalAttributeSpan = BuildSourceSpanFromNode(node.Name)
//                        };

//                    _builder = IntermediateNodeBuilder.Create(attributeNode);
//                    VisitTagHelperAttributeValue(attributeValueNode);
//                    _builder = null;
//                    _currentTagHelperNode.Children.Add(attributeNode);
//                }
//            }
//            else
//            {
//                var addHtmlAttribute = new TagHelperHtmlAttributeIntermediateNode()
//                {
//                    AttributeName = attributeName,
//                    AttributeStructure = node.TagHelperAttributeInfo.AttributeStructure
//                };

//                _builder = IntermediateNodeBuilder.Create(addHtmlAttribute);
//                VisitTagHelperAttributeValue(attributeValueNode);
//                _builder = null;
//                _currentTagHelperNode.Children.Add(addHtmlAttribute);
//            }
//        }

//        private void VisitTagHelperAttributeValue(SyntaxNode node)
//        {
//            if (node == null)
//            {
//                return;
//            }

//            var children = new ChildNodesHelper(node.ChildNodesAndTokens());
//            var position = node.Position;
//            if (children.FirstOrDefault().AsNode() is MarkupBlockSyntax { Children: [MarkupTextLiteralSyntax, MarkupEphemeralTextLiteralSyntax] } markupBlock)
//            {
//                // This is a special case when we have an attribute like attr="@@foo".
//                // In this case, we want the foo to be written out as HtmlContent and not HtmlAttributeValue.
//                Visit(markupBlock);
//                children = children.Skip(1);
//                position = children.Count > 0 ? children[0].Position : position;
//            }

//            if (children.TryCast<MarkupLiteralAttributeValueSyntax>(out var attributeLiteralArray))
//            {
//                using PooledArrayBuilder<SyntaxToken> builder = [];

//                foreach (var literal in attributeLiteralArray)
//                {
//                    var mergedValue = MergeAttributeValue(literal);
//                    builder.AddRange(mergedValue.LiteralTokens);
//                }

//                var rewritten = SyntaxFactory.MarkupTextLiteral(builder.ToList()).Green.CreateRed(node.Parent, position);
//                Visit(rewritten);
//            }
//            else if (children.TryCast<MarkupTextLiteralSyntax>(out var markupLiteralArray))
//            {
//                using PooledArrayBuilder<SyntaxToken> builder = [];

//                foreach (var literal in markupLiteralArray)
//                {
//                    builder.AddRange(literal.LiteralTokens);
//                }

//                var rewritten = SyntaxFactory.MarkupTextLiteral(builder.ToList()).Green.CreateRed(node.Parent, position);
//                Visit(rewritten);
//            }
//            else if (children.TryCast<CSharpExpressionLiteralSyntax>(out var expressionLiteralArray))
//            {
//                using PooledArrayBuilder<SyntaxToken> builder = [];

//                SpanEditHandler editHandler = null;
//                ISpanChunkGenerator generator = null;
//                foreach (var literal in expressionLiteralArray)
//                {
//                    generator = literal.ChunkGenerator;
//                    editHandler = literal.EditHandler;
//                    builder.AddRange(literal.LiteralTokens);
//                }

//                var rewritten = SyntaxFactory.CSharpExpressionLiteral(builder.ToList(), generator, editHandler).Green.CreateRed(node.Parent, position);
//                Visit(rewritten);
//            }
//            else
//            {
//                Visit(node);
//            }
//        }

//        public override void VisitMarkupTextLiteral(MarkupTextLiteralSyntax node)
//        {
//            if (_builder == null)
//            {
//                return;
//            }

//            if (node.ChunkGenerator == SpanChunkGenerator.Null)
//            {
//                return;
//            }

//            if (node.LiteralTokens is [{ Kind: SyntaxKind.Marker, Content.Length: 0 }])
//            {
//                // We don't want to create IR nodes for marker tokens.
//                return;
//            }

//            _builder.Add(IntermediateNodeFactory.HtmlToken(
//                arg: node,
//                contentFactory: static node => node.GetContent(),
//                source: BuildSourceSpanFromNode(node)));
//        }

//        public override void VisitCSharpExpressionLiteral(CSharpExpressionLiteralSyntax node)
//        {
//            if (_builder == null)
//            {
//                return;
//            }

//            if (_builder.Current is TagHelperHtmlAttributeIntermediateNode)
//            {
//                // If we are top level in a tag helper HTML attribute, we want to be rendered as markup.
//                // This case happens for duplicate non-string bound attributes. They would be initially be categorized as
//                // CSharp but since they are duplicate, they should just be markup.
//                var markupLiteral = SyntaxFactory.MarkupTextLiteral(node.LiteralTokens).Green.CreateRed(node.Parent, node.Position);
//                Visit(markupLiteral);
//                return;
//            }

//            _builder.Add(IntermediateNodeFactory.CSharpToken(
//                arg: node,
//                contentFactory: static node => node.GetContent(),
//                source: BuildSourceSpanFromNode(node)));

//            base.VisitCSharpExpressionLiteral(node);
//        }
//    }

//    private ref struct DirectiveAttributeName(string original)
//    {
//        // Directive attributes should start with '@' unless the descriptors are misconfigured.
//        // In that case, we would have already logged an error.
//        public readonly ReadOnlySpan<char> Span = original.StartsWith('@') ? original.AsSpan()[1..] : original;

//        public string Text => field ??= (Span.Length < original.Length ? Span.ToString() : original);

//        private bool? _hasParameter;

//        public bool HasParameter => _hasParameter ??= Span.IndexOf(':') >= 0;

//        public string TextWithoutParameter
//            => field ??= Span.IndexOf(':') is int index && index >= 0 ? Span[..index].ToString() : Text;
//    }
//}
