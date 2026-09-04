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
    public async Task ExecuteAsyncBuildsTrustedDotNetCommandAndPublishesOutput()
    {
        var projectPath = CreateProject("process-vs-thread");
        var processRunner = new RecordingProcessRunner(
            0,
            new ProcessOutputChunk(
                LabOutputStream.StandardOutput,
                "first\n"),
            new ProcessOutputChunk(
                LabOutputStream.StandardError,
                "warning\n"));
        var executor = CreateExecutor(processRunner, "process-vs-thread", projectPath);
        var context = CreateContext("process-vs-thread");
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
        Assert.Equal("dotnet", processRunner.Command!.FileName);
        Assert.Equal(Path.GetDirectoryName(projectPath), processRunner.Command.WorkingDirectory);
        Assert.Equal(
            new[]
            {
                "run",
                "--project",
                projectPath,
                "--no-launch-profile",
                "--nologo"
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
        Assert.Equal("dotnet exited with code 17.", result.FailureReason);
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

    private static LabExecutionContext CreateContext(string labId)
    {
        return new LabExecutionContext(
            Guid.NewGuid(),
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
