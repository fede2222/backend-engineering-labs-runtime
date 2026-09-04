namespace Labs.Runtime.Core.Jobs;

public enum LabJobStatus
{
    Queued,
    Preparing,
    Running,
    CleaningUp,
    Succeeded,
    Failed,
    TimedOut,
    Cancelled
}
