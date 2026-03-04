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

        // Assert - IR
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C#
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
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

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
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

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
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

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_NonTagHelperElementsAlongsideTagHelpers_ProducesIdenticalIR()
    {
        // Arrange - only <input> is a tag helper, <p> and <form> are not
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly");

        var content = @"@addTagHelper *, TestAssembly
<p>Hola</p>
<form>
    <input value='Hello' type='text' />
</form>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_NonTagHelperElementsWithAttributes_ProducesIdenticalIR()
    {
        // Arrange - only <input> is a tag helper; <div> with attributes is not
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly");

        var content = @"@addTagHelper *, TestAssembly
<div class=""container"" id=""main"">
    <input value='Hello' />
</div>
<span style=""color:red"">text</span>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_NestedTagHelpers_ProducesIdenticalIR()
    {
        // Arrange - nested tag helpers: p, form, and input with bound attribute
        var tagHelpers = TagHelperCollection.Create([
            CreateTagHelperDescriptor(tagName: "p", typeName: "PTagHelper", assemblyName: "TestAssembly"),
            CreateTagHelperDescriptor(tagName: "form", typeName: "FormTagHelper", assemblyName: "TestAssembly"),
            CreateTagHelperDescriptor(
                tagName: "input",
                typeName: "InputTagHelper",
                assemblyName: "TestAssembly",
                attributes: [builder => builder
                    .Name("value")
                    .PropertyName("FooProp")
                    .TypeName("System.String")])]);

        var content = @"@addTagHelper *, TestAssembly
<p someattr>Hola</p>
<form unbound=""foo"">
    <input value=Hello type='text' />
</form>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, tagHelpers);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, tagHelpers);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, tagHelpers);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, tagHelpers);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_NestedTagHelpers_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelpers = TagHelperCollection.Create([
            CreateTagHelperDescriptor(tagName: "div", typeName: "DivTagHelper", assemblyName: "TestAssembly"),
            CreateTagHelperDescriptor(tagName: "span", typeName: "SpanTagHelper", assemblyName: "TestAssembly")]);

        var content = "<div><span>Hello</span></div>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, tagHelpers);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, tagHelpers);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, tagHelpers);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, tagHelpers);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_MultipleBoundAttributes_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly",
            attributes: [
                builder => builder.Name("value").PropertyName("ValueProp").TypeName("System.String"),
                builder => builder.Name("type").PropertyName("TypeProp").TypeName("System.String")]);

        var content = @"@addTagHelper *, TestAssembly
<input value='Hello' type='text' />";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_MultipleBoundAttributes_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly",
            attributes: [
                builder => builder.Name("value").PropertyName("ValueProp").TypeName("System.String"),
                builder => builder.Name("type").PropertyName("TypeProp").TypeName("System.String")]);

        var content = "<input value='Hello' type='text' />";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_ExpressionAttribute_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "span",
            typeName: "SpanTagHelper",
            assemblyName: "TestAssembly",
            attributes: [builder => builder
                .Name("val")
                .PropertyName("ValProp")
                .TypeName("System.String")]);

        var content = @"@addTagHelper *, TestAssembly
<span val=""@Hello""></span>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_ExpressionAttribute_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "span",
            typeName: "SpanTagHelper",
            assemblyName: "TestAssembly",
            attributes: [builder => builder
                .Name("val")
                .PropertyName("ValProp")
                .TypeName("System.String")]);

        var content = "<span val=\"@Hello\"></span>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_MinimizedAttribute_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly",
            attributes: [builder => builder
                .Name("disabled")
                .PropertyName("DisabledProp")
                .TypeName("System.Boolean")]);

        var content = @"@addTagHelper *, TestAssembly
