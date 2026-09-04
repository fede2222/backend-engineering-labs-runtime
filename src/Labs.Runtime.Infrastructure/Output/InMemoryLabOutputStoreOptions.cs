namespace Labs.Runtime.Infrastructure.Output;

public sealed class InMemoryLabOutputStoreOptions
{
    public InMemoryLabOutputStoreOptions(int maxBufferedOutputsPerJob = 4096)
    {
        if (maxBufferedOutputsPerJob <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBufferedOutputsPerJob),
                "The output buffer size must be greater than zero.");
        }

        MaxBufferedOutputsPerJob = maxBufferedOutputsPerJob;
    }

    public int MaxBufferedOutputsPerJob { get; }
}
