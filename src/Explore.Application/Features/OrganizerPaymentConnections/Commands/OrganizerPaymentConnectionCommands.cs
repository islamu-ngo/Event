using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.OrganizerPaymentConnections.Commands;

public sealed record RecordOrganizerPaymentConnectionCommand(
    Guid TenantId,
    Guid OrganizerActorId,
    string ProviderCode,
    string ConnectPlatformId,
    string ExternalAccountId) : ICommand<BaseCommandResponse<Guid>>;

public sealed record ReplaceOrganizerPaymentConnectionCommand(
    Guid TenantId,
    Guid OrganizerActorId,
    Guid CurrentConnectionId,
    string NewExternalAccountId) : ICommand<BaseCommandResponse<Guid>>;

public sealed record DisableOrganizerPaymentConnectionCommand(
    Guid TenantId,
    Guid OrganizerActorId,
    Guid ConnectionId,
    string ReasonCode) : ICommand<BaseCommandResponse<Guid>>;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManagePaidEventCommerce)]
public sealed record CreateOrganizerPaymentOnboardingLinkCommand(
    Guid EventId,
    Uri ReturnUrl,
    Uri RefreshUrl) : ICommand<BaseCommandResponse<OrganizerPaymentOnboardingLinkResult>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}

public sealed record OrganizerPaymentOnboardingLinkResult(
    Uri OnboardingUrl,
    bool ReusedExistingConnection);
