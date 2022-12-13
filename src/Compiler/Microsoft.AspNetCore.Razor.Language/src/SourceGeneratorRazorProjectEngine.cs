// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Razor.Language.Components;
using Microsoft.AspNetCore.Razor.Language.Extensions;
using System.Threading.Tasks;
using System.Linq;

namespace Microsoft.AspNetCore.Razor.Language;

internal class SourceGeneratorRazorProjectEngine : DefaultRazorProjectEngine
{
    public SourceGeneratorRazorProjectEngine(RazorConfiguration configuration, RazorEngine engine, RazorProjectFileSystem fileSystem, IReadOnlyList<IRazorProjectEngineFeature> projectFeatures)
        : base(configuration, engine, fileSystem, projectFeatures)
    {
    }

    public RazorCodeDocument ProcessInitialParse(RazorProjectItem projectItem)
    {
        if (projectItem == null)
        {
            throw new ArgumentNullException(nameof(projectItem));
        }

        var codeDocument = CreateCodeDocumentCore(projectItem);
        ProcessPartial(codeDocument, 0, 2);
        return codeDocument;

    }

    //PROTOTYPE: it's weird that we mutate the code document in place, but still return it? Yep that break some stuff. Of course it does, *sigh*. Even after changing them they still compare equal. Gosh darn.
    //Ok, we'll clone it for now, should fix the object equality. we should probably make these things immutable with proper comparisons.

    public RazorCodeDocument ProcessTagHelpers(RazorCodeDocument codeDocument, /*string fileKind, IReadOnlyList<RazorSourceDocument> importSources,*/ IReadOnlyList<TagHelperDescriptor> tagHelpers, bool checkForIdempotency)
    {
        //PROTOTYPE: do we need the import sources, don't we already have those from the projectItem?

        // PROTOYPE: clean up the logic flow here

        int startIndex = 2;
        var inputTagHelpers = codeDocument.GetTagHelpers();
        if (checkForIdempotency && inputTagHelpers is not null)
        {
            // compare the input tag helpers with the ones the document last used
            if (Enumerable.SequenceEqual(inputTagHelpers, tagHelpers))
            {
                // tag helpers are the same, nothing to do!
                return codeDocument;
            }
            else
            {
                // re-run the scope check, and see if the ones in scope are the same as last time
                var oldContextHelpers = codeDocument.GetTagHelperContext().TagHelpers;
                codeDocument.SetTagHelpers(tagHelpers);
                ProcessPartial(codeDocument, 2, 3);
                var newContextHelpers = codeDocument.GetTagHelperContext().TagHelpers;

                if (Enumerable.SequenceEqual(oldContextHelpers, newContextHelpers))
                {
                    // the overall set of tag helpers changed, but the ones this document can see in scope did not, we can re-use
                    return codeDocument;
                }

                //TODO: can we run this check first? I.e. if the length of the input helpers is the same, we don't need to re-calc the context. Maybe?
                //      consider: I remove a tag helper that wasn't in context, and add one that now is. If we look at all the used tag helpers, then we'd incorrectly say we can short circuit,
                //      even though the added one might actually be in the document. So we have to check the scopes first, annoyingly. (I think? write a test to prove this).

                // In the case of a tag helper removal, we can still check if it was used or not. However we have to consider the case where one tag helper is added and two are removed
                // We need to know that one was added and thus have to do a full re-parse. In other words we can only short circuit when we're sure no tag helpers were added.

                HashSet<TagHelperDescriptor> originalTagHelpers = new HashSet<TagHelperDescriptor>(inputTagHelpers);
                bool foundDiff = false;

                foreach (var newTagHelper in tagHelpers)
                {
                    if (!originalTagHelpers.Contains(newTagHelper))
                    {
                        // we found a new tag helper, we have to re-parse
                        foundDiff = true;
                        break;
                    }
                }

                if (!foundDiff)
                {
                    var newContextSet = new HashSet<TagHelperDescriptor>(newContextHelpers);
                    foreach (var usedHelper in codeDocument.GetReferencedTagHelpers())
                    {
                        if (!newContextSet.Contains(usedHelper))
                        {
                            // the new set doesn't contain a helper we used last time, we need to re-parse
                            foundDiff = true;
                            break;
                        }
                    }
                }

                if (!foundDiff)
                {
                    return codeDocument;
                }


                // Need to do a full re-write, but can skip the scoping check as we just did it
                startIndex = 3;
            }
        }

        codeDocument.SetTagHelpers(tagHelpers);
        ProcessPartial(codeDocument, startIndex, 4);
        return codeDocument.Clone();
    }

