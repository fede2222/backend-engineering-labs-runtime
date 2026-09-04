using Labs.Runtime.Core.Jobs;

namespace Labs.Runtime.Tests.Jobs;

public sealed class LabJobTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 3, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_StartsQueued()
    {
        var job = LabJob.Create("process-vs-thread", CreatedAt);

        Assert.Equal(LabJobStatus.Queued, job.Status);
        Assert.False(job.IsTerminal);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public void HappyPath_TraversesLifecycleAndSucceeds()
    {
        var job = LabJob.Create("process-vs-thread", CreatedAt);

        job.MarkPreparing(CreatedAt.AddSeconds(1));
        job.MarkRunning(CreatedAt.AddSeconds(2));
        job.MarkCleaningUp(CreatedAt.AddSeconds(3));
        job.MarkSucceeded(CreatedAt.AddSeconds(4));

        Assert.Equal(LabJobStatus.Succeeded, job.Status);
        Assert.True(job.IsTerminal);
        Assert.Equal(CreatedAt.AddSeconds(1), job.StartedAt);
        Assert.Equal(CreatedAt.AddSeconds(4), job.CompletedAt);
        Assert.Equal(0, job.ExitCode);
        Assert.Null(job.FailureReason);
    }

    [Fact]
    public void TimedOutJob_CompletesAfterCleanup()
    {
        var job = LabJob.Create("process-vs-thread", CreatedAt);

        job.MarkPreparing(CreatedAt.AddSeconds(1));
        job.MarkRunning(CreatedAt.AddSeconds(2));
        job.MarkCleaningUp(CreatedAt.AddSeconds(12));
        job.MarkTimedOut(CreatedAt.AddSeconds(13));

        Assert.Equal(LabJobStatus.TimedOut, job.Status);
        Assert.True(job.IsTerminal);
        Assert.NotNull(job.FailureReason);
    }

    [Fact]
    public void MarkRunning_WhenQueued_ThrowsInvalidTransition()
    {
        var job = LabJob.Create("process-vs-thread", CreatedAt);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            job.MarkRunning(CreatedAt.AddSeconds(1)));

        Assert.Contains("Queued", exception.Message);
        Assert.Contains("Running", exception.Message);
    }

    [Fact]
    public void TransitionBeforePreviousTransition_Throws()
    {
        var job = LabJob.Create("process-vs-thread", CreatedAt);

        job.MarkPreparing(CreatedAt.AddSeconds(2));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            job.MarkRunning(CreatedAt.AddSeconds(1)));
    }

    [Fact]
    public void QueuedJob_CanBeCancelledWithoutStarting()
    {
        var job = LabJob.Create("process-vs-thread", CreatedAt);

        job.MarkCancelled(CreatedAt.AddSeconds(1));

        Assert.Equal(LabJobStatus.Cancelled, job.Status);
        Assert.True(job.IsTerminal);
        Assert.Null(job.StartedAt);
        Assert.Equal(CreatedAt.AddSeconds(1), job.CompletedAt);
    }
}
