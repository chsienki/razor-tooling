// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Razor.Language;

namespace Microsoft.CodeAnalysis.Razor;

internal static class TagHelperTargetAssemblyExtensions
{
    private static readonly object TargetAssemblyKey = new object();

    public static ISymbol? GetTargetAssembly(this ItemCollection items)
    {
        if (items.Count == 0 || items[TargetAssemblyKey] is not ISymbol symbol)
        {
            return null;
        }

        return symbol;
    }

    public static void SetTargetAssembly(this ItemCollection items, ISymbol symbol)
    {
        items[TargetAssemblyKey] = symbol;
    }
}
