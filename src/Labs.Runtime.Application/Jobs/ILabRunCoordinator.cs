using Labs.Runtime.Core.Jobs;

namespace Labs.Runtime.Application.Jobs;

public interface ILabRunCoordinator
{
    ValueTask<LabJobSnapshot> StartAsync(
        string labId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> CancelAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
