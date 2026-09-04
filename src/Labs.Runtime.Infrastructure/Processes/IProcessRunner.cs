namespace Labs.Runtime.Infrastructure.Processes;

public interface IProcessRunner
{
    Task<int> RunAsync(
        ProcessCommand command,
        Func<ProcessOutputChunk, CancellationToken, ValueTask> publishOutput,
        CancellationToken cancellationToken);
}
