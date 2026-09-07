// ABOUTME: Publishes the non-secret impact and expiring confirmation for an email-delivery disable operation.
// ABOUTME: Includes actual scope revisions while keeping SMTP configuration and actor authority server-side.

using System.Collections.Immutable;

namespace Explore.Application.DTOs.EmailDispatch;

public sealed record EmailDeliveryDisableAffectedScopeDto(Guid? TenantId, long Revision);

public sealed record EmailDeliveryDisablePreviewDto(
    Guid? TenantId,
    long ExpectedRevision,
    bool IsLocked,
    ImmutableArray<EmailDeliveryDisableAffectedScopeDto> AffectedScopes,
    string? ConfirmationToken,
    DateTimeOffset? ExpiresAtUtc)
{
    public bool CanDisable => !IsLocked && !AffectedScopes.IsDefaultOrEmpty;
}
