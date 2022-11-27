// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor;

namespace Microsoft.NET.Sdk.Razor.SourceGenerators
{
    public partial class RazorSourceGenerator
    {
        private static string GetIdentifierFromPath(string filePath)
        {
            var builder = new StringBuilder(filePath.Length);

            for (var i = 0; i < filePath.Length; i++)
            {
                switch (filePath[i])
                {
                    case ':' or '\\' or '/':
                    case char ch when !char.IsLetterOrDigit(ch):
                        builder.Append('_');
                        break;
                    default:
                        builder.Append(filePath[i]);
                        break;
                }
            }

            return builder.ToString();
        }

        internal static RazorProjectEngine GetDeclarationProjectEngine(
            SourceGeneratorProjectItem item,
            IEnumerable<SourceGeneratorProjectItem> imports,
            RazorSourceGenerationOptions razorSourceGeneratorOptions)
        {
            var fileSystem = new VirtualRazorProjectFileSystem();
            fileSystem.Add(item);
            foreach (var import in imports)
            {
                fileSystem.Add(import);
            }

            var discoveryProjectEngine = RazorProjectEngine.Create(razorSourceGeneratorOptions.Configuration, fileSystem, b =>
            {
                b.Features.Add(new DefaultTypeNameFeature());
                b.Features.Add(new ConfigureRazorCodeGenerationOptions(options =>
                {
                    options.SuppressPrimaryMethodBody = true;
                    options.SuppressChecksum = true;
                    options.SupportLocalizedComponentNames = razorSourceGeneratorOptions.SupportLocalizedComponentNames;
                }));

                b.SetRootNamespace(razorSourceGeneratorOptions.RootNamespace);

                CompilerFeatures.Register(b);
                RazorExtensions.Register(b);

                b.SetCSharpLanguageVersion(razorSourceGeneratorOptions.CSharpLanguageVersion);
            });

            return discoveryProjectEngine;
        }

        internal static RazorProjectEngine GetDiscoveryProjectEngine(
            IReadOnlyList<MetadataReference> references,
            StaticCompilationTagHelperFeature tagHelperFeature)
        {
            var discoveryProjectEngine = RazorProjectEngine.Create(RazorConfiguration.Default, new VirtualRazorProjectFileSystem(), b =>
            {
                b.Features.Add(new DefaultMetadataReferenceFeature { References = references });
                b.Features.Add(tagHelperFeature);
                b.Features.Add(new DefaultTagHelperDescriptorProvider());

                CompilerFeatures.Register(b);
                RazorExtensions.Register(b);
            });

            return discoveryProjectEngine;
        }



        internal static RazorProjectEngine GetInitialParseProjectEngine(RazorProjectItemEx projectItem)
        {
            var projectEngine = RazorProjectEngine.CreateParseEngine(projectItem.Options.Configuration, projectItem.FileSystem, b =>
            {
                b.Features.Add(new DefaultTypeNameFeature());
                b.SetRootNamespace(projectItem.Options.RootNamespace);

                b.Features.Add(new ConfigureRazorCodeGenerationOptions(options =>
                {
                    options.SuppressMetadataSourceChecksumAttributes = !projectItem.Options.GenerateMetadataSourceChecksumAttributes;
                    options.SupportLocalizedComponentNames = projectItem.Options.SupportLocalizedComponentNames;
                }));

                CompilerFeatures.Register(b);
                RazorExtensions.Register(b);

                b.SetCSharpLanguageVersion(projectItem.Options.CSharpLanguageVersion);
            }, preParse: true);

            return projectEngine;
        }

