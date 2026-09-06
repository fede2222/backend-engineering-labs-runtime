using Labs.Runtime.Core.Labs;

namespace Labs.Runtime.Infrastructure.Catalog;

public static class BuiltInLabDefinitions
{
    public static IReadOnlyList<LabDefinition> All { get; } =
        Array.AsReadOnly(
            new[]
            {
                new LabDefinition(
                    "process-vs-thread",
                    "Process vs Thread",
                    "dotnet",
                    TimeSpan.FromSeconds(120))
            });
}
