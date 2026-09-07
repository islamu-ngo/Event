using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;

namespace Explore.Domain;

public sealed class EventParticipationConfiguration :
    ITenantEntity,
    IAuditableEntity,
    ISoftDeletable,
    IConcurrencyAware
{
    private readonly List<ParticipationRequirementAttachment> _requirementAttachments = [];

    private EventParticipationConfiguration()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; private set; }
    public Event? Event { get; private set; }
    public int ParticipationHandlingModeId { get; private set; }
    public ParticipationHandlingMode? ParticipationHandlingMode { get; private set; }
    public int AdvanceRegistrationObligationId { get; private set; }
    public AdvanceRegistrationObligation? AdvanceRegistrationObligation { get; private set; }
    public int? IdentityAccessModeId { get; private set; }
    public IdentityAccessMode? IdentityAccessMode { get; private set; }
    public GuestRecoveryPolicyEnum? GuestRecoveryPolicy { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public IReadOnlyCollection<ParticipationRequirementAttachment> RequirementAttachments =>
        _requirementAttachments.AsReadOnly();

    public static EventParticipationConfiguration Create(
        Guid eventId,
        Guid tenantId,
        int participationHandlingModeId,
        int advanceRegistrationObligationId,
        int? identityAccessModeId,
        GuestRecoveryPolicyEnum? guestRecoveryPolicy,
        DateTime now)
    {
        EnsureValid(
            eventId,
            tenantId,
            participationHandlingModeId,
            advanceRegistrationObligationId,
            identityAccessModeId,
            guestRecoveryPolicy);

        return new EventParticipationConfiguration
        {
            Id = eventId,
            TenantId = tenantId,
            ParticipationHandlingModeId = participationHandlingModeId,
            AdvanceRegistrationObligationId = advanceRegistrationObligationId,
            IdentityAccessModeId = identityAccessModeId,
            GuestRecoveryPolicy = guestRecoveryPolicy,
            CreatedAt = EnsureUtc(now),
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    public void Reconfigure(
        int participationHandlingModeId,
        int advanceRegistrationObligationId,
        int? identityAccessModeId,
        GuestRecoveryPolicyEnum? guestRecoveryPolicy)
    {
        EnsureValid(
            Id,
            TenantId,
            participationHandlingModeId,
            advanceRegistrationObligationId,
            identityAccessModeId,
            guestRecoveryPolicy);

        foreach (ParticipationRequirementAttachment attachment in _requirementAttachments.Where(value => !value.IsDeleted))
        {
            RegistrationRequirement? requirement = attachment.RegistrationRequirement;
            if (requirement is null || requirement.IsDeleted ||
                !IsCompletionEffectAllowed(participationHandlingModeId, requirement) ||
                participationHandlingModeId == (int)ParticipationHandlingModeEnum.ExternalManaged &&
                requirement.Channels.Any(channel => !channel.IsDeleted && channel.IsNative) ||
                attachment.IsStandaloneQuestionnaire &&
                participationHandlingModeId != (int)ParticipationHandlingModeEnum.WalkIn)
            {
                throw new InvalidOperationException("Existing registration requirement attachments are incompatible with the requested participation mode.");
            }
        }

        ParticipationHandlingModeId = participationHandlingModeId;
        AdvanceRegistrationObligationId = advanceRegistrationObligationId;
        IdentityAccessModeId = identityAccessModeId;
        GuestRecoveryPolicy = guestRecoveryPolicy;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public ParticipationRequirementAttachment AttachRequirement(
        Guid attachmentId,
        RegistrationWorkflow workflow,
        RegistrationRequirement requirement,
        RegistrationFormVersion? formVersion,
        bool isStandaloneQuestionnaire,
        DateTime attachedAt)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(requirement);
        EnsureUtc(attachedAt);
        if (workflow.TenantId != TenantId || workflow.EventId != Id ||
            requirement.TenantId != TenantId || requirement.EventId != Id ||
            requirement.RegistrationWorkflowId != workflow.Id || requirement.IsDeleted ||
            workflow.IsDeleted || !workflow.Requirements.Contains(requirement) ||
            !string.Equals(workflow.Purpose, "registration", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Requirement must be active and owned by this event registration workflow.", nameof(requirement));
        }

        if (_requirementAttachments.Any(value => !value.IsDeleted &&
                (value.Id == attachmentId || value.RegistrationRequirementId == requirement.Id)))
        {
            throw new InvalidOperationException("The registration requirement is already attached.");
        }

        if (!IsCompletionEffectAllowed(ParticipationHandlingModeId, requirement))
        {
            throw new InvalidOperationException("REGISTRATION_REQUIREMENT_MODE_INVALID");
        }

        bool hasNativeChannel = requirement.Channels.Any(channel => !channel.IsDeleted && channel.IsNative);
        if (ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.ExternalManaged && hasNativeChannel)
        {
            throw new InvalidOperationException("REGISTRATION_REQUIREMENT_MODE_INVALID");
        }

        if (isStandaloneQuestionnaire)
        {
            if (ParticipationHandlingModeId != (int)ParticipationHandlingModeEnum.WalkIn || !hasNativeChannel ||
                formVersion is null || formVersion.IsDeleted ||
                formVersion.StatusId != (int)RegistrationFormStatusEnum.Published ||
                formVersion.TenantId != TenantId || formVersion.EventId != Id ||
                string.IsNullOrWhiteSpace(formVersion.SchemaHash) ||
                string.IsNullOrWhiteSpace(formVersion.DataSchemaArtifact) ||
                string.IsNullOrWhiteSpace(formVersion.UiSchemaArtifact) ||
                string.IsNullOrWhiteSpace(formVersion.LogicSchemaArtifact) ||
                string.IsNullOrWhiteSpace(formVersion.MappingArtifact) ||
                _requirementAttachments.Any(value => !value.IsDeleted && value.IsStandaloneQuestionnaire))
            {
                throw new InvalidOperationException("REGISTRATION_REQUIREMENT_MODE_INVALID");
            }
        }
        else if (formVersion is not null)
        {
            throw new ArgumentException("A form version is only valid for a standalone questionnaire.", nameof(formVersion));
        }

        ParticipationRequirementAttachment attachment = ParticipationRequirementAttachment.Create(
            attachmentId,
            this,
            workflow,
            requirement,
            formVersion,
            isStandaloneQuestionnaire,
            attachedAt);
        _requirementAttachments.Add(attachment);
        ConcurrencyStamp = Guid.CreateVersion7();
        return attachment;
    }

    public bool DetachRequirement(Guid requirementId, DateTime detachedAt)
    {
        EnsureUtc(detachedAt);
        ParticipationRequirementAttachment? attachment = _requirementAttachments.FirstOrDefault(
            value => !value.IsDeleted && value.RegistrationRequirementId == requirementId);
        if (attachment is null)
        {
            return false;
        }

        attachment.Detach(detachedAt);
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    private static bool IsCompletionEffectAllowed(
        int participationHandlingModeId,
        RegistrationRequirement requirement)
    {
        return (ParticipationHandlingModeEnum)participationHandlingModeId switch
        {
            ParticipationHandlingModeEnum.InformationOnly or ParticipationHandlingModeEnum.WalkIn =>
                requirement.CompletionEffectId == (int)RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            ParticipationHandlingModeEnum.ExternalManaged =>
                requirement.CompletionEffectId != (int)RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            ParticipationHandlingModeEnum.PlatformManaged => true,
            _ => false
        };
    }

    private static void EnsureValid(
        Guid eventId,
        Guid tenantId,
        int participationHandlingModeId,
        int advanceRegistrationObligationId,
        int? identityAccessModeId,
        GuestRecoveryPolicyEnum? guestRecoveryPolicy)
    {
        var errors = EventAuthorityRules.ValidateParticipationConfiguration(
            participationHandlingModeId,
            advanceRegistrationObligationId,
            identityAccessModeId,
            guestRecoveryPolicy).ToList();

        if (eventId == Guid.Empty)
        {
            errors.Add(new(
                EventParticipationConfigurationErrorCode.EventIdRequired,
                "Event id is required."));
        }

        if (tenantId == Guid.Empty)
        {
            errors.Add(new(
                EventParticipationConfigurationErrorCode.TenantIdRequired,
                "Tenant id is required."));
        }

        if (errors.Count > 0)
        {
            throw new EventParticipationConfigurationValidationException(errors);
        }
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", nameof(value));
        }

        return value;
    }
}
