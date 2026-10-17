using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Original administrator custody for the existing unannotated provisioning action.
/// No Tenant/create or InstanceSetting/update question is invented for this handler.
/// </summary>
public sealed class HumanManagedProviderProvisioningPreparation(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IMachinePrincipalAccessor machine,
    ITenantContext tenantContext,
    TimeProvider clock,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanAuthorizationRouteSource> resolveRoute,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<IHumanManagedProviderProvisioningDataSource> resolveData) : IHumanManagedProviderProvisioningPreparation
{
    public async Task<IPreparedHumanManagedProviderProvisioning> PrepareAsync(
        EnsureManagedProviderClientProvisionedCommand request, Guid administrator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ManagementRequest is not null || request.ProvisioningDto is null
            || administrator == Guid.Empty || machine.IsMachineCaller)
            throw new UnauthorizedAccessException("Original human provisioning authority is required.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("The original provisioning administrator is required.");
        var context = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var authenticated = context.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticated, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic provisioning credential evidence is required.");
        Guid tenant = tenantContext.TenantId;
        string originalContent = JsonSerializer.Serialize(request);
        if (administrator != subject.PrincipalId || subject.AccountKey != lifetime.AccountKey)
            throw new UnauthorizedAccessException("The administrator is not the original authenticated account.");

        void Custody()
        {
            if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(httpContext.HttpContext, context) || !ReferenceEquals(context.User, authenticated)
                || tenantContext.TenantId != tenant
                || !string.Equals(originalContent, JsonSerializer.Serialize(request), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Original human provisioning custody changed.");
        }

        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var exclusionSource = resolveExclusion();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var original = await principalSource.ObserveAsync(cancellationToken);
        if (original.SelectedTenantId is not null)
            throw new UnauthorizedAccessException("Human provisioning requires its original Global owner.");
        var originalRoute = await routeSource.ObserveAsync(cancellationToken);
        var originalSupport = await supportSource.ObserveAsync(cancellationToken);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var route = HumanAuthorizationRouteProjector.Project(originalRoute, deployment, settingsGovernance: true,
            localDecisionSourcesCoLocated: lifetime.LocalSession is null || original.LocalSession is not null);
        // Preserve the exact persisted platform-administrator question. This handler
        // has no selected PDP action, so a route snapshot is continuity, not a grant.
        var administratorRoles = original.Roles.Where(role =>
            original.PlatformGrants.Any(grant => grant.RoleId == role.Id)).ToArray();
        if (!administratorRoles.Any(role => role.RoleScopeId == (int)RoleScopeEnum.Platform
                && role.MatchesPlatformAdministratorCode))
            throw new UnauthorizedAccessException("Current instance administrator authority is required.");
        Compare(original, originalRoute);
        Time(originalSupport, clock.GetUtcNow());
        var originalData = await resolveData().ObserveAsync(request, cancellationToken);

        return new Prepared(request, async token =>
        {
            Custody();
            var exclusion = await exclusionSource.AcquireAsync(token);
            var principal = await principalSource.AcquireAsync(exclusion, token);
            var currentRoute = await routeSource.AcquireAsync(exclusion, token);
            var support = await supportSource.AcquireAsync(exclusion, principal, cancellationToken: token);
            var data = await originalData.AcquireAsync(request, exclusion, token);
            Compare(principal.Facts, currentRoute.Facts);
            Time(support.Facts, clock.GetUtcNow());
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                var finalPrincipal = await principal.ValidateAsync(finalToken);
                var finalRoute = await currentRoute.ValidateAsync(finalToken);
                finalSupport = await support.ValidateAsync(finalToken);
                Compare(finalPrincipal, finalRoute);
                await data.ValidateAsync(finalToken);
            }, now => Time(finalSupport
                ?? throw new InvalidOperationException("Terminal provisioning sources have not been admitted."), now));
            return data;
        });

        void Compare(HumanPrincipalSourceFacts current, HumanAuthorizationRouteFacts currentRoute)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current);
            var projected = HumanAuthorizationRouteProjector.Project(currentRoute, deployment, settingsGovernance: true,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || current.LocalSession is not null);
            if (current.SelectedTenantId != original.SelectedTenantId
                || current.SelectedTenantId.HasValue && current.SelectedTenantId != tenant
                || currentRoute.PrincipalId != administrator || currentRoute.SelectedTenantId != original.SelectedTenantId
                || !route.IsSemanticallyEquivalentTo(projected)
                || !original.Users.SequenceEqual(current.Users) || !original.Bindings.SequenceEqual(current.Bindings)
                || !original.PlatformGrants.OrderBy(row => row.Id).SequenceEqual(current.PlatformGrants.OrderBy(row => row.Id))
                || !administratorRoles.OrderBy(row => row.Id).SequenceEqual(current.Roles
                    .Where(role => original.PlatformGrants.Any(grant => grant.RoleId == role.Id)).OrderBy(row => row.Id))
                || !SameLocal(original.LocalSession, current.LocalSession))
                throw new UnauthorizedAccessException("Original provisioning administrator sources changed.");
            // A provisioned administrator may be the caller. Its new tenant membership
            // is an effect, not an input to the persisted platform-administrator question.
        }

        void Time(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original provisioning credential expired.");
            RequireSupport(originalSupport, now);
            RequireSupport(support, now);
        }

        void RequireSupport(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            if (!ReferenceEquals(support.Forwarding, originalSupport.Forwarding)
                || !originalSupport.Sessions.SequenceEqual(support.Sessions)
                || !originalSupport.Settings.SequenceEqual(support.Settings))
                throw new UnauthorizedAccessException("Original provisioning support sources changed.");
            var access = HumanSupportAccessProjection.Context(support, now);
            if (support.Forwarding.WasForwarded && !access.IsActive || access.IsActive && !access.AllowsWrites)
                throw new UnauthorizedAccessException("Original support does not permit provisioning.");
        }
    }

    private static bool SameLocal(HumanPrincipalLocalSessionFacts? original, HumanPrincipalLocalSessionFacts? current) =>
        original is null ? current is null : current is not null
            && original.CredentialState == current.CredentialState
            && original.EmailDeliveryEnabled == current.EmailDeliveryEnabled
            && original.Accounts.OrderBy(row => row.Id).SequenceEqual(current.Accounts.OrderBy(row => row.Id))
            && original.Operations.OrderBy(row => row.Id).SequenceEqual(current.Operations.OrderBy(row => row.Id))
            && original.PersonalActors.OrderBy(row => row.Id).SequenceEqual(current.PersonalActors.OrderBy(row => row.Id));

    private sealed class Prepared(EnsureManagedProviderClientProvisionedCommand request,
        Func<CancellationToken, Task<IHumanManagedProviderProvisioningData>> acquire) : IPreparedHumanManagedProviderProvisioning
    {
        private int consumed;

        public Task<IHumanManagedProviderProvisioningData> AcquireAsync(EnsureManagedProviderClientProvisionedCommand originalRequest,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref consumed, 1) != 0)
                throw new InvalidOperationException("Original provisioning authority cannot be replayed.");
            if (!ReferenceEquals(request, originalRequest))
                throw new UnauthorizedAccessException("Provisioning authority belongs to another original request.");
            cancellationToken.ThrowIfCancellationRequested();
            return acquire(cancellationToken);
        }
    }
}