<input disabled />";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_MinimizedAttribute_ProducesIdenticalIR()
    {
        // Arrange
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly",
            attributes: [builder => builder
                .Name("disabled")
                .PropertyName("DisabledProp")
                .TypeName("System.Boolean")]);

        var content = "<input disabled />";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_VoidElement_ProducesIdenticalIR()
    {
        // Arrange - void element <input> without self-closing slash
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly");

        var content = @"@addTagHelper *, TestAssembly
<input value='Hello'>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_TagHelperPrefix_ProducesIdenticalIR()
    {
        // Arrange - tag helper prefix
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly");

        var content = @"@addTagHelper *, TestAssembly
@tagHelperPrefix th:
<th:input value='Hello' />";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
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

    private string RunCurrentPipelineCSharp(string content, RazorFileKind fileKind, TagHelperCollection tagHelpers)
    {
        // Standard pipeline: run all phases through C# lowering
        var codeDocument = ProjectEngine.CreateCodeDocument(content, fileKind, tagHelpers);
        codeDocument = ProjectEngine.ExecutePhasesThrough<IRazorCSharpLoweringPhase>(codeDocument);

        return codeDocument.GetRequiredCSharpDocument().Text.ToString();
    }

    private RazorProjectEngine CreateNewPipelineEngine()
    {
        return CreateProjectEngine(builder =>
        {
            // Remove the tag helper rewrite phase (syntax tree rewrite)
            for (var i = builder.Phases.Count - 1; i >= 0; i--)
            {
                if (builder.Phases[i] is DefaultRazorTagHelperRewritePhase)
                {
                    builder.Phases.RemoveAt(i);
                }
            }

            // Find and save the discovery phase, then remove it
            IRazorEnginePhase discoveryPhase = null;
            for (var i = builder.Phases.Count - 1; i >= 0; i--)
            {
                if (builder.Phases[i] is DefaultRazorTagHelperContextDiscoveryPhase)
                {
                    discoveryPhase = builder.Phases[i];
                    builder.Phases.RemoveAt(i);
                    break;
                }
            }

            // Replace the standard lowering phase with our test version
            for (var i = 0; i < builder.Phases.Count; i++)
            {
                if (builder.Phases[i] is IRazorIntermediateNodeLoweringPhase)
                {
                    builder.Phases[i] = new TestRazorIntermediateNodeLoweringPhase();

                    // Insert discovery phase right after lowering, then IR rewrite phase after that
                    if (discoveryPhase != null)
                    {
                        builder.Phases.Insert(i + 1, discoveryPhase);
                        builder.Phases.Insert(i + 2, new TagHelperIntermediateNodeRewritePhase());
                    }

                    break;
                }
            }
        });
    }

    private string RunNewPipeline(string content, RazorFileKind fileKind, TagHelperCollection tagHelpers)
    {
        // Modified pipeline: Parse → SyntaxTree → Modified Lower → Discovery → IR Rewrite Phase
        var noRewriteEngine = CreateNewPipelineEngine();

        var codeDocument = noRewriteEngine.CreateCodeDocument(content, fileKind, tagHelpers);
        codeDocument = noRewriteEngine.ExecutePhasesThrough<TagHelperIntermediateNodeRewritePhase>(codeDocument);

        var documentNode = codeDocument.GetDocumentNode();
        Assert.NotNull(documentNode);

        return IntermediateNodeSerializer.Serialize(documentNode);
    }

    private string RunNewPipelineCSharp(string content, RazorFileKind fileKind, TagHelperCollection tagHelpers)
    {
        // Modified pipeline: run all phases through C# lowering
        var noRewriteEngine = CreateNewPipelineEngine();

        var codeDocument = noRewriteEngine.CreateCodeDocument(content, fileKind, tagHelpers);
        codeDocument = noRewriteEngine.ExecutePhasesThrough<IRazorCSharpLoweringPhase>(codeDocument);

        return codeDocument.GetRequiredCSharpDocument().Text.ToString();
    }

    [Fact]
    public void TagHelperRewrite_Component_DirectiveAttribute_Ref_ProducesIdenticalIR()
    {
        // Arrange - component with @ref directive attribute
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly");
        var refTagHelper = CreateRefTagHelper();

        var content = """<MyComponent @ref="myRef" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper, refTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper, refTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, refTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, refTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_DirectiveAttribute_Key_ProducesIdenticalIR()
    {
        // Arrange - component with @key directive attribute
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly");
        var keyTagHelper = CreateKeyTagHelper();

        var content = """<MyComponent @key="myKey" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper, keyTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper, keyTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, keyTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, keyTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_DirectiveAttribute_EventHandler_ProducesIdenticalIR()
    {
        // Arrange - component with @onclick event handler directive attribute
        var componentTagHelper = CreateComponentTagHelper("button", "ButtonTagHelper", "TestAssembly");
        var onclickTagHelper = CreateEventHandlerTagHelper("onclick");

        var content = """<button @onclick="HandleClick">Click</button>""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper, onclickTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper, onclickTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, onclickTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, onclickTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_SplatAttribute_ProducesIdenticalIR()
    {
        // Arrange - component with @attributes splat
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly");
        var splatTagHelper = CreateSplatTagHelper();

        var content = """<MyComponent @attributes="myDict" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper, splatTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper, splatTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, splatTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, splatTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_DirectiveAttribute_RefAndKey_ProducesIdenticalIR()
    {
        // Arrange - component with both @ref and @key
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly",
            b => { b.Name = "Value"; b.PropertyName = "Value"; b.TypeName = typeof(string).FullName!; });
        var refTagHelper = CreateRefTagHelper();
        var keyTagHelper = CreateKeyTagHelper();

        var content = """<MyComponent Value="test" @ref="myRef" @key="myKey" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component,
            [componentTagHelper, refTagHelper, keyTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component,
            [componentTagHelper, refTagHelper, keyTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, refTagHelper, keyTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, refTagHelper, keyTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_MinimizedDirectiveAttribute_ProducesIdenticalIR()
    {
        // Arrange - minimized directive attribute (e.g., @onclick:preventDefault)
        var componentTagHelper = CreateComponentTagHelper("button", "ButtonTagHelper", "TestAssembly");
        var onclickTagHelper = CreateEventHandlerTagHelper("onclick");

        var content = """<button @onclick="HandleClick" @onclick:preventDefault>Click</button>""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper, onclickTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper, onclickTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, onclickTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, onclickTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    // ================================================================
    // Additional SxS tests targeting scenarios that failed during
    // production pipeline merge (component pipeline, code blocks,
    // TagStructure, @bind, child content, etc.)
    // ================================================================

    [Fact]
    public void TagHelperRewrite_Component_ExpressionBoundAttribute_ProducesIdenticalIR()
    {
        // Arrange - C# expression in bound attribute (CopyAttributeValueChildren mismatch scenario)
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly",
            b => { b.Name = "Value"; b.PropertyName = "Value"; b.TypeName = typeof(string).FullName!; });

        var content = """<MyComponent Value="@someExpression" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_MultipleExpressionBoundAttributes_ProducesIdenticalIR()
    {
        // Arrange - multiple C# expressions in bound attributes
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly",
            b => { b.Name = "Title"; b.PropertyName = "Title"; b.TypeName = typeof(string).FullName!; },
            b => { b.Name = "Count"; b.PropertyName = "Count"; b.TypeName = typeof(int).FullName!; });

        var content = """<MyComponent Title="@title" Count="@count" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_MixedBoundAndUnboundAttributes_ProducesIdenticalIR()
    {
        // Arrange - mix of bound and unbound attributes on a component
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly",
            b => { b.Name = "Value"; b.PropertyName = "Value"; b.TypeName = typeof(string).FullName!; });

        var content = """<MyComponent Value="@expr" class="my-class" id="comp1" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_ChildContent_ProducesIdenticalIR()
    {
        // Arrange - component with text/HTML child content
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly");

        var content = """<MyComponent>Some <strong>child</strong> content</MyComponent>""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_NestedComponents_ProducesIdenticalIR()
    {
        // Arrange - nested component inside component
        var outerComponent = CreateComponentTagHelper("Outer", "Outer", "TestAssembly");
        var innerComponent = CreateComponentTagHelper("Inner", "Inner", "TestAssembly",
            b => { b.Name = "Value"; b.PropertyName = "Value"; b.TypeName = typeof(string).FullName!; });

        var content = """<Outer><Inner Value="test" /></Outer>""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [outerComponent, innerComponent]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [outerComponent, innerComponent]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [outerComponent, innerComponent]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [outerComponent, innerComponent]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_PlainHtmlAlongsideComponents_ProducesIdenticalIR()
    {
        // Arrange - plain HTML elements alongside components in a .razor file
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly");

        var content = """
            <div class="container">
                <h1>Title</h1>
                <MyComponent />
                <p>Some text</p>
            </div>
            """;

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_CodeBlockWithTagHelper_ProducesIdenticalIR()
    {
        // Arrange - component inside @if code block
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly",
            b => { b.Name = "Value"; b.PropertyName = "Value"; b.TypeName = typeof(string).FullName!; });

        var content = """
            @if (true)
            {
                <MyComponent Value="test" />
            }
            """;

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_CodeBlockWithTagHelper_ProducesIdenticalIR()
    {
        // Arrange - legacy tag helper inside @if code block
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "span",
            typeName: "SpanTagHelper",
            assemblyName: "TestAssembly",
            attributes: [builder => builder
                .Name("val")
                .PropertyName("ValProp")
                .TypeName("System.String")]);

        var content = @"@addTagHelper *, TestAssembly
@if (true)
{
    <span val=""test"">content</span>
}";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_DynamicContent_ProducesIdenticalIR()
    {
        // Arrange - tag helper with dynamic C# expression in body
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "span",
            typeName: "SpanTagHelper",
            assemblyName: "TestAssembly");

        var content = @"@addTagHelper *, TestAssembly
<span>Hello @DateTime.Now World</span>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_TagStructureWithoutEndTag_ProducesIdenticalIR()
    {
        // Arrange - tag helper with TagStructure.WithoutEndTag
        var tagHelper = CreateTagHelperDescriptorWithTagStructure(
            tagName: "input",
            typeName: "InputTagHelper",
            assemblyName: "TestAssembly",
            tagStructure: TagStructure.WithoutEndTag,
            attributes: [builder => builder
                .Name("value")
                .PropertyName("ValueProp")
                .TypeName("System.String")]);

        var content = @"@addTagHelper *, TestAssembly
<input value='Hello'>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_MultipleDistinctTagHelpers_ProducesIdenticalIR()
    {
        // Arrange - multiple different tag helpers in one document
        var tagHelpers = TagHelperCollection.Create([
            CreateTagHelperDescriptor(tagName: "span", typeName: "SpanTagHelper", assemblyName: "TestAssembly"),
            CreateTagHelperDescriptor(
                tagName: "input",
                typeName: "InputTagHelper",
                assemblyName: "TestAssembly",
                attributes: [builder => builder
                    .Name("value")
                    .PropertyName("ValueProp")
                    .TypeName("System.String")]),
            CreateTagHelperDescriptor(tagName: "div", typeName: "DivTagHelper", assemblyName: "TestAssembly")]);

        var content = @"@addTagHelper *, TestAssembly
<div>
    <span>text</span>
    <input value='Hello' />
</div>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, tagHelpers);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, tagHelpers);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, tagHelpers);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, tagHelpers);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_BindDirective_ProducesIdenticalIR()
    {
        // Arrange - @bind directive attribute on a component element
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly",
            b => { b.Name = "Value"; b.PropertyName = "Value"; b.TypeName = typeof(string).FullName!; },
            b => { b.Name = "ValueChanged"; b.PropertyName = "ValueChanged"; b.TypeName = "Microsoft.AspNetCore.Components.EventCallback<System.String>"; });
        var bindTagHelper = CreateBindTagHelper("Value", "ValueChanged");

        var content = """<MyComponent @bind-Value="myField" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper, bindTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper, bindTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, bindTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper, bindTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_EventCallback_ProducesIdenticalIR()
    {
        // Arrange - component with EventCallback bound property
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly",
            b => { b.Name = "OnClick"; b.PropertyName = "OnClick"; b.TypeName = "Microsoft.AspNetCore.Components.EventCallback"; });

        var content = """<MyComponent OnClick="@HandleClick" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_LiteralAndExpressionAttributes_ProducesIdenticalIR()
    {
        // Arrange - mix of literal string and C# expression in bound attributes
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly",
            b => { b.Name = "Title"; b.PropertyName = "Title"; b.TypeName = typeof(string).FullName!; },
            b => { b.Name = "Count"; b.PropertyName = "Count"; b.TypeName = typeof(int).FullName!; });

        var content = """<MyComponent Title="Hello" Count="@myCount" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Legacy_MixedBoundUnboundWithExpressions_ProducesIdenticalIR()
    {
        // Arrange - legacy tag helper with bound + unbound attrs and expressions
        var tagHelper = CreateTagHelperDescriptor(
            tagName: "span",
            typeName: "SpanTagHelper",
            assemblyName: "TestAssembly",
            attributes: [builder => builder
                .Name("val")
                .PropertyName("ValProp")
                .TypeName("System.String")]);

        var content = @"@addTagHelper *, TestAssembly
<span val=""@expr"" class=""@cssClass"" data-id=""static"">@bodyExpr</span>";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Legacy, [tagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Legacy, [tagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Legacy, [tagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_DirectiveAttribute_BindAndEvent_ProducesIdenticalIR()
    {
        // Arrange - component with both @bind and @onclick directive attributes
        var componentTagHelper = CreateComponentTagHelper("MyInput", "MyInput", "TestAssembly",
            b => { b.Name = "Value"; b.PropertyName = "Value"; b.TypeName = typeof(string).FullName!; },
            b => { b.Name = "ValueChanged"; b.PropertyName = "ValueChanged"; b.TypeName = "Microsoft.AspNetCore.Components.EventCallback<System.String>"; });
        var bindTagHelper = CreateBindTagHelper("Value", "ValueChanged");
        var onclickTagHelper = CreateEventHandlerTagHelper("click");

        var content = """<MyInput @bind-Value="myField" @onclick="HandleClick" />""";

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component,
            [componentTagHelper, bindTagHelper, onclickTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component,
            [componentTagHelper, bindTagHelper, onclickTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component,
            [componentTagHelper, bindTagHelper, onclickTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component,
            [componentTagHelper, bindTagHelper, onclickTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_HtmlInsideComponentBody_ProducesIdenticalIR()
    {
        // Arrange - HTML elements nested inside component body
        var componentTagHelper = CreateComponentTagHelper("MyComponent", "MyComponent", "TestAssembly");

        var content = """
            <MyComponent>
                <div class="inner">
                    <span>@value</span>
                </div>
            </MyComponent>
            """;

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
    }

    [Fact]
    public void TagHelperRewrite_Component_ForeachWithComponent_ProducesIdenticalIR()
    {
        // Arrange - component inside @foreach loop
        var componentTagHelper = CreateComponentTagHelper("Item", "Item", "TestAssembly",
            b => { b.Name = "Name"; b.PropertyName = "Name"; b.TypeName = typeof(string).FullName!; });

        var content = """
            @foreach (var item in items)
            {
                <Item Name="@item" />
            }
            """;

        // Act
        var currentIR = RunCurrentPipeline(content, RazorFileKind.Component, [componentTagHelper]);
        var newIR = RunNewPipeline(content, RazorFileKind.Component, [componentTagHelper]);

        // Assert
        Assert.Equal(currentIR, newIR);

        // Assert - Generated C# also matches
        var currentCSharp = RunCurrentPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        var newCSharp = RunNewPipelineCSharp(content, RazorFileKind.Component, [componentTagHelper]);
        Assert.Equal(currentCSharp, newCSharp);
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

    private static TagHelperDescriptor CreateComponentTagHelper(
        string tagName,
        string typeName,
        string assemblyName,
        params ReadOnlySpan<Action<BoundAttributeDescriptorBuilder>> attributes)
    {
        var builder = TagHelperDescriptorBuilder.CreateComponent(typeName, assemblyName);
        builder.SetTypeName(typeName, typeNamespace: null, typeNameIdentifier: null);
        builder.CaseSensitive = true;

        foreach (var attributeBuilder in attributes)
        {
            builder.BoundAttributeDescriptor(attributeBuilder);
        }

        builder.TagMatchingRuleDescriptor(ruleBuilder => ruleBuilder.RequireTagName(tagName));

        return builder.Build();
    }

    private static TagHelperDescriptor CreateRefTagHelper()
    {
        using var _ = TagHelperDescriptorBuilder.GetPooledInstance(
            TagHelperKind.Ref, "Ref", "Microsoft.AspNetCore.Components",
            out var builder);

        builder.SetTypeName(
            fullName: "Microsoft.AspNetCore.Components.Ref",
            typeNamespace: "Microsoft.AspNetCore.Components",
            typeNameIdentifier: "Ref");

        builder.CaseSensitive = true;
        builder.ClassifyAttributesOnly = true;

        builder.TagMatchingRule(rule =>
        {
            rule.TagName = "*";
            rule.Attribute(attribute =>
            {
                attribute.Name = "@ref";
                attribute.IsDirectiveAttribute = true;
            });
        });

        builder.BindAttribute(attribute =>
        {
            attribute.Name = "@ref";
            attribute.TypeName = typeof(object).FullName;
            attribute.IsDirectiveAttribute = true;
            attribute.PropertyName = "Ref";
        });

        return builder.Build();
    }

    private static TagHelperDescriptor CreateKeyTagHelper()
    {
        using var _ = TagHelperDescriptorBuilder.GetPooledInstance(
            TagHelperKind.Key, "Key", "Microsoft.AspNetCore.Components",
            out var builder);

        builder.SetTypeName(
            fullName: "Microsoft.AspNetCore.Components.Key",
            typeNamespace: "Microsoft.AspNetCore.Components",
            typeNameIdentifier: "Key");

        builder.CaseSensitive = true;
        builder.ClassifyAttributesOnly = true;

        builder.TagMatchingRule(rule =>
        {
            rule.TagName = "*";
            rule.Attribute(attribute =>
            {
                attribute.Name = "@key";
                attribute.IsDirectiveAttribute = true;
            });
        });

        builder.BindAttribute(attribute =>
        {
            attribute.Name = "@key";
            attribute.TypeName = typeof(object).FullName;
            attribute.IsDirectiveAttribute = true;
            attribute.PropertyName = "Key";
        });

        return builder.Build();
    }

    private static TagHelperDescriptor CreateEventHandlerTagHelper(string eventName)
    {
        var fullAttributeName = "@on" + eventName;

        using var _ = TagHelperDescriptorBuilder.GetPooledInstance(
            TagHelperKind.EventHandler, "EventHandler_" + eventName, "Microsoft.AspNetCore.Components",
            out var builder);

        builder.SetTypeName(
            fullName: "Microsoft.AspNetCore.Components.EventHandlers",
            typeNamespace: "Microsoft.AspNetCore.Components",
            typeNameIdentifier: "EventHandlers");

        builder.CaseSensitive = true;
        builder.ClassifyAttributesOnly = true;

        builder.TagMatchingRule(rule =>
        {
            rule.TagName = "*";
            rule.Attribute(attribute =>
            {
                attribute.Name = fullAttributeName;
                attribute.IsDirectiveAttribute = true;
            });
        });

        builder.BindAttribute(attribute =>
        {
            attribute.Name = fullAttributeName;
            attribute.TypeName = "Microsoft.AspNetCore.Components.EventCallback<Microsoft.AspNetCore.Components.Web.MouseEventArgs>";
            attribute.IsDirectiveAttribute = true;
            attribute.PropertyName = eventName;

            attribute.BindAttributeParameter(p =>
            {
                p.Name = "preventDefault";
                p.PropertyName = "PreventDefault";
                p.TypeName = typeof(bool).FullName;
            });

            attribute.BindAttributeParameter(p =>
            {
                p.Name = "stopPropagation";
                p.PropertyName = "StopPropagation";
                p.TypeName = typeof(bool).FullName;
            });
        });

        return builder.Build();
    }

    private static TagHelperDescriptor CreateSplatTagHelper()
    {
        using var _ = TagHelperDescriptorBuilder.GetPooledInstance(
            TagHelperKind.Splat, "Attributes", "Microsoft.AspNetCore.Components",
            out var builder);

        builder.SetTypeName(
            fullName: "Microsoft.AspNetCore.Components.Attributes",
            typeNamespace: "Microsoft.AspNetCore.Components",
            typeNameIdentifier: "Attributes");

        builder.CaseSensitive = true;
        builder.ClassifyAttributesOnly = true;

        builder.TagMatchingRule(rule =>
        {
            rule.TagName = "*";
            rule.Attribute(attribute =>
            {
                attribute.Name = "@attributes";
                attribute.IsDirectiveAttribute = true;
            });
        });

        builder.BindAttribute(attribute =>
        {
            attribute.Name = "@attributes";
            attribute.TypeName = typeof(object).FullName;
            attribute.IsDirectiveAttribute = true;
            attribute.PropertyName = "Attributes";
        });

        return builder.Build();
    }

    private static TagHelperDescriptor CreateTagHelperDescriptorWithTagStructure(
        string tagName,
        string typeName,
        string assemblyName,
        TagStructure tagStructure,
        params ReadOnlySpan<Action<BoundAttributeDescriptorBuilder>> attributes)
    {
        var builder = TagHelperDescriptorBuilder.CreateTagHelper(typeName, assemblyName);
        builder.SetTypeName(typeName, typeNamespace: null, typeNameIdentifier: null);

        foreach (var attributeBuilder in attributes)
        {
            builder.BoundAttributeDescriptor(attributeBuilder);
        }

        builder.TagMatchingRuleDescriptor(ruleBuilder =>
        {
            ruleBuilder.RequireTagName(tagName);
            ruleBuilder.RequireTagStructure(tagStructure);
        });

        return builder.Build();
    }

    private static TagHelperDescriptor CreateBindTagHelper(string valueName, string changeAttributeName)
    {
        var fullAttributeName = "@bind-" + valueName;

        using var _ = TagHelperDescriptorBuilder.GetPooledInstance(
            TagHelperKind.Bind, "Bind_" + valueName, "Microsoft.AspNetCore.Components",
            out var builder);

        builder.SetTypeName(
            fullName: "Microsoft.AspNetCore.Components.Bind",
            typeNamespace: "Microsoft.AspNetCore.Components",
            typeNameIdentifier: "Bind");

        builder.CaseSensitive = true;
        builder.ClassifyAttributesOnly = true;

        builder.TagMatchingRule(rule =>
        {
            rule.TagName = "*";
            rule.Attribute(attribute =>
            {
                attribute.Name = fullAttributeName;
                attribute.IsDirectiveAttribute = true;
            });
        });

        builder.BindAttribute(attribute =>
        {
            attribute.Name = fullAttributeName;
            attribute.TypeName = typeof(object).FullName;
            attribute.IsDirectiveAttribute = true;
            attribute.PropertyName = valueName;

            attribute.BindAttributeParameter(p =>
            {
                p.Name = "event";
                p.PropertyName = "Event";
                p.TypeName = typeof(string).FullName;
            });
        });

        return builder.Build();
    }
}
