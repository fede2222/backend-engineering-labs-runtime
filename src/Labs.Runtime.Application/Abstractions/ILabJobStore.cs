using Labs.Runtime.Core.Jobs;

namespace Labs.Runtime.Application.Abstractions;

public interface ILabJobStore
{
    ValueTask AddAsync(
        LabJob job,
        CancellationToken cancellationToken);

    ValueTask UpdateAsync(
        LabJob job,
        CancellationToken cancellationToken);

    ValueTask<LabJobSnapshot?> FindAsync(
        Guid jobId,
        CancellationToken cancellationToken);
}
