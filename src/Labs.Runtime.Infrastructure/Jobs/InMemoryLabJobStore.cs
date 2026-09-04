using System.Collections.Concurrent;
using Labs.Runtime.Application.Abstractions;
using Labs.Runtime.Core.Jobs;

namespace Labs.Runtime.Infrastructure.Jobs;

public sealed class InMemoryLabJobStore : ILabJobStore
{
    private readonly ConcurrentDictionary<Guid, LabJobSnapshot> _jobs = new();

    public ValueTask AddAsync(
        LabJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = LabJobSnapshot.From(job);
        if (!_jobs.TryAdd(job.Id, snapshot))
        {
            throw new InvalidOperationException(
                $"Lab job '{job.Id}' already exists.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(
        LabJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_jobs.ContainsKey(job.Id))
        {
            throw new KeyNotFoundException(
                $"Lab job '{job.Id}' was not found.");
        }

        _jobs[job.Id] = LabJobSnapshot.From(job);
        return ValueTask.CompletedTask;
    }

    public ValueTask<LabJobSnapshot?> FindAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _jobs.TryGetValue(jobId, out var snapshot);
        return ValueTask.FromResult(snapshot);
    }
}
