using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Authentication.Local.Handlers.Queries;

public sealed record GetLocalIdentityLifecycleCapabilitiesQuery(
    LocalSessionAuthority? Authority = null, bool PublicDiscovery = false) : IQuery<LocalIdentityLifecycleCapabilities>;

public sealed record LocalIdentityLifecycleCapabilities(bool VerifyEmail, bool RecoverPassword, bool ChangePassword);

public sealed class GetLocalIdentityLifecycleCapabilitiesQueryHandler(
    IAuthenticationProviderDispatcher providers,
    ILocalIdentityAuthService authentication,
    ILocalCredentialAdministration credentials,
    IEmailDeliveryCapabilityResolver email)
    : IQueryHandler<GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities>
{
    public async Task<LocalIdentityLifecycleCapabilities> QueryAsync(
        GetLocalIdentityLifecycleCapabilitiesQuery request, CancellationToken cancellationToken)
    {
        if (request.PublicDiscovery)
        {
            if (await providers.GetActivePrimaryProviderAsync(cancellationToken) != AuthenticationProviderKind.Local)
                return new(false, false, false);
            bool available = (await email.ResolveAsync(null, cancellationToken)).State == EmailDeliveryState.Available;
            return new(available, available, false);
        }
        if (request.Authority is not { } authority
            || await authentication.ValidateSessionAsync(authority, cancellationToken) != LocalSessionValidationOutcome.Valid
            || (await credentials.ReadLinkedIdentityAsync(authority.LocalSubjectId, cancellationToken))?.CredentialState != LocalCredentialState.Ready)
            return new(false, false, false);
        bool deliveryAvailable = (await email.ResolveAsync(null, cancellationToken)).State == EmailDeliveryState.Available;
        return new(deliveryAvailable, deliveryAvailable && authority.EmailVerified, true);
    }
}
