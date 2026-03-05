// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DotNet.Interactive.Bash;

/// <summary>
/// Configuration options for the bash kernel.
/// </summary>
public class BashKernelOptions
{
    /// <summary>
    /// Explicit path to bash executable. If set, disables auto-discovery.
    /// </summary>
    public string? BashPath { get; set; }

    /// <summary>
    /// Preferred environment type for Windows (e.g., Wsl, GitBash).
    /// When set, the specified environment is tried first before fallback.
    /// </summary>
    public BashEnvironmentType? PreferredEnvironment { get; set; }

    /// <summary>
    /// WSL distribution to use when multiple are installed.
    /// If not specified, uses the default distribution.
    /// </summary>
    public string? WslDistribution { get; set; }
}
