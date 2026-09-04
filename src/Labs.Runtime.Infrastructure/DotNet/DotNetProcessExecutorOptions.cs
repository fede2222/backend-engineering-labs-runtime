namespace Labs.Runtime.Infrastructure.DotNet;

public sealed class DotNetProcessExecutorOptions
{
    public DotNetProcessExecutorOptions(
        string labsRoot,
        IEnumerable<DotNetLabProject> projects,
        string dockerExecutable = "docker",
        string sdkImage = "mcr.microsoft.com/dotnet/sdk:10.0",
        int memoryLimitMegabytes = 512,
        double cpuLimit = 2,
        int pidsLimit = 256,
        int tempFileSystemSizeMegabytes = 512,
        string containerUser = "65534:65534")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labsRoot);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentException.ThrowIfNullOrWhiteSpace(dockerExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(sdkImage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memoryLimitMegabytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cpuLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pidsLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            tempFileSystemSizeMegabytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerUser);

        LabsRoot = Path.GetFullPath(labsRoot);
        Projects = projects.ToArray();
        DockerExecutable = dockerExecutable;
        SdkImage = sdkImage;
        MemoryLimitMegabytes = memoryLimitMegabytes;
        CpuLimit = cpuLimit;
        PidsLimit = pidsLimit;
        TempFileSystemSizeMegabytes = tempFileSystemSizeMegabytes;
        ContainerUser = containerUser;
    }

    public string LabsRoot { get; }

    public IReadOnlyCollection<DotNetLabProject> Projects { get; }

    public string DockerExecutable { get; }

    public string SdkImage { get; }

    public int MemoryLimitMegabytes { get; }

    public double CpuLimit { get; }

    public int PidsLimit { get; }

    public int TempFileSystemSizeMegabytes { get; }

    public string ContainerUser { get; }
}
