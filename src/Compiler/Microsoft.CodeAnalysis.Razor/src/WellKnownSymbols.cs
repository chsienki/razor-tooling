// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.ComponentModel;
using Microsoft.AspNetCore.Razor.Language.Components;

namespace Microsoft.CodeAnalysis.Razor;

internal class WellKnownSymbols
{
    public WellKnownSymbols(Compilation? compilation)
    {
        if (compilation is null)
        {
            return;
        }

        ComponentBase = compilation.GetTypeByMetadataName(ComponentsApi.ComponentBase.MetadataName);
        IComponent = compilation.GetTypeByMetadataName(ComponentsApi.IComponent.MetadataName);
        ParameterAttribute = compilation.GetTypeByMetadataName(ComponentsApi.ParameterAttribute.MetadataName);
        RenderFragment = compilation.GetTypeByMetadataName(ComponentsApi.RenderFragment.MetadataName);
        RenderFragmentOfT = compilation.GetTypeByMetadataName(ComponentsApi.RenderFragmentOfT.MetadataName);
        EventCallback = compilation.GetTypeByMetadataName(ComponentsApi.EventCallback.MetadataName);
        EventCallbackOfT = compilation.GetTypeByMetadataName(ComponentsApi.EventCallbackOfT.MetadataName);
        ITagHelper = compilation.GetTypeByMetadataName(TagHelperTypes.ITagHelper);
        CascadingTypeParameterAttribute = compilation.GetTypeByMetadataName(ComponentsApi.CascadingTypeParameterAttribute.MetadataName);
        HtmlAttributeNameAttributeSymbol = compilation.GetTypeByMetadataName(TagHelperTypes.HtmlAttributeNameAttribute);
        HtmlAttributeNotBoundAttributeSymbol = compilation.GetTypeByMetadataName(TagHelperTypes.HtmlAttributeNotBoundAttribute);
        HtmlTargetElementAttributeSymbol = compilation.GetTypeByMetadataName(TagHelperTypes.HtmlTargetElementAttribute);
        OutputElementHintAttributeSymbol = compilation.GetTypeByMetadataName(TagHelperTypes.OutputElementHintAttribute);
        RestrictChildrenAttributeSymbol = compilation.GetTypeByMetadataName(TagHelperTypes.RestrictChildrenAttribute);
        EditorBrowsableAttributeSymbol = compilation.GetTypeByMetadataName(typeof(EditorBrowsableAttribute).FullName);
        IDictionarySymbol = compilation.GetTypeByMetadataName(TagHelperTypes.IDictionary);
    }

    public INamedTypeSymbol? ComponentBase { get; private init; }

    public INamedTypeSymbol? IComponent { get; private init; }

    public INamedTypeSymbol? ParameterAttribute { get; private init; }

    public INamedTypeSymbol? RenderFragment { get; private init; }

    public INamedTypeSymbol? RenderFragmentOfT { get; private init; }

    public INamedTypeSymbol? EventCallback { get; private init; }

    public INamedTypeSymbol? EventCallbackOfT { get; private init; }

    public INamedTypeSymbol? ITagHelper { get; internal init; }

    public INamedTypeSymbol? CascadingTypeParameterAttribute { get; private init; }

    public INamedTypeSymbol? HtmlAttributeNameAttributeSymbol { get; private init; }

    public INamedTypeSymbol? HtmlAttributeNotBoundAttributeSymbol { get; private init; }

    public INamedTypeSymbol? HtmlTargetElementAttributeSymbol { get; private init; }

    public INamedTypeSymbol? OutputElementHintAttributeSymbol { get; private init; }

    public INamedTypeSymbol? IDictionarySymbol { get; private init; }

    public INamedTypeSymbol? RestrictChildrenAttributeSymbol { get; private init; }

    public INamedTypeSymbol? EditorBrowsableAttributeSymbol { get; private init; }
}
