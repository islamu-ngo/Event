using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Retains the actual authenticated request or native Local creation receipt. Configured
/// startup never borrows a human lifetime, and setup never falls back to an administrator role.
/// </summary>
public sealed class TrustedSetupSettingsAuthority(
    IHttpContextAccessor contextAccessor,
    IEnumerable<IHumanAuthenticationLifetimeObserver> lifetimeObservers,
    ISetupSecretProvider setupProvider,
    ILocalCredentialAdministration credentialStore,
    IAuthenticationProviderDispatcher providerDispatcher,
    IEnumerable<IConfiguredAdministratorBootstrapProvider> bootstrapProviders,
    TimeProvider timeProvider) : ITrustedSetupSettingsAuthority
{
    private readonly IHttpContextAccessor contexts = contextAccessor;
    private readonly IEnumerable<IHumanAuthenticationLifetimeObserver> lifetimes = lifetimeObservers;
    private readonly ISetupSecretProvider setup = setupProvider;
    private readonly ILocalCredentialAdministration credentials = credentialStore;
    private readonly IAuthenticationProviderDispatcher providers = providerDispatcher;
    private readonly IEnumerable<IConfiguredAdministratorBootstrapProvider> configuredProviders = bootstrapProviders;
    private readonly TimeProvider clock = timeProvider;

    public async Task<ITrustedSetupSettingsAuthorityLease> CaptureAsync(
        TrustedSetupSettingsSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Only a configured native credential receipt admits a request-free startup.
        bool startup = source.Local is not null && source.ConfiguredAccount is not null;
        HttpContext? request = startup ? null : contexts.HttpContext
            ?? throw new UnauthorizedAccessException("Setup requires its original authenticated request.");
        var principal = request?.User;
        HumanAuthenticationLifetime? lifetime = null;
        string? secret = null;
        if (request is not null)
        {
            var authenticated = await request.AuthenticateAsync();
            if (!authenticated.Succeeded || principal is null
                || !principal.Identities.Any(identity => identity.IsAuthenticated)
                || authenticated.Principal is null
                || !authenticated.Principal.Identities.Any(identity => principal.Identities.Contains(identity)))
                throw new UnauthorizedAccessException("Setup request authentication custody is absent.");
            bool setupAuthentication = authenticated.Ticket?.AuthenticationScheme == ApiAuthenticationSchemeNames.SetupSecret;
            if (source.Local is null && (source.Account is not null || !setupAuthentication))
            {
                var observer = lifetimes.SingleOrDefault()
                    ?? throw new UnauthorizedAccessException("Setup enrollment has no authenticated lifetime observer.");
                lifetime = await observer.ObserveAsync(principal, cancellationToken);
                if (lifetime is null || source.Account is not null && lifetime.AccountKey != source.Account)
                    throw new UnauthorizedAccessException("Setup enrollment requires its original credential.");
            }
            if (source.Local is not null && (!setupAuthentication || !principal.Identities.Any(identity =>
                    identity.IsAuthenticated && identity.AuthenticationType == ApiAuthenticationSchemeNames.SetupSecret)))
                throw new UnauthorizedAccessException("Local bootstrap requires its actual setup authentication.");
            if (source.ConfiguredAccount is null)
            {
                secret = request.Request.Headers["X-Setup-Secret"].FirstOrDefault();
                if (await setup.ValidateSecretAsync(secret, cancellationToken) != SetupSecretValidationOutcome.Accepted)
                    throw new UnauthorizedAccessException("The original setup secret is no longer admitted.");
            }
        }

        var lease = new Lease(this, source, request, principal, lifetime, secret);
        await lease.RequireCurrentAsync(cancellationToken);
        lease.RequireCurrentTime();
        return lease;
    }

    private sealed class Lease(
        TrustedSetupSettingsAuthority owner, TrustedSetupSettingsSource source,
        HttpContext? request, System.Security.Claims.ClaimsPrincipal? principal,
        HumanAuthenticationLifetime? lifetime, string? secret) : ITrustedSetupSettingsAuthorityLease
    {
        public async Task RequireCurrentAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireRequest();
            if (lifetime is not null && !ReferenceEquals(
                    await (owner.lifetimes.SingleOrDefault()
                        ?? throw new UnauthorizedAccessException("The original credential observer is unavailable."))
                        .ObserveAsync(principal!, cancellationToken), lifetime))
                throw new UnauthorizedAccessException("Setup cannot replace the original authenticated lifetime.");
            if (source.Local is { } local)
            {
                if (await owner.providers.GetActivePrimaryProviderAsync(cancellationToken) != AuthenticationProviderKind.Local
                    || local.Receipt.Kind != LocalCredentialOperationKind.Create
                    || local.Receipt.Stage != LocalCredentialOperationStage.ProvisioningPending
                    || !local.EmailVerified
                    || source.Account?.ProviderKind != AuthenticationProviderKind.Local
                    || source.Account.Value != local.Receipt.LocalSubjectId.ToString("D")
                    || await owner.credentials.ReadProvisioningAsync(local.Receipt.OperationId, cancellationToken) != local)
                    throw new UnauthorizedAccessException("The original Local provisioning receipt is no longer current.");
            }
            if (source.ConfiguredAccount is { } account)
            {
                var provider = owner.configuredProviders.SingleOrDefault()
                    ?? throw new UnauthorizedAccessException("Configured setup has no original binding authority.");
                var binding = await provider.GetVerifiedBindingAsync(account, cancellationToken);
                if (binding is null || binding.AccountKey != account || binding.AccountKey != source.Account
                    || binding.Generation != source.ConfiguredGeneration
                    || binding.IdentityFingerprint != source.ConfiguredFingerprint
                    || binding.AdministratorProfile != source.ConfiguredProfile
                    || JsonSerializer.Serialize(binding.Settings) != source.ConfiguredSettings)
                    throw new UnauthorizedAccessException("Configured setup sources changed during completion.");
            }
            RequireCurrentTime();
        }

        public void RequireCurrentTime()
        {
            RequireRequest();
            if (lifetime is not null && !lifetime.IsValidAt(owner.clock.GetUtcNow()))
                throw new UnauthorizedAccessException("The original setup credential lifetime has ended.");
        }

        private void RequireRequest()
        {
            if (request is null)
                return;
            request.RequestAborted.ThrowIfCancellationRequested();
            if (!ReferenceEquals(owner.contexts.HttpContext, request) || !ReferenceEquals(request.User, principal))
                throw new UnauthorizedAccessException("The original setup request changed.");
            // Durable pending/completed state is checked on the original transaction by
            // the coordinator, not through a new scope that could block on its own write.
            if (source.ConfiguredAccount is null
                && (request.Request.Headers["X-Setup-Secret"].FirstOrDefault() != secret
                    || !owner.setup.ValidateSecret(secret)))
                throw new UnauthorizedAccessException("The original setup secret was replaced or locked.");
        }
    }
}
