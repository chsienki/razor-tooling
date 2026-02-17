// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Microsoft.AspNetCore.Razor.Language;

internal class DefaultRazorDeclCSharpLoweringPhase : RazorEnginePhaseBase, IRazorCSharpLoweringPhase
{
    protected override RazorCodeDocument ExecuteCore(RazorCodeDocument codeDocument, CancellationToken cancellationToken)
    {
        var documentNode = codeDocument.GetDocumentNode();
        ThrowForMissingDocumentDependency(documentNode);

        var target = documentNode.Target;
        if (target == null)
        {
            var message = Resources.FormatDocumentMissingTarget(
                documentNode.DocumentKind,
                nameof(CodeTarget),
                nameof(DocumentIntermediateNode.Target));
            throw new InvalidOperationException(message);
        }

        //PROTOTYPE: we need to only do this for components and when in the non-decl rendering
        if (codeDocument.FileKind == RazorFileKind.Component && codeDocument.CodeGenerationOptions.SuppressPrimaryMethodBody != true)
        {
            // find the render tree method and remove it from the primary class
            var renderMethod = documentNode.FindPrimaryMethod(); // BuildRenderTree()
            var primaryClass = documentNode.FindPrimaryClass();
            primaryClass!.Children.Remove(renderMethod); //TODO: error handling

            // use that to generate a doc without the render method
            var declDoc = DefaultRazorCSharpLoweringPhase.WriteDocument(documentNode, codeDocument, cancellationToken);

            // remove everything except the primary namespace and its usings, and the primary class with the render method
            var ns = documentNode.FindPrimaryNamespace();
            var usings = ns!.FindDescendantNodes<UsingDirectiveIntermediateNode>();

            primaryClass.Children.Clear();
            primaryClass.Children.Add(renderMethod);

            ns!.Children.Clear();
            ns!.Children.AddRange(usings);
            ns!.Children.Add(primaryClass);

            documentNode.Children.Clear();
            documentNode.Children.Add(ns);

            return codeDocument.WithDeclCSharpDocument(declDoc);
        }
        else
        {
            return codeDocument;
        }

    }
}
