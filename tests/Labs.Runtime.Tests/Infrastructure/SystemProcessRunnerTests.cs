using System.Diagnostics;
using Labs.Runtime.Core.Output;
using Labs.Runtime.Infrastructure.Processes;

namespace Labs.Runtime.Tests.Infrastructure;

public sealed class SystemProcessRunnerTests
{
    [Fact]
    public async Task RunAsyncStreamsStandardOutputAndStandardError()
    {
        var runner = new SystemProcessRunner();
        var outputs = new List<ProcessOutputChunk>();
        var firstOutputReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new ProcessCommand(
            "/bin/sh",
            Path.GetTempPath(),
            new[] { "-c", "printf first; sleep 0.5; printf second >&2" });

        var runTask = runner.RunAsync(
            command,
            (output, _) =>
            {
                outputs.Add(output);

                if (output.Stream == LabOutputStream.StandardOutput
                    && output.Content.Contains("first", StringComparison.Ordinal))
                {
                    firstOutputReceived.TrySetResult();
                }

                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        await firstOutputReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(runTask.IsCompleted);

        var exitCode = await runTask;

        Assert.Equal(0, exitCode);
        Assert.Contains(
            outputs,
            output => output.Stream == LabOutputStream.StandardOutput
                && output.Content.Contains("first", StringComparison.Ordinal));
        Assert.Contains(
            outputs,
            output => output.Stream == LabOutputStream.StandardError
                && output.Content.Contains("second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsyncKillsProcessTreeWhenCancelled()
    {
        var runner = new SystemProcessRunner();
        var command = new ProcessCommand(
            "/bin/sh",
            Path.GetTempPath(),
            new[] { "-c", "sleep 30" });
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(
                command,
                (_, _) => ValueTask.CompletedTask,
                cancellation.Token));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Cancellation took {stopwatch.Elapsed}.");
    }
}
