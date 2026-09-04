using Labs.Runtime.Core.Labs;
using Labs.Runtime.Infrastructure.Catalog;

namespace Labs.Runtime.Tests.Infrastructure;

public sealed class InMemoryLabCatalogTests
{
    [Fact]
    public async Task FindAsync_DefaultCatalog_ReturnsProcessVsThread()
    {
        var catalog = new InMemoryLabCatalog();

        var lab = await catalog.FindAsync(
            "process-vs-thread",
            CancellationToken.None);

        Assert.NotNull(lab);
        Assert.Equal("Process vs Thread", lab.DisplayName);
        Assert.Equal("dotnet", lab.ExecutorType);
        Assert.Equal(TimeSpan.FromSeconds(10), lab.Timeout);
    }

    [Fact]
    public async Task FindAsync_IsCaseInsensitive()
    {
        var catalog = new InMemoryLabCatalog();

        var lab = await catalog.FindAsync(
            "PROCESS-VS-THREAD",
            CancellationToken.None);

        Assert.NotNull(lab);
    }

    [Fact]
    public void Constructor_WithDuplicateIds_Throws()
    {
        var first = new LabDefinition(
            "duplicate",
            "First",
            "dotnet",
            TimeSpan.FromSeconds(1));
        var second = new LabDefinition(
            "DUPLICATE",
            "Second",
            "dotnet",
            TimeSpan.FromSeconds(1));

        Assert.Throws<ArgumentException>(() =>
            new InMemoryLabCatalog([first, second]));
    }
}
