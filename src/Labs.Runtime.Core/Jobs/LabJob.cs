namespace Labs.Runtime.Core.Jobs;

public sealed class LabJob
{
    private LabJob(Guid id, string labId, DateTimeOffset createdAt)
    {
        Id = id;
        LabId = labId;
        CreatedAt = createdAt;
        LastTransitionAt = createdAt;
        Status = LabJobStatus.Queued;
    }

    private DateTimeOffset LastTransitionAt { get; set; }

    public Guid Id { get; }

    public string LabId { get; }

    public LabJobStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public int? ExitCode { get; private set; }

    public string? FailureReason { get; private set; }

    public bool IsTerminal => Status is
        LabJobStatus.Succeeded or
        LabJobStatus.Failed or
        LabJobStatus.TimedOut or
        LabJobStatus.Cancelled;

    public static LabJob Create(
        string labId,
        DateTimeOffset createdAt,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labId);

        var jobId = id ?? Guid.NewGuid();
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("The job id cannot be empty.", nameof(id));
        }

        return new LabJob(jobId, labId, createdAt);
    }

    public void MarkPreparing(DateTimeOffset timestamp)
    {
        EnsureTransition(LabJobStatus.Queued, LabJobStatus.Preparing);
        EnsureTimestamp(timestamp, LastTransitionAt);

        StartedAt = timestamp;
        LastTransitionAt = timestamp;
        Status = LabJobStatus.Preparing;
    }

    public void MarkRunning(DateTimeOffset timestamp)
    {
        EnsureTransition(LabJobStatus.Preparing, LabJobStatus.Running);
        EnsureTimestamp(timestamp, LastTransitionAt);

        LastTransitionAt = timestamp;
        Status = LabJobStatus.Running;
    }

    public void MarkCleaningUp(DateTimeOffset timestamp)
    {
        if (Status is not (LabJobStatus.Preparing or LabJobStatus.Running))
        {
            throw InvalidTransition(LabJobStatus.CleaningUp);
        }

        EnsureTimestamp(timestamp, LastTransitionAt);
        LastTransitionAt = timestamp;
        Status = LabJobStatus.CleaningUp;
    }

    public void MarkSucceeded(DateTimeOffset timestamp)
    {
        Complete(LabJobStatus.Succeeded, timestamp, 0, null);
    }

    public void MarkFailed(
        DateTimeOffset timestamp,
        string failureReason,
        int? exitCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        Complete(LabJobStatus.Failed, timestamp, exitCode, failureReason);
    }

    public void MarkTimedOut(DateTimeOffset timestamp)
    {
        Complete(
            LabJobStatus.TimedOut,
            timestamp,
            null,
            "The lab execution timed out.");
    }

    public void MarkCancelled(DateTimeOffset timestamp)
    {
        if (Status == LabJobStatus.Queued)
        {
            EnsureTimestamp(timestamp, LastTransitionAt);
            CompletedAt = timestamp;
            LastTransitionAt = timestamp;
            Status = LabJobStatus.Cancelled;
            return;
        }

        Complete(
            LabJobStatus.Cancelled,
            timestamp,
            null,
            "The lab execution was cancelled.");
    }

    private void Complete(
        LabJobStatus finalStatus,
        DateTimeOffset timestamp,
        int? exitCode,
        string? failureReason)
    {
        EnsureTransition(LabJobStatus.CleaningUp, finalStatus);
        EnsureTimestamp(timestamp, LastTransitionAt);

        CompletedAt = timestamp;
        ExitCode = exitCode;
        FailureReason = failureReason;
        LastTransitionAt = timestamp;
        Status = finalStatus;
    }

    private void EnsureTransition(
        LabJobStatus expectedCurrent,
        LabJobStatus next)
    {
        if (Status != expectedCurrent)
        {
            throw InvalidTransition(next);
        }
    }

    private InvalidOperationException InvalidTransition(LabJobStatus next)
    {
        return new InvalidOperationException(
            $"A lab job cannot transition from {Status} to {next}.");
    }

    private static void EnsureTimestamp(
        DateTimeOffset timestamp,
        DateTimeOffset earliest)
    {
        if (timestamp < earliest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp),
                "A job transition cannot occur before the previous transition.");
        }
    }
}
