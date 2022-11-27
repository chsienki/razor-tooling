
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;

namespace Microsoft.NET.Sdk.Razor.SourceGenerators
{
    internal static class IncrementalValuesProviderExtensions
    {
        internal static IncrementalValueProvider<T> WithLambdaComparer<T>(this IncrementalValueProvider<T> source, Func<T, T, bool> equal, Func<T, int> getHashCode)
        {
            var comparer = new LambdaComparer<T>(equal, getHashCode);
            return source.WithComparer(comparer);
        }

        internal static IncrementalValuesProvider<T> WithLambdaComparer<T>(this IncrementalValuesProvider<T> source, Func<T, T, bool> equal, Func<T, int> getHashCode)
        {
            var comparer = new LambdaComparer<T>(equal, getHashCode);
            return source.WithComparer(comparer);
        }

        internal static IncrementalValuesProvider<TSource> ReportDiagnostics<TSource>(this IncrementalValuesProvider<(TSource?, Diagnostic?)> source, IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(source, (spc, source) =>
            {
                var (sourceItem, diagnostic) = source;
                if (sourceItem == null && diagnostic != null)
                {
                    spc.ReportDiagnostic(diagnostic);
                }
            });

            return source.Where((pair) => pair.Item1 != null).Select((pair, ct) => pair.Item1!);
        }

        internal static IncrementalValueProvider<TSource> ReportDiagnostics<TSource>(this IncrementalValueProvider<(TSource?, Diagnostic?)> source, IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(source, (spc, source) =>
            {
                var (sourceItem, diagnostic) = source;
                if (sourceItem == null && diagnostic != null)
                {
                    spc.ReportDiagnostic(diagnostic);
                }
            });

            return source.Select((pair, ct) => pair.Item1!);
        }



        internal static IncrementalValuesProvider<RazorProjectItemEx> AsProjectItemEx(this IncrementalValuesProvider<SourceGeneratorProjectItem> projectItems, IncrementalValuesProvider<SourceGeneratorProjectItem> imports, IncrementalValueProvider<RazorSourceGenerationOptions> options)
        {
            return projectItems
                .Combine(imports.Collect())
                .WithLambdaComparer((old, @new) => old.Left.Equals(@new.Left) && Enumerable.SequenceEqual(old.Right, @new.Right), (item) => item.GetHashCode())
                .Combine(options)
                .Select((combined, _) =>
                {
                    var ((item, imports), options) = combined;

                    var fileSystem = new VirtualRazorProjectFileSystem();
                    fileSystem.Add(item);
                    foreach (var import in imports)
                    {
                        fileSystem.Add(import);
                    }

                    return new RazorProjectItemEx(item, imports, fileSystem, options);
                });
        }

    }
    internal record RazorProjectItemEx(SourceGeneratorProjectItem Item, IEnumerable<SourceGeneratorProjectItem> Imports, VirtualRazorProjectFileSystem FileSystem, RazorSourceGenerationOptions Options, RazorCodeDocument? CodeDocument = null);


    internal sealed class LambdaComparer<T> : IEqualityComparer<T>
    {
        private readonly Func<T, T, bool> _equal;
        private readonly Func<T, int> _getHashCode;

        public LambdaComparer(Func<T, T, bool> equal, Func<T, int> getHashCode)
        {
            _equal = equal;
            _getHashCode = getHashCode;
        }

        public bool Equals(T x, T y) => _equal(x, y);

        public int GetHashCode(T obj) => _getHashCode(obj);
    }
}
