// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;

namespace Microsoft.AspNetCore.Razor.Language;

internal sealed class DefaultRazorTagHelperRewritePhase : RazorEnginePhaseBase
{
    protected override RazorCodeDocument ExecuteCore(RazorCodeDocument codeDocument, CancellationToken cancellationToken)
    {
        var syntaxTree = codeDocument.GetSyntaxTree();
        ThrowForMissingDocumentDependency(syntaxTree);

        var documentNode = codeDocument.GetRequiredDocumentNode();
        ThrowForMissingDocumentDependency(documentNode);

        if (!codeDocument.TryGetTagHelperContext(out var context) ||
            context.TagHelpers is [])
        {
            // No descriptors, so no need to see if any are used. Without setting this though,
            // we trigger an Assert in the ProcessRemaining method in the source generator.
            return codeDocument.WithReferencedTagHelpers([]);
        }

        var binder = context.GetBinder();
        using var usedHelpers = new TagHelperCollection.Builder();

        // Rewrite the intermediate document tree instead of the syntax tree
        TagHelperIntermediateNodeRewriter.Rewrite(
            documentNode,
            syntaxTree.Source,
            binder,
            syntaxTree.Options,
            usedHelpers,
            cancellationToken);

        return codeDocument.WithReferencedTagHelpers(usedHelpers.ToCollection());
    }
}
