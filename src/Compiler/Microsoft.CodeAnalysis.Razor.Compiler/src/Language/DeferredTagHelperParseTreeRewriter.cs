// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.AspNetCore.Razor.Language.Syntax;
using Microsoft.AspNetCore.Razor.PooledObjects;

namespace Microsoft.AspNetCore.Razor.Language;

internal static class DeferredTagHelperParseTreeRewriter
{
    public static RazorSyntaxTree Rewrite(
        RazorSyntaxTree syntaxTree,
        TagHelperBinder binder,
        ImmutableArray<ElementOrTagHelperIntermediateNode> deferredNodes,
        TagHelperCollection.Builder usedDescriptors,
        CancellationToken cancellationToken = default)
    {
        using var errorSink = new ErrorSink();
        var rewriteCandidates = new Dictionary<MarkupElementSyntax, DeferredRewriteCandidate>(deferredNodes.Length);
        var nodesToRewrite = ComputeRewriteCandidates(
            deferredNodes,
            binder,
            syntaxTree.Source,
            usedDescriptors,
            errorSink,
            rewriteCandidates,
            cancellationToken);

        var rewrittenRoot = nodesToRewrite.Length == 0
            ? syntaxTree.Root
            : syntaxTree.Root.ReplaceNodes(
                nodesToRewrite,
                (original, rewritten) => RewriteDeferredElement(
                    (MarkupElementSyntax)rewritten,
                    rewriteCandidates[(MarkupElementSyntax)original],
                    syntaxTree.Options,
                    syntaxTree.Source,
                    errorSink));

        var treeDiagnostics = syntaxTree.Diagnostics;
        var sinkDiagnostics = errorSink.GetErrorsAndClear();
        using var diagnosticBuilder = new PooledArrayBuilder<RazorDiagnostic>(capacity: treeDiagnostics.Length + sinkDiagnostics.Length);

        diagnosticBuilder.AddRange(treeDiagnostics);
        diagnosticBuilder.AddRange(sinkDiagnostics);

        foreach (var descriptor in binder.TagHelpers)
        {
            descriptor.AppendAllDiagnostics(ref diagnosticBuilder.AsRef());
        }

        var diagnostics = diagnosticBuilder.ToImmutableOrderedBy(static d => d.Span.AbsoluteIndex);
        return new RazorSyntaxTree(rewrittenRoot, syntaxTree.Source, diagnostics, syntaxTree.Options);
    }

    private static ImmutableArray<MarkupElementSyntax> ComputeRewriteCandidates(
        ImmutableArray<ElementOrTagHelperIntermediateNode> deferredNodes,
        TagHelperBinder binder,
        RazorSourceDocument source,
        TagHelperCollection.Builder usedHelpers,
        ErrorSink errorSink,
        Dictionary<MarkupElementSyntax, DeferredRewriteCandidate> rewriteCandidates,
        CancellationToken cancellationToken)
    {
        using var nodesToRewrite = new PooledArrayBuilder<MarkupElementSyntax>();
        var nodeStates = new Dictionary<SyntaxNode, DeferredNodeState>(deferredNodes.Length);

        foreach (var deferredNode in deferredNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var syntaxNode = deferredNode.SyntaxNode;
            var parentContext = GetParentContext(syntaxNode, nodeStates);

            if (syntaxNode is MarkupTagHelperElementSyntax tagHelperElement)
            {
                usedHelpers.AddRange(tagHelperElement.TagHelperInfo.BindingResult.TagHelpers);
                nodeStates[syntaxNode] = new DeferredNodeState(
                    tagHelperElement.TagHelperInfo.TagName,
                    IsTagHelper: true,
                    TracksChildren: tagHelperElement.TagHelperInfo.TagMode == TagMode.StartTagAndEndTag);
                continue;
            }

            if (syntaxNode is not MarkupElementSyntax element)
            {
                continue;
            }

            if (TryCreateRewriteCandidate(
                element,
                parentContext,
                binder,
                source,
                usedHelpers,
                errorSink,
                out var rewriteCandidate))
            {
                rewriteCandidates[element] = rewriteCandidate;
                nodesToRewrite.Add(element);
                nodeStates[syntaxNode] = new DeferredNodeState(
                    rewriteCandidate.TagName,
                    IsTagHelper: true,
                    TracksChildren: rewriteCandidate.TracksChildren);
            }
            else
            {
                var startTag = element.StartTag;
                var tracksChildren = startTag is not null &&
                    (element.EndTag is not null || (!startTag.IsSelfClosing() && !startTag.IsVoidElement()));
                nodeStates[syntaxNode] = new DeferredNodeState(
                    deferredNode.TagName,
                    IsTagHelper: false,
                    TracksChildren: tracksChildren);
            }
        }

        return nodesToRewrite.ToImmutableAndClear();
    }

    private static ParentContext GetParentContext(SyntaxNode syntaxNode, Dictionary<SyntaxNode, DeferredNodeState> nodeStates)
    {
        for (var parent = syntaxNode.Parent; parent is not null; parent = parent.Parent)
        {
            if (nodeStates.TryGetValue(parent, out var state) && state.TracksChildren)
            {
                return new ParentContext(state.TagName, state.IsTagHelper);
            }
        }

        return default;
    }

