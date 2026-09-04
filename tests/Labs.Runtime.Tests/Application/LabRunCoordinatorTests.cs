using Labs.Runtime.Application.Abstractions;
using Labs.Runtime.Application.Execution;
using Labs.Runtime.Application.Jobs;
using Labs.Runtime.Core.Execution;
using Labs.Runtime.Core.Jobs;
using Labs.Runtime.Core.Labs;
using Labs.Runtime.Core.Output;
using Labs.Runtime.Infrastructure.Jobs;
using Labs.Runtime.Infrastructure.Output;

namespace Labs.Runtime.Tests.Application;

public sealed class LabRunCoordinatorTests
{
    [Fact]
    public async Task StartAsync_ReturnsQueuedJobAndReplaysEarlyOutput()
    {
        var executor = new ControlledExecutor();
        var jobStore = new InMemoryLabJobStore();
        var outputStore = CreateOutputStore();
        await using var coordinator = CreateCoordinator(
            executor,
            jobStore,
            outputStore);

        var started = await coordinator.StartAsync("process-vs-thread");

        Assert.Equal(LabJobStatus.Queued, started.Status);
        await executor.OutputPublished.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await using var reader = outputStore
            .ReadAllAsync(started.Id, 0, CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal("early output", reader.Current.Content);

        executor.Release.TrySetResult();
        var completed = await WaitForTerminalJobAsync(jobStore, started.Id);

        Assert.Equal(LabJobStatus.Succeeded, completed.Status);
        Assert.False(await reader.MoveNextAsync());
    }

    [Fact]
    public async Task CancelAsync_CancelsActiveExecutionAndCompletesOutput()
    {
        var executor = new ControlledExecutor();
        var jobStore = new InMemoryLabJobStore();
        var outputStore = CreateOutputStore();
        await using var coordinator = CreateCoordinator(
            executor,
            jobStore,
            outputStore);
        var started = await coordinator.StartAsync("process-vs-thread");
        await executor.OutputPublished.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var cancellationRequested = await coordinator.CancelAsync(started.Id);
        var completed = await WaitForTerminalJobAsync(jobStore, started.Id);

        Assert.True(cancellationRequested);
        Assert.Equal(LabJobStatus.Cancelled, completed.Status);

        var outputs = await ReadAllAsync(outputStore, started.Id);
        Assert.Single(outputs);
        Assert.Equal("early output", outputs[0].Content);
    }

    private static LabRunCoordinator CreateCoordinator(
        ControlledExecutor executor,
        ILabJobStore jobStore,
        ILabOutputStore outputStore)
    {
        var lab = new LabDefinition(
            "process-vs-thread",
            "Process vs Thread",
            executor.Type,
            TimeSpan.FromSeconds(5));
        var orchestrator = new LabJobOrchestrator(
            new StubLabCatalog(lab),
            jobStore,
            new LabExecutorResolver([executor]),
            new LabJobOrchestratorOptions(TimeSpan.FromSeconds(1)),
            TimeProvider.System);

        return new LabRunCoordinator(orchestrator, outputStore);
    }

    private static InMemoryLabOutputStore CreateOutputStore()
    {
        return new InMemoryLabOutputStore(
            new InMemoryLabOutputStoreOptions());
    }

    private static async Task<LabJobSnapshot> WaitForTerminalJobAsync(
        ILabJobStore jobStore,
        Guid jobId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();

            var snapshot = await jobStore.FindAsync(jobId, timeout.Token)
                ?? throw new InvalidOperationException("The job was not stored.");

            if (snapshot.IsTerminal)
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static async Task<IReadOnlyList<LabOutput>> ReadAllAsync(
        ILabOutputStore outputStore,
        Guid jobId)
    {
        var outputs = new List<LabOutput>();

        await foreach (var output in outputStore.ReadAllAsync(
                           jobId,
                           0,
                           CancellationToken.None))
        {
            outputs.Add(output);
        }

        return outputs;
    }

    private sealed class StubLabCatalog(LabDefinition lab) : ILabCatalog
    {
        public ValueTask<LabDefinition?> FindAsync(
            string labId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<LabDefinition?>(
                lab.Id == labId ? lab : null);
        }
    }

    private sealed class ControlledExecutor : ILabExecutor
    {
        public string Type => "controlled";

        public TaskCompletionSource OutputPublished { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PrepareAsync(
            LabExecutionContext context,
            Func<LabOutput, CancellationToken, ValueTask> publishOutput,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task<LabExecutionResult> ExecuteAsync(
            LabExecutionContext context,
            Func<LabOutput, CancellationToken, ValueTask> publishOutput,
            CancellationToken cancellationToken)
        {
            await publishOutput(
                new LabOutput(
                    context.JobId,
                    1,
                    DateTimeOffset.UtcNow,
                    LabOutputStream.StandardOutput,
                    "early output"),
                cancellationToken);
            OutputPublished.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);

            return new LabExecutionResult(
                LabExecutionOutcome.Succeeded,
                ExitCode: 0);
        }

        public Task CleanupAsync(
            LabExecutionContext context,
            Func<LabOutput, CancellationToken, ValueTask> publishOutput,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
