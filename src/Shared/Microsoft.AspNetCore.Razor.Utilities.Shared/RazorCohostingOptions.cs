// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See License.txt in the project root for license information.

namespace Microsoft.AspNetCore.Razor;

internal static class RazorCohostingOptions
{
    /// <summary>
    /// True if razor is running in the cohosting mode
    /// </summary>
    internal static bool UseRazorCohostServer { get; set; } = false;
}
