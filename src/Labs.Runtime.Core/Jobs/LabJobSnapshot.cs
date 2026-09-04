namespace Labs.Runtime.Core.Jobs;

public sealed record LabJobSnapshot(
    Guid Id,
    string LabId,
    LabJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    string? FailureReason)
{
    public bool IsTerminal => Status is
        LabJobStatus.Succeeded or
        LabJobStatus.Failed or
        LabJobStatus.TimedOut or
        LabJobStatus.Cancelled;

    public static LabJobSnapshot From(LabJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new LabJobSnapshot(
            job.Id,
            job.LabId,
            job.Status,
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.ExitCode,
            job.FailureReason);
    }
}
