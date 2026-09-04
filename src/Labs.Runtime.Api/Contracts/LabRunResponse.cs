using Labs.Runtime.Core.Jobs;

namespace Labs.Runtime.Api.Contracts;

public sealed record LabRunResponse(
    Guid Id,
    string LabId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? ExitCode,
    string? FailureReason,
    string StatusUrl,
    string EventsUrl)
{
    public static LabRunResponse From(LabJobSnapshot job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new LabRunResponse(
            job.Id,
            job.LabId,
            job.Status.ToString(),
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.ExitCode,
            job.FailureReason,
            $"/api/runs/{job.Id}",
            $"/api/runs/{job.Id}/events");
    }
}
