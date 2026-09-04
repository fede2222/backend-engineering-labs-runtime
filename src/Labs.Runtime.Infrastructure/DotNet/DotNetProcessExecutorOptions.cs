namespace Labs.Runtime.Infrastructure.DotNet;

public sealed class DotNetProcessExecutorOptions
{
    public DotNetProcessExecutorOptions(
        string labsRoot,
        IEnumerable<DotNetLabProject> projects,
        string dotNetExecutable = "dotnet")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labsRoot);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentException.ThrowIfNullOrWhiteSpace(dotNetExecutable);

        LabsRoot = Path.GetFullPath(labsRoot);
        Projects = projects.ToArray();
        DotNetExecutable = dotNetExecutable;
    }

    public string LabsRoot { get; }

    public IReadOnlyCollection<DotNetLabProject> Projects { get; }

    public string DotNetExecutable { get; }
}
