// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.AspNetCore.Razor.Language.IntegrationTests;
using Xunit;

namespace Microsoft.AspNetCore.Razor.Language;

public class TagHelperIntermediateNodeRewritePhaseTest : RazorProjectEngineTestBase
{
    protected override RazorLanguageVersion Version => RazorLanguageVersion.Latest;

    [Fact]
    public void TagHelperRewrite_Component_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "span",
            typeName: "SpanTagHelper",
            assemblyName: "TestAssembly");

        var content = "<span val=\"@Hello World\"></span>";

        // Act - Pipeline A: current pipeline (with syntax tree tag helper rewrite)
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [tagHelper]);

        // Act - Pipeline B: new pipeline (skip rewrite, lower, then IR rewrite phase)
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "span",
            typeName: "SpanTagHelper",
            assemblyName: "TestAssembly");

        var content = @"@addTagHelper *, TestAssembly
<span val=""@Hello World""></span>";

        // Act - Pipeline A: current pipeline (with syntax tree tag helper rewrite)
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Act - Pipeline B: new pipeline (skip rewrite, lower, then IR rewrite phase)
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);
    }

    [Fact]
    public void TagHelperRewrite_Component_WithBoundAttribute_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly",
            attributes: [builder => builder
                .Name("bound")
                .PropertyName("FooProp")
                .TypeName("System.String")]);

        var content = "<input bound='foo' />";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_WithBoundAttribute_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly",
            attributes: [builder => builder
                .Name("bound")
                .PropertyName("FooProp")
                .TypeName("System.String")]);

        var content = @"@addTagHelper *, TestAssembly
<input bound='foo' />";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);
    }

    private string RunCurrentPipeline(string content, RazorFileKind fileKind, TagHelperCollection tagHelpers)
    {
        // Standard pipeline: Parse → Discovery → Rewrite → Lower
        var codeDocument = ProjectEngine.CreateCodeDocument(content, fileKind, tagHelpers);
        codeDocument = ProjectEngine.ExecutePhasesThrough<IRazorIntermediateNodeLoweringPhase>(codeDocument);

        var documentNode = codeDocument.GetDocumentNode();
        Assert.NotNull(documentNode);

        return IntermediateNodeSerializer.Serialize(documentNode);
    }

    private string RunNewPipeline(string content, RazorFileKind fileKind, TagHelperCollection tagHelpers)
    {
        // Modified pipeline: Parse → Discovery → (skip Rewrite) → Modified Lower → IR Rewrite Phase
        var noRewriteEngine = CreateProjectEngine(builder =>
        {
            // Remove the tag helper rewrite phase
            for (var i = builder.Phases.Count - 1; i >= 0; i--)
            {
                if (builder.Phases[i] is DefaultRazorTagHelperRewritePhase)
                {
                    builder.Phases.RemoveAt(i);
                }
            }

            // Replace the standard lowering phase with our test version
            for (var i = 0; i < builder.Phases.Count; i++)
            {
                if (builder.Phases[i] is IRazorIntermediateNodeLoweringPhase)
                {
                    builder.Phases[i] = new TestRazorIntermediateNodeLoweringPhase();
                    break;
                }
            }
        });

        var codeDocument = noRewriteEngine.CreateCodeDocument(content, fileKind, tagHelpers);
        codeDocument = noRewriteEngine.ExecutePhasesThrough<IRazorIntermediateNodeLoweringPhase>(codeDocument);

        // Now run the new IR rewrite phase
        var rewritePhase = new TagHelperIntermediateNodeRewritePhase();
        rewritePhase.Initialize(noRewriteEngine.Engine);
        codeDocument = rewritePhase.Execute(codeDocument);

        var documentNode = codeDocument.GetDocumentNode();
        Assert.NotNull(documentNode);

        return IntermediateNodeSerializer.Serialize(documentNode);
    }

    private static TagHelperDescriptor CreateTagHelperDescriptor(
        string tagName,
        string typeName,
        string assemblyName,
        params ReadOnlySpan<Action<BoundAttributeDescriptorBuilder>> attributes)
    {
        var builder = TagHelperDescriptorBuilder.CreateTagHelper(typeName, assemblyName);
        builder.SetTypeName(typeName, typeNamespace: null, typeNameIdentifier: null);

        foreach (var attributeBuilder in attributes)
        {
            builder.BoundAttributeDescriptor(attributeBuilder);
        }

        builder.TagMatchingRuleDescriptor(ruleBuilder => ruleBuilder.RequireTagName(tagName));

        return builder.Build();
    }
}
