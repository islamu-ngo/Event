using Explore.Domain.Enums;

namespace Explore.Domain;

public class AtprotoIdentityModerationRecord
{
    private AtprotoIdentityModerationRecord()
    {
    }

    public Guid Id { get; private set; }
    public Guid AtprotoIdentityId { get; private set; }
    public AtprotoIdentity AtprotoIdentity { get; private set; } = null!;
    public GlobalModerationAction Action { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    public static AtprotoIdentityModerationRecord Create(
        Guid atprotoIdentityId,
        GlobalModerationAction action,
        string reasonCode,
        DateTime createdAt,
        Guid createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        return new AtprotoIdentityModerationRecord
        {
            Id = Guid.CreateVersion7(),
            AtprotoIdentityId = atprotoIdentityId,
            Action = action,
            ReasonCode = reasonCode.Trim(),
            CreatedAt = createdAt,
            CreatedBy = createdBy
        };
    }
}
