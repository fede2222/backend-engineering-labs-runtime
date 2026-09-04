using Labs.Runtime.Core.Execution;
using Labs.Runtime.Core.Output;
using Labs.Runtime.Infrastructure.Processes;

namespace Labs.Runtime.Infrastructure.DotNet;

public sealed class DotNetProcessExecutor : ILabExecutor
{
    public const string ExecutorType = "dotnet";

    private readonly DotNetProcessExecutorOptions _options;
    private readonly IProcessRunner _processRunner;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyDictionary<string, DotNetLabProject> _projects;

    public DotNetProcessExecutor(
        DotNetProcessExecutorOptions options,
        IProcessRunner processRunner,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options;
        _processRunner = processRunner;
        _timeProvider = timeProvider;
        _projects = options.Projects.ToDictionary(
            project => project.LabId,
            StringComparer.OrdinalIgnoreCase);
    }

    public string Type => ExecutorType;

    public Task PrepareAsync(
        LabExecutionContext context,
        Func<LabOutput, CancellationToken, ValueTask> publishOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(publishOutput);
        cancellationToken.ThrowIfCancellationRequested();

        _ = ResolveProjectPath(context.Lab.Id);
        return Task.CompletedTask;
    }

    public async Task<LabExecutionResult> ExecuteAsync(
        LabExecutionContext context,
        Func<LabOutput, CancellationToken, ValueTask> publishOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(publishOutput);

        var projectPath = ResolveProjectPath(context.Lab.Id);
        var sequence = 0L;
        var command = new ProcessCommand(
            _options.DotNetExecutable,
            Path.GetDirectoryName(projectPath)!,
            new[]
            {
                "run",
                "--project",
                projectPath,
                "--no-launch-profile",
                "--nologo"
            });

        var exitCode = await _processRunner.RunAsync(
            command,
            async (output, token) =>
            {
                var labOutput = new LabOutput(
                    context.JobId,
                    Interlocked.Increment(ref sequence),
                    _timeProvider.GetUtcNow(),
                    output.Stream,
                    output.Content);

                await publishOutput(labOutput, token);
            },
            cancellationToken);

        return exitCode == 0
            ? new LabExecutionResult(
                LabExecutionOutcome.Succeeded,
                ExitCode: exitCode)
            : new LabExecutionResult(
                LabExecutionOutcome.Failed,
                ExitCode: exitCode,
                FailureReason: $"dotnet exited with code {exitCode}.");
    }

    public Task CleanupAsync(
        LabExecutionContext context,
        Func<LabOutput, CancellationToken, ValueTask> publishOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(publishOutput);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private string ResolveProjectPath(string labId)
    {
        if (!_projects.TryGetValue(labId, out var project))
        {
            throw new KeyNotFoundException(
                $"No .NET project is configured for lab '{labId}'.");
        }

        var projectPath = Path.GetFullPath(
            Path.Combine(_options.LabsRoot, project.RelativeProjectPath));
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(
            _options.LabsRoot) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!projectPath.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidOperationException(
                $"The project configured for lab '{labId}' escapes the labs root.");
        }

        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException(
                $"The project configured for lab '{labId}' was not found.",
                projectPath);
        }

        return projectPath;
    }
}
