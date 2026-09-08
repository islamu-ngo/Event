using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventLocationDisclosureAudit : ITenantEntity
{
    private Guid _tenantId;

    private EventLocationDisclosureAudit()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => SetTenantId(value);
    }

    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => SetTenantId(value);
    }

    public Guid EventLocationId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public EventLocationDisclosureFields PreviousFields { get; private set; }
    public EventLocationDisclosureFields NewFields { get; private set; }
    public int PreviousAudienceId { get; private set; }
    public int NewAudienceId { get; private set; }
    public DateTime? PreviousRevealFullDetailsFromUtc { get; private set; }
    public DateTime? NewRevealFullDetailsFromUtc { get; private set; }
    public int PreviousPolicyVersion { get; private set; }
    public int NewPolicyVersion { get; private set; }
    public EventLocationDisclosureAuditReasonEnum Reason { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public static EventLocationDisclosureAudit Create(
        Guid tenantId,
        Guid eventLocationId,
        Guid actorUserId,
        EventLocationDisclosureFields previousFields,
        EventLocationDisclosureFields newFields,
        LocationDisclosureAudienceEnum previousAudience,
        LocationDisclosureAudienceEnum newAudience,
        DateTime? previousRevealFullDetailsFromUtc,
        DateTime? newRevealFullDetailsFromUtc,
        int previousPolicyVersion,
        int newPolicyVersion,
        EventLocationDisclosureAuditReasonEnum reason,
        DateTime occurredAtUtc)
    {
        RequireId(tenantId, nameof(tenantId));
        RequireId(eventLocationId, nameof(eventLocationId));
        RequireId(actorUserId, nameof(actorUserId));
        ValidateFields(previousFields, nameof(previousFields));
        ValidateFields(newFields, nameof(newFields));
        ValidateAudience(previousAudience, nameof(previousAudience));
        ValidateAudience(newAudience, nameof(newAudience));
        ValidateReason(reason, nameof(reason));
        RequireUtc(previousRevealFullDetailsFromUtc, nameof(previousRevealFullDetailsFromUtc));
        RequireUtc(newRevealFullDetailsFromUtc, nameof(newRevealFullDetailsFromUtc));
        RequireUtc(occurredAtUtc, nameof(occurredAtUtc));

        if (previousPolicyVersion < 0 || newPolicyVersion != previousPolicyVersion + 1)
        {
            throw new ArgumentException("A disclosure audit must advance the policy version by exactly one.", nameof(newPolicyVersion));
        }

        bool isAssociationCreation = previousPolicyVersion == 0
            && newPolicyVersion == 1
            && reason == EventLocationDisclosureAuditReasonEnum.AssociationCreated;
        if (!isAssociationCreation
            && reason is not EventLocationDisclosureAuditReasonEnum.GovernanceTightening
                and not EventLocationDisclosureAuditReasonEnum.PrivacyErasureRemediation
            && previousFields == newFields
            && previousAudience == newAudience
            && previousRevealFullDetailsFromUtc == newRevealFullDetailsFromUtc)
        {
            throw new ArgumentException("A disclosure audit requires an effective policy change.", nameof(newFields));
        }

        return new EventLocationDisclosureAudit
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EventLocationId = eventLocationId,
            ActorUserId = actorUserId,
            PreviousFields = previousFields,
            NewFields = newFields,
            PreviousAudienceId = (int)previousAudience,
            NewAudienceId = (int)newAudience,
            PreviousRevealFullDetailsFromUtc = previousRevealFullDetailsFromUtc,
            NewRevealFullDetailsFromUtc = newRevealFullDetailsFromUtc,
            PreviousPolicyVersion = previousPolicyVersion,
            NewPolicyVersion = newPolicyVersion,
            Reason = reason,
            OccurredAtUtc = occurredAtUtc
        };
    }

    private static void ValidateFields(EventLocationDisclosureFields fields, string parameterName)
    {
        if ((fields & ~EventLocationDisclosureFields.All) != 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Disclosure fields contain an unknown value.");
        }
    }

    private static void ValidateAudience(LocationDisclosureAudienceEnum audience, string parameterName)
    {
        if (!Enum.IsDefined(audience))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateReason(EventLocationDisclosureAuditReasonEnum reason, string parameterName)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void RequireId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }

    private static void RequireUtc(DateTime? value, string parameterName)
    {
        if (value.HasValue && (value.Value == default || value.Value.Kind != DateTimeKind.Utc))
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }
    }

    private void SetTenantId(Guid value)
    {
        RequireId(value, nameof(TenantId));
        if (_tenantId != Guid.Empty && _tenantId != value)
        {
            throw new InvalidOperationException("Disclosure audit tenant identity is immutable.");
        }

        _tenantId = value;
    }
}
