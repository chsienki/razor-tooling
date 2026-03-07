// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.AspNetCore.Razor.Language.Syntax;

namespace Microsoft.AspNetCore.Razor.Language;

internal sealed class DefaultRazorTagHelperRewritePhase : RazorEnginePhaseBase
{
    protected override RazorCodeDocument ExecuteCore(RazorCodeDocument codeDocument, CancellationToken cancellationToken)
    {
        if (!codeDocument.TryGetPreTagHelperSyntaxTree(out var syntaxTree) ||
            !codeDocument.TryGetTagHelperContext(out var context) ||
            context.TagHelpers is [])
        {
            // No descriptors, so no need to see if any are used. Without setting this though,
            // we trigger an Assert in the ProcessRemaining method in the source generator.
            return codeDocument.WithReferencedTagHelpers([]);
        }

        var binder = context.GetBinder();
        using var usedHelpers = new TagHelperCollection.Builder();
        RazorSyntaxTree rewrittenSyntaxTree;

        if (codeDocument.ParserOptions.UseDeferredTagHelperLowering &&
            syntaxTree.Diagnostics.IsEmpty &&
            codeDocument.TryGetDocumentNode(out var documentNode) &&
            documentNode is not null)
        {
            var deferredNodes = documentNode.FindDescendantNodes<ElementOrTagHelperIntermediateNode>();
            rewrittenSyntaxTree = !deferredNodes.IsDefaultOrEmpty && CanUseDeferredRewrite(deferredNodes)
                ? DeferredTagHelperParseTreeRewriter.Rewrite(syntaxTree, binder, deferredNodes, usedHelpers, cancellationToken)
                : TagHelperParseTreeRewriter.Rewrite(syntaxTree, binder, usedHelpers, cancellationToken);
        }
        else
        {
            rewrittenSyntaxTree = TagHelperParseTreeRewriter.Rewrite(syntaxTree, binder, usedHelpers, cancellationToken);
        }

        return codeDocument
            .WithReferencedTagHelpers(usedHelpers.ToCollection())
            .WithSyntaxTree(rewrittenSyntaxTree);
    }

    private static bool CanUseDeferredRewrite(ImmutableArray<ElementOrTagHelperIntermediateNode> deferredNodes)
    {
        foreach (var deferredNode in deferredNodes)
        {
            var syntaxNode = deferredNode.SyntaxNode;
            if (syntaxNode is MarkupElementSyntax element)
            {
                var startTag = element.StartTag;
                var endTag = element.EndTag;

                if ((startTag is not null && startTag.CloseAngle.IsMissing) ||
                    (endTag is not null && endTag.CloseAngle.IsMissing))
                {
                    return false;
                }

                if (startTag is not null && HasMalformedAttributes(startTag))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasMalformedAttributes(MarkupStartTagSyntax startTag)
    {
        foreach (var attribute in startTag.Attributes)
        {
            if (attribute is CSharpCodeBlockSyntax)
            {
                return true;
            }

            if (attribute is MarkupAttributeBlockSyntax { Name: null })
            {
                return true;
            }

            if (attribute is MarkupMinimizedAttributeBlockSyntax { Name: null })
            {
                return true;
            }

            if (attribute is MarkupAttributeBlockSyntax attributeBlock &&
                attributeBlock.ValuePrefix is null &&
                attributeBlock.ValueSuffix is null &&
                attributeBlock.Value is { } value &&
                IsLikelyMalformedUnquotedAttributeValue(value.GetContent()))
            {
                return true;
            }

            if (attribute is MarkupMiscAttributeContentSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLikelyMalformedUnquotedAttributeValue(string valueContent)
        => valueContent.IndexOfAny(['=', '"', '\'']) >= 0;
}
