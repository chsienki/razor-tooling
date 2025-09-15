using System;

namespace Microsoft.NET.Sdk.Razor.SourceGenerators;

internal static class RazorCohostingOptions
{
    /// <summary>
    /// True if razor is running in the cohosting mode
    /// </summary>
    internal static bool UseRazorCohostServer { get; set; } = false;

    internal static bool UseRazorCohostServerWithFallback
    {
        get
        {
            if (UseRazorCohostServer)
            {
                return true;
            }

            // HACK HACK HACK
            // In VS it's possible for us to get called before the razor tooling has a chance to
            // set the flag. If we win that race, we'll be stuck in a situation where we think the generator
            // should be off, but we really want it on. Roslyn will not re-run us until something in the 
            // solution changes, which then breaks co-hosting.
            // Here, we do some reflection to work out if we're in VS, and if we are, manually check the status
            // of the flag when we don't see it on. 

            var globalOptions = Type.GetType("Microsoft.VisualStudio.PlatformUI.GlobalOptions");




            return true;
        }
    }
}
