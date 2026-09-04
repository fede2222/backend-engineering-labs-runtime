using Labs.Runtime.Application.Abstractions;
using Labs.Runtime.Application.Execution;
using Labs.Runtime.Application.Jobs;
using Labs.Runtime.Core.Execution;
using Labs.Runtime.Core.Jobs;
using Labs.Runtime.Core.Labs;
using Labs.Runtime.Core.Output;

namespace Labs.Runtime.Tests.Application;

public sealed class LabJobOrchestratorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);

    private static readonly Func<LabOutput, CancellationToken, ValueTask>
        IgnoreOutput = static (_, _) => ValueTask.CompletedTask;

    [Fact]
    public async Task RunAsync_SuccessfulExecution_TraversesAllStates()
    {
        var executor = new StubExecutor();
        var store = new RecordingJobStore();
        var orchestrator = CreateOrchestrator(executor, store);

        var job = await orchestrator.RunAsync(
            "process-vs-thread",
            IgnoreOutput);

        Assert.Equal(LabJobStatus.Succeeded, job.Status);
        Assert.Equal(0, job.ExitCode);
        Assert.True(executor.PrepareCalled);
        Assert.True(executor.ExecuteCalled);
        Assert.True(executor.CleanupCalled);
        Assert.Equal(
            new[]
            {
                LabJobStatus.Queued,
                LabJobStatus.Preparing,
                LabJobStatus.Running,
                LabJobStatus.CleaningUp,
                LabJobStatus.Succeeded
            },
            store.RecordedStatuses);
    }

    [Fact]
    public async Task RunAsync_WhenExecutionFails_StillCleansUp()
    {
        var executor = new StubExecutor
        {
            Execute = _ => throw new InvalidOperationException("boom")
        };
        var store = new RecordingJobStore();
        var orchestrator = CreateOrchestrator(executor, store);

        var job = await orchestrator.RunAsync(
            "process-vs-thread",
            IgnoreOutput);

        Assert.Equal(LabJobStatus.Failed, job.Status);
        Assert.Equal("boom", job.FailureReason);
        Assert.True(executor.CleanupCalled);
    }

    [Fact]
    public async Task RunAsync_WhenExecutionExceedsTimeout_MarksTimedOut()
    {
        var executor = new StubExecutor
        {
            Execute = async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new LabExecutionResult(LabExecutionOutcome.Succeeded);
            }
        };
        var store = new RecordingJobStore();
        var orchestrator = CreateOrchestrator(
            executor,
            store,
            executionTimeout: TimeSpan.FromMilliseconds(25));

        var job = await orchestrator.RunAsync(
            "process-vs-thread",
            IgnoreOutput);

        Assert.Equal(LabJobStatus.TimedOut, job.Status);
        Assert.True(executor.CleanupCalled);
    }

    [Fact]
    public async Task RunAsync_WhenCallerCancels_MarksCancelled()
    {
        var executor = new StubExecutor
        {
            Execute = async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new LabExecutionResult(LabExecutionOutcome.Succeeded);
            }
        };
        var store = new RecordingJobStore();
        var orchestrator = CreateOrchestrator(executor, store);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(25));

        var job = await orchestrator.RunAsync(
            "process-vs-thread",
            IgnoreOutput,
            cancellation.Token);

        Assert.Equal(LabJobStatus.Cancelled, job.Status);
        Assert.True(executor.CleanupCalled);
    }

    [Fact]
    public async Task RunAsync_WhenCleanupFails_MarksFailed()
    {
        var executor = new StubExecutor
        {
            Cleanup = _ => throw new InvalidOperationException("cleanup boom")
        };
        var store = new RecordingJobStore();
        var orchestrator = CreateOrchestrator(executor, store);

        var job = await orchestrator.RunAsync(
            "process-vs-thread",
            IgnoreOutput);

        Assert.Equal(LabJobStatus.Failed, job.Status);
        Assert.Contains("cleanup boom", job.FailureReason);
    }

    private static LabJobOrchestrator CreateOrchestrator(
        StubExecutor executor,
        RecordingJobStore store,
        TimeSpan? executionTimeout = null)
    {
        var lab = new LabDefinition(
            "process-vs-thread",
            "Process vs Thread",
            executor.Type,
            executionTimeout ?? TimeSpan.FromSeconds(5));

        return new LabJobOrchestrator(
            new StubLabCatalog(lab),
            store,
            new LabExecutorResolver([executor]),
            new LabJobOrchestratorOptions(TimeSpan.FromSeconds(1)),
            new FixedTimeProvider(Now));
    }

    private sealed class StubLabCatalog(LabDefinition lab) : ILabCatalog
    {
        public ValueTask<LabDefinition?> FindAsync(
            string labId,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<LabDefinition?>(
                lab.Id == labId ? lab : null);
        }
    }

    private sealed class RecordingJobStore : ILabJobStore
    {
        private LabJob? _job;

        public List<LabJobStatus> RecordedStatuses { get; } = [];

        public ValueTask AddAsync(
            LabJob job,
            CancellationToken cancellationToken)
        {
            _job = job;
            RecordedStatuses.Add(job.Status);
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateAsync(
            LabJob job,
            CancellationToken cancellationToken)
        {
            _job = job;
            RecordedStatuses.Add(job.Status);
            return ValueTask.CompletedTask;
        }

        public ValueTask<LabJob?> FindAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(
                _job?.Id == jobId ? _job : null);
        }
    }

    private sealed class StubExecutor : ILabExecutor
    {
        public string Type => "dotnet";

        public bool PrepareCalled { get; private set; }

        public bool ExecuteCalled { get; private set; }

        public bool CleanupCalled { get; private set; }

        public Func<CancellationToken, Task<LabExecutionResult>> Execute { get; init; } =
            _ => Task.FromResult(new LabExecutionResult(
                LabExecutionOutcome.Succeeded,
                ExitCode: 0));

        public Func<CancellationToken, Task> Cleanup { get; init; } =
            _ => Task.CompletedTask;

        public Task PrepareAsync(
            LabExecutionContext context,
            Func<LabOutput, CancellationToken, ValueTask> publishOutput,
            CancellationToken cancellationToken)
        {
            PrepareCalled = true;
            return Task.CompletedTask;
        }

        public Task<LabExecutionResult> ExecuteAsync(
            LabExecutionContext context,
            Func<LabOutput, CancellationToken, ValueTask> publishOutput,
            CancellationToken cancellationToken)
        {
            ExecuteCalled = true;
            return Execute(cancellationToken);
        }

        public Task CleanupAsync(
            LabExecutionContext context,
            Func<LabOutput, CancellationToken, ValueTask> publishOutput,
            CancellationToken cancellationToken)
        {
            CleanupCalled = true;
            return Cleanup(cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
