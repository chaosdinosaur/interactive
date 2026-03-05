// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Formatting;
using Microsoft.DotNet.Interactive.ValueSharing;

namespace Microsoft.DotNet.Interactive.Bash;

/// <summary>
/// A kernel that executes bash shell scripts with persistent session state.
/// </summary>
public partial class BashKernel :
    Kernel,
    IKernelCommandHandler<SubmitCode>,
    IKernelCommandHandler<RequestValueInfos>,
    IKernelCommandHandler<RequestValue>,
    IKernelCommandHandler<SendValue>
{
    public const string DefaultKernelName = "bash";

    private readonly BashKernelOptions _options;
    private readonly BashEnvironment _environment;
    private readonly string _uniquePrompt;
    private readonly ConcurrentDictionary<string, object?> _variables = new();
    private readonly ConcurrentDictionary<string, string> _environmentVariables = new();

    private Process? _bashProcess;
    private StreamWriter? _stdin;
    private TaskCompletionSource<int>? _currentCompletion;
    private KernelInvocationContext? _currentContext;
    private readonly object _syncLock = new();

    public BashKernel(string name = DefaultKernelName, BashKernelOptions? options = null)
        : base(name)
    {
        _options = options ?? new BashKernelOptions();
        _uniquePrompt = $"DOTNET_INTERACTIVE_PROMPT_{Guid.NewGuid():N}";

        var discovery = new BashEnvironmentDiscovery(_options);
        _environment = discovery.Discover();

        KernelInfo.LanguageName = "bash";
        KernelInfo.LanguageVersion = "5.0";
        KernelInfo.DisplayName = $"{KernelInfo.LocalName} - Bash ({_environment.Description})";
        KernelInfo.Description = "Execute bash shell scripts";

        RegisterForDisposal(Disposable.Create(StopBashProcess));
    }

    private void EnsureBashProcessStarted()
    {
        if (_bashProcess is not null && !_bashProcess.HasExited)
            return;

        StartBashProcess();
    }

    private void StartBashProcess()
    {
        StopBashProcess();

        var startInfo = _environment.CreateProcessStartInfo();

        // Add tracked environment variables
        foreach (var (name, value) in _environmentVariables)
        {
            startInfo.EnvironmentVariables[name] = value;
        }

        _bashProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        _bashProcess.OutputDataReceived += OnOutputReceived;
        _bashProcess.ErrorDataReceived += OnErrorReceived;
        _bashProcess.Exited += OnProcessExited;

        _bashProcess.Start();
        _bashProcess.BeginOutputReadLine();
        _bashProcess.BeginErrorReadLine();

        _stdin = _bashProcess.StandardInput;

        // Initialize session with unique prompt
        _stdin.WriteLine($"export PS1='{_uniquePrompt}>'");
        _stdin.WriteLine($"export PS2='{_uniquePrompt}+'");
        _stdin.WriteLine("export PROMPT_COMMAND=''");
        _stdin.WriteLine("export PAGER=cat");
        _stdin.Flush();
    }

    private void StopBashProcess()
    {
        if (_bashProcess is null)
            return;

        try
        {
            _bashProcess.OutputDataReceived -= OnOutputReceived;
            _bashProcess.ErrorDataReceived -= OnErrorReceived;
            _bashProcess.Exited -= OnProcessExited;

            if (!_bashProcess.HasExited)
            {
                _stdin?.WriteLine("exit");
                _stdin?.Flush();

                if (!_bashProcess.WaitForExit(1000))
                {
                    _bashProcess.Kill();
                }
            }

            _bashProcess.Dispose();
        }
        catch
        {
            // Ignore cleanup errors
        }
        finally
        {
            _bashProcess = null;
            _stdin = null;
        }
    }

    private void InterruptBashProcess()
    {
        if (_bashProcess is null || _bashProcess.HasExited)
            return;

        try
        {
            var pid = _bashProcess.Id;

            // On Unix-like systems, send SIGINT to the process group
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                // Get the actual process group ID (PGID) - it may differ from PID
                var pgid = GetProcessGroupId(pid);

                // Step 1: Send SIGINT to the process group
                var signalSent = SendSignalToProcessGroup(pgid ?? pid, "INT");

                // Step 2: Wait briefly and check if process terminated
                if (signalSent && _bashProcess is not null && !_bashProcess.HasExited)
                {
                    Thread.Sleep(200);
                }

                // Step 3: If still running, send SIGTERM to process group
                if (_bashProcess is not null && !_bashProcess.HasExited)
                {
                    SendSignalToProcessGroup(pgid ?? pid, "TERM");
                    Thread.Sleep(200);
                }

                // Step 4: If still running, force kill the entire process tree
                if (_bashProcess is not null && !_bashProcess.HasExited)
                {
                    try
                    {
                        // Use pkill to kill all processes in the process group
                        using var pkillProcess = Process.Start(new ProcessStartInfo
                        {
                            FileName = "pkill",
                            Arguments = $"-9 -g {pgid ?? pid}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        });
                        pkillProcess?.WaitForExit(500);
                    }
                    catch
                    {
                        // Fallback to .NET Kill
                        try
                        {
                            _bashProcess?.Kill(entireProcessTree: true);
                        }
                        catch
                        {
                            // Ignore kill errors
                        }
                    }
                }

                // Step 5: If process was killed, restart for next command
                if (_bashProcess is not null && _bashProcess.HasExited)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            StopBashProcess();
                        }
                        catch
                        {
                            // Ignore cleanup errors
                        }
                    });
                }
            }
            else
            {
                // On Windows, send Ctrl+C through stdin, then force kill if needed
                _stdin?.Write('\x03');
                _stdin?.Flush();

                Thread.Sleep(300);

                if (_bashProcess is not null && !_bashProcess.HasExited)
                {
                    try
                    {
                        _bashProcess.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // Ignore kill errors
                    }
                }
            }
        }
        catch
        {
            // Ignore interrupt errors
        }
    }

    private static int? GetProcessGroupId(int pid)
    {
        try
        {
            using var psProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "ps",
                Arguments = $"-o pgid= -p {pid}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (psProcess is not null)
            {
                var output = psProcess.StandardOutput.ReadToEnd().Trim();
                psProcess.WaitForExit(1000);

                if (int.TryParse(output, out var pgid))
                {
                    return pgid;
                }
            }
        }
        catch
        {
            // Ignore errors, will fall back to using PID
        }

        return null;
    }

    private static bool SendSignalToProcessGroup(int pgid, string signal)
    {
        try
        {
            // Send signal to entire process group (negative PGID)
            using var killProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "kill",
                Arguments = $"-{signal} -{pgid}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            killProcess?.WaitForExit(500);
            return true;
        }
        catch
        {
            // Try sending to just the PGID as a regular PID
            try
            {
                using var killProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "kill",
                    Arguments = $"-{signal} {pgid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                killProcess?.WaitForExit(500);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private void OnOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
            return;

        // Detect exit code marker
        var exitMarker = $"{_uniquePrompt}EXIT:";
        if (e.Data.StartsWith(exitMarker))
        {
            var exitCodeStr = e.Data.Substring(exitMarker.Length).Trim();
            var match = ExitCodeRegex().Match(exitCodeStr);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var exitCode))
            {
                _currentCompletion?.TrySetResult(exitCode);
            }
            else
            {
                // Fallback: assume success if we can't parse
                _currentCompletion?.TrySetResult(0);
            }
            return;
        }

        // Skip prompt lines
        if (e.Data.StartsWith(_uniquePrompt))
            return;

        // Stream output to notebook
        _currentContext?.Publish(new StandardOutputValueProduced(
            _currentContext.Command,
            [new FormattedValue("text/plain", e.Data + Environment.NewLine)]));
    }

    private void OnErrorReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
            return;

        _currentContext?.Publish(new StandardErrorValueProduced(
            _currentContext.Command,
            [new FormattedValue("text/plain", e.Data + Environment.NewLine)]));
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        // Process exited unexpectedly - complete any pending operation
        _currentCompletion?.TrySetException(
            new InvalidOperationException("Bash process terminated unexpectedly."));
    }

    [GeneratedRegex(@"^(\d+)$")]
    private static partial Regex ExitCodeRegex();

    [GeneratedRegex(@"declare -x\s+(\w+)(?:=""(.*)"")?$", RegexOptions.Multiline)]
    private static partial Regex ExportedVariableRegex();

    async Task IKernelCommandHandler<SubmitCode>.HandleAsync(
        SubmitCode command,
        KernelInvocationContext context)
    {
        context.Publish(new CodeSubmissionReceived(command));

        var code = command.Code.Trim();
        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        context.Publish(new CompleteCodeSubmissionReceived(command));

        EnsureBashProcessStarted();

        var completionSource = new TaskCompletionSource<int>();

        lock (_syncLock)
        {
            _currentContext = context;
            _currentCompletion = completionSource;
        }

        try
        {
            // Send code to bash
            await _stdin!.WriteLineAsync(code);

            // Send exit code capture command
            await _stdin.WriteLineAsync($"echo \"{_uniquePrompt}EXIT:$?\"");
            await _stdin.FlushAsync();

            // Wait for completion with cancellation support
            using var registration = context.CancellationToken.Register(() =>
            {
                InterruptBashProcess();
                completionSource.TrySetCanceled();
            });

            var exitCode = await completionSource.Task;

            // Capture exported variables after execution
            await CaptureExportedVariablesAsync();

            if (exitCode != 0)
            {
                context.Fail(command, message: $"Command failed with exit code {exitCode}");
            }
        }
        catch (OperationCanceledException)
        {
            context.Fail(command, message: "Command was cancelled");
        }
        catch (Exception ex)
        {
            context.Fail(command, exception: ex);
        }
        finally
        {
            lock (_syncLock)
            {
                _currentContext = null;
                _currentCompletion = null;
            }
        }
    }

    Task IKernelCommandHandler<RequestValueInfos>.HandleAsync(
        RequestValueInfos command,
        KernelInvocationContext context)
    {
        var valueInfos = _variables
            .Select(kvp => new KernelValueInfo(
                kvp.Key,
                FormattedValue.CreateSingleFromObject(kvp.Value, command.MimeType),
                kvp.Value?.GetType() ?? typeof(string)))
            .ToArray();

        context.Publish(new ValueInfosProduced(valueInfos, command));
        return Task.CompletedTask;
    }

    Task IKernelCommandHandler<RequestValue>.HandleAsync(
        RequestValue command,
        KernelInvocationContext context)
    {
        if (_variables.TryGetValue(command.Name, out var value))
        {
            var formattedValue = FormattedValue.CreateSingleFromObject(
                value,
                command.MimeType);

            context.Publish(new ValueProduced(
                value,
                command.Name,
                formattedValue,
                command));
        }
        else
        {
            context.Fail(command, message: $"Variable '{command.Name}' not found in bash kernel.");
        }

        return Task.CompletedTask;
    }

    async Task IKernelCommandHandler<SendValue>.HandleAsync(
        SendValue command,
        KernelInvocationContext context)
    {
        await SetValueAsync(command, context, (name, value, declaredType) =>
        {
            // Store in variable dictionary
            _variables[name] = value;

            // Convert to string for bash environment
            var stringValue = value switch
            {
                null => "",
                string s => s,
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => JsonSerializer.Serialize(value)
            };

            _environmentVariables[name] = stringValue;

            // If process is running, also export to current session
            if (_bashProcess is not null && !_bashProcess.HasExited && _stdin is not null)
            {
                // Escape the value for bash
                var escapedValue = stringValue
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("$", "\\$")
                    .Replace("`", "\\`");

                _stdin.WriteLine($"export {name}=\"{escapedValue}\"");
                _stdin.Flush();
            }

            return Task.CompletedTask;
        });
    }

    private async Task CaptureExportedVariablesAsync()
    {
        if (_bashProcess is null || _bashProcess.HasExited || _stdin is null)
            return;

        var capturePrompt = $"{_uniquePrompt}CAPTURE:";
        var captureComplete = new TaskCompletionSource<string>();
        var captureBuffer = new StringBuilder();
        var capturing = false;

        void CaptureHandler(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
                return;

            if (e.Data.StartsWith($"{capturePrompt}START"))
            {
                capturing = true;
                return;
            }

            if (e.Data.StartsWith($"{capturePrompt}END"))
            {
                capturing = false;
                captureComplete.TrySetResult(captureBuffer.ToString());
                return;
            }

            if (capturing)
            {
                captureBuffer.AppendLine(e.Data);
            }
        }

        _bashProcess.OutputDataReceived += CaptureHandler;

        try
        {
            // Request exported variables
            await _stdin.WriteLineAsync($"echo \"{capturePrompt}START\"");
            await _stdin.WriteLineAsync("export -p");
            await _stdin.WriteLineAsync($"echo \"{capturePrompt}END\"");
            await _stdin.FlushAsync();

            // Wait with timeout
            var timeoutTask = Task.Delay(5000);
            var completedTask = await Task.WhenAny(captureComplete.Task, timeoutTask);

            if (completedTask == captureComplete.Task)
            {
                var output = await captureComplete.Task;
                ParseExportedVariables(output);
            }
        }
        finally
        {
            _bashProcess.OutputDataReceived -= CaptureHandler;
        }
    }

    private void ParseExportedVariables(string exportOutput)
    {
        // Skip system variables that shouldn't be shared
        var systemVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PATH", "HOME", "USER", "SHELL", "PWD", "OLDPWD", "TERM",
            "PS1", "PS2", "PROMPT_COMMAND", "PAGER", "SHLVL", "_"
        };

        var matches = ExportedVariableRegex().Matches(exportOutput);
        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            var value = match.Groups[2].Success ? match.Groups[2].Value : "";

            // Skip system variables and variables starting with our prompt
            if (systemVariables.Contains(name) || name.StartsWith("DOTNET_INTERACTIVE"))
                continue;

            // Unescape bash string
            value = value
                .Replace("\\\"", "\"")
                .Replace("\\$", "$")
                .Replace("\\`", "`")
                .Replace("\\\\", "\\");

            _variables[name] = value;
            _environmentVariables[name] = value;
        }
    }
}
