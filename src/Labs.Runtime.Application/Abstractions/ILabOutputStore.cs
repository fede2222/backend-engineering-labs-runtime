using Labs.Runtime.Core.Output;

namespace Labs.Runtime.Application.Abstractions;

public interface ILabOutputStore
{
    ValueTask CreateAsync(
        Guid jobId,
        CancellationToken cancellationToken);

    ValueTask AppendAsync(
        LabOutput output,
        CancellationToken cancellationToken);

    IAsyncEnumerable<LabOutput> ReadAllAsync(
        Guid jobId,
        long afterSequence,
        CancellationToken cancellationToken);

    ValueTask CompleteAsync(
        Guid jobId,
        CancellationToken cancellationToken);
}
