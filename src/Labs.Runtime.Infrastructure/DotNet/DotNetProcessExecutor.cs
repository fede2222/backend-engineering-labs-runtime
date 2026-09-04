using System.Globalization;
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
        var command = CreateRunCommand(context.JobId, projectPath);

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
                FailureReason: $"docker exited with code {exitCode}.");
    }

    public async Task CleanupAsync(
        LabExecutionContext context,
        Func<LabOutput, CancellationToken, ValueTask> publishOutput,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(publishOutput);
        cancellationToken.ThrowIfCancellationRequested();

        var command = new ProcessCommand(
            _options.DockerExecutable,
            _options.LabsRoot,
            new[]
            {
                "container",
                "rm",
                "--force",
                "--volumes",
                GetContainerName(context.JobId)
            });

        _ = await _processRunner.RunAsync(
            command,
            static (_, _) => ValueTask.CompletedTask,
            cancellationToken);
    }

    private ProcessCommand CreateRunCommand(Guid jobId, string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var containerProjectPath = $"/lab/{Path.GetFileName(projectPath)}";
        var cpuLimit = _options.CpuLimit.ToString(
            "0.###",
            CultureInfo.InvariantCulture);

        return new ProcessCommand(
            _options.DockerExecutable,
            _options.LabsRoot,
            new[]
            {
                "run",
                "--rm",
                "--pull",
                "never",
                "--name",
                GetContainerName(jobId),
                "--network",
                "none",
                "--memory",
                $"{_options.MemoryLimitMegabytes}m",
                "--memory-swap",
                $"{_options.MemoryLimitMegabytes}m",
                "--cpus",
                cpuLimit,
                "--pids-limit",
                _options.PidsLimit.ToString(CultureInfo.InvariantCulture),
                "--read-only",
                "--cap-drop",
                "ALL",
                "--security-opt",
                "no-new-privileges",
                "--user",
                _options.ContainerUser,
                "--tmpfs",
                $"/tmp:rw,nosuid,nodev,size={_options.TempFileSystemSizeMegabytes}m,mode=1777",
                "--mount",
                $"type=bind,source={projectDirectory},target=/lab,readonly",
                "--workdir",
                "/lab",
                "--env",
                "DOTNET_CLI_HOME=/tmp/dotnet",
                "--env",
                "NUGET_PACKAGES=/tmp/nuget",
                "--env",
                "HOME=/tmp",
                "--env",
                "DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK=true",
                "--env",
                "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true",
                _options.SdkImage,
                "dotnet",
                "run",
                "--project",
                containerProjectPath,
                "--artifacts-path",
                "/tmp/artifacts",
                "--no-launch-profile",
                "--nologo",
                "-p:UseAppHost=false"
            });
    }

    private static string GetContainerName(Guid jobId)
    {
        return $"backend-lab-{jobId:N}";
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
