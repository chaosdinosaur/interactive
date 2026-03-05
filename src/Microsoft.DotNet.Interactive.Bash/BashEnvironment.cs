// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.DotNet.Interactive.Bash;

/// <summary>
/// Types of bash environments that can be detected.
/// </summary>
public enum BashEnvironmentType
{
    Native,
    Wsl,
    GitBash,
    Msys2,
    Cygwin
}

/// <summary>
/// Represents a detected bash environment.
/// </summary>
public sealed class BashEnvironment
{
    public required BashEnvironmentType Type { get; init; }
    public required string BashPath { get; init; }
    public required string Description { get; init; }
    public string? WslDistribution { get; init; }

    public ProcessStartInfo CreateProcessStartInfo()
    {
        return Type switch
        {
            BashEnvironmentType.Native => CreateNativeStartInfo(),
            BashEnvironmentType.Wsl => CreateWslStartInfo(),
            BashEnvironmentType.GitBash => CreateGitBashStartInfo(),
            BashEnvironmentType.Msys2 => CreateMsys2StartInfo(),
            BashEnvironmentType.Cygwin => CreateCygwinStartInfo(),
            _ => throw new NotSupportedException($"Environment type {Type} is not supported.")
        };
    }

    private ProcessStartInfo CreateNativeStartInfo() => new()
    {
        FileName = BashPath,
        Arguments = "--norc --noprofile",
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        StandardOutputEncoding = System.Text.Encoding.UTF8,
        StandardErrorEncoding = System.Text.Encoding.UTF8
    };

    private ProcessStartInfo CreateWslStartInfo() => new()
    {
        FileName = "wsl.exe",
        Arguments = WslDistribution is not null
            ? $"-d {WslDistribution} -- bash --norc --noprofile"
            : "-- bash --norc --noprofile",
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        StandardOutputEncoding = System.Text.Encoding.UTF8,
        StandardErrorEncoding = System.Text.Encoding.UTF8
    };

    private ProcessStartInfo CreateGitBashStartInfo() => CreateNativeStartInfo();

    private ProcessStartInfo CreateMsys2StartInfo() => CreateNativeStartInfo();

    private ProcessStartInfo CreateCygwinStartInfo() => CreateNativeStartInfo();
}
