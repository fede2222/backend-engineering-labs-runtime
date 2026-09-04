namespace Labs.Runtime.Core.Execution;

public sealed record LabExecutionResult(
    LabExecutionOutcome Outcome,
    int? ExitCode = null,
    string? FailureReason = null);
