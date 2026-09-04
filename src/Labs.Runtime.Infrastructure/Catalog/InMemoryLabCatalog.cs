using Labs.Runtime.Application.Abstractions;
using Labs.Runtime.Core.Labs;

namespace Labs.Runtime.Infrastructure.Catalog;

public sealed class InMemoryLabCatalog : ILabCatalog
{
    private readonly IReadOnlyDictionary<string, LabDefinition> _labs;

    public InMemoryLabCatalog()
        : this(BuiltInLabDefinitions.All)
    {
    }

    public InMemoryLabCatalog(IEnumerable<LabDefinition> labs)
    {
        ArgumentNullException.ThrowIfNull(labs);

        _labs = labs.ToDictionary(
            lab => lab.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    public ValueTask<LabDefinition?> FindAsync(
        string labId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labId);
        cancellationToken.ThrowIfCancellationRequested();

        _labs.TryGetValue(labId, out var lab);
        return ValueTask.FromResult(lab);
    }
}
