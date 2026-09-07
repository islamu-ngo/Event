using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class AiConsentGrant : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.CreateVersion7();

    [ForeignKey(nameof(Id))]
    public AiConsentGrant Self { get; set; } = null!;

    public Guid TenantId { get; set; }
    [ForeignKey(nameof(TenantId))]
    public Tenant Tenant { get; set; } = null!;

    /// <summary>
    /// The data subject whose classified field is being disclosed to an AI prompt.
    /// </summary>
    public Guid SubjectUserId { get; set; }
    [ForeignKey(nameof(SubjectUserId))]
    public User SubjectUser { get; set; } = null!;

    /// <summary>
    /// Registry-keyed entity name (e.g. <c>UserPii</c>). Must match a registered row in <c>AiContextDisclosureRegistry</c>.
    /// </summary>
    public required string EntityName { get; set; }

    /// <summary>
    /// Registry-keyed field name (e.g. <c>Email</c>). Must match a registered row in <c>AiContextDisclosureRegistry</c>.
    /// </summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// Maps to <see cref="AiProviderTrustTierEnum"/>. The provider-trust tier this grant authorizes.
    /// Grants are tier-specific; a grant at <c>TenantConfiguredExternalProcessor</c> does not authorize <c>LocalInProcessOrSameNetworkModel</c> unless re-granted.
    /// </summary>
    public int ProviderTrustTierId { get; set; } = (int)AiProviderTrustTierEnum.Unknown;

    /// <summary>
    /// Maps to <see cref="AiConsentGrantStatusEnum"/>. Only <c>Granted</c> authorizes disclosure.
    /// </summary>
    public int StatusId { get; set; } = (int)AiConsentGrantStatusEnum.Pending;

    /// <summary>
    /// Free-form purpose statement captured at grant time (e.g. "Provide my email to my event organizer's AI assistant"). Used for audit and revocation UX.
    /// </summary>
    public string? Purpose { get; set; }

    /// <summary>
    /// UTC timestamp at which the subject granted consent. Set when transitioning to <c>Granted</c>.
    /// </summary>
    public DateTimeOffset GrantedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp at which the subject revoked consent. Set when transitioning to <c>Revoked</c>.
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; set; }

    /// <summary>
    /// Optional UTC expiry. When in the past, the gateway treats the grant as <c>Expired</c> regardless of stored <c>StatusId</c>.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
