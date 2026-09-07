namespace Explore.Domain;

public enum EventAddOnInventoryOutcome
{
    Reserved = 1,
    AlreadyReserved = 2,
    InsufficientInventory = 3,
    NotFound = 4,
    TenantMismatch = 5,
}

public enum EventAddOnFulfillmentOutcome
{
    Fulfilled = 1,
    AlreadyFulfilled = 2,
    NotReserved = 3,
    NotFound = 4,
    TenantMismatch = 5,
}

public enum EventAddOnRefundOutcome
{
    Allocated = 1,
    AlreadyAllocated = 2,
    ExceedsCapturedAmount = 3,
    NotFound = 4,
    TenantMismatch = 5,
    ProviderFailed = 6,
}

public enum EventAddOnRefundAllocationStatus
{
    PendingProvider = 1,
    Confirmed = 2,
    Failed = 3,
    ConfirmedInventoryReleasePending = 4,
}

public sealed record EventAddOnInventoryResult
{
    private EventAddOnInventoryResult(
        EventAddOnInventoryOutcome outcome,
        EventAddOnInventoryAllocation? allocation)
    {
        Outcome = outcome;
        Allocation = allocation;
    }

    public EventAddOnInventoryOutcome Outcome { get; }

    public EventAddOnInventoryAllocation? Allocation { get; }

    public static EventAddOnInventoryResult Reserved(EventAddOnInventoryAllocation allocation) =>
        new(EventAddOnInventoryOutcome.Reserved, allocation);

    public static EventAddOnInventoryResult Existing(EventAddOnInventoryAllocation allocation) =>
        new(EventAddOnInventoryOutcome.AlreadyReserved, allocation);

    public static EventAddOnInventoryResult Failure(EventAddOnInventoryOutcome outcome) =>
        outcome is EventAddOnInventoryOutcome.InsufficientInventory or
            EventAddOnInventoryOutcome.NotFound or
            EventAddOnInventoryOutcome.TenantMismatch
            ? new EventAddOnInventoryResult(outcome, null)
            : throw new ArgumentOutOfRangeException(nameof(outcome));
}

public sealed record EventAddOnFulfillmentResult
{
    private EventAddOnFulfillmentResult(
        EventAddOnFulfillmentOutcome outcome,
        EventAddOnFulfillment? fulfillment)
    {
        Outcome = outcome;
        Fulfillment = fulfillment;
    }

    public EventAddOnFulfillmentOutcome Outcome { get; }

    public EventAddOnFulfillment? Fulfillment { get; }

    public static EventAddOnFulfillmentResult Fulfilled(EventAddOnFulfillment fulfillment) =>
        new(EventAddOnFulfillmentOutcome.Fulfilled, fulfillment);

    public static EventAddOnFulfillmentResult Existing(EventAddOnFulfillment fulfillment) =>
        new(EventAddOnFulfillmentOutcome.AlreadyFulfilled, fulfillment);

    public static EventAddOnFulfillmentResult Failure(EventAddOnFulfillmentOutcome outcome) =>
        outcome is EventAddOnFulfillmentOutcome.NotReserved or
            EventAddOnFulfillmentOutcome.NotFound or
            EventAddOnFulfillmentOutcome.TenantMismatch
            ? new EventAddOnFulfillmentResult(outcome, null)
            : throw new ArgumentOutOfRangeException(nameof(outcome));
}

public sealed record EventAddOnRefundResult
{
    private EventAddOnRefundResult(
        EventAddOnRefundOutcome outcome,
        EventAddOnRefundAllocation? allocation)
    {
        Outcome = outcome;
        Allocation = allocation;
    }

    public EventAddOnRefundOutcome Outcome { get; }

    public EventAddOnRefundAllocation? Allocation { get; }

    public static EventAddOnRefundResult Allocated(EventAddOnRefundAllocation allocation) =>
        new(EventAddOnRefundOutcome.Allocated, allocation);

    public static EventAddOnRefundResult Existing(EventAddOnRefundAllocation allocation) =>
        new(EventAddOnRefundOutcome.AlreadyAllocated, allocation);

    public static EventAddOnRefundResult Failure(EventAddOnRefundOutcome outcome) =>
        outcome is EventAddOnRefundOutcome.ExceedsCapturedAmount or
            EventAddOnRefundOutcome.NotFound or
            EventAddOnRefundOutcome.TenantMismatch or
            EventAddOnRefundOutcome.ProviderFailed
            ? new EventAddOnRefundResult(outcome, null)
            : throw new ArgumentOutOfRangeException(nameof(outcome));
}
