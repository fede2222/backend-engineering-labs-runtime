using Labs.Runtime.Core.Output;

namespace Labs.Runtime.Api.Contracts;

public sealed record LabOutputResponse(
    Guid JobId,
    long Sequence,
    DateTimeOffset Timestamp,
    string Stream,
    string Content)
{
    public static LabOutputResponse From(LabOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        return new LabOutputResponse(
            output.JobId,
            output.Sequence,
            output.Timestamp,
            output.Stream.ToString(),
            output.Content);
    }
}
