using Explore.Application.Authorization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionCheckInAuthority(
    IAuthorizationProvider authorization,
    IAdmissionScannerCapabilityRepository scannerCapabilities) : IAdmissionCheckInAuthority
{
    public async Task<AdmissionCheckInAuthorizationDecision> AuthorizeAsync(
        AdmissionCheckInAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.EventId == Guid.Empty || request.TargetId == Guid.Empty)
            return Denied();

        if (request.ScannerCapabilityId is Guid scannerCapabilityId)
        {
            AdmissionScannerCapability? capability = await scannerCapabilities.GetAsync(
                request.TenantId, scannerCapabilityId, cancellationToken);
            AdmissionScannerCapabilityAction action = request.Action switch
            {
                AdmissionCheckInAction.CheckIn => AdmissionScannerCapabilityAction.CheckIn,
                AdmissionCheckInAction.Undo => AdmissionScannerCapabilityAction.Undo,
                _ => AdmissionScannerCapabilityAction.None
            };
            return capability is not null
                && capability.TenantId == request.TenantId
                && capability.EventId == request.EventId
                && capability.Permits(request.TargetId, action, request.OccurredAtUtc.UtcDateTime)
                    ? Allowed()
                    : Denied();
        }

        if (request.StaffActorId is not Guid staffActorId || staffActorId == Guid.Empty)
            return Denied();
        AuthorizationDecision decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.Event,
            request.EventId.ToString("D"),
            AuthorizationActions.Events.EventCheckInManage,
            new AuthorizationScope(TenantId: request.TenantId.ToString("D")),
            new EventScopedAuthorizationFacts(request.TenantId, request.EventId),
            new AuthorizationSubject(staffActorId)), cancellationToken);
        return decision.IsAllowed ? Allowed() : Denied();
    }

    private static AdmissionCheckInAuthorizationDecision Allowed() =>
        new(AdmissionCheckInAuthorizationOutcome.Authorized);

    private static AdmissionCheckInAuthorizationDecision Denied() =>
        new(AdmissionCheckInAuthorizationOutcome.Denied);
}
