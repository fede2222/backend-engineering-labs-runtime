namespace Labs.Runtime.Infrastructure.DotNet;

public sealed record DotNetLabProject
{
    public DotNetLabProject(string labId, string relativeProjectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeProjectPath);

        if (Path.IsPathRooted(relativeProjectPath))
        {
            throw new ArgumentException(
                "The project path must be relative to the configured labs root.",
                nameof(relativeProjectPath));
        }

        if (!string.Equals(
                Path.GetExtension(relativeProjectPath),
                ".csproj",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The project path must point to a .csproj file.",
                nameof(relativeProjectPath));
        }

        LabId = labId;
        RelativeProjectPath = relativeProjectPath;
    }

    public string LabId { get; }

    public string RelativeProjectPath { get; }
}
