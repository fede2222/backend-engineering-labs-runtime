using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Labs.Runtime.Application.Abstractions;
using Labs.Runtime.Core.Output;

namespace Labs.Runtime.Infrastructure.Output;

public sealed class InMemoryLabOutputStore : ILabOutputStore
{
    private readonly ConcurrentDictionary<Guid, JobOutputBuffer> _buffers = new();
    private readonly int _maxBufferedOutputsPerJob;

    public InMemoryLabOutputStore(InMemoryLabOutputStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _maxBufferedOutputsPerJob = options.MaxBufferedOutputsPerJob;
    }

    public ValueTask CreateAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "The job id cannot be empty.",
                nameof(jobId));
        }

        if (!_buffers.TryAdd(
                jobId,
                new JobOutputBuffer(jobId, _maxBufferedOutputsPerJob)))
        {
            throw new InvalidOperationException(
                $"An output buffer for lab job '{jobId}' already exists.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AppendAsync(
        LabOutput output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();

        GetBuffer(output.JobId).Append(output);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<LabOutput> ReadAllAsync(
        Guid jobId,
        long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (afterSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(afterSequence),
                "The output sequence cannot be negative.");
        }

        var buffer = GetBuffer(jobId);
        var nextSequence = afterSequence;

        while (true)
        {
            var read = buffer.ReadAfter(nextSequence);

            foreach (var output in read.Outputs)
            {
                nextSequence = output.Sequence;
                yield return output;
            }

            if (read.IsCompleted)
            {
                yield break;
            }

            if (read.Outputs.Count == 0)
            {
                await read.Changed.WaitAsync(cancellationToken);
            }
        }
    }

    public ValueTask CompleteAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetBuffer(jobId).Complete();
        return ValueTask.CompletedTask;
    }

    private JobOutputBuffer GetBuffer(Guid jobId)
    {
        return _buffers.TryGetValue(jobId, out var buffer)
            ? buffer
            : throw new KeyNotFoundException(
                $"An output buffer for lab job '{jobId}' was not found.");
    }

    private sealed class JobOutputBuffer(
        Guid jobId,
        int maxBufferedOutputs)
    {
        private readonly object _gate = new();
        private readonly List<LabOutput> _outputs = [];
        private TaskCompletionSource _changed = CreateSignal();
        private bool _isCompleted;

        public void Append(LabOutput output)
        {
            TaskCompletionSource changed;

            lock (_gate)
            {
                if (_isCompleted)
                {
                    throw new InvalidOperationException(
                        $"The output buffer for lab job '{jobId}' is complete.");
                }

                if (_outputs.Count > 0
                    && output.Sequence <= _outputs[^1].Sequence)
                {
                    throw new InvalidOperationException(
                        "Lab output sequences must be strictly increasing.");
                }

                _outputs.Add(output);
                if (_outputs.Count > maxBufferedOutputs)
                {
                    _outputs.RemoveAt(0);
                }

                changed = _changed;
                _changed = CreateSignal();
            }

            changed.TrySetResult();
        }

        public OutputRead ReadAfter(long sequence)
        {
            lock (_gate)
            {
                var outputs = _outputs
                    .Where(output => output.Sequence > sequence)
                    .ToArray();

                return new OutputRead(
                    outputs,
                    _isCompleted,
                    _changed.Task);
            }
        }

        public void Complete()
        {
            TaskCompletionSource changed;

            lock (_gate)
            {
                if (_isCompleted)
                {
                    return;
                }

                _isCompleted = true;
                changed = _changed;
            }

            changed.TrySetResult();
        }

        private static TaskCompletionSource CreateSignal()
        {
            return new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private sealed record OutputRead(
        IReadOnlyList<LabOutput> Outputs,
        bool IsCompleted,
        Task Changed);
}
