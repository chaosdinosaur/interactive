// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Microsoft.DotNet.Interactive.Bash;

/// <summary>
/// Discovers available bash environments on the current system.
/// </summary>
public sealed class BashEnvironmentDiscovery
{
    private readonly BashKernelOptions _options;

    public BashEnvironmentDiscovery(BashKernelOptions? options = null)
    {
        _options = options ?? new BashKernelOptions();
    }

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Discovers the best available bash environment.
    /// </summary>
    /// <returns>The discovered bash environment.</returns>
    /// <exception cref="BashNotAvailableException">Thrown when no bash environment is found.</exception>
    public BashEnvironment Discover()
    {
        // Check for explicit path first
        if (!string.IsNullOrEmpty(_options.BashPath))
        {
            if (!File.Exists(_options.BashPath))
            {
                throw new FileNotFoundException(
                    $"Specified bash path not found: {_options.BashPath}");
            }

            return new BashEnvironment
            {
                Type = BashEnvironmentType.Native,
                BashPath = _options.BashPath,
                Description = "User-specified bash"
            };
        }

        // Non-Windows: use native bash
        if (!IsWindows)
        {
            return DiscoverNativeBash()
                ?? throw new BashNotAvailableException();
        }

        // Windows: Check preferred environment first
        if (_options.PreferredEnvironment.HasValue)
        {
            var preferred = _options.PreferredEnvironment.Value switch
            {
                BashEnvironmentType.Wsl => DiscoverWsl(),
                BashEnvironmentType.GitBash => DiscoverGitBash(),
                BashEnvironmentType.Msys2 => DiscoverMsys2(),
                BashEnvironmentType.Cygwin => DiscoverCygwin(),
                _ => null
            };

            if (preferred is not null)
                return preferred;
        }

        // Windows: Standard discovery order
        return DiscoverWsl()
            ?? DiscoverGitBash()
            ?? DiscoverMsys2()
            ?? DiscoverCygwin()
            ?? throw new BashNotAvailableException();
    }

    private BashEnvironment? DiscoverNativeBash()
    {
        string[] paths = ["/bin/bash", "/usr/bin/bash", "/usr/local/bin/bash"];

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                return new BashEnvironment
                {
                    Type = BashEnvironmentType.Native,
                    BashPath = path,
                    Description = "Native bash"
                };
            }
        }

        return null;
    }

    private BashEnvironment? DiscoverWsl()
    {
        var wslPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "wsl.exe");

        if (!File.Exists(wslPath))
            return null;

        // Verify WSL has at least one distribution
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = wslPath,
                    Arguments = "--list --quiet",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.Unicode
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            // Use specified distribution or first available
            var distributions = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (distributions.Length == 0)
                return null;

            var distribution = !string.IsNullOrEmpty(_options.WslDistribution)
                ? _options.WslDistribution
                : distributions[0];

            return new BashEnvironment
            {
                Type = BashEnvironmentType.Wsl,
                BashPath = wslPath,
                WslDistribution = distribution,
                Description = $"WSL ({distribution})"
            };
        }
        catch
        {
            return null;
        }
    }

    private BashEnvironment? DiscoverGitBash()
    {
        // Check registry first (Windows only)
        var regPath = OperatingSystem.IsWindows() ? FindGitBashFromRegistry() : null;
        if (regPath is not null && File.Exists(regPath))
        {
            return new BashEnvironment
            {
                Type = BashEnvironmentType.GitBash,
                BashPath = regPath,
                Description = "Git Bash"
            };
        }

        // Check known paths
        string[] paths =
        [
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Git", "bin", "bash.exe")
        ];

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                return new BashEnvironment
                {
                    Type = BashEnvironmentType.GitBash,
                    BashPath = path,
                    Description = "Git Bash"
                };
            }
        }

        return null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? FindGitBashFromRegistry()
    {
        if (!IsWindows)
            return null;

        try
        {
            // Check HKLM for machine-wide installation
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GitForWindows");
            if (key is not null)
            {
                var installPath = key.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(installPath))
                {
                    return Path.Combine(installPath, "bin", "bash.exe");
                }
            }

            // Check HKCU for user installation
            using var userKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\GitForWindows");
            if (userKey is not null)
            {
                var installPath = userKey.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(installPath))
                {
                    return Path.Combine(installPath, "bin", "bash.exe");
                }
            }
        }
        catch
        {
            // Registry access may fail
        }

        return null;
    }

    private BashEnvironment? DiscoverMsys2()
    {
        string[] paths =
        [
            @"C:\msys64\usr\bin\bash.exe",
            @"C:\msys32\usr\bin\bash.exe",
            @"C:\tools\msys64\usr\bin\bash.exe"
        ];

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                return new BashEnvironment
                {
                    Type = BashEnvironmentType.Msys2,
                    BashPath = path,
                    Description = "MSYS2"
                };
            }
        }

        return null;
    }

    private BashEnvironment? DiscoverCygwin()
    {
        string[] paths =
        [
            @"C:\cygwin64\bin\bash.exe",
            @"C:\cygwin\bin\bash.exe"
        ];

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                return new BashEnvironment
                {
                    Type = BashEnvironmentType.Cygwin,
                    BashPath = path,
                    Description = "Cygwin"
                };
            }
        }

        return null;
    }
}

/// <summary>
/// Exception thrown when no bash environment is available.
/// </summary>
public sealed class BashNotAvailableException : Exception
{
    public BashNotAvailableException() : base(GetErrorMessage())
    {
    }

    private static string GetErrorMessage()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Bash is not installed on this system. Please install bash using your package manager.";
        }

        return """
            Bash is not available on this Windows system.

            To use the bash kernel, install one of the following:

            1. WSL (Windows Subsystem for Linux) - Recommended
               - Open PowerShell as Administrator
               - Run: wsl --install
               - Restart your computer
               - More info: https://aka.ms/wsl

            2. Git for Windows (includes Git Bash)
               - Download from: https://gitforwindows.org/

            3. MSYS2
               - Download from: https://www.msys2.org/

            After installation, restart your notebook session.
            """;
    }
}
