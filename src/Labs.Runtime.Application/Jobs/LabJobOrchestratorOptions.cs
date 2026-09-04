namespace Labs.Runtime.Application.Jobs;

public sealed class LabJobOrchestratorOptions
{
    public LabJobOrchestratorOptions(TimeSpan cleanupTimeout)
    {
        if (cleanupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cleanupTimeout),
                "The cleanup timeout must be greater than zero.");
        }

        CleanupTimeout = cleanupTimeout;
    }

    public TimeSpan CleanupTimeout { get; }
}
