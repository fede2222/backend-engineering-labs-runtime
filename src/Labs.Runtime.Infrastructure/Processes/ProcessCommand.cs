namespace Labs.Runtime.Infrastructure.Processes;

public sealed record ProcessCommand(
    string FileName,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);
