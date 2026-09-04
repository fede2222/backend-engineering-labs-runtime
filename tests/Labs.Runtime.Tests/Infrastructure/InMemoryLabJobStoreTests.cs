using Labs.Runtime.Core.Jobs;
using Labs.Runtime.Infrastructure.Jobs;

namespace Labs.Runtime.Tests.Infrastructure;

public sealed class InMemoryLabJobStoreTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_StoresSnapshotInsteadOfMutableJob()
    {
        var store = new InMemoryLabJobStore();
        var job = LabJob.Create("process-vs-thread", CreatedAt);

        await store.AddAsync(job, CancellationToken.None);
        job.MarkPreparing(CreatedAt.AddSeconds(1));

        var stored = await store.FindAsync(job.Id, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(LabJobStatus.Queued, stored.Status);
        Assert.Null(stored.StartedAt);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesStoredSnapshot()
    {
        var store = new InMemoryLabJobStore();
        var job = LabJob.Create("process-vs-thread", CreatedAt);
        await store.AddAsync(job, CancellationToken.None);

        job.MarkPreparing(CreatedAt.AddSeconds(1));
        await store.UpdateAsync(job, CancellationToken.None);
        var stored = await store.FindAsync(job.Id, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(LabJobStatus.Preparing, stored.Status);
        Assert.Equal(CreatedAt.AddSeconds(1), stored.StartedAt);
    }

    [Fact]
    public async Task AddAsync_WithExistingJob_Throws()
    {
        var store = new InMemoryLabJobStore();
        var job = LabJob.Create("process-vs-thread", CreatedAt);
        await store.AddAsync(job, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.AddAsync(job, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentAdds_PreserveEveryJob()
    {
        var store = new InMemoryLabJobStore();
        var jobs = Enumerable.Range(0, 100)
            .Select(_ => LabJob.Create("process-vs-thread", CreatedAt))
            .ToArray();

        await Task.WhenAll(
            jobs.Select(job =>
                store.AddAsync(job, CancellationToken.None).AsTask()));

        var storedJobs = await Task.WhenAll(
            jobs.Select(async job =>
                await store.FindAsync(job.Id, CancellationToken.None)));

        Assert.All(storedJobs, Assert.NotNull);
    }
}
