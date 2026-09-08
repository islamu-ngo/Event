namespace Explore.Domain;

public sealed class NotificationFanoutProcessorState
{
    public Guid Id { get; set; }
    public required string ProcessorCode { get; set; }
    public bool OptionalRemindersDeferred { get; set; }
    public DateTime UpdatedAt { get; set; }
}
