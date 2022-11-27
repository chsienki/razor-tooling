// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.NET.Sdk.Razor.SourceGenerators
{
    [Generator]
    public partial class RazorSourceGenerator : IIncrementalGenerator
    {
        private static RazorSourceGeneratorEventSource Log => RazorSourceGeneratorEventSource.Log;

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var analyzerConfigOptions = context.AnalyzerConfigOptionsProvider;
            var parseOptions = context.ParseOptionsProvider;
            var compilation = context.CompilationProvider;

            // determine if we should suppress this run and filter out all the additional files if so
            var isGeneratorSuppressed = context.AnalyzerConfigOptionsProvider.Select(GetSuppressionStatus);
            var additionalTexts = context.AdditionalTextsProvider
                 .Combine(isGeneratorSuppressed)
                 .Where(pair => !pair.Right)
                 .Select((pair, _) => pair.Left);

            var razorSourceGeneratorOptions = analyzerConfigOptions
                .Combine(parseOptions)
                .Select(ComputeRazorSourceGeneratorOptions)
                .ReportDiagnostics(context);

            var sourceItems = additionalTexts
                .Where(static (file) => file.Path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || file.Path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
                .Combine(analyzerConfigOptions)
                .Select(ComputeProjectItems)
                .ReportDiagnostics(context);

            var hasRazorFiles = sourceItems.Collect()
                .Select(static (sourceItems, _) => sourceItems.Any());

            var importFiles = sourceItems.Where(static file =>
            {
                var path = file.FilePath;
                if (path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    return string.Equals(fileName, "_Imports", StringComparison.OrdinalIgnoreCase);
                }
                else if (path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    return string.Equals(fileName, "_ViewImports", StringComparison.OrdinalIgnoreCase);
                }

                return false;
            });

            var componentFiles = sourceItems.Where(static file => file.FilePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase));

            var generatedDeclarationCode = componentFiles
                .Combine(importFiles.Collect())
                .Combine(razorSourceGeneratorOptions)
                .Select(static (pair, _) =>
                {

                    var ((sourceItem, importFiles), razorSourceGeneratorOptions) = pair;
                    RazorSourceGeneratorEventSource.Log.GenerateDeclarationCodeStart(sourceItem.FilePath);

                    var projectEngine = GetDeclarationProjectEngine(sourceItem, importFiles, razorSourceGeneratorOptions);

                    var codeGen = projectEngine.Process(sourceItem);

                    var result = codeGen.GetCSharpDocument().GeneratedCode;

                    RazorSourceGeneratorEventSource.Log.GenerateDeclarationCodeStop(sourceItem.FilePath);

                    return result;
                });

            var generatedDeclarationSyntaxTrees = generatedDeclarationCode
                .Combine(parseOptions)
                .Select(static (pair, _) =>
                {
                    var (generatedDeclarationCode, parseOptions) = pair;
                    return CSharpSyntaxTree.ParseText(generatedDeclarationCode, (CSharpParseOptions)parseOptions);
                });

            // TODO: we need to untangle the razor files from the compilation
            //       that will allow us to not re-parse the razor ones each time, and ensure they compare equal down
            //       the line via reference, speeding things up there too.
            var tagHelpersFromCompilation = compilation
                .Combine(generatedDeclarationSyntaxTrees.Collect())
                .Combine(razorSourceGeneratorOptions)
                .Select(static (pair, _) =>
                {
                    RazorSourceGeneratorEventSource.Log.DiscoverTagHelpersFromCompilationStart();

                    var ((compilation, generatedDeclarationSyntaxTrees), razorSourceGeneratorOptions) = pair;

                    var tagHelperFeature = new StaticCompilationTagHelperFeature();
                    var discoveryProjectEngine = GetDiscoveryProjectEngine(compilation.References.ToImmutableArray(), tagHelperFeature);

                    var compilationWithDeclarations = compilation.AddSyntaxTrees(generatedDeclarationSyntaxTrees);

                    tagHelperFeature.Compilation = compilationWithDeclarations;
                    tagHelperFeature.TargetAssembly = compilationWithDeclarations.Assembly;

                    var result = (IList<TagHelperDescriptor>)tagHelperFeature.GetDescriptors();
                    RazorSourceGeneratorEventSource.Log.DiscoverTagHelpersFromCompilationStop();
                    return result;
                })
                .WithLambdaComparer(static (a, b) =>
                {
                    if (a.Count != b.Count)
                    {
                        return false;
                    }

                    for (var i = 0; i < a.Count; i++)
                    {
                        if (!a[i].Equals(b[i]))
                        {
                            return false;
                        }
                    }

                    return true;
                }, getHashCode: static a => a.Count);

            var tagHelpersFromReferences = compilation
                .Combine(razorSourceGeneratorOptions)
                .Combine(hasRazorFiles)
                .WithLambdaComparer(static (a, b) =>
                {
                    var ((compilationA, razorSourceGeneratorOptionsA), hasRazorFilesA) = a;
                    var ((compilationB, razorSourceGeneratorOptionsB), hasRazorFilesB) = b;

                    if (!compilationA.References.SequenceEqual(compilationB.References))
                    {
                        return false;
                    }

                    if (razorSourceGeneratorOptionsA != razorSourceGeneratorOptionsB)
                    {
                        return false;
                    }

                    return hasRazorFilesA == hasRazorFilesB;
                },
                static item =>
                {
                    // we'll use the number of references as a hashcode.
                    var ((compilationA, razorSourceGeneratorOptionsA), hasRazorFilesA) = item;
                    return compilationA.References.GetHashCode();
                })
                .Select(static (pair, _) =>
                {
                    RazorSourceGeneratorEventSource.Log.DiscoverTagHelpersFromReferencesStart();

                    var ((compilation, razorSourceGeneratorOptions), hasRazorFiles) = pair;
                    if (!hasRazorFiles)
                    {
                        // If there's no razor code in this app, don't do anything.
                        RazorSourceGeneratorEventSource.Log.DiscoverTagHelpersFromReferencesStop();
                        return ImmutableArray<TagHelperDescriptor>.Empty;
                    }

                    var tagHelperFeature = new StaticCompilationTagHelperFeature();
                    var discoveryProjectEngine = GetDiscoveryProjectEngine(compilation.References.ToImmutableArray(), tagHelperFeature);

                    List<TagHelperDescriptor> descriptors = new();
                    tagHelperFeature.Compilation = compilation;
                    foreach (var reference in compilation.References)
                    {
                        if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                        {
                            tagHelperFeature.TargetAssembly = assembly;
                            descriptors.AddRange(tagHelperFeature.GetDescriptors());
                        }
                    }

                    RazorSourceGeneratorEventSource.Log.DiscoverTagHelpersFromReferencesStop();
                    return (ICollection<TagHelperDescriptor>)descriptors;
                });

            var allTagHelpers = tagHelpersFromCompilation
                .Combine(tagHelpersFromReferences)
                .Select(static (pair, _) =>
                {
                    var (tagHelpersFromCompilation, tagHelpersFromReferences) = pair;
                    var count = tagHelpersFromCompilation.Count + tagHelpersFromReferences.Count;
                    if (count == 0)
                    {
                        return Array.Empty<TagHelperDescriptor>();
                    }

                    var allTagHelpers = new TagHelperDescriptor[count];
                    tagHelpersFromCompilation.CopyTo(allTagHelpers, 0);
                    tagHelpersFromReferences.CopyTo(allTagHelpers, tagHelpersFromCompilation.Count);

                    return allTagHelpers;
                });



            var sourceItemsEx = sourceItems.AsProjectItemEx(importFiles, razorSourceGeneratorOptions);



            // PROTOTYPE: it should be possible to create a single engine, and just replace the tag helpers as we go, rather than needing a new one each time.
            //            That needs us to be able to pass a VFS and the tag helpers to process, so for now lets just recreate it every time


            // TODO: right. We don't want to do the actual NoRewritePhase as part of the initial parsing

            // parse the razor files up to the point where they are independent
            var initialParseItems = sourceItemsEx
                .Select((item, _) =>
                {

                    var engine = GetGeneratorProjectEngine(Array.Empty<TagHelperDescriptor>(), item.Item, item.FileSystem, item.Options);
                    return item with { CodeDocument = engine.ProcessInitialParse(item.Item) };
                });

            // now we need to do the first parse, with whatever tag helpers we have (but don't care if they changed)
            var initialTagHelperParseItems = initialParseItems
                .Combine(allTagHelpers)
                .WithLambdaComparer((old, @new) => old.Left.Equals(@new.Left), (item) => item.GetHashCode())
                .Select((combined, _) =>
                {
                    var (document, tagHelpers) = combined;

                    var tagHelperEngine = GetGeneratorProjectEngine(tagHelpers, document.Item, document.FileSystem, document.Options);
                    return document with { CodeDocument = tagHelperEngine.ProcessTagHelpers(document.CodeDocument!, tagHelpers, checkForIdempotency: false) };
                });

            // next we do a second parse, but check for idempotency. If the tag helpers used on the previous parse match, the compiler can skip recomputing them
            var secondaryTagHelperParseItems = initialTagHelperParseItems
                .Combine(allTagHelpers)
                .Select((combined, _) =>
                {
                    var (document, tagHelpers) = combined;

                    var tagHelperEngine = GetGeneratorProjectEngine(tagHelpers, document.Item, document.FileSystem, document.Options);
                    return document with { CodeDocument = tagHelperEngine.ProcessTagHelpers(document.CodeDocument!, tagHelpers, checkForIdempotency: true) };
                });

            // at this point we can perform the rest of the parse
            var parsedRazorDocs = secondaryTagHelperParseItems
                .Select((document, _) =>
                {
                    var engine = GetGeneratorProjectEngine(Array.Empty<TagHelperDescriptor>(), document.Item, document.FileSystem, document.Options);
                    return document with { CodeDocument = engine.ProcessRemaining(document.CodeDocument!) };
                });
            var generatedOutput = parsedRazorDocs
                .Select(static (document, _) =>
                {
                    // PROTOTYPE: this step is basically not needed now. we're just adding a file path
                    RazorSourceGeneratorEventSource.Log.RazorCodeGenerateStart(document.Item.FilePath);

                    // Add a generated suffix so tools, such as coverlet, consider the file to be generated
                    var hintName = GetIdentifierFromPath(document.Item.RelativePhysicalPath) + ".g.cs";
                    var csharpDocument = document.CodeDocument.GetCSharpDocument();

                    RazorSourceGeneratorEventSource.Log.RazorCodeGenerateStop(document.Item.FilePath);
                    return (hintName, csharpDocument);
                })
                .WithLambdaComparer(static (a, b) =>
                {
                    if (a.csharpDocument.Diagnostics.Count > 0 || b.csharpDocument.Diagnostics.Count > 0)
                    {
                        // if there are any diagnostics, treat the documents as unequal and force RegisterSourceOutput to be called uncached.
                        return false;
                    }

                    return string.Equals(a.csharpDocument.GeneratedCode, b.csharpDocument.GeneratedCode, StringComparison.Ordinal);
                }, static a => StringComparer.Ordinal.GetHashCode(a.csharpDocument));

            context.RegisterSourceOutput(generatedOutput, static (context, pair) =>
            {
                var (hintName, csharpDocument) = pair;
                RazorSourceGeneratorEventSource.Log.AddSyntaxTrees(hintName);
                for (var i = 0; i < csharpDocument.Diagnostics.Count; i++)
                {
                    var razorDiagnostic = csharpDocument.Diagnostics[i];
                    var csharpDiagnostic = razorDiagnostic.AsDiagnostic();
                    context.ReportDiagnostic(csharpDiagnostic);
                }

                context.AddSource(hintName, csharpDocument.GeneratedCode);
            });
        }
    }
}
