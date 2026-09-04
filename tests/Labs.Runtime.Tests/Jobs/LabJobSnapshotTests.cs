using Labs.Runtime.Core.Jobs;

namespace Labs.Runtime.Tests.Jobs;

public sealed class LabJobSnapshotTests
{
    [Fact]
    public void From_CapturesJobStateAtThatMoment()
    {
        var createdAt = new DateTimeOffset(
            2026,
            9,
            4,
            0,
            0,
            0,
            TimeSpan.Zero);
        var job = LabJob.Create("process-vs-thread", createdAt);

        var snapshot = LabJobSnapshot.From(job);
        job.MarkPreparing(createdAt.AddSeconds(1));

        Assert.Equal(LabJobStatus.Queued, snapshot.Status);
        Assert.False(snapshot.IsTerminal);
        Assert.Null(snapshot.StartedAt);
    }
}
