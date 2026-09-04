using Labs.Runtime.Core.Execution;

namespace Labs.Runtime.Application.Execution;

public sealed class LabExecutorResolver
{
    private readonly IReadOnlyDictionary<string, ILabExecutor> _executors;

    public LabExecutorResolver(IEnumerable<ILabExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);

        _executors = executors.ToDictionary(
            executor => executor.Type,
            StringComparer.OrdinalIgnoreCase);
    }

    public ILabExecutor Resolve(string executorType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executorType);

        if (_executors.TryGetValue(executorType, out var executor))
        {
            return executor;
        }

        throw new KeyNotFoundException(
            $"No lab executor is registered for type '{executorType}'.");
    }
}