    private static bool TryCreateRewriteCandidate(
        MarkupElementSyntax element,
        ParentContext parentContext,
        TagHelperBinder binder,
        RazorSourceDocument source,
        TagHelperCollection.Builder usedHelpers,
        ErrorSink errorSink,
        out DeferredRewriteCandidate candidate)
    {
        candidate = default;

        if (IsPartOfStartTag(element))
        {
            return false;
        }

        var startTag = element.StartTag;
        if (startTag is null)
        {
            return false;
        }

        var tagName = startTag.GetTagNameWithOptionalBang();
        if (string.IsNullOrEmpty(tagName) ||
            tagName.StartsWith("!", StringComparison.Ordinal) ||
            !IsPotentialTagHelperStart(tagName, startTag))
        {
            return false;
        }

        var attributes = TagHelperParseTreeRewriter.Rewriter.GetAttributeNameValuePairs(startTag);
        var binding = binder.GetBinding(tagName, attributes, parentContext.TagName, parentContext.IsTagHelper);
        if (binding is null)
        {
            return false;
        }

        usedHelpers.AddRange(binding.TagHelpers);

        var tagMode = TagHelperBlockRewriter.GetTagMode(startTag, element.EndTag, binding);
        if (tagMode == TagMode.StartTagAndEndTag && element.EndTag is null)
        {
            var sourceSpan = new SourceSpan(SourceLocationTracker.Advance(startTag.GetSourceLocation(source), "<"), tagName.Length);
            errorSink.OnError(
                startTag.IsVoidElement()
                    ? RazorDiagnosticFactory.CreateParsing_VoidElement(sourceSpan, tagName)
                    : RazorDiagnosticFactory.CreateParsing_TagHelperFoundMalformedTagHelper(sourceSpan, tagName));
        }

        candidate = new DeferredRewriteCandidate(
            tagName,
            binding,
            tagMode,
            TracksChildren: tagMode == TagMode.StartTagAndEndTag && element.EndTag is not null);
        return true;
    }

    private static SyntaxNode RewriteDeferredElement(
        MarkupElementSyntax rewrittenElement,
        DeferredRewriteCandidate candidate,
        RazorParserOptions options,
        RazorSourceDocument source,
        ErrorSink errorSink)
    {
        var rewrittenStartTag = TagHelperBlockRewriter.Rewrite(
            candidate.TagName,
            options,
            rewrittenElement.StartTag,
            candidate.Binding,
            errorSink,
            source);

        var tagHelperInfo = new TagHelperInfo(candidate.TagName, candidate.TagMode, candidate.Binding);
        if (candidate.TagMode is TagMode.SelfClosing or TagMode.StartTagOnly)
        {
            var tagHelperElement = SyntaxFactory.MarkupTagHelperElement(rewrittenStartTag, body: default, endTag: null, tagHelperInfo);
            if (rewrittenElement.Body.Count == 0 && rewrittenElement.EndTag is null)
            {
                return tagHelperElement;
            }

            using PooledArrayBuilder<RazorSyntaxNode> rewrittenBody = [];
            rewrittenBody.Add(tagHelperElement);
            rewrittenBody.AddRange(rewrittenElement.Body);
            return SyntaxFactory.MarkupElement(startTag: null, body: rewrittenBody.ToList(), endTag: rewrittenElement.EndTag);
        }

        var rewrittenEndTag = rewrittenElement.EndTag is null ? null : RewriteEndTag(rewrittenElement.EndTag);
        return SyntaxFactory.MarkupTagHelperElement(rewrittenStartTag, rewrittenElement.Body, rewrittenEndTag, tagHelperInfo);
    }

    private static MarkupTagHelperEndTagSyntax RewriteEndTag(MarkupEndTagSyntax endTag)
    {
        return SyntaxFactory.MarkupTagHelperEndTag(
            endTag.OpenAngle,
            endTag.ForwardSlash,
            endTag.Bang,
            endTag.Name,
            endTag.MiscAttributeContent,
            endTag.CloseAngle,
            chunkGenerator: null,
            editHandler: null);
    }

    private static bool IsPotentialTagHelperStart(string tagName, MarkupStartTagSyntax startTag)
        => !string.Equals(tagName, SyntaxConstants.TextTagName, StringComparison.OrdinalIgnoreCase) ||
           !startTag.IsMarkupTransition;

    private static bool IsPartOfStartTag(SyntaxNode node)
    {
        var parent = node.FirstAncestorOrSelf<SyntaxNode>(static n => n.Parent is MarkupElementSyntax element && element.StartTag == n);
        return parent is not null;
    }

    private readonly record struct ParentContext(string TagName, bool IsTagHelper);

    private readonly record struct DeferredNodeState(string TagName, bool IsTagHelper, bool TracksChildren);

    private readonly record struct DeferredRewriteCandidate(string TagName, TagHelperBinding Binding, TagMode TagMode, bool TracksChildren);
}