        internal static RazorProjectEngine GetTagHelperParseProjectEngine(
            IReadOnlyList<TagHelperDescriptor> tagHelpers,
            RazorProjectItemEx itemEx,
            bool checkIdempotency)
        {

            var projectEngine = RazorProjectEngine.CreateParseEngine(itemEx.Options.Configuration, itemEx.FileSystem, b =>
            {
                b.Features.Add(new DefaultTypeNameFeature());
                b.SetRootNamespace(itemEx.Options.RootNamespace);

                b.Features.Add(new ConfigureRazorCodeGenerationOptions(options =>
                {
                    options.SuppressMetadataSourceChecksumAttributes = !itemEx.Options.GenerateMetadataSourceChecksumAttributes;
                    options.SupportLocalizedComponentNames = itemEx.Options.SupportLocalizedComponentNames;
                }));


                b.Features.Add(new StaticTagHelperFeature { TagHelpers = tagHelpers });
                b.Features.Add(new DefaultTagHelperDescriptorProvider());

                CompilerFeatures.Register(b);
                RazorExtensions.Register(b);

                b.SetCSharpLanguageVersion(itemEx.Options.CSharpLanguageVersion);
            }, preParse: false);

            return projectEngine;
        }

        internal static SourceGeneratorRazorProjectEngine GetGeneratorProjectEngine(
            IReadOnlyList<TagHelperDescriptor> tagHelpers,
            SourceGeneratorProjectItem item,
            RazorProjectFileSystem fileSystem,
            RazorSourceGenerationOptions razorSourceGeneratorOptions)
        {
            var projectEngine = SourceGeneratorRazorProjectEngine.CreateSourceGeneratorEngine(razorSourceGeneratorOptions.Configuration, fileSystem, b =>
            {
                b.Features.Add(new DefaultTypeNameFeature());
                b.SetRootNamespace(razorSourceGeneratorOptions.RootNamespace);

                b.Features.Add(new ConfigureRazorCodeGenerationOptions(options =>
                {
                    options.SuppressMetadataSourceChecksumAttributes = !razorSourceGeneratorOptions.GenerateMetadataSourceChecksumAttributes;
                    options.SupportLocalizedComponentNames = razorSourceGeneratorOptions.SupportLocalizedComponentNames;
                }));

                b.Features.Add(new StaticTagHelperFeature { TagHelpers = tagHelpers });
                b.Features.Add(new DefaultTagHelperDescriptorProvider());

                CompilerFeatures.Register(b);
                RazorExtensions.Register(b);

                b.SetCSharpLanguageVersion(razorSourceGeneratorOptions.CSharpLanguageVersion);
            });

            return projectEngine;

        }


        internal static RazorProjectEngine GetGenerationProjectEngine(
            IReadOnlyList<TagHelperDescriptor> tagHelpers,
            SourceGeneratorProjectItem item,
            IEnumerable<SourceGeneratorProjectItem> imports,
            RazorSourceGenerationOptions razorSourceGeneratorOptions)
        {
            var fileSystem = new VirtualRazorProjectFileSystem();
            fileSystem.Add(item);
            foreach (var import in imports)
            {
                fileSystem.Add(import);
            }

            var projectEngine = RazorProjectEngine.Create(razorSourceGeneratorOptions.Configuration, fileSystem, b =>
            {
                b.Features.Add(new DefaultTypeNameFeature());
                b.SetRootNamespace(razorSourceGeneratorOptions.RootNamespace);

                b.Features.Add(new ConfigureRazorCodeGenerationOptions(options =>
                {
                    options.SuppressMetadataSourceChecksumAttributes = !razorSourceGeneratorOptions.GenerateMetadataSourceChecksumAttributes;
                    options.SupportLocalizedComponentNames = razorSourceGeneratorOptions.SupportLocalizedComponentNames;
                }));

                b.Features.Add(new StaticTagHelperFeature { TagHelpers = tagHelpers });
                b.Features.Add(new DefaultTagHelperDescriptorProvider());

                CompilerFeatures.Register(b);
                RazorExtensions.Register(b);

                b.SetCSharpLanguageVersion(razorSourceGeneratorOptions.CSharpLanguageVersion);
            });

            return projectEngine;
        }
    }
}
