// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Razor.Microbenchmarks.Generator.SampleApp;

public record Razor_Added_Independent() : RazorBenchmarks(new(IndependentRazorFile, "/Pages/Generated/0.razor"), "\\0.razor");





public abstract record RazorBenchmarks(ProjectSetup.InMemoryAdditionalText? AddedFile, string? RemovedFileSuffix)
    : Benchmarks
{
    protected override GeneratorDriver UpdateDriver(ProjectSetup.RazorProject project)
    {
        var removed = RemovedFileSuffix is not null ? ExistingByEnding(RemovedFileSuffix) : null;

        if (AddedFile is not null && removed is not null)
        {
            return project.GeneratorDriver.ReplaceAdditionalText(AddedFile, removed);
        }
        else if (AddedFile is not null)
        {
            return project.GeneratorDriver.AddAdditionalTexts(ImmutableArray.Create((AdditionalText)AddedFile));
        }
        else if (removed is not null)
        {
            return project.GeneratorDriver.RemoveAdditionalTexts(ImmutableArray.Create(removed));
        }
        return project.GeneratorDriver;
    }

    protected const string IndependentRazorFile = "<h1>Independent File</h1>";

}