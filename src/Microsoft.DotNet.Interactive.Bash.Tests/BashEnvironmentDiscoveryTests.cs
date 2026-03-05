// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace Microsoft.DotNet.Interactive.Bash.Tests;

public class BashEnvironmentDiscoveryTests
{
    [Fact]
    public void It_throws_BashNotAvailableException_when_no_bash_found()
    {
        var options = new BashKernelOptions
        {
            BashPath = "/nonexistent/path/to/bash"
        };

        var discovery = new BashEnvironmentDiscovery(options);

        var action = () => discovery.Discover();

        action.Should().Throw<System.IO.FileNotFoundException>();
    }

    [SkipOnWindowsWithoutBashFact]
    public void It_discovers_bash_environment()
    {
        var discovery = new BashEnvironmentDiscovery();

        var environment = discovery.Discover();

        environment.Should().NotBeNull();
        environment.BashPath.Should().NotBeNullOrEmpty();
        environment.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void It_respects_explicit_bash_path()
    {
        string testPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, we can test with a path that doesn't exist
            // The discovery will throw FileNotFoundException
            testPath = @"C:\test\bash.exe";

            var options = new BashKernelOptions { BashPath = testPath };
            var discovery = new BashEnvironmentDiscovery(options);

            var action = () => discovery.Discover();
            action.Should().Throw<System.IO.FileNotFoundException>();
        }
        else
        {
            // On Linux/macOS, use the actual bash path
            testPath = "/bin/bash";

            if (System.IO.File.Exists(testPath))
            {
                var options = new BashKernelOptions { BashPath = testPath };
                var discovery = new BashEnvironmentDiscovery(options);

                var environment = discovery.Discover();

                environment.BashPath.Should().Be(testPath);
            }
        }
    }

    [WindowsOnlyFact]
    public void It_detects_correct_platform()
    {
        BashEnvironmentDiscovery.IsWindows.Should().BeTrue();
        BashEnvironmentDiscovery.IsLinux.Should().BeFalse();
        BashEnvironmentDiscovery.IsMacOS.Should().BeFalse();
    }

    [LinuxOnlyFact]
    public void On_linux_it_prefers_native_bash()
    {
        var discovery = new BashEnvironmentDiscovery();

        var environment = discovery.Discover();

        environment.Type.Should().Be(BashEnvironmentType.Native);
    }

    [MacOSOnlyFact]
    public void On_macos_it_prefers_native_bash()
    {
        var discovery = new BashEnvironmentDiscovery();

        var environment = discovery.Discover();

        environment.Type.Should().Be(BashEnvironmentType.Native);
    }
}

public class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Skip = "This test only runs on Windows";
        }
    }
}

public class LinuxOnlyFactAttribute : FactAttribute
{
    public LinuxOnlyFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Skip = "This test only runs on Linux";
        }
    }
}

public class MacOSOnlyFactAttribute : FactAttribute
{
    public MacOSOnlyFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Skip = "This test only runs on macOS";
        }
    }
}