    public RazorCodeDocument ProcessRemaining(RazorCodeDocument codeDocument)
    {
        // PROTOTYPE: assert we're at a point that this can process.

        ProcessPartial(codeDocument, 4, Engine.Phases.Count);
        return codeDocument.Clone();
    }

    private void ProcessPartial(RazorCodeDocument codeDocument, int startIndex, int endIndex)
    {
        for (var i = startIndex; i < endIndex; i++)
        {
            Engine.Phases[i].Execute(codeDocument);
        }
    }

    //TODO: factor this out somehow
    public static SourceGeneratorRazorProjectEngine CreateSourceGeneratorEngine(
      RazorConfiguration configuration,
      RazorProjectFileSystem fileSystem,
      Action<RazorProjectEngineBuilder> configure)
    {
        if (fileSystem == null)
        {
            throw new ArgumentNullException(nameof(fileSystem));
        }

        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var builder = new DefaultRazorProjectEngineBuilder(configuration, fileSystem);

        // The initialization order is somewhat important.
        //
        // Defaults -> Extensions -> Additional customization
        //
        // This allows extensions to rely on default features, and customizations to override choices made by
        // extensions.
        AddDefaultPhases(builder.Phases);
        AddDefaultFeatures(builder.Features);

        if (configuration.LanguageVersion.CompareTo(RazorLanguageVersion.Version_5_0) >= 0)
        {
            builder.Features.Add(new ViewCssScopePass());
        }

        if (configuration.LanguageVersion.CompareTo(RazorLanguageVersion.Version_3_0) >= 0)
        {
            FunctionsDirective.Register(builder);
            ImplementsDirective.Register(builder);
            InheritsDirective.Register(builder);
            NamespaceDirective.Register(builder);
            AttributeDirective.Register(builder);

            AddComponentFeatures(builder, configuration.LanguageVersion);
        }

        LoadExtensions(builder, configuration.Extensions);

        configure?.Invoke(builder);

        var superType = builder.Build();
        return new SourceGeneratorRazorProjectEngine(superType.Configuration, superType.Engine, superType.FileSystem, superType.ProjectFeatures);
    }

    private static void AddDefaultPhases(IList<IRazorEnginePhase> phases)
    {
        phases.Add(new DefaultRazorParsingPhase());
        phases.Add(new DefaultRazorSyntaxTreePhase());

        phases.Add(new RazorTagHelperInScopeDiscoveryPhase());
        phases.Add(new RewriteRazorTagHelperBinderPhase());

        phases.Add(new DefaultRazorIntermediateNodeLoweringPhase());
        phases.Add(new DefaultRazorDocumentClassifierPhase());
        phases.Add(new DefaultRazorDirectiveClassifierPhase());
        phases.Add(new DefaultRazorOptimizationPhase());
        phases.Add(new DefaultRazorCSharpLoweringPhase());
    }



