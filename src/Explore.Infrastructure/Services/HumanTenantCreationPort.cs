using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Tenants;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Application.Management;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Admits the unchanged Tenant/create question on its originally selected creator owner.
/// Worker payloads and persisted creation receipts cannot issue human authority.
/// </summary>
public sealed class HumanTenantCreationPort(
    IHumanCommitScopePreparation humanScope,
    IHumanOriginalCredentialCustody credentialCustody,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IMachinePrincipalAccessor machine,
    ITenantContext tenantContext,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    IOptions<ManagedControlPlaneOptions> managedOptions,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanAuthorizationRouteSource> resolveRoute,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<AuthorizationResourceContextResolver> resolveResource,
    Func<ISerializableAttemptOwner> resolveOwner,
    Func<IEventDiscoveryEnrollmentGate> resolveGate,
    Func<ISettingMutationLock> resolveMutationLock,
    Func<IHumanTenantCreationGraphWriter> resolveWriter) : IHumanTenantCreationPort
{
    private Authority? current;

    public async Task<HumanTenantCreationResult> CreateAsync(CreateTenantCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (machine.IsMachineCaller || current is not null)
            throw new UnauthorizedAccessException("Original human tenant creation is required.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("The original tenant creator is required.");
        var originalHttp = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var originalPrincipal = originalHttp.User;
        var originalServices = originalHttp.RequestServices;
        var lifetime = await lifetimeObserver.ObserveAsync(originalPrincipal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic creator credential evidence is required.");
        if (lifetime.AccountKey != subject.AccountKey
            || request.RequestingUserId is { } requestedActor && requestedActor != subject.PrincipalId
            || request.TenantDto.AssignCurrentUserAsTenantAdmin && request.RequestingUserId is null)
            throw new UnauthorizedAccessException("The tenant creator does not match the authenticated account.");
        Guid ambientTenant = tenantContext.TenantId;
        string commandImage = JsonSerializer.Serialize(request);
        bool capacityEnforced = managedOptions.Value.Enabled;
        int maximumTenants = managedOptions.Value.MaximumTenantCount;
        var plan = Plan(request, subject.PrincipalId, clock.GetUtcNow().UtcDateTime,
            capacityEnforced, maximumTenants);
        void Custody()
        {
            if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(httpContext.HttpContext, originalHttp)
                || !ReferenceEquals(originalHttp.User, originalPrincipal)
                || !ReferenceEquals(originalHttp.RequestServices, originalServices) || tenantContext.TenantId != ambientTenant
                || commandImage != JsonSerializer.Serialize(request)
                || managedOptions.Value.Enabled != capacityEnforced
                || managedOptions.Value.MaximumTenantCount != maximumTenants)
                throw new UnauthorizedAccessException("The original tenant creation command or credential changed.");
            credentialCustody.RequireOriginalCredential(lifetime);
        }

        await using var gate = await resolveGate().AcquireAsync(cancellationToken);
        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var original = await principalSource.ObserveAsync(cancellationToken);
        var originalRoute = await routeSource.ObserveAsync(cancellationToken);
        var originalSupport = await supportSource.ObserveAsync(cancellationToken);
        if (original.SelectedTenantId is not null || originalRoute.SelectedTenantId is not null)
            throw new UnauthorizedAccessException("Tenant creation requires its original Global creator owner.");
        var secure = (ISecureRequest)request;
        string resourceId = secure.ResourceId ?? nameof(CreateTenantCommand);
        var resource = resolveResource();
        var originalResource = await resource.ResolveAsync(request, ResourceKinds.Tenant,
            AuthorizationActions.Create, resourceId, secure.AuthorizationFacts, cancellationToken);
        var check = new AuthorizationRequest(ResourceKinds.Tenant,
            originalResource.ResourceId ?? resourceId, AuthorizationActions.Create, Facts: originalResource.Facts);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var route = HumanAuthorizationRouteProjector.Project(originalRoute, deployment, settingsGovernance: true,
            localDecisionSourcesCoLocated: lifetime.LocalSession is null || original.LocalSession is not null);
        var projectedPrincipal = CerbosPrincipalBuilder.BuildPrincipalFromSources(original);
        var serialized = CerbosPrincipalBuilder.CapturePrincipal(projectedPrincipal);
        var localInputs = FallbackAuthorizationService.PreparedTenantInputs(original, check, ambientTenant);
        Compare(original, originalRoute);
        Time(originalSupport, clock.GetUtcNow());
        bool allowed;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = FallbackAuthorizationService.EvaluatePreparedTenant(original, check, ambientTenant, false);
        else if (route.Provider == HumanAuthorizationRouteProvider.CerbosInstance)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(projectedPrincipal, check, ambientTenant,
                deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
            allowed = decision.IsAllowed;
        }
        else allowed = false;
        if (!allowed) throw new AuthorizationException(check.ResourceKind, check.Action);

        var exclusionSource = resolveExclusion();
        try
        {
            string slugLock = TenantMutationLockKeys.ForSlug(plan.Creation.Slug);
            IEnumerable<string>[] lockGroups = capacityEnforced
                ? [[GovernanceSettingKeys.Deployment.Mode], [slugLock]]
                : [[slugLock]];
            // Retain session locks through settlement, before the owner's BEGIN and
            // all source locks; transaction-local reacquisition would invert this order.
            return await resolveMutationLock().ExecuteOrderedGroupsAsync(lockGroups,
                outerToken => resolveOwner().ExecuteAsync(async token =>
            {
                Custody();
                var sourceSet = await exclusionSource.AcquireAsync(token);
                var principal = await principalSource.AcquireAsync(sourceSet, token);
                var selectedRoute = await routeSource.AcquireAsync(sourceSet, token);
                var support = await supportSource.AcquireAsync(sourceSet, principal, cancellationToken: token);
                async Task Validate(CancellationToken finalToken)
                {
                    Custody();
                    await sourceSet.ValidateAsync(finalToken);
                    Compare(await principal.ValidateAsync(finalToken), await selectedRoute.ValidateAsync(finalToken));
                    var currentResource = await resource.ResolveAsync(request, ResourceKinds.Tenant,
                        AuthorizationActions.Create, resourceId, secure.AuthorizationFacts, finalToken);
                    if (currentResource.ResourceId != originalResource.ResourceId
                        || !Equals(currentResource.Facts, originalResource.Facts))
                        throw new UnauthorizedAccessException("The original Tenant/create question changed.");
                    Time(await support.ValidateAsync(finalToken), clock.GetUtcNow());
                }
                await Validate(token);
                var issued = new Authority(this, plan, Validate);
                current = issued;
                HumanSupportAccessSourceFacts? finalSupport = null;
                resolveFinalizer().Register(async finalToken =>
                {
                    await RequireCurrentAsync(issued, plan, finalToken);
                    finalSupport = await support.ValidateAsync(finalToken);
                }, now => Time(finalSupport
                    ?? throw new InvalidOperationException("Creator support admission did not complete."), now));
                return await resolveWriter().WriteAsync(issued, plan, token);
            }, outerToken), cancellationToken);
        }
        finally
        {
            if (current is { } issued) issued.Active = false;
            current = null;
        }

        void Compare(HumanPrincipalSourceFacts facts, HumanAuthorizationRouteFacts routeFacts)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts);
            var effect = current is { } active
                ? resolveWriter().ReadCompletedAdministratorEffect(active, plan) : null;
            var inputs = HumanTenantCreationPrincipalProjection.ExcludingCompletedEffect(
                original, facts, effect, plan, subject.PrincipalId);
            HumanTenantCreationPrincipalProjection.RequireUnchanged(original, inputs);
            var projected = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, settingsGovernance: true,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || inputs.LocalSession is not null);
            if (facts.SelectedTenantId is not null || routeFacts.SelectedTenantId is not null
                || routeFacts.PrincipalId != subject.PrincipalId || !route.IsSemanticallyEquivalentTo(projected)
                || !serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(CerbosPrincipalBuilder.BuildPrincipalFromSources(inputs)))
                || localInputs != FallbackAuthorizationService.PreparedTenantInputs(inputs, check, ambientTenant))
                throw new UnauthorizedAccessException("The original tenant creator's policy sources changed.");
        }

        void Time(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now)) throw new UnauthorizedAccessException("The original creator credential expired.");
            foreach (var facts in new[] { originalSupport, support })
                if (HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.ResourceKind,
                        check.Action, AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
                    throw new UnauthorizedAccessException("The original support boundary does not permit tenant creation.");
            if (!ReferenceEquals(originalSupport.Forwarding, support.Forwarding))
                throw new UnauthorizedAccessException("The original creator support selector changed.");
        }
    }

    public Task RequireCurrentAsync(IHumanTenantCreationAuthority authority, HumanTenantCreationPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (authority is not Authority issued || !ReferenceEquals(issued.Issuer, this)
            || !ReferenceEquals(current, issued) || !ReferenceEquals(issued.Plan, plan) || !issued.Active)
            throw new UnauthorizedAccessException("The original issuer-owned human creation authority is required.");
        cancellationToken.ThrowIfCancellationRequested();
        return issued.Validate(cancellationToken);
    }

    private sealed class Authority(HumanTenantCreationPort issuer, HumanTenantCreationPlan plan,
        Func<CancellationToken, Task> validate) : IHumanTenantCreationAuthority
    {
        internal HumanTenantCreationPort Issuer => issuer;
        internal HumanTenantCreationPlan Plan => plan;
        internal Func<CancellationToken, Task> Validate => validate;
        internal bool Active { get; set; } = true;
    }

    private static HumanTenantCreationPlan Plan(CreateTenantCommand request, Guid human, DateTime occurredAt,
        bool capacityEnforced, int maximumTenants)
    {
        var dto = request.TenantDto;
        Guid tenant = Guid.CreateVersion7();
        var branding = TenantBrandingSettingsDocumentDefaults.Create(tenant, dto.FullName);
        var identity = dto.DirectoryOperatorIdentity is null
            ? TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenant, dto.FullName)
            : TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenant, dto.DirectoryOperatorIdentity.ToPayload());
        return new(new TenantCreationRequest(tenant, dto.FullName, dto.Slug,
                dto.IsActive ? (int)Explore.Domain.Enums.TenantStatusEnum.Active : (int)Explore.Domain.Enums.TenantStatusEnum.Provisioning,
                human, occurredAt,
                new(Guid.CreateVersion7(), branding.SchemaVersion, branding.DefaultsVersion, branding.PayloadJson),
                new(Guid.CreateVersion7(), identity.SchemaVersion, identity.DefaultsVersion, identity.PayloadJson)),
            dto.AssignCurrentUserAsTenantAdmin ? human : null,
            dto.AssignCurrentUserAsTenantAdmin ? Guid.CreateVersion7() : null,
            dto.AssignCurrentUserAsTenantAdmin ? Guid.CreateVersion7() : null, capacityEnforced, maximumTenants);
    }
}
