using Labs.Runtime.Core.Output;
using Labs.Runtime.Infrastructure.Output;

namespace Labs.Runtime.Tests.Infrastructure;

public sealed class InMemoryLabOutputStoreTests
{
    [Fact]
    public async Task ReadAllAsync_ReplaysBufferedOutputAndWaitsForFutureOutput()
    {
        var jobId = Guid.NewGuid();
        var store = CreateStore();
        await store.CreateAsync(jobId, CancellationToken.None);
        await store.AppendAsync(Output(jobId, 1, "first"), CancellationToken.None);
        await using var reader = store
            .ReadAllAsync(jobId, 0, CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("first", reader.Current.Content);

        var nextOutput = reader.MoveNextAsync().AsTask();
        Assert.False(nextOutput.IsCompleted);

        await store.AppendAsync(Output(jobId, 2, "second"), CancellationToken.None);

        Assert.True(await nextOutput.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal("second", reader.Current.Content);

        await store.CompleteAsync(jobId, CancellationToken.None);
        Assert.False(await reader.MoveNextAsync());
    }

    [Fact]
    public async Task ReadAllAsync_AfterCompletion_ReplaysOnlyRetainedOutput()
    {
        var jobId = Guid.NewGuid();
        var store = CreateStore(maxBufferedOutputsPerJob: 2);
        await store.CreateAsync(jobId, CancellationToken.None);
        await store.AppendAsync(Output(jobId, 1, "one"), CancellationToken.None);
        await store.AppendAsync(Output(jobId, 2, "two"), CancellationToken.None);
        await store.AppendAsync(Output(jobId, 3, "three"), CancellationToken.None);
        await store.CompleteAsync(jobId, CancellationToken.None);

        var outputs = await ReadAllAsync(store, jobId);

        Assert.Equal(new long[] { 2, 3 }, outputs.Select(output => output.Sequence));
    }

    [Fact]
    public async Task AppendAsync_WithNonIncreasingSequence_Throws()
    {
        var jobId = Guid.NewGuid();
        var store = CreateStore();
        await store.CreateAsync(jobId, CancellationToken.None);
        await store.AppendAsync(Output(jobId, 2, "first"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.AppendAsync(
                Output(jobId, 2, "duplicate"),
                CancellationToken.None).AsTask());

        Assert.Contains("strictly increasing", exception.Message);
    }

    [Fact]
    public async Task AppendAsync_AfterCompletion_Throws()
    {
        var jobId = Guid.NewGuid();
        var store = CreateStore();
        await store.CreateAsync(jobId, CancellationToken.None);
        await store.CompleteAsync(jobId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.AppendAsync(
                Output(jobId, 1, "late"),
                CancellationToken.None).AsTask());

        Assert.Contains("is complete", exception.Message);
    }

    private static InMemoryLabOutputStore CreateStore(
        int maxBufferedOutputsPerJob = 100)
    {
        return new InMemoryLabOutputStore(
            new InMemoryLabOutputStoreOptions(maxBufferedOutputsPerJob));
    }

    private static LabOutput Output(Guid jobId, long sequence, string content)
    {
        return new LabOutput(
            jobId,
            sequence,
            DateTimeOffset.UtcNow,
            LabOutputStream.StandardOutput,
            content);
    }

    private static async Task<IReadOnlyList<LabOutput>> ReadAllAsync(
        InMemoryLabOutputStore store,
        Guid jobId)
    {
        var outputs = new List<LabOutput>();

        await foreach (var output in store.ReadAllAsync(
                           jobId,
                           0,
                           CancellationToken.None))
        {
            outputs.Add(output);
        }

        return outputs;
    }
}
