using Labs.Runtime.Application.Execution;
using Labs.Runtime.Core.Execution;
using Labs.Runtime.Core.Output;

namespace Labs.Runtime.Tests.Application;

public sealed class LabExecutorResolverTests
{
    [Fact]
    public void Resolve_WithRegisteredType_ReturnsExecutor()
    {
        var executor = new StubExecutor("dotnet");
        var resolver = new LabExecutorResolver([executor]);

        var result = resolver.Resolve("DOTNET");

        Assert.Same(executor, result);
    }

    [Fact]
    public void Resolve_WithUnknownType_Throws()
    {
        var resolver = new LabExecutorResolver([]);

        Assert.Throws<KeyNotFoundException>(() => resolver.Resolve("docker"));
    }

    private sealed class StubExecutor(string type) : ILabExecutor
    {
        public string Type { get; } = type;

        public Task PrepareAsync(
            LabExecutionContext context,
            Func<LabOutput, CancellationToken, ValueTask> publishOutput,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<LabExecutionResult> ExecuteAsync(
            LabExecutionContext context,
            Func<LabOutput, CancellationToken, ValueTask> publishOutput,
            CancellationToken cancellationToken) =>
            Task.FromResult(new LabExecutionResult(
                LabExecutionOutcome.Succeeded,
                ExitCode: 0));

        public Task CleanupAsync(
            LabExecutionContext context,
            Func<LabOutput, CancellationToken, ValueTask> publishOutput,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
