using Labs.Runtime.Application.Abstractions;
using Labs.Runtime.Application.Execution;
using Labs.Runtime.Core.Execution;
using Labs.Runtime.Core.Jobs;
using Labs.Runtime.Core.Output;

namespace Labs.Runtime.Application.Jobs;

public sealed class LabJobOrchestrator
{
    private readonly ILabCatalog _labCatalog;
    private readonly ILabJobStore _jobStore;
    private readonly LabExecutorResolver _executorResolver;
    private readonly LabJobOrchestratorOptions _options;
    private readonly TimeProvider _timeProvider;

    public LabJobOrchestrator(
        ILabCatalog labCatalog,
        ILabJobStore jobStore,
        LabExecutorResolver executorResolver,
        LabJobOrchestratorOptions options,
        TimeProvider timeProvider)
    {
        _labCatalog = labCatalog;
        _jobStore = jobStore;
        _executorResolver = executorResolver;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<LabJob> RunAsync(
        string labId,
        Func<LabOutput, CancellationToken, ValueTask> publishOutput,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labId);
        ArgumentNullException.ThrowIfNull(publishOutput);

        var job = await CreateAsync(labId, cancellationToken);
        return await RunAsync(job, publishOutput, cancellationToken);
    }

    public async Task<LabJob> CreateAsync(
        string labId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labId);

        var lab = await _labCatalog.FindAsync(labId, cancellationToken)
            ?? throw new KeyNotFoundException($"Lab '{labId}' was not found.");

        _ = _executorResolver.Resolve(lab.ExecutorType);
        var job = LabJob.Create(lab.Id, _timeProvider.GetUtcNow());
        await _jobStore.AddAsync(job, cancellationToken);
        return job;
    }

    public async Task<LabJob> RunAsync(
        LabJob job,
        Func<LabOutput, CancellationToken, ValueTask> publishOutput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(publishOutput);

        if (job.Status != LabJobStatus.Queued)
        {
            throw new InvalidOperationException(
                $"Lab job '{job.Id}' must be queued before execution.");
        }

        var lab = await _labCatalog.FindAsync(job.LabId, cancellationToken)
            ?? throw new KeyNotFoundException($"Lab '{job.LabId}' was not found.");
        var executor = _executorResolver.Resolve(lab.ExecutorType);
        var context = new LabExecutionContext(job.Id, lab);

        using var executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        executionCancellation.CancelAfter(lab.Timeout);

        var result = new LabExecutionResult(
            LabExecutionOutcome.Failed,
            FailureReason: "The lab execution did not complete.");

        try
        {
            await TransitionAsync(
                job,
                () => job.MarkPreparing(_timeProvider.GetUtcNow()));

            await executor.PrepareAsync(
                context,
                publishOutput,
                executionCancellation.Token);

            await TransitionAsync(
                job,
                () => job.MarkRunning(_timeProvider.GetUtcNow()));

            result = await executor.ExecuteAsync(
                context,
                publishOutput,
                executionCancellation.Token);
        }
        catch (OperationCanceledException)
            when (executionCancellation.IsCancellationRequested)
        {
            result = cancellationToken.IsCancellationRequested
                ? new LabExecutionResult(LabExecutionOutcome.Cancelled)
                : new LabExecutionResult(LabExecutionOutcome.TimedOut);
        }
        catch (Exception exception)
        {
            result = new LabExecutionResult(
                LabExecutionOutcome.Failed,
                FailureReason: exception.Message);
        }
        finally
        {
            await TransitionAsync(
                job,
                () => job.MarkCleaningUp(_timeProvider.GetUtcNow()));

            result = await CleanupAsync(
                executor,
                context,
                publishOutput,
                result);

            await CompleteAsync(job, result);
        }

        return job;
    }

    private async Task<LabExecutionResult> CleanupAsync(
        ILabExecutor executor,
        LabExecutionContext context,
        Func<LabOutput, CancellationToken, ValueTask> publishOutput,
        LabExecutionResult currentResult)
    {
        using var cleanupCancellation =
            new CancellationTokenSource(_options.CleanupTimeout);

        try
        {
            await executor.CleanupAsync(
                context,
                publishOutput,
                cleanupCancellation.Token);

            return currentResult;
        }
        catch (OperationCanceledException)
            when (cleanupCancellation.IsCancellationRequested)
        {
            return new LabExecutionResult(
                LabExecutionOutcome.Failed,
                FailureReason: "Lab cleanup timed out.");
        }
        catch (Exception exception)
        {
            return new LabExecutionResult(
                LabExecutionOutcome.Failed,
                FailureReason: $"Lab cleanup failed: {exception.Message}");
        }
    }

    private async Task CompleteAsync(
        LabJob job,
        LabExecutionResult result)
    {
        var completedAt = _timeProvider.GetUtcNow();

        switch (result.Outcome)
        {
            case LabExecutionOutcome.Succeeded:
                job.MarkSucceeded(completedAt);
                break;

            case LabExecutionOutcome.Failed:
                job.MarkFailed(
                    completedAt,
                    result.FailureReason ?? "Lab execution failed.",
                    result.ExitCode);
                break;

            case LabExecutionOutcome.TimedOut:
                job.MarkTimedOut(completedAt);
                break;

            case LabExecutionOutcome.Cancelled:
                job.MarkCancelled(completedAt);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(result),
                    result.Outcome,
                    "Unknown lab execution outcome.");
        }

        await _jobStore.UpdateAsync(job, CancellationToken.None);
    }

    private async Task TransitionAsync(
        LabJob job,
        Action transition)
    {
        transition();
        await _jobStore.UpdateAsync(job, CancellationToken.None);
    }
}
