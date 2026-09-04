namespace Labs.Runtime.Core.Output;

public sealed record LabOutput(
    Guid JobId,
    long Sequence,
    DateTimeOffset Timestamp,
    LabOutputStream Stream,
    string Content);
