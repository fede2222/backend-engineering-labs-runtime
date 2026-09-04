using Labs.Runtime.Core.Labs;

namespace Labs.Runtime.Application.Abstractions;

public interface ILabCatalog
{
    ValueTask<LabDefinition?> FindAsync(
        string labId,
        CancellationToken cancellationToken);
}
