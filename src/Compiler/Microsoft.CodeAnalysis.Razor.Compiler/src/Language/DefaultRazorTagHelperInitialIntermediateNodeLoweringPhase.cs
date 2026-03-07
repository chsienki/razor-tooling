// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.AspNetCore.Razor.Language.Syntax;

namespace Microsoft.AspNetCore.Razor.Language;

internal sealed class DefaultRazorTagHelperInitialIntermediateNodeLoweringPhase : RazorEnginePhaseBase
{
    protected override RazorCodeDocument ExecuteCore(RazorCodeDocument codeDocument, CancellationToken cancellationToken)
    {
        if (!codeDocument.ParserOptions.UseDeferredTagHelperLowering)
        {
            return codeDocument;
        }

        var syntaxTree = codeDocument.GetSyntaxTree();
        ThrowForMissingDocumentDependency(syntaxTree);

        var documentNode = new DocumentIntermediateNode()
        {
            Options = codeDocument.CodeGenerationOptions,
        };

        var visitor = new ElementOrTagHelperCaptureVisitor(documentNode, syntaxTree.Source, cancellationToken);
        visitor.Visit(syntaxTree.Root);

        return codeDocument.WithDocumentNode(documentNode);
    }

    private sealed class ElementOrTagHelperCaptureVisitor(
        DocumentIntermediateNode document,
        RazorSourceDocument source,
        CancellationToken cancellationToken) : SyntaxWalker
    {
        private readonly DocumentIntermediateNode _document = document;
        private readonly RazorSourceDocument _source = source;
        private readonly CancellationToken _cancellationToken = cancellationToken;

        public override void VisitMarkupElement(MarkupElementSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (CanBecomeTagHelper(node))
            {
                var tagName = node.StartTag?.GetTagNameWithOptionalBang() ??
                              node.EndTag?.GetTagNameWithOptionalBang() ??
                              string.Empty;

                _document.Children.Add(new ElementOrTagHelperIntermediateNode(node, tagName)
                {
                    Source = node.GetSourceSpan(_source),
                });
            }

            base.VisitMarkupElement(node);
        }

        public override void VisitMarkupTagHelperElement(MarkupTagHelperElementSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            _document.Children.Add(new ElementOrTagHelperIntermediateNode(node, node.TagHelperInfo.TagName)
            {
                Source = node.GetSourceSpan(_source),
            });

            base.VisitMarkupTagHelperElement(node);
        }

        private static bool CanBecomeTagHelper(MarkupElementSyntax node)
        {
            if (node.StartTag is { } startTag)
            {
                var startName = startTag.GetTagNameWithOptionalBang();
                if (!string.IsNullOrEmpty(startName) &&
                    !startName.StartsWith("!", StringComparison.Ordinal) &&
                    IsPotentialTagHelperStart(startName, startTag))
                {
                    return true;
                }
            }

            if (node.EndTag is { } endTag)
            {
                var endName = endTag.GetTagNameWithOptionalBang();
                if (!string.IsNullOrEmpty(endName) &&
                    !endName.StartsWith("!", StringComparison.Ordinal) &&
                    IsPotentialTagHelperEnd(endName, endTag))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPotentialTagHelperStart(string tagName, MarkupStartTagSyntax startTag)
            => !string.Equals(tagName, SyntaxConstants.TextTagName, StringComparison.OrdinalIgnoreCase) ||
               !startTag.IsMarkupTransition;

        private static bool IsPotentialTagHelperEnd(string tagName, MarkupEndTagSyntax endTag)
            => !string.Equals(tagName, SyntaxConstants.TextTagName, StringComparison.OrdinalIgnoreCase) ||
               !endTag.IsMarkupTransition;
    }
}

internal sealed class ElementOrTagHelperIntermediateNode(SyntaxNode syntaxNode, string tagName) : ExtensionIntermediateNode
{
    public SyntaxNode SyntaxNode { get; } = syntaxNode;

    public string TagName { get; } = tagName;

    public override IntermediateNodeCollection Children => IntermediateNodeCollection.ReadOnly;

    public override void Accept(IntermediateNodeVisitor visitor)
        => AcceptExtensionNode(this, visitor);

    public override void WriteNode(CodeTarget target, CodeRenderingContext context)
        => throw new InvalidOperationException("Deferred ElementOrTagHelper nodes must be lowered before code generation.");

    public override void FormatNode(IntermediateNodeFormatter formatter)
    {
        formatter.WriteContent(TagName);
        formatter.WriteProperty(nameof(TagName), TagName);
    }
}
