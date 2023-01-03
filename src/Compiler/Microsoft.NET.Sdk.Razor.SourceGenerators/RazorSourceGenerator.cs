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
                .WithLambdaComparer((old, @new) => (old.Right.Equals(@new.Right) && old.Left.Left.Equals(@new.Left.Left) && old.Left.Right.SequenceEqual(@new.Left.Right)), (a) => a.GetHashCode())
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
            
            // TODO: can the compilation affect the tag helper? Yes. we still need to look at the compilation unfortunately
            //       Hmm, how though? It seems to use the FQN of the type? yes, it does. Doh
            var tagHelpersFromComponents = generatedDeclarationSyntaxTrees
                .Combine(compilation)
                .Combine(razorSourceGeneratorOptions)
                .SelectMany(static (pair, _) =>
                {
                    //TODO: hmm. If this is only coming from components, is there any need to run any of the other tag helper kinds??

                    RazorSourceGeneratorEventSource.Log.DiscoverTagHelpersFromCompilationStart();

                    var ((generatedDeclarationSyntaxTree, compilation), razorSourceGeneratorOptions) = pair;

                    var tagHelperFeature = new StaticCompilationTagHelperFeature();
                    var discoveryProjectEngine = GetDiscoveryProjectEngine(compilation.References.ToImmutableArray(), tagHelperFeature);

                    var compilationWithDeclarations = compilation.AddSyntaxTrees(generatedDeclarationSyntaxTree);

                    var classSyntax = generatedDeclarationSyntaxTree.GetRoot().ChildNodes().Single(n => n.IsKind(CodeAnalysis.CSharp.SyntaxKind.NamespaceDeclaration)).ChildNodes().Single(n => n.IsKind(CodeAnalysis.CSharp.SyntaxKind.ClassDeclaration));
                    var classSymbol = compilationWithDeclarations.GetSemanticModel(generatedDeclarationSyntaxTree).GetDeclaredSymbol(classSyntax);

                    tagHelperFeature.Compilation = compilationWithDeclarations;
                    tagHelperFeature.TargetAssembly = classSymbol;

                    var result = (IList<TagHelperDescriptor>)tagHelperFeature.GetDescriptors();
                    RazorSourceGeneratorEventSource.Log.DiscoverTagHelpersFromCompilationStop();
                    return result;
                });

            var tagHelpersFromCompilation = compilation
            .Combine(razorSourceGeneratorOptions)
            .Select(static (pair, _) =>
            {
                RazorSourceGeneratorEventSource.Log.DiscoverTagHelpersFromCompilationStart();

                var (compilation, razorSourceGeneratorOptions) = pair;

                var tagHelperFeature = new StaticCompilationTagHelperFeature();
                var discoveryProjectEngine = GetDiscoveryProjectEngine(compilation.References.ToImmutableArray(), tagHelperFeature);

                tagHelperFeature.Compilation = compilation;
                tagHelperFeature.TargetAssembly = compilation.Assembly;

                var result = (IList<TagHelperDescriptor>)tagHelperFeature.GetDescriptors();
                RazorSourceGeneratorEventSource.Log.DiscoverTagHelpersFromCompilationStop();
                return result;
            });

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

            var allTagHelpers = tagHelpersFromComponents.Collect()
                .Combine(tagHelpersFromCompilation)
                .Combine(tagHelpersFromReferences)
                .Select(static (pair, _) =>
                {
                    var ((tagHelpersFromComponents, tagHelpersFromCompilation), tagHelpersFromReferences) = pair;
                    var count = tagHelpersFromCompilation.Count + tagHelpersFromReferences.Count + tagHelpersFromComponents.Length;
                    if (count == 0)
                    {
                        return Array.Empty<TagHelperDescriptor>();
                    }

                    var allTagHelpers = new TagHelperDescriptor[count];
                    tagHelpersFromCompilation.CopyTo(allTagHelpers, 0);
                    tagHelpersFromReferences.CopyTo(allTagHelpers, tagHelpersFromCompilation.Count);
                    tagHelpersFromComponents.CopyTo(allTagHelpers, tagHelpersFromCompilation.Count + tagHelpersFromReferences.Count);

                    return allTagHelpers;
                });

            var initialProcess = sourceItems
                .Combine(importFiles.Collect())
                .WithLambdaComparer((old, @new) => old.Left.Equals(@new.Left) && old.Right.SequenceEqual(@new.Right), (a) => GetHashCode())
                .Combine(razorSourceGeneratorOptions)
                .Select(static (pair, _) =>
                {
                    var ((sourceItem, imports), razorSourceGeneratorOptions) = pair;

                    RazorSourceGeneratorEventSource.Log.RazorCodeGenerateStart(sourceItem.FilePath);

                    // Add a generated suffix so tools, such as coverlet, consider the file to be generated
                    var hintName = GetIdentifierFromPath(sourceItem.RelativePhysicalPath) + ".g.cs";

                    var tagHelperFeature = new StaticTagHelperFeature();
                    var projectEngine = (SourceGeneratorRazorProjectEngine)GetGenerationProjectEngine(sourceItem, imports, razorSourceGeneratorOptions, tagHelperFeature);

                    var codeDocument = projectEngine.ProcessInitialParse(sourceItem);
                    RazorSourceGeneratorEventSource.Log.RazorCodeGenerateStop(sourceItem.FilePath);

                    return (projectEngine, hintName, codeDocument, tagHelperFeature);
                })

                // Add the tag helpers in, but ignore if they've changed or not
                .Combine(allTagHelpers)
                .WithLambdaComparer((old, @new) => old.Left.Equals(@new.Left), (item) => item.GetHashCode())
                .Select((pair, _) =>
                {
                    var ((projectEngine, hintName, codeDocument, tagHelperFeature), allTagHelpers) = pair;
                    //TODO: why is this getting called during remove operations?
                    tagHelperFeature.TagHelpers = allTagHelpers;
                    codeDocument = projectEngine.ProcessTagHelpers(codeDocument, allTagHelpers, false);
                    return (projectEngine, hintName, codeDocument);
                })

                // next we do a second parse, along with the helper, but check for idempotency. If the tag helpers used on the previous parse match, the compiler can skip re-computing them
                .Combine(allTagHelpers)
                .Select((pair, _) => {
                    
                    var ((projectEngine, hintName, codeDocument), allTagHelpers) = pair;
                    codeDocument = projectEngine.ProcessTagHelpers(codeDocument, allTagHelpers, true);
                    return (projectEngine, hintName, codeDocument);
                })
                .Select((pair, _) =>
                {
                    var (projectEngine, hintName, codeDocument) = pair;
                    codeDocument = projectEngine.ProcessRemaining(codeDocument);
                    var csharpDocument = codeDocument.GetCSharpDocument();

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

            context.RegisterSourceOutput(initialProcess, static (context, pair) =>
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
