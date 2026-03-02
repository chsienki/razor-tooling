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
        // Modified pipeline: Parse → SyntaxTree → Modified Lower → Discovery → IR Rewrite Phase
        // The lowering phase knows nothing about tag helpers. Discovery runs after lowering.
        var noRewriteEngine = CreateProjectEngine(builder =>
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

                    // Insert discovery phase right after lowering
                    if (discoveryPhase != null)
                    {
                        builder.Phases.Insert(i + 1, discoveryPhase);
                    }

                    break;
                }
            }
        });

        var codeDocument = noRewriteEngine.CreateCodeDocument(content, fileKind, tagHelpers);
        // Execute through Discovery (which now comes after lowering)
        codeDocument = noRewriteEngine.ExecutePhasesThrough<DefaultRazorTagHelperContextDiscoveryPhase>(codeDocument);

        // Now run the new IR rewrite phase
        var rewritePhase = new TagHelperIntermediateNodeRewritePhase();
        rewritePhase.Initialize(noRewriteEngine.Engine);
        codeDocument = rewritePhase.Execute(codeDocument);

        var documentNode = codeDocument.GetDocumentNode();
        Assert.NotNull(documentNode);

        return IntermediateNodeSerializer.Serialize(documentNode);
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
}
