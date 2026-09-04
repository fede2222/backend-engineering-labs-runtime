using System.Diagnostics;
using System.Threading.Channels;
using Labs.Runtime.Core.Output;

namespace Labs.Runtime.Infrastructure.Processes;

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(
        ProcessCommand command,
        Func<ProcessOutputChunk, CancellationToken, ValueTask> publishOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(publishOutput);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(command)
        };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Process '{command.FileName}' could not be started.");
        }

        var outputChannel = Channel.CreateUnbounded<ProcessOutputChunk>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        var standardOutputTask = PumpAsync(
            process.StandardOutput,
            LabOutputStream.StandardOutput,
            outputChannel.Writer);
        var standardErrorTask = PumpAsync(
            process.StandardError,
            LabOutputStream.StandardError,
            outputChannel.Writer);
        var publishTask = PublishAsync(
            outputChannel.Reader,
            publishOutput,
            cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            outputChannel.Writer.TryComplete();
            await publishTask;

            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        catch
        {
            TryKill(process);

            if (!process.HasExited)
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }

            await IgnoreFailureAsync(standardOutputTask, standardErrorTask);
            outputChannel.Writer.TryComplete();
            await IgnoreFailureAsync(publishTask);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task PumpAsync(
        StreamReader reader,
        LabOutputStream stream,
        ChannelWriter<ProcessOutputChunk> writer)
    {
        var buffer = new char[1024];

        while (true)
        {
            var charactersRead = await reader.ReadAsync(buffer.AsMemory());
            if (charactersRead == 0)
            {
                return;
            }

            await writer.WriteAsync(
                new ProcessOutputChunk(
                    stream,
                    new string(buffer, 0, charactersRead)));
        }
    }

    private static async Task PublishAsync(
        ChannelReader<ProcessOutputChunk> reader,
        Func<ProcessOutputChunk, CancellationToken, ValueTask> publishOutput,
        CancellationToken cancellationToken)
    {
        await foreach (var output in reader.ReadAllAsync(cancellationToken))
        {
            await publishOutput(output, cancellationToken);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }
    }

    private static async Task IgnoreFailureAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Preserve the original process or cancellation exception.
        }
    }
}
