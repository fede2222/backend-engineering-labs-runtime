using System.Collections.Concurrent;
using Labs.Runtime.Application.Abstractions;
using Labs.Runtime.Core.Jobs;

namespace Labs.Runtime.Application.Jobs;

public sealed class LabRunCoordinator : ILabRunCoordinator, IAsyncDisposable
{
    private readonly LabJobOrchestrator _orchestrator;
    private readonly ILabOutputStore _outputStore;
    private readonly ConcurrentDictionary<Guid, ActiveLabRun> _activeRuns = new();
    private readonly CancellationTokenSource _shutdown = new();

    public LabRunCoordinator(
        LabJobOrchestrator orchestrator,
        ILabOutputStore outputStore)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(outputStore);

        _orchestrator = orchestrator;
        _outputStore = outputStore;
    }

    public async ValueTask<LabJobSnapshot> StartAsync(
        string labId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_shutdown.IsCancellationRequested, this);

        var job = await _orchestrator.CreateAsync(labId, cancellationToken);
        await _outputStore.CreateAsync(job.Id, CancellationToken.None);

        var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _shutdown.Token);
        var activeRun = new ActiveLabRun(executionCancellation);

        if (!_activeRuns.TryAdd(job.Id, activeRun))
        {
            executionCancellation.Dispose();
            throw new InvalidOperationException(
                $"Lab job '{job.Id}' is already running.");
        }

        var snapshot = LabJobSnapshot.From(job);
        _ = ExecuteInBackgroundAsync(job, activeRun);
        return snapshot;
    }

    public ValueTask<bool> CancelAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_activeRuns.TryGetValue(jobId, out var activeRun))
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(activeRun.TryCancel());
    }

    public async ValueTask DisposeAsync()
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        await _shutdown.CancelAsync();

        var completions = _activeRuns.Values
            .Select(activeRun => activeRun.Completion.Task)
            .ToArray();

        await Task.WhenAll(completions);
        _shutdown.Dispose();
    }

    private async Task ExecuteInBackgroundAsync(
        LabJob job,
        ActiveLabRun activeRun)
    {
        try
        {
            await _orchestrator.RunAsync(
                job,
                (output, _) => _outputStore.AppendAsync(
                    output,
                    CancellationToken.None),
                activeRun.Token);
        }
        catch
        {
            // The orchestrator converts expected execution failures to job states.
            // An unexpected infrastructure failure must not become unobserved.
        }
        finally
        {
            await _outputStore.CompleteAsync(job.Id, CancellationToken.None);
            _activeRuns.TryRemove(job.Id, out _);
            activeRun.Dispose();
            activeRun.Completion.TrySetResult();
        }
    }

    private sealed class ActiveLabRun(
        CancellationTokenSource cancellation) : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = cancellation;
        private bool _isDisposed;

        public CancellationToken Token => _cancellation.Token;

        public TaskCompletionSource Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryCancel()
        {
            lock (_gate)
            {
                if (_isDisposed)
                {
                    return false;
                }

                _cancellation.Cancel();
                return true;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _cancellation.Dispose();
            }
        }
    }
}
