namespace Explore.Domain.Enums;

public enum StorageObjectDeletionState
{
    AwaitingProducer = 1,
    Ready = 2,
    Deleting = 3,
    Absent = 4
}
