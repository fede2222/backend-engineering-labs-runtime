using Labs.Runtime.Core.Output;

namespace Labs.Runtime.Infrastructure.Processes;

public sealed record ProcessOutputChunk(
    LabOutputStream Stream,
    string Content);
