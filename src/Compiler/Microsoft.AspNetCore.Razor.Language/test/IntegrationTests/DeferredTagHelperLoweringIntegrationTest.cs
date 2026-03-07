// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.AspNetCore.Razor.PooledObjects;
using Microsoft.AspNetCore.Razor.Test.Common;
using Microsoft.CodeAnalysis.Razor;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.AspNetCore.Razor.Language.IntegrationTests;

public class DeferredTagHelperLoweringIntegrationTest : IntegrationTestBase
{
    private const string DeferredTagHelperWorkItem = "https://github.com/dotnet/razor/issues/12261";

    private readonly bool _designTime;

    public DeferredTagHelperLoweringIntegrationTest(bool designTime = false)
        : base(layer: TestProject.Layer.Compiler)
    {
        _designTime = designTime;

        AddCSharpSyntaxTree(
            """
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Web;

            namespace Test;

            public class MyComponent : ComponentBase
            {
                [Parameter] public string Value { get; set; }
                [Parameter] public EventCallback<string> ValueChanged { get; set; }
                [Parameter] public RenderFragment ChildContent { get; set; }
            }
            """,
            filePath: "MyComponent.cs");
    }

    [IntegrationTestFact, WorkItem(DeferredTagHelperWorkItem)]
    public void Legacy_SimpleTagHelpers_MatchesBaseline()
        => AssertLegacyParity("SimpleTagHelpers", TestTagHelperDescriptors.SimpleTagHelperDescriptors);

    [IntegrationTestFact, WorkItem(DeferredTagHelperWorkItem)]
    public void Legacy_IncompleteTagHelper_MatchesBaseline()
        => AssertLegacyParity("IncompleteTagHelper", TestTagHelperDescriptors.DefaultPAndInputTagHelperDescriptors);

    [IntegrationTestFact, WorkItem(DeferredTagHelperWorkItem)]
    public void Legacy_EmptyAttributeTagHelpers_MatchesBaseline()
        => AssertLegacyParity("EmptyAttributeTagHelpers", TestTagHelperDescriptors.DefaultPAndInputTagHelperDescriptors);

    [IntegrationTestFact, WorkItem(DeferredTagHelperWorkItem)]
    public void Component_BindAndComponent_MatchesBaseline()
    {
        AssertComponentParity(
            scenarioName: nameof(Component_BindAndComponent_MatchesBaseline),
            sourceText:
            """
            <input @bind="Value" />
            <MyComponent @bind-Value="Value" />

            @code {
                private string Value { get; set; } = "hello";
            }
            """);
    }

    public override string GetTestFileName(string? testName = null)
        => $"TestFiles/IntegrationTests/CodeGenerationIntegrationTest/{testName}_{(_designTime ? "DesignTime" : "Runtime")}";

    private void AssertLegacyParity(string scenarioName, TagHelperCollection tagHelpers)
    {
        var baseline = GenerateLegacyOutput(scenarioName, tagHelpers, useDeferredTagHelperLowering: false);
        var deferred = GenerateLegacyOutput(scenarioName, tagHelpers, useDeferredTagHelperLowering: true);
        AssertGeneratedOutputEquivalent(baseline, deferred);
    }

    private void AssertComponentParity(string scenarioName, string sourceText)
    {
        var baseline = GenerateComponentOutput(scenarioName, sourceText, useDeferredTagHelperLowering: false);
        var deferred = GenerateComponentOutput(scenarioName, sourceText, useDeferredTagHelperLowering: true);
        AssertGeneratedOutputEquivalent(baseline, deferred);
    }

    private GeneratedOutput GenerateLegacyOutput(string scenarioName, TagHelperCollection tagHelpers, bool useDeferredTagHelperLowering)
    {
        var projectEngine = CreateProjectEngine(builder =>
        {
            RazorExtensions.Register(builder);
            builder.ConfigureParserOptions(options => options.UseDeferredTagHelperLowering = useDeferredTagHelperLowering);
        });

        var projectItem = CreateProjectItemFromFile(testName: scenarioName);
        var imports = GetImports(projectEngine, projectItem);
        var source = RazorSourceDocument.ReadFrom(projectItem);

        var codeDocument = _designTime
            ? projectEngine.ProcessDesignTime(source, RazorFileKind.Legacy, imports, tagHelpers)
            : projectEngine.Process(source, RazorFileKind.Legacy, imports, tagHelpers);

        return ToGeneratedOutput(codeDocument);
    }

    private GeneratedOutput GenerateComponentOutput(string scenarioName, string sourceText, bool useDeferredTagHelperLowering)
    {
        var projectEngine = CreateProjectEngine(builder =>
        {
            CompilerFeatures.Register(builder);
            builder.ConfigureParserOptions(options => options.UseDeferredTagHelperLowering = useDeferredTagHelperLowering);
        });

        var projectItem = AddProjectItemFromText(sourceText, filePath: $"{scenarioName}.razor", testName: scenarioName);
        var imports = GetImports(projectEngine, projectItem);
        var source = RazorSourceDocument.ReadFrom(projectItem);

        var codeDocument = _designTime
            ? projectEngine.ProcessDesignTime(source, RazorFileKind.Component, imports, tagHelpers: null)
            : projectEngine.Process(source, RazorFileKind.Component, imports, tagHelpers: null);

        return ToGeneratedOutput(codeDocument);
    }

    private static GeneratedOutput ToGeneratedOutput(RazorCodeDocument codeDocument)
    {
        var csharpDocument = codeDocument.GetRequiredCSharpDocument();
        return new GeneratedOutput(
            CSharp: csharpDocument.Text.ToString(),
            Diagnostics: string.Join("\n", csharpDocument.Diagnostics.Select(RazorDiagnosticSerializer.Serialize)));
    }

    private static void AssertGeneratedOutputEquivalent(GeneratedOutput baseline, GeneratedOutput deferred)
    {
        Assert.Equal(baseline.CSharp, deferred.CSharp);
        Assert.Equal(baseline.Diagnostics, deferred.Diagnostics);
    }

    private static ImmutableArray<RazorSourceDocument> GetImports(RazorProjectEngine projectEngine, RazorProjectItem projectItem)
    {
        using var result = new PooledArrayBuilder<RazorSourceDocument>();
        foreach (var import in projectEngine.GetImports(projectItem, static item => item.Exists))
        {
            result.Add(RazorSourceDocument.ReadFrom(import));
        }

        return result.ToImmutable();
    }

    private readonly record struct GeneratedOutput(string CSharp, string Diagnostics);
}
