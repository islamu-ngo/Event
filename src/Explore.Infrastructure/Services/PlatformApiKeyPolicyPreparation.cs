using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>The original instance-setting question, evaluated before the dedicated transaction.</summary>
public sealed class PlatformApiKeyPolicyPreparation(
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    ITenantContext tenantContext,
    TimeProvider clock) : IPlatformApiKeyPolicyPreparation
{
    public async Task<IPreparedPlatformApiKeyPolicy> PrepareAsync(InstanceApiKeyAction action, Guid? keyId,
        HumanCommitSubject subject, HumanAuthenticationLifetime lifetime, PlatformApiKeySourceFacts original,
        CancellationToken cancellationToken)
    {
        var check = PlatformApiKeyActionPolicy.Request(action, keyId);
        Guid ambientTenant = tenantContext.TenantId;
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var route = HumanAuthorizationRouteProjector.Project(original.Route, deployment, settingsGovernance: true,
            localDecisionSourcesCoLocated: lifetime.LocalSession is null || original.Principal.LocalSession is not null);
        var projected = CerbosPrincipalBuilder.BuildPrincipalFromSources(original.Principal);
        var serialized = CerbosPrincipalBuilder.CapturePrincipal(projected);
        bool administrator = IsAdministrator(original.Principal);
        void ValidateSources(PlatformApiKeySourceFacts current)
        {
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current.Principal);
            Guid? policyTenant = ambientTenant == Guid.Empty ? null : ambientTenant;
            if (tenantContext.TenantId != ambientTenant || current.Principal.SelectedTenantId != policyTenant
                || current.Route.PrincipalId != subject.PrincipalId || current.Route.SelectedTenantId != policyTenant
                || !ReferenceEquals(current.Support.Forwarding, original.Support.Forwarding)
                || !ReferenceEquals(current.Support.Forwarding.Subject, subject)
                || current.Support.Forwarding.ResolvedTenantId != policyTenant)
                throw new UnauthorizedAccessException("The original platform key source selection changed.");
            var currentRoute = HumanAuthorizationRouteProjector.Project(current.Route, deployment, settingsGovernance: true,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || current.Principal.LocalSession is not null);
            bool same = route.Provider == HumanAuthorizationRouteProvider.Local
                ? administrator == IsAdministrator(current.Principal)
                : serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                    CerbosPrincipalBuilder.BuildPrincipalFromSources(current.Principal)));
            // Native key effects retain administrator-only semantics even if an external PDP allows.
            if (!same || !IsAdministrator(current.Principal) || !route.IsSemanticallyEquivalentTo(currentRoute))
                throw new UnauthorizedAccessException("The original platform key policy inputs changed.");
        }
        void ValidateTime(PlatformApiKeySourceFacts current, DateTimeOffset now)
        {
            if (!lifetime.IsValidAt(now) || tenantContext.TenantId != ambientTenant
                || original.Support.Forwarding.WasForwarded
                    && (!HumanSupportAccessProjection.Context(original.Support, now).IsActive
                        || !HumanSupportAccessProjection.Context(current.Support, now).IsActive)
                || HumanSupportAccessProjection.BoundaryDenialReason(original.Support, now,
                    check.ResourceKind, check.Action, null) is not null
                || HumanSupportAccessProjection.BoundaryDenialReason(current.Support, now,
                    check.ResourceKind, check.Action, null) is not null)
                throw new UnauthorizedAccessException("The original platform key credential or support horizon ended.");
        }
        ValidateSources(original);
        ValidateTime(original, clock.GetUtcNow());
        AuthorizationDecision? decision = route.Provider switch
        {
            HumanAuthorizationRouteProvider.Local => administrator
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local),
            HumanAuthorizationRouteProvider.CerbosInstance => await cerbos.AuthorizePreparedHumanAsync(
                projected, check, ambientTenant, deployment.UsePolicyScope, route.Endpoint!, cancellationToken),
            _ => null
        };
        cancellationToken.ThrowIfCancellationRequested();
        if (decision?.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
            throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
        if (decision?.IsAllowed != true)
            throw new AuthorizationException(check.ResourceKind, check.Action);
        ValidateTime(original, clock.GetUtcNow());
        return new Prepared(ValidateSources, ValidateTime);
    }

    private static bool IsAdministrator(HumanPrincipalSourceFacts facts) =>
        facts.PlatformGrants.Any(grant => facts.Roles.Any(role => role.Id == grant.RoleId
            && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode));

    private sealed class Prepared(Action<PlatformApiKeySourceFacts> sources,
        Action<PlatformApiKeySourceFacts, DateTimeOffset> time) : IPreparedPlatformApiKeyPolicy
    {
        public void ValidateSources(PlatformApiKeySourceFacts current) => sources(current);
        public void ValidateTime(PlatformApiKeySourceFacts current, DateTimeOffset now) => time(current, now);
    }
}

public static class PlatformApiKeyPolicyPreparationRegistration
{
    public static IServiceCollection AddPlatformApiKeyPolicyPreparation(this IServiceCollection services)
    {
        services.AddScoped<IPlatformApiKeyPolicyPreparation, PlatformApiKeyPolicyPreparation>();
        return services;
    }
}
