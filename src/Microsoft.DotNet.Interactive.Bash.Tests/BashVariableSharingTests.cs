// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.CSharp;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Tests.Utility;
using Xunit;

namespace Microsoft.DotNet.Interactive.Bash.Tests;

public class BashVariableSharingTests
{
    [SkipOnWindowsWithoutBashFact]
    public async Task It_can_share_string_variable_from_bash_to_csharp()
    {
        using var compositeKernel = new CompositeKernel
        {
            new BashKernel(),
            new CSharpKernel().UseValueSharing()
        };

        compositeKernel.DefaultKernelName = "bash";

        // Set variable in bash
        await compositeKernel.SubmitCodeAsync("export MY_VALUE='hello from bash'");

        // Share to C#
        await compositeKernel.SubmitCodeAsync("#!csharp\n#!share --from bash MY_VALUE");

        // Verify in C#
        var result = await compositeKernel.SubmitCodeAsync("#!csharp\nMY_VALUE");

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<ReturnValueProduced>()
            .Which.Value.Should().Be("hello from bash");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_can_share_variable_from_csharp_to_bash()
    {
        using var compositeKernel = new CompositeKernel
        {
            new CSharpKernel().UseValueSharing(),
            new BashKernel()
        };

        compositeKernel.DefaultKernelName = "csharp";

        // Set variable in C#
        await compositeKernel.SubmitCodeAsync("#!csharp\nvar greeting = \"Hello from C#\";");

        // Share to bash
        await compositeKernel.SubmitCodeAsync("#!bash\n#!share --from csharp greeting");

        // Verify in bash
        var result = await compositeKernel.SubmitCodeAsync("#!bash\necho $greeting");

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<StandardOutputValueProduced>()
            .Which.FormattedValues.Should().ContainSingle()
            .Which.Value.Should().Contain("Hello from C#");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_can_request_value_infos()
    {
        using var kernel = new BashKernel();

        await kernel.SubmitCodeAsync("export TEST_VAR1='value1'");
        await kernel.SubmitCodeAsync("export TEST_VAR2='value2'");

        var result = await kernel.SendAsync(new RequestValueInfos());

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<ValueInfosProduced>()
            .Which.ValueInfos.Should()
            .Contain(v => v.Name == "TEST_VAR1")
            .And
            .Contain(v => v.Name == "TEST_VAR2");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_can_request_specific_value()
    {
        using var kernel = new BashKernel();

        await kernel.SubmitCodeAsync("export MY_VAR='specific_value'");

        var result = await kernel.SendAsync(new RequestValue("MY_VAR"));

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<ValueProduced>()
            .Which.Value.Should().Be("specific_value");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_can_receive_sent_value()
    {
        using var kernel = new BashKernel();

        await kernel.SendAsync(new SendValue("RECEIVED_VAR", "sent_value", null));

        var result = await kernel.SubmitCodeAsync("echo $RECEIVED_VAR");

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<StandardOutputValueProduced>()
            .Which.FormattedValues.Should().ContainSingle()
            .Which.Value.Should().Contain("sent_value");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_serializes_complex_types_as_json()
    {
        using var compositeKernel = new CompositeKernel
        {
            new CSharpKernel().UseValueSharing(),
            new BashKernel()
        };

        compositeKernel.DefaultKernelName = "csharp";

        // Set complex object in C#
        await compositeKernel.SubmitCodeAsync(
            "#!csharp\nvar data = new { Name = \"Test\", Value = 42 };");

        // Share to bash
        await compositeKernel.SubmitCodeAsync("#!bash\n#!share --from csharp data");

        // Verify in bash - should be JSON
        var result = await compositeKernel.SubmitCodeAsync("#!bash\necho $data");

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<StandardOutputValueProduced>()
            .Which.FormattedValues.Should().ContainSingle()
            .Which.Value.Should().Contain("Name").And.Contain("Test");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_fails_when_requesting_nonexistent_variable()
    {
        using var kernel = new BashKernel();

        var result = await kernel.SendAsync(new RequestValue("NONEXISTENT_VAR_12345"));

        result.Events.Should().ContainSingle<CommandFailed>()
            .Which.Message.Should().Contain("not found");
    }

    [SkipOnWindowsWithoutBashFact]
    public async Task It_handles_special_characters_in_sent_values()
    {
        using var kernel = new BashKernel();

        await kernel.SendAsync(new SendValue("SPECIAL", "value with 'quotes' and spaces", null));
        var result = await kernel.SubmitCodeAsync("echo \"$SPECIAL\"");

        result.Events.Should()
            .NotContainErrors()
            .And
            .ContainSingle<StandardOutputValueProduced>()
            .Which.FormattedValues.Should().ContainSingle()
            .Which.Value.Should().Contain("quotes");
    }
}
