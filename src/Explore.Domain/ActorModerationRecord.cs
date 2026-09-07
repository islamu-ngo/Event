using Explore.Domain.Enums;

namespace Explore.Domain;

public class ActorModerationRecord
{
    private ActorModerationRecord()
    {
    }

    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public Actor Actor { get; private set; } = null!;
    public GlobalModerationAction Action { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    public static ActorModerationRecord Create(
        Guid actorId,
        GlobalModerationAction action,
        string reasonCode,
        DateTime createdAt,
        Guid createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        return new ActorModerationRecord
        {
            Id = Guid.CreateVersion7(),
            ActorId = actorId,
            Action = action,
            ReasonCode = reasonCode.Trim(),
            CreatedAt = createdAt,
            CreatedBy = createdBy
        };
    }
}
