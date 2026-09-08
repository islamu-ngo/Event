using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class Actor : IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }

    [ForeignKey("ActorType")]
    public int ActorTypeId { get; set; }

    public required ActorType ActorType { get; set; }

    // Navigation Properties & Foreign Keys
    [ForeignKey(nameof(UserId))]
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    [ForeignKey(nameof(GroupId))]
    public Guid? GroupId { get; set; }
    public Group? Group { get; set; }

    [ForeignKey(nameof(ExternalActorSubjectId))]
    public Guid? ExternalActorSubjectId { get; set; }
    public ExternalActorSubject? ExternalActorSubject { get; set; }

    [ForeignKey(nameof(ServicePrincipalId))]
    public Guid? ServicePrincipalId { get; set; }
    public ServicePrincipal? ServicePrincipal { get; set; }

    public ICollection<AtprotoIdentity> AtprotoIdentities { get; set; } = [];
    public ICollection<ActorModerationRecord> ModerationRecords { get; set; } = [];
    public ICollection<ActorMerge> MergesFrom { get; set; } = [];
    public ICollection<ActorMerge> MergesInto { get; set; } = [];

    public bool IsSuspended { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public Guid? SuspendedBy { get; set; }
    public string? ModerationReasonCode { get; set; }

    /// <summary>
    /// 1:1 extension table containing actor-identifying fields.
    /// </summary>
    public required ActorPii Pii { get; set; }

    [NotMapped]
    public string DisplayName
    {
        get => Pii.DisplayName;
        set => Pii.DisplayName = value;
    }

    public string? Description { get; set; }
    public string? ProfilePictureCid { get; set; }
    public string? BackgroundColor { get; set; }
    public string? BackgroundEffect { get; set; }
    public string? BannerColor { get; set; }

    [NotMapped]
    public string? ProfilePictureUri
    {
        get => Pii.ProfilePictureUri;
        set => Pii.ProfilePictureUri = value;
    }

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Soft delete fields
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public void PromoteToOrganization(
        Organization organization,
        ActorType organizationActorType,
        DateTime when,
        Guid by)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ValidateTargetOwner(organization.Id, organization.IsDeleted, organization.Actor, nameof(organization));
        ValidateTargetActorType(organizationActorType, ActorTypeEnum.Organization, nameof(organizationActorType));
        ExternalActorSubject externalSubject = RequireActiveExternalUnclassifiedSource();
        ValidateTransitionAudit(by);

        externalSubject.Retire(when, by);
        externalSubject.Actor = null;
        ExternalActorSubjectId = null;
        ExternalActorSubject = null;
        OrganizationId = organization.Id;
        Organization = organization;
        organization.Actor = this;
        ActorTypeId = organizationActorType.Id;
        ActorType = organizationActorType;
        MarkUpdated(when, by);
    }

    public void PromoteToGroup(
        Group group,
        ActorType groupActorType,
        DateTime when,
        Guid by)
    {
        ArgumentNullException.ThrowIfNull(group);
        ValidateTargetOwner(group.Id, group.IsDeleted, group.Actor, nameof(group));
        ValidateTargetActorType(groupActorType, ActorTypeEnum.Group, nameof(groupActorType));
        ExternalActorSubject externalSubject = RequireActiveExternalUnclassifiedSource();
        ValidateTransitionAudit(by);

        externalSubject.Retire(when, by);
        externalSubject.Actor = null;
        ExternalActorSubjectId = null;
        ExternalActorSubject = null;
        GroupId = group.Id;
        Group = group;
        group.Actor = this;
        ActorTypeId = groupActorType.Id;
        ActorType = groupActorType;
        MarkUpdated(when, by);
    }

    public void RetireAsMergedSource(DateTime when, Guid by)
    {
        RequireActiveExternalUnclassifiedSource();
        ValidateTransitionAudit(by);

        IsDeleted = true;
        DeletedAt = when;
        DeletedBy = by;
        MarkUpdated(when, by);
    }

    public void TombstoneForUserPrivacyErasure(DateTime when, Guid subjectId)
    {
        ValidateTransitionAudit(subjectId);
        if (UserId != subjectId
            || OrganizationId is not null || Organization is not null
            || GroupId is not null || Group is not null
            || ExternalActorSubjectId is not null || ExternalActorSubject is not null
            || ServicePrincipalId is not null || ServicePrincipal is not null)
        {
            throw new InvalidOperationException(
                "Only an exclusively user-owned Actor can be tombstoned for User privacy erasure.");
        }

        UserId = null;
        User = null;
        CreatedBy = null;
        IsDeleted = true;
        DeletedAt = when;
        DeletedBy = null;
        UpdatedAt = when;
        UpdatedBy = null;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Suspend(string reasonCode, DateTime when, Guid by)
    {
        string normalizedReasonCode = ValidateModerationTransition(reasonCode, when, by);
        if (IsSuspended)
        {
            return;
        }

        IsSuspended = true;
        SuspendedAt = when;
        SuspendedBy = by;
        ModerationReasonCode = normalizedReasonCode;
        ModerationRecords.Add(ActorModerationRecord.Create(Id, GlobalModerationAction.Suspend, normalizedReasonCode, when, by));
        MarkUpdated(when, by);
    }

    public void Reinstate(string reasonCode, DateTime when, Guid by)
    {
        string normalizedReasonCode = ValidateModerationTransition(reasonCode, when, by);
        if (!IsSuspended)
        {
            return;
        }

        IsSuspended = false;
        SuspendedAt = null;
        SuspendedBy = null;
        ModerationReasonCode = null;
        ModerationRecords.Add(ActorModerationRecord.Create(Id, GlobalModerationAction.Reinstate, normalizedReasonCode, when, by));
        MarkUpdated(when, by);
    }

    private ExternalActorSubject RequireActiveExternalUnclassifiedSource()
    {
        if (IsDeleted || IsSuspended)
        {
            throw new InvalidOperationException("Only an active Actor can transition from an external subject.");
        }

        if (ActorTypeId != (int)ActorTypeEnum.ExternalUnclassified)
        {
            throw new InvalidOperationException("Only an ExternalUnclassified Actor can transition from an external subject.");
        }

        if (UserId is not null || User is not null
            || OrganizationId is not null || Organization is not null
            || GroupId is not null || Group is not null
            || ServicePrincipalId is not null || ServicePrincipal is not null)
        {
            throw new InvalidOperationException("The Actor must be owned only by an external Actor subject.");
        }

        if (ExternalActorSubjectId is not Guid externalSubjectId
            || externalSubjectId == Guid.Empty
            || ExternalActorSubject is null
            || ExternalActorSubject.Id != externalSubjectId
            || ExternalActorSubject.IsDeleted
            || ExternalActorSubject.Actor is not null && !ReferenceEquals(ExternalActorSubject.Actor, this))
        {
            throw new InvalidOperationException("The Actor must have one active matching external Actor subject owner.");
        }

        return ExternalActorSubject;
    }

    private void ValidateTargetOwner(Guid ownerId, bool ownerIsDeleted, Actor? existingActor, string parameterName)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("The promoted owner must have an ID.", parameterName);
        }

        if (ownerIsDeleted)
        {
            throw new InvalidOperationException("The promoted owner must be active.");
        }

        if (existingActor is not null && !ReferenceEquals(existingActor, this))
        {
            throw new InvalidOperationException("The promoted owner already belongs to another Actor.");
        }
    }

    private static void ValidateTargetActorType(
        ActorType actorType,
        ActorTypeEnum expectedType,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(actorType, parameterName);
        if (actorType.Id != (int)expectedType)
        {
            throw new ArgumentException($"The Actor type must be {expectedType}.", parameterName);
        }
    }

    private static void ValidateTransitionAudit(Guid by)
    {
        if (by == Guid.Empty)
        {
            throw new ArgumentException("A transitioning user is required.", nameof(by));
        }
    }

    private string ValidateModerationTransition(string reasonCode, DateTime when, Guid by)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("Deleted actors cannot be moderated.");
        }

        ValidateTransitionAudit(by);
        if (when.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A UTC moderation timestamp is required.", nameof(when));
        }

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("A moderation reason code is required.", nameof(reasonCode));
        }

        string normalizedReasonCode = reasonCode.Trim();
        if (normalizedReasonCode.Length > 128)
        {
            throw new ArgumentException("A moderation reason code must be 128 characters or fewer.", nameof(reasonCode));
        }

        return normalizedReasonCode;
    }

    private void MarkUpdated(DateTime when, Guid by)
    {
        UpdatedAt = when;
        UpdatedBy = by;
        ConcurrencyStamp = Guid.CreateVersion7();
    }
}
