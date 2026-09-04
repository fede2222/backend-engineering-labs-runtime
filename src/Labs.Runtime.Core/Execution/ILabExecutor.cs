using Labs.Runtime.Core.Output;

namespace Labs.Runtime.Core.Execution;

public interface ILabExecutor
{
    string Type { get; }

    Task<LabExecutionResult> ExecuteAsync(
        LabExecutionContext context,
        Func<LabOutput, CancellationToken, ValueTask> publishOutput,
        CancellationToken cancellationToken);
}
