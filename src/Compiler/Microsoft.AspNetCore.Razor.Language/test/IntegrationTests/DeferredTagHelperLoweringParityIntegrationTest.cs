// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.AspNetCore.Razor.Language.Syntax;
using Microsoft.AspNetCore.Razor.PooledObjects;
using Microsoft.CodeAnalysis.Razor;
using Xunit;

namespace Microsoft.AspNetCore.Razor.Language.IntegrationTests;

public class DeferredTagHelperLoweringParityIntegrationTest : IntegrationTestBase
{
    public enum DescriptorSet
    {
        Simple,
        DefaultPAndInput,
        CssSelector,
        DuplicateTarget,
        AttributeTargeting,
        PrefixedAttribute,
        DynamicAttribute,
        Minimized,
        SymbolBound,
        Enum,
        TagHelpersInSection,
    }

    public DeferredTagHelperLoweringParityIntegrationTest()
        : base(layer: TestProject.Layer.Compiler)
    {
        AddCSharpSyntaxTree(
            """
            using Microsoft.AspNetCore.Components;

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

    public override string GetTestFileName(string testName = null)
        => $"TestFiles/IntegrationTests/CodeGenerationIntegrationTest/{testName}_Runtime";

    public static TheoryData<string, DescriptorSet> CshtmlTagHelperScenarios { get; } = new()
    {
        { "Markup_InCodeBlocksWithTagHelper", DescriptorSet.Simple },
        { "SimpleTagHelpers", DescriptorSet.Simple },
        { "TagHelpersWithBoundAttributes", DescriptorSet.Simple },
        { "TagHelpersWithBoundAttributesAndRazorComment", DescriptorSet.Simple },
        { "TagHelpersWithPrefix", DescriptorSet.Simple },
        { "NestedTagHelpers", DescriptorSet.Simple },
        { "SingleTagHelper", DescriptorSet.DefaultPAndInput },
        { "SingleTagHelperWithNewlineBeforeAttributes", DescriptorSet.DefaultPAndInput },
        { "TagHelpersWithWeirdlySpacedAttributes", DescriptorSet.DefaultPAndInput },
        { "IncompleteTagHelper", DescriptorSet.DefaultPAndInput },
        { "BasicTagHelpers", DescriptorSet.DefaultPAndInput },
        { "BasicTagHelpers_Prefixed", DescriptorSet.DefaultPAndInput },
        { "BasicTagHelpers_RemoveTagHelper", DescriptorSet.DefaultPAndInput },
        { "CssSelectorTagHelperAttributes", DescriptorSet.CssSelector },
        { "ComplexTagHelpers", DescriptorSet.DefaultPAndInput },
        { "EmptyAttributeTagHelpers", DescriptorSet.DefaultPAndInput },
        { "EscapedTagHelpers", DescriptorSet.DefaultPAndInput },
        { "DuplicateTargetTagHelper", DescriptorSet.DuplicateTarget },
        { "AttributeTargetingTagHelpers", DescriptorSet.AttributeTargeting },
        { "PrefixedAttributeTagHelpers", DescriptorSet.PrefixedAttribute },
        { "DuplicateAttributeTagHelpers", DescriptorSet.DefaultPAndInput },
        { "DynamicAttributeTagHelpers", DescriptorSet.DynamicAttribute },
        { "TransitionsInTagHelperAttributes", DescriptorSet.DefaultPAndInput },
        { "MinimizedTagHelpers", DescriptorSet.Minimized },
        { "NestedScriptTagTagHelpers", DescriptorSet.DefaultPAndInput },
        { "SymbolBoundAttributes", DescriptorSet.SymbolBound },
        { "EnumTagHelpers", DescriptorSet.Enum },
        { "TagHelpersInSection", DescriptorSet.TagHelpersInSection },
        { "TagHelpersWithTemplate", DescriptorSet.Simple },
        { "TagHelpersWithDataDashAttributes", DescriptorSet.Simple },
        { "EscapedIdentifier", DescriptorSet.Simple },
        { "EscapedExpression", DescriptorSet.Simple },
    };

    public static TheoryData<string, string> RazorTagHelperScenarios { get; } = new()
    {
        {
            "Razor_ElementBindAndEvent",
            """
            @page "/bind"
            <input @bind="Value" />
            <button @onclick="Increment">Increment</button>

            @code {
                private int _count;
                private string Value { get; set; } = "Hello";
                private void Increment() => _count++;
            }
            """
        },
        {
            "Razor_ElementPreventDefault",
            """
            <button @onkeydown:preventDefault @onclick="OnKey">Key</button>

            @code {
                private void OnKey() { }
            }
            """
        },
        {
            "Razor_ElementRefKeySplat",
            """
            @using System.Collections.Generic
            @using Microsoft.AspNetCore.Components

            <div @ref="_element" @attributes="_attrs" @key="_key">hello</div>

            @code {
                private ElementReference _element;
                private int _key = 7;
                private Dictionary<string, object> _attrs = new()
                {
                    ["data-a"] = "one",
                    ["data-b"] = "two",
                };
            }
            """
        },
        {
            "Razor_ComponentSimple",
            """
            <MyComponent Value="abc" />
            """
        },
        {
            "Razor_ComponentBind",
            """
            <MyComponent @bind-Value="_value" />

            @code {
                private string _value = "bound-value";
            }
            """
        },
        {
            "Razor_ComponentChildContentWithKey",
            """
            <MyComponent Value="_value">
                <span @key="_key">@_value</span>
            </MyComponent>

            @code {
                private string _value = "child-content";
                private int _key = 123;
            }
            """
        },
        {
            "Razor_DynamicComponent",
            """
            @using System.Collections.Generic

            <DynamicComponent Type="typeof(MyComponent)" Parameters="_parameters" />

            @code {
                private IReadOnlyDictionary<string, object> _parameters = new Dictionary<string, object>
                {
                    ["Value"] = "dynamic",
                };
            }
            """
        },
        {
            "Razor_MixedMarkupAndComponent",
            """
            <input @bind="_value" />
            <MyComponent Value="_value" />

            @code {
                private string _value = "mixed";
            }
            """
        },
        {
            "Razor_ElementCombinedEventModifiers",
            """
            <button @onclick="OnClick" @onclick:preventDefault @onclick:stopPropagation>Click me</button>

            @code {
                private void OnClick() { }
            }
            """
        },
        {
            "Razor_ElementStopPropagationOnly",
            """
            <button @onmousedown="OnMouseDown" @onmousedown:stopPropagation>Mouse down</button>

            @code {
                private void OnMouseDown() { }
            }
            """
        },
        {
            "Razor_ElementMultipleEventHandlers",
            """
            <input @onfocus="OnFocus" @onblur="OnBlur" @onchange="OnChange" />

            @code {
                private void OnFocus() { }
                private void OnBlur() { }
                private void OnChange() { }
            }
            """
        },
        {
            "Razor_ElementKeyboardEventCombination",
            """
            <input @onkeydown="OnKeyDown" @onkeydown:preventDefault @onkeyup="OnKeyUp" @onkeyup:stopPropagation />

            @code {
                private void OnKeyDown() { }
                private void OnKeyUp() { }
            }
            """
        },
        {
            "Razor_ElementMixedKeyboardAndPointerHandlers",
            """
            <div tabindex="0" @onkeydown="OnKeyDown" @onclick="OnClick" @onclick:stopPropagation>Interactive</div>

            @code {
                private void OnKeyDown() { }
                private void OnClick() { }
            }
            """
        },
        {
            "Razor_ComponentRefAndKey",
            """
            <MyComponent @ref="_componentRef" @key="_componentKey" Value="_value" />

            @code {
                private object _componentRef;
                private int _componentKey = 9;
                private string _value = "component";
            }
            """
        },
        {
            "Razor_ComponentSplatWithOtherAttributes",
            """
            @using System.Collections.Generic

            <MyComponent Value="_value" @attributes="_attributes" @key="_key" />

            @code {
                private string _value = "splat";
                private int _key = 3;
                private Dictionary<string, object> _attributes = new()
                {
                    ["data-id"] = "1",
                    ["aria-label"] = "label",
                };
            }
            """
        },
        {
            "Razor_NestedDirectivesWithComponentAndElement",
            """
            @using System.Collections.Generic

            @if (_show)
            {
                <MyComponent Value="_value" @key="_componentKey">
                    <section @attributes="_sectionAttributes">
                        @for (var i = 0; i < _items.Length; i++)
                        {
                            <button @onclick="OnClick" @onclick:preventDefault @onclick:stopPropagation>@_items[i]</button>
                        }
                    </section>
                </MyComponent>
            }

            @code {
                private bool _show = true;
                private string _value = "nested";
                private int _componentKey = 17;
                private string[] _items = ["one", "two"];
                private Dictionary<string, object> _sectionAttributes = new()
                {
                    ["class"] = "panel",
                };
                private void OnClick() { }
            }
            """
        },
        {
            "Razor_NestedElementEventHandlersWithStopPropagation",
            """
            <div>
                @if (_enabled)
                {
                    <input @bind="_value" @onkeydown="OnKeyDown" @onkeydown:stopPropagation />
                    <button @onclick="OnClick" @onclick:preventDefault>Save</button>
                }
            </div>

            @code {
                private bool _enabled = true;
                private string _value = string.Empty;
                private void OnKeyDown() { }
                private void OnClick() { }
            }
            """
        },
    };

    [Theory]
    [MemberData(nameof(CshtmlTagHelperScenarios))]
    public void Cshtml_DeferredLowering_MatchesBaseline(string scenarioName, DescriptorSet descriptorSet)
    {
        var baseline = GenerateLegacyCSharp(scenarioName, descriptorSet, useDeferredPipeline: false);
        var @new = GenerateLegacyCSharp(scenarioName, descriptorSet, useDeferredPipeline: true);

        Assert.Equal(baseline, @new);
    }

    [Theory]
    [MemberData(nameof(RazorTagHelperScenarios))]
    public void Razor_DeferredLowering_MatchesBaseline(string scenarioName, string source)
    {
        var baseline = GenerateComponentCSharp(scenarioName, source, useDeferredPipeline: false);
        var @new = GenerateComponentCSharp(scenarioName, source, useDeferredPipeline: true);

        Assert.Equal(baseline, @new);
    }

    private string GenerateLegacyCSharp(string scenarioName, DescriptorSet descriptorSet, bool useDeferredPipeline)
    {
        var projectEngine = CreateProjectEngine(RazorExtensions.Register);
        var projectItem = CreateProjectItemFromFile(testName: scenarioName);
        var imports = GetImports(projectEngine, projectItem);
        var source = RazorSourceDocument.ReadFrom(projectItem);
        var tagHelpers = GetTagHelpers(descriptorSet);

        var codeDocument = projectEngine.CreateCodeDocument(source, RazorFileKind.Legacy, imports, tagHelpers, cssScope: null);
        codeDocument = useDeferredPipeline
            ? ExecuteDeferredPipeline(projectEngine, codeDocument)
            : ExecuteBaselinePipeline(projectEngine, codeDocument);

        return codeDocument.GetRequiredCSharpDocument().Text.ToString();
    }

    private string GenerateComponentCSharp(string scenarioName, string sourceText, bool useDeferredPipeline)
    {
        var projectEngine = CreateProjectEngine(CompilerFeatures.Register);
        var projectItem = AddProjectItemFromText(sourceText, filePath: $"{scenarioName}.razor", testName: scenarioName);
        var imports = GetImports(projectEngine, projectItem);
        var source = RazorSourceDocument.ReadFrom(projectItem);

        var codeDocument = projectEngine.CreateCodeDocument(source, RazorFileKind.Component, imports, tagHelpers: null, cssScope: null);
        codeDocument = useDeferredPipeline
            ? ExecuteDeferredPipeline(projectEngine, codeDocument)
            : ExecuteBaselinePipeline(projectEngine, codeDocument);

        return codeDocument.GetRequiredCSharpDocument().Text.ToString();
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

    private static RazorCodeDocument ExecuteBaselinePipeline(RazorProjectEngine projectEngine, RazorCodeDocument codeDocument)
    {
        var phases = projectEngine.Engine.Phases;
        var discoveryIndex = GetPhaseIndex<DefaultRazorTagHelperContextDiscoveryPhase>(projectEngine);
        var rewriteIndex = GetPhaseIndex<DefaultRazorTagHelperRewritePhase>(projectEngine);
        var loweringIndex = GetPhaseIndex<DefaultRazorIntermediateNodeLoweringPhase>(projectEngine);

        Assert.True(discoveryIndex < rewriteIndex, "Unexpected phase ordering: discovery should run before rewrite.");
        Assert.True(rewriteIndex < loweringIndex, "Unexpected phase ordering: rewrite should run before lowering.");

        codeDocument = ExecutePhaseRange(phases, start: 0, end: discoveryIndex, codeDocument);
        codeDocument = phases[discoveryIndex].Execute(codeDocument);

        // Baseline copy of the current rewrite phase.
        var baselineRewrite = new BaselineTagHelperRewritePhase
        {
            Engine = projectEngine.Engine,
        };

        codeDocument = baselineRewrite.Execute(codeDocument);
        codeDocument = phases[loweringIndex].Execute(codeDocument);
        codeDocument = ExecutePhaseRange(phases, start: loweringIndex + 1, end: phases.Length, codeDocument);

        return codeDocument;
    }

    private static RazorCodeDocument ExecuteDeferredPipeline(RazorProjectEngine projectEngine, RazorCodeDocument codeDocument)
    {
        var phases = projectEngine.Engine.Phases;
        var discoveryIndex = GetPhaseIndex<DefaultRazorTagHelperContextDiscoveryPhase>(projectEngine);
        var rewriteIndex = GetPhaseIndex<DefaultRazorTagHelperRewritePhase>(projectEngine);
        var loweringIndex = GetPhaseIndex<DefaultRazorIntermediateNodeLoweringPhase>(projectEngine);

        Assert.True(discoveryIndex < rewriteIndex, "Unexpected phase ordering: discovery should run before rewrite.");
        Assert.True(rewriteIndex < loweringIndex, "Unexpected phase ordering: rewrite should run before lowering.");

        // Parse + syntax tree.
        codeDocument = ExecutePhaseRange(phases, start: 0, end: discoveryIndex, codeDocument);

        // New initial lowering (test-only) that captures tag-helper-capable nodes in IR.
        var initialLowering = new DeferredInitialLoweringPhase
        {
            Engine = projectEngine.Engine,
        };

        codeDocument = initialLowering.Execute(codeDocument);
        Assert.NotEmpty(codeDocument.GetRequiredDocumentNode().FindDescendantNodes<ElementOrTagHelperIntermediateNode>());

        // Discovery after initial lowering.
        codeDocument = phases[discoveryIndex].Execute(codeDocument);

        // New deferred lowering phase (test-only).
        var deferredLowering = new DeferredTagHelperLoweringPhase
        {
            Engine = projectEngine.Engine,
        };

        codeDocument = deferredLowering.Execute(codeDocument);

        // The new pipeline intentionally does not mutate the code document syntax tree with tag helper nodes.
        Assert.Empty(codeDocument.GetRequiredSyntaxTree().Root.DescendantNodes().OfType<MarkupTagHelperElementSyntax>());

        // Continue with normal downstream phases.
        codeDocument = ExecutePhaseRange(phases, start: loweringIndex + 1, end: phases.Length, codeDocument);
        return codeDocument;
    }

    private static RazorCodeDocument ExecutePhaseRange(
        ImmutableArray<IRazorEnginePhase> phases,
        int start,
        int end,
        RazorCodeDocument codeDocument)
    {
        var current = codeDocument;
        for (var i = start; i < end; i++)
        {
            current = phases[i].Execute(current);
        }

        return current;
    }

    private static int GetPhaseIndex<TPhase>(RazorProjectEngine projectEngine)
        where TPhase : IRazorEnginePhase
    {
        var phases = projectEngine.Engine.Phases;
        for (var i = 0; i < phases.Length; i++)
        {
            if (phases[i] is TPhase)
            {
                return i;
            }
        }

        throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Unable to find phase '{0}'.", typeof(TPhase).Name));
    }

    private static TagHelperCollection GetTagHelpers(DescriptorSet descriptorSet)
    {
        return descriptorSet switch
        {
            DescriptorSet.Simple => TestTagHelperDescriptors.SimpleTagHelperDescriptors,
            DescriptorSet.DefaultPAndInput => TestTagHelperDescriptors.DefaultPAndInputTagHelperDescriptors,
            DescriptorSet.CssSelector => TestTagHelperDescriptors.CssSelectorTagHelperDescriptors,
            DescriptorSet.DuplicateTarget => TestTagHelperDescriptors.DuplicateTargetTagHelperDescriptors,
            DescriptorSet.AttributeTargeting => TestTagHelperDescriptors.AttributeTargetingTagHelperDescriptors,
            DescriptorSet.PrefixedAttribute => TestTagHelperDescriptors.PrefixedAttributeTagHelperDescriptors,
            DescriptorSet.DynamicAttribute => TestTagHelperDescriptors.DynamicAttributeTagHelpers_Descriptors,
            DescriptorSet.Minimized => TestTagHelperDescriptors.MinimizedTagHelpers_Descriptors,
            DescriptorSet.SymbolBound => TestTagHelperDescriptors.SymbolBoundTagHelperDescriptors,
            DescriptorSet.Enum => TestTagHelperDescriptors.EnumTagHelperDescriptors,
            DescriptorSet.TagHelpersInSection => TestTagHelperDescriptors.TagHelpersInSectionDescriptors,
            _ => throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Unknown descriptor set '{0}'.", descriptorSet)),
        };
    }

    // Baseline copy of DefaultRazorTagHelperRewritePhase.
    private sealed class BaselineTagHelperRewritePhase : RazorEnginePhaseBase
    {
        protected override RazorCodeDocument ExecuteCore(RazorCodeDocument codeDocument, CancellationToken cancellationToken)
        {
            if (!codeDocument.TryGetPreTagHelperSyntaxTree(out var syntaxTree) ||
                !codeDocument.TryGetTagHelperContext(out var context) ||
                context.TagHelpers is [])
            {
                return codeDocument.WithReferencedTagHelpers([]);
            }

            var binder = context.GetBinder();
            using var usedHelpers = new TagHelperCollection.Builder();
            var rewrittenSyntaxTree = TagHelperParseTreeRewriter.Rewrite(syntaxTree, binder, usedHelpers, cancellationToken);

            return codeDocument
                .WithReferencedTagHelpers(usedHelpers.ToCollection())
                .WithSyntaxTree(rewrittenSyntaxTree);
        }
    }

    private sealed class DeferredInitialLoweringPhase : RazorEnginePhaseBase, IRazorIntermediateNodeLoweringPhase
    {
        protected override RazorCodeDocument ExecuteCore(RazorCodeDocument codeDocument, CancellationToken cancellationToken)
        {
            var syntaxTree = codeDocument.GetSyntaxTree();
            ThrowForMissingDocumentDependency(syntaxTree);

            var documentNode = new DocumentIntermediateNode()
            {
                Options = codeDocument.CodeGenerationOptions,
            };

            var visitor = new ElementOrTagHelperCaptureVisitor(documentNode, syntaxTree.Source);
            visitor.Visit(syntaxTree.Root);

            return codeDocument.WithDocumentNode(documentNode);
        }
    }

    private sealed class DeferredTagHelperLoweringPhase : RazorEnginePhaseBase
    {
        private const char InvalidAttributeValueMarker = '\0';

        protected override RazorCodeDocument ExecuteCore(RazorCodeDocument codeDocument, CancellationToken cancellationToken)
        {
            var currentDocument = codeDocument.GetDocumentNode();
            ThrowForMissingDocumentDependency(currentDocument);

            var syntaxTree = codeDocument.GetSyntaxTree();
            ThrowForMissingDocumentDependency(syntaxTree);

            if (!codeDocument.TryGetTagHelperContext(out var context))
            {
                throw new InvalidOperationException("Tag helper context must be available before deferred lowering.");
            }

            var baselineLowering = new DefaultRazorIntermediateNodeLoweringPhase
            {
                Engine = Engine,
            };

            if (context.TagHelpers is [])
            {
                var loweredWithoutTagHelpers = baselineLowering.Execute(codeDocument, cancellationToken);
                return codeDocument
                    .WithReferencedTagHelpers([])
                    .WithDocumentNode(loweredWithoutTagHelpers.GetRequiredDocumentNode());
            }

            var deferredNodes = currentDocument.FindDescendantNodes<ElementOrTagHelperIntermediateNode>();
            var binder = context.GetBinder();
            using var usedHelpers = new TagHelperCollection.Builder();
            using var errorSink = new ErrorSink();
            var rewriteCandidates = new Dictionary<MarkupElementSyntax, DeferredRewriteCandidate>();
            var nodesToRewrite = ComputeRewriteCandidates(
                deferredNodes,
                binder,
                syntaxTree.Source,
                usedHelpers,
                errorSink,
                rewriteCandidates,
                cancellationToken);

            var rewrittenRoot = nodesToRewrite.Length == 0
                ? syntaxTree.Root
                : syntaxTree.Root.ReplaceNodes(
                    nodesToRewrite,
                    (original, rewritten) => RewriteDeferredElement(
                        rewritten,
                        rewriteCandidates[original],
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
            var rewrittenSyntaxTree = new RazorSyntaxTree(rewrittenRoot, syntaxTree.Source, diagnostics, syntaxTree.Options);
            var loweredDocument = baselineLowering.Execute(codeDocument.WithSyntaxTree(rewrittenSyntaxTree), cancellationToken);

            return codeDocument
                .WithReferencedTagHelpers(usedHelpers.ToCollection())
                .WithDocumentNode(loweredDocument.GetRequiredDocumentNode());
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

            var attributes = GetAttributeNameValuePairs(startTag);
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

        private static ImmutableArray<KeyValuePair<string, string>> GetAttributeNameValuePairs(MarkupStartTagSyntax startTag)
        {
            if (startTag.Attributes.Count == 0)
            {
                return [];
            }

            var attributeValueBuilder = new StringBuilder();
            using var attributes = new PooledArrayBuilder<KeyValuePair<string, string>>();

            foreach (var attribute in startTag.Attributes)
            {
                if (attribute is CSharpCodeBlockSyntax)
                {
                    break;
                }

                if (attribute is MarkupMinimizedAttributeBlockSyntax minimizedAttribute)
                {
                    if (minimizedAttribute.Name is null)
                    {
                        attributeValueBuilder.Append(InvalidAttributeValueMarker);
                        continue;
                    }

                    attributes.Add(new(minimizedAttribute.Name.GetContent(), string.Empty));
                    continue;
                }

                if (attribute is not MarkupAttributeBlockSyntax attributeBlock)
                {
                    continue;
                }

                if (attributeBlock.Name is null)
                {
                    attributeValueBuilder.Append(InvalidAttributeValueMarker);
                    continue;
                }

                if (attributeBlock.Value is not null)
                {
                    foreach (var child in attributeBlock.Value.Children)
                    {
                        if (child is MarkupLiteralAttributeValueSyntax literalValue)
                        {
                            attributeValueBuilder.Append(literalValue.GetContent());
                        }
                        else
                        {
                            attributeValueBuilder.Append(InvalidAttributeValueMarker);
                        }
                    }
                }

                attributes.Add(new(attributeBlock.Name.GetContent(), attributeValueBuilder.ToString()));
                attributeValueBuilder.Clear();
            }

            return attributes.ToImmutableAndClear();
        }

        private readonly record struct ParentContext(string TagName, bool IsTagHelper);

        private readonly record struct DeferredNodeState(string TagName, bool IsTagHelper, bool TracksChildren);

        private readonly record struct DeferredRewriteCandidate(string TagName, TagHelperBinding Binding, TagMode TagMode, bool TracksChildren);
    }

    private sealed class ElementOrTagHelperCaptureVisitor(DocumentIntermediateNode document, RazorSourceDocument source) : SyntaxWalker
    {
        private readonly DocumentIntermediateNode _document = document;
        private readonly RazorSourceDocument _source = source;

        public override void VisitMarkupElement(MarkupElementSyntax node)
        {
            if (CanBecomeTagHelper(node))
            {
                var tagName = node.StartTag?.GetTagNameWithOptionalBang() ?? node.EndTag?.GetTagNameWithOptionalBang() ?? string.Empty;
                _document.Children.Add(new ElementOrTagHelperIntermediateNode(node, tagName)
                {
                    Source = node.GetSourceSpan(_source),
                });
            }

            base.VisitMarkupElement(node);
        }

        public override void VisitMarkupTagHelperElement(MarkupTagHelperElementSyntax node)
        {
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

    private sealed class ElementOrTagHelperIntermediateNode(SyntaxNode syntaxNode, string tagName) : ExtensionIntermediateNode
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
}
