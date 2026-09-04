using Labs.Runtime.Core.Execution;
using Labs.Runtime.Core.Labs;
using Labs.Runtime.Core.Output;
using Labs.Runtime.Infrastructure.DotNet;
using Labs.Runtime.Infrastructure.Processes;

namespace Labs.Runtime.Tests.Infrastructure;

public sealed class DotNetProcessExecutorTests : IDisposable
{
    private readonly string _labsRoot = Path.Combine(
        Path.GetTempPath(),
        $"labs-runtime-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExecuteAsyncBuildsIsolatedDockerCommandAndPublishesOutput()
    {
        var projectPath = CreateProject("process-vs-thread");
        var jobId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var processRunner = new RecordingProcessRunner(
            0,
            new ProcessOutputChunk(
                LabOutputStream.StandardOutput,
                "first\n"),
            new ProcessOutputChunk(
                LabOutputStream.StandardError,
                "warning\n"));
        var executor = CreateExecutor(processRunner, "process-vs-thread", projectPath);
        var context = CreateContext("process-vs-thread", jobId);
        var published = new List<LabOutput>();

        var result = await executor.ExecuteAsync(
            context,
            (output, _) =>
            {
                published.Add(output);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(LabExecutionOutcome.Succeeded, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("docker", processRunner.Command!.FileName);
        Assert.Equal(_labsRoot, processRunner.Command.WorkingDirectory);
        Assert.Equal(
            new[]
            {
                "run",
                "--rm",
                "--pull",
                "never",
                "--name",
                "backend-lab-aaaaaaaabbbbccccddddeeeeeeeeeeee",
                "--network",
                "none",
                "--memory",
                "512m",
                "--memory-swap",
                "512m",
                "--cpus",
                "2",
                "--pids-limit",
                "256",
                "--read-only",
                "--cap-drop",
                "ALL",
                "--security-opt",
                "no-new-privileges",
                "--user",
                "65534:65534",
                "--tmpfs",
                "/tmp:rw,nosuid,nodev,size=512m,mode=1777",
                "--mount",
                $"type=bind,source={Path.GetDirectoryName(projectPath)},target=/lab,readonly",
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
                "mcr.microsoft.com/dotnet/sdk:10.0",
                "dotnet",
                "run",
                "--project",
                "/lab/process-vs-thread.csproj",
                "--artifacts-path",
                "/tmp/artifacts",
                "--no-launch-profile",
                "--nologo",
                "-p:UseAppHost=false"
            },
            processRunner.Command.Arguments);
        Assert.Collection(
            published,
            output =>
            {
                Assert.Equal(1, output.Sequence);
                Assert.Equal(LabOutputStream.StandardOutput, output.Stream);
                Assert.Equal("first\n", output.Content);
            },
            output =>
            {
                Assert.Equal(2, output.Sequence);
                Assert.Equal(LabOutputStream.StandardError, output.Stream);
                Assert.Equal("warning\n", output.Content);
            });
    }

    [Fact]
    public async Task ExecuteAsyncReturnsFailedResultForNonZeroExitCode()
    {
        var projectPath = CreateProject("failing-lab");
        var executor = CreateExecutor(
            new RecordingProcessRunner(17),
            "failing-lab",
            projectPath);

        var result = await executor.ExecuteAsync(
            CreateContext("failing-lab"),
            (_, _) => ValueTask.CompletedTask,
            CancellationToken.None);

        Assert.Equal(LabExecutionOutcome.Failed, result.Outcome);
        Assert.Equal(17, result.ExitCode);
        Assert.Equal("docker exited with code 17.", result.FailureReason);
    }

    [Fact]
    public async Task CleanupAsyncForcesRemovalOfJobContainer()
    {
        var jobId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var projectPath = CreateProject("process-vs-thread");
        var processRunner = new RecordingProcessRunner(0);
        var executor = CreateExecutor(
            processRunner,
            "process-vs-thread",
            projectPath);

        await executor.CleanupAsync(
            CreateContext("process-vs-thread", jobId),
            (_, _) => ValueTask.CompletedTask,
            CancellationToken.None);

        Assert.Equal("docker", processRunner.Command!.FileName);
        Assert.Equal(_labsRoot, processRunner.Command.WorkingDirectory);
        Assert.Equal(
            new[]
            {
                "container",
                "rm",
                "--force",
                "--volumes",
                "backend-lab-aaaaaaaabbbbccccddddeeeeeeeeeeee"
            },
            processRunner.Command.Arguments);
    }

    [Fact]
    public async Task PrepareAsyncRejectsUnknownLab()
    {
        var options = new DotNetProcessExecutorOptions(
            _labsRoot,
            Array.Empty<DotNetLabProject>());
        var executor = new DotNetProcessExecutor(
            options,
            new RecordingProcessRunner(0),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => executor.PrepareAsync(
                CreateContext("unknown"),
                (_, _) => ValueTask.CompletedTask,
                CancellationToken.None));

        Assert.Contains("unknown", exception.Message);
    }

    [Fact]
    public async Task PrepareAsyncRejectsProjectOutsideLabsRoot()
    {
        Directory.CreateDirectory(_labsRoot);
        var options = new DotNetProcessExecutorOptions(
            _labsRoot,
            new[] { new DotNetLabProject("unsafe", "../unsafe.csproj") });
        var executor = new DotNetProcessExecutor(
            options,
            new RecordingProcessRunner(0),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.PrepareAsync(
                CreateContext("unsafe"),
                (_, _) => ValueTask.CompletedTask,
                CancellationToken.None));

        Assert.Contains("escapes the labs root", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_labsRoot))
        {
            Directory.Delete(_labsRoot, recursive: true);
        }
    }

    private string CreateProject(string labId)
    {
        var directory = Path.Combine(_labsRoot, labId);
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, $"{labId}.csproj");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        return projectPath;
    }

    private DotNetProcessExecutor CreateExecutor(
        IProcessRunner processRunner,
        string labId,
        string projectPath)
    {
        var relativeProjectPath = Path.GetRelativePath(_labsRoot, projectPath);
        var options = new DotNetProcessExecutorOptions(
            _labsRoot,
            new[] { new DotNetLabProject(labId, relativeProjectPath) });

        return new DotNetProcessExecutor(
            options,
            processRunner,
            TimeProvider.System);
    }

    private static LabExecutionContext CreateContext(
        string labId,
        Guid? jobId = null)
    {
        return new LabExecutionContext(
            jobId ?? Guid.NewGuid(),
            new LabDefinition(
                labId,
                labId,
                DotNetProcessExecutor.ExecutorType,
                TimeSpan.FromSeconds(10)));
    }

    private sealed class RecordingProcessRunner(
        int exitCode,
        params ProcessOutputChunk[] outputs) : IProcessRunner
    {
        public ProcessCommand? Command { get; private set; }

        public async Task<int> RunAsync(
            ProcessCommand command,
            Func<ProcessOutputChunk, CancellationToken, ValueTask> publishOutput,
            CancellationToken cancellationToken)
        {
            Command = command;

            foreach (var output in outputs)
            {
                await publishOutput(output, cancellationToken);
            }

            return exitCode;
        }
    }
}
