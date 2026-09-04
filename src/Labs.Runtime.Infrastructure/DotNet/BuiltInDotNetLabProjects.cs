namespace Labs.Runtime.Infrastructure.DotNet;

public static class BuiltInDotNetLabProjects
{
    public static IReadOnlyCollection<DotNetLabProject> All { get; } =
        Array.AsReadOnly(
            new[]
            {
                new DotNetLabProject(
                    "process-vs-thread",
                    "labs/csharp-dotnet/process-vs-thread/process-vs-thread.csproj")
            });
}
