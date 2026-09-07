namespace Explore.Domain.Enums;

public enum PaymentAttemptStatusEnum
{
    Created = 1,
    DispatchPending = 2,
    RequiresAction = 3,
    Processing = 4,
    Succeeded = 5,
    Failed = 6,
    Cancelled = 7,
    Unknown = 8
}
