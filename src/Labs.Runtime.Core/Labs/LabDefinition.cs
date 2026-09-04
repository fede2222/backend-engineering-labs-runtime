namespace Labs.Runtime.Core.Labs;

public sealed record LabDefinition
{
    public LabDefinition(
        string id,
        string displayName,
        string executorType,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executorType);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "The lab timeout must be greater than zero.");
        }

        Id = id;
        DisplayName = displayName;
        ExecutorType = executorType;
        Timeout = timeout;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string ExecutorType { get; }

    public TimeSpan Timeout { get; }
}
