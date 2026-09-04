using Labs.Runtime.Core.Labs;

namespace Labs.Runtime.Tests.Labs;

public sealed class LabDefinitionTests
{
    [Fact]
    public void Create_WithValidValues_PreservesConfiguration()
    {
        var timeout = TimeSpan.FromSeconds(10);

        var lab = new LabDefinition(
            "process-vs-thread",
            "Process vs Thread",
            "dotnet",
            timeout);

        Assert.Equal("process-vs-thread", lab.Id);
        Assert.Equal("Process vs Thread", lab.DisplayName);
        Assert.Equal("dotnet", lab.ExecutorType);
        Assert.Equal(timeout, lab.Timeout);
    }

    [Fact]
    public void Create_WithNonPositiveTimeout_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LabDefinition(
                "process-vs-thread",
                "Process vs Thread",
                "dotnet",
                TimeSpan.Zero));
    }
}