    private static void AddDefaultFeatures(ICollection<IRazorFeature> features)
    {
        features.Add(new DefaultImportProjectFeature());

        // General extensibility
        features.Add(new DefaultRazorDirectiveFeature());
        features.Add(new DefaultMetadataIdentifierFeature());

        // Options features
        features.Add(new DefaultRazorParserOptionsFactoryProjectFeature());
        features.Add(new DefaultRazorCodeGenerationOptionsFactoryProjectFeature());

        // Legacy options features
        //
        // These features are obsolete as of 2.1. Our code will resolve this but not invoke them.
        features.Add(new DefaultRazorParserOptionsFeature(designTime: false, version: RazorLanguageVersion.Version_2_0, fileKind: null));
        features.Add(new DefaultRazorCodeGenerationOptionsFeature(designTime: false));

        // Syntax Tree passes
        features.Add(new DefaultDirectiveSyntaxTreePass());
        features.Add(new HtmlNodeOptimizationPass());

        // Intermediate Node Passes
        features.Add(new DefaultDocumentClassifierPass());
        features.Add(new MetadataAttributePass());
        features.Add(new DesignTimeDirectivePass());
        features.Add(new DirectiveRemovalOptimizationPass());
        features.Add(new DefaultTagHelperOptimizationPass());
        features.Add(new PreallocatedTagHelperAttributeOptimizationPass());
        features.Add(new EliminateMethodBodyPass());

        // Default Code Target Extensions
        var targetExtensionFeature = new DefaultRazorTargetExtensionFeature();
        features.Add(targetExtensionFeature);
        targetExtensionFeature.TargetExtensions.Add(new MetadataAttributeTargetExtension());
        targetExtensionFeature.TargetExtensions.Add(new DefaultTagHelperTargetExtension());
        targetExtensionFeature.TargetExtensions.Add(new PreallocatedAttributeTargetExtension());
        targetExtensionFeature.TargetExtensions.Add(new DesignTimeDirectiveTargetExtension());

        // Default configuration
        var configurationFeature = new DefaultDocumentClassifierPassFeature();
        features.Add(configurationFeature);
        configurationFeature.ConfigureClass.Add((document, @class) =>
        {
            @class.ClassName = "Template";
            @class.Modifiers.Add("public");
        });

        configurationFeature.ConfigureNamespace.Add((document, @namespace) =>
        {
            @namespace.Content = "Razor";
        });

        configurationFeature.ConfigureMethod.Add((document, method) =>
        {
            method.MethodName = "ExecuteAsync";
            method.ReturnType = $"global::{typeof(Task).FullName}";

            method.Modifiers.Add("public");
            method.Modifiers.Add("async");
            method.Modifiers.Add("override");
        });
    }

    private static void AddComponentFeatures(RazorProjectEngineBuilder builder, RazorLanguageVersion razorLanguageVersion)
    {
        // Project Engine Features
        builder.Features.Add(new ComponentImportProjectFeature());

        // Directives (conditional on file kind)
        ComponentCodeDirective.Register(builder);
        ComponentInjectDirective.Register(builder);
        ComponentLayoutDirective.Register(builder);
        ComponentPageDirective.Register(builder);



        if (razorLanguageVersion.CompareTo(RazorLanguageVersion.Version_6_0) >= 0)
        {
            ComponentConstrainedTypeParamDirective.Register(builder);
        }
        else
        {
            ComponentTypeParamDirective.Register(builder);
        }

        if (razorLanguageVersion.CompareTo(RazorLanguageVersion.Version_5_0) >= 0)
        {
            ComponentPreserveWhitespaceDirective.Register(builder);
        }

        // Document Classifier
        builder.Features.Add(new ComponentDocumentClassifierPass());

        // Directive Classifier
        builder.Features.Add(new ComponentWhitespacePass());

        // Optimization
        builder.Features.Add(new ComponentComplexAttributeContentPass());
        builder.Features.Add(new ComponentLoweringPass());
        builder.Features.Add(new ComponentScriptTagPass());
        builder.Features.Add(new ComponentEventHandlerLoweringPass());
        builder.Features.Add(new ComponentKeyLoweringPass());
        builder.Features.Add(new ComponentReferenceCaptureLoweringPass());
        builder.Features.Add(new ComponentSplatLoweringPass());
        builder.Features.Add(new ComponentBindLoweringPass(razorLanguageVersion.CompareTo(RazorLanguageVersion.Version_7_0) >= 0));
        builder.Features.Add(new ComponentCssScopePass());
        builder.Features.Add(new ComponentTemplateDiagnosticPass());
        builder.Features.Add(new ComponentGenericTypePass());
        builder.Features.Add(new ComponentChildContentDiagnosticPass());
        builder.Features.Add(new ComponentMarkupDiagnosticPass());
        builder.Features.Add(new ComponentMarkupBlockPass());
        builder.Features.Add(new ComponentMarkupEncodingPass());
    }

    private static void LoadExtensions(RazorProjectEngineBuilder builder, IReadOnlyList<RazorExtension> extensions)
    {
        for (var i = 0; i < extensions.Count; i++)
        {
            // For now we only handle AssemblyExtension - which is not user-constructable. We're keeping a tight
            // lid on how things work until we add official support for extensibility everywhere. So, this is
            // intentionally inflexible for the time being.
            if (extensions[i] is AssemblyExtension extension)
            {
                var initializer = extension.CreateInitializer();
                initializer?.Initialize(builder);
            }
        }
    }

}
