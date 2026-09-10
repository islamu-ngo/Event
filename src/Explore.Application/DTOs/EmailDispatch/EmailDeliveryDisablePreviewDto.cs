
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
