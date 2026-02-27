// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.AspNetCore.Razor.Language.Intermediate;

public sealed class MarkupElementIntermediateNode : IntermediateNode
{
    public IEnumerable<HtmlAttributeIntermediateNode> Attributes => Children.OfType<HtmlAttributeIntermediateNode>();

    public IEnumerable<ReferenceCaptureIntermediateNode> Captures => Children.OfType<ReferenceCaptureIntermediateNode>();

    public IEnumerable<SetKeyIntermediateNode> SetKeys => Children.OfType<SetKeyIntermediateNode>();

    public IEnumerable<IntermediateNode> Body => Children.Where(c =>
    {
        return c is not (ComponentAttributeIntermediateNode or
            HtmlAttributeIntermediateNode or
            SplatIntermediateNode or
            SetKeyIntermediateNode or
            ReferenceCaptureIntermediateNode or
            FormNameIntermediateNode);
    });

    public override IntermediateNodeCollection Children { get => field ??= []; }

    public string TagName { get; set; }

    /// <summary>
    /// The tag mode of the element (e.g., self-closing, start tag and end tag, start tag only).
    /// </summary>
    public TagMode TagMode { get; set; }

    /// <summary>
    /// Index in <see cref="IntermediateNode.Children"/> where the body content starts.
    /// Children before this index are start tag tokens (tag text and attributes).
    /// This is set during lowering and used by the IR rewrite phase to extract body content.
    /// </summary>
    public int BodyStartIndex { get; set; }

    /// <summary>
    /// The source span of the start tag (e.g., &lt;tagname attr="value"&gt;).
    /// Used to reconstruct tag text when flattening non-tag-helper elements.
    /// </summary>
    public SourceSpan? StartTagSource { get; set; }

    /// <summary>
    /// The source span of the end tag (e.g., &lt;/tagname&gt;).
    /// Used to reconstruct tag text when flattening non-tag-helper elements.
    /// </summary>
    public SourceSpan? EndTagSource { get; set; }

    public override void Accept(IntermediateNodeVisitor visitor)
    {
        if (visitor == null)
        {
            throw new ArgumentNullException(nameof(visitor));
        }

        visitor.VisitMarkupElement(this);
    }

    public override void FormatNode(IntermediateNodeFormatter formatter)
    {
        if (formatter == null)
        {
            throw new ArgumentNullException(nameof(formatter));
        }

        formatter.WriteContent(TagName);

        formatter.WriteProperty(nameof(TagName), TagName);
    }
}
