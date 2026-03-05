// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Tests.Utility;
using Xunit;

namespace Microsoft.DotNet.Interactive.Bash.Tests;

public class BashKernelTests
{
    [SkipOnWindowsWithoutBashFact]
    public async Task It_can_execute_simple_command()
    {
        using var kernel = new BashKernel();

        var result = await kernel.SubmitCodeAsync("echo 'Hello, World!'");

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<StandardOutputValueProduced>()
            .Which.FormattedValues.Should().ContainSingle()
            .Which.Value.Should().Contain("Hello, World!");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_preserves_environment_variables_across_submissions()
    {
        using var kernel = new BashKernel();

        await kernel.SubmitCodeAsync("export MY_VAR='test_value'");
        var result = await kernel.SubmitCodeAsync("echo $MY_VAR");

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<StandardOutputValueProduced>()
            .Which.FormattedValues.Should().ContainSingle()
            .Which.Value.Should().Contain("test_value");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_preserves_working_directory_across_submissions()
    {
        using var kernel = new BashKernel();

        await kernel.SubmitCodeAsync("cd /tmp");
        var result = await kernel.SubmitCodeAsync("pwd");

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<StandardOutputValueProduced>()
            .Which.FormattedValues.Should().ContainSingle()
            .Which.Value.Should().Contain("/tmp");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_reports_exit_code_on_failure()
    {
        using var kernel = new BashKernel();

        var result = await kernel.SubmitCodeAsync("exit 42");

        result.Events.Should().ContainSingle<CommandFailed>()
            .Which.Message.Should().Contain("exit code 42");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_captures_stderr()
    {
        using var kernel = new BashKernel();

        var result = await kernel.SubmitCodeAsync("echo 'error message' >&2");

        result.Events.Should()
            .ContainSingle<StandardErrorValueProduced>()
            .Which.FormattedValues.Should().ContainSingle()
            .Which.Value.Should().Contain("error message");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_can_execute_multiline_script()
    {
        using var kernel = new BashKernel();

        var result = await kernel.SubmitCodeAsync("""
            for i in 1 2 3
            do
                echo "Number: $i"
            done
            """);

        result.Events.Should().NotContainErrors();

        var output = result.Events
            .OfType<StandardOutputValueProduced>()
            .SelectMany(e => e.FormattedValues)
            .Select(fv => fv.Value)
            .ToList();

        output.Should().Contain(v => v.Contains("Number: 1"));
        output.Should().Contain(v => v.Contains("Number: 2"));
        output.Should().Contain(v => v.Contains("Number: 3"));
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task Kernel_info_includes_bash_language()
    {
        using var kernel = new BashKernel();

        kernel.KernelInfo.LanguageName.Should().Be("bash");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_can_be_cancelled_during_long_running_command()
    {
        using var kernel = new BashKernel();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var result = await kernel.SubmitCodeAsync("sleep 10", cts.Token);

        result.Events.Should().ContainSingle<CommandFailed>()
            .Which.Message.Should().Contain("cancelled");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_handles_empty_input_gracefully()
    {
        using var kernel = new BashKernel();

        var result = await kernel.SubmitCodeAsync("");

        result.Events.Should().NotContainErrors();
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_handles_whitespace_only_input_gracefully()
    {
        using var kernel = new BashKernel();

        var result = await kernel.SubmitCodeAsync("   \n\t  \n   ");

        result.Events.Should().NotContainErrors();
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_preserves_function_definitions_across_submissions()
    {
        using var kernel = new BashKernel();

        await kernel.SubmitCodeAsync("greet() { echo \"Hello, $1!\"; }");
        var result = await kernel.SubmitCodeAsync("greet World");

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<StandardOutputValueProduced>()
            .Which.FormattedValues.Should().ContainSingle()
            .Which.Value.Should().Contain("Hello, World!");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_preserves_aliases_across_submissions()
    {
        using var kernel = new BashKernel();

        await kernel.SubmitCodeAsync("shopt -s expand_aliases && alias greet='echo Hello'");
        var result = await kernel.SubmitCodeAsync("shopt -s expand_aliases && greet");

        result.Events.Should().NotContainErrors();
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_handles_unicode_content()
    {
        using var kernel = new BashKernel();

        var result = await kernel.SubmitCodeAsync("echo '你好世界'");

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<StandardOutputValueProduced>()
            .Which.FormattedValues.Should().ContainSingle()
            .Which.Value.Should().Contain("你好世界");
    }
}

/// <summary>
/// Skips test on Windows when bash is not available via WSL or Git Bash.
/// </summary>
public class SkipOnWindowsWithoutBashFactAttribute : FactAttribute
{
    public SkipOnWindowsWithoutBashFactAttribute()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            try
            {
                var discovery = new BashEnvironmentDiscovery();
                discovery.Discover();
            }
            catch (BashNotAvailableException)
            {
                Skip = "Bash is not available on this Windows system (no WSL or Git Bash found)";
            }
        }
    }
}
