using Labs.Runtime.Core.Labs;

namespace Labs.Runtime.Core.Execution;

public sealed record LabExecutionContext(
    Guid JobId,
    LabDefinition Lab);
