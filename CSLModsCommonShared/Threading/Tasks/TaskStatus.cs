namespace CSLModsCommon.Threading.Tasks; 
public enum TaskStatus {
    Created,
    WaitingForActivation,
    WaitingToRun,
    Running,

    // Blocked,
    WaitingForChildrenToComplete,
    RanToCompletion,
    Canceled,
    Faulted
}