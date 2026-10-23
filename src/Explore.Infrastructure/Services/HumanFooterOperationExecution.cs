using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Operations;
using Explore.Domain.Enums;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.Settings;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Complete original Footer admission. Only this producer can open its active body; lower
/// writers cannot substitute an actor, transaction, another request, or a broader purpose.
/// </summary>
public sealed class HumanFooterOperationExecution(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    ITenantContext tenantContext,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    IMachinePrincipalAccessor machine,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanAuthorizationRouteSource> resolveRoute,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<ISerializableAttemptOwner> resolveOwner,
    Func<ISettingMutationLock> resolveLocks,
    Func<IFooterExecutionScopeFactory> resolveScope,
    Func<IHierarchicalSettingsResolver> resolveSettings,
    Func<IFooterSetupReadAuthority> resolveSetup) : IFooterOperationExecution
{
    private Admission? active;

    public async Task<T> ExecuteAsync<T>(object request, Func<CancellationToken, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        if (!FooterOperationScope.Handles(request.GetType()) || active is not null)
            throw new InvalidOperationException("Footer execution requires one closed original operation.");
        if (machine.Current is not null)
            return await body(cancellationToken);

        bool instance = request is GetFooterGovernanceSettingsQuery or UpdateFooterGovernanceSettingsCommand;
        var selection = instance ? PolicyMutationScope.Instance : new PolicyMutationScope(tenantContext.TenantId);
        resolveScope().RequireCurrentOwner(selection);
        if (request is GetFooterGovernanceSettingsQuery
            && await resolveSetup().TryAcquireAsync(cancellationToken) is { } setup)
        {
            async Task ValidateSetup(CancellationToken token)
            {
                resolveScope().RequireCurrentOwner(selection);
                await setup.ValidateAsync(token);
                resolveScope().RequireCurrentOwner(selection);
            }
            active = new(request, null, ValidateSetup);
            try
            {
                await ValidateSetup(cancellationToken);
                var result = await body(cancellationToken);
                await ValidateSetup(cancellationToken);
                return result;
            }
            finally { active = null; }
        }

        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("Footer requires the original authenticated human.");
        var http = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("Footer requires the original authenticated request.");
        var authenticated = http.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticated, cancellationToken)
            ?? throw new UnauthorizedAccessException("Footer requires authentic credential lifetime evidence.");
        Guid? selectedTenant = instance ? null : tenantContext.TenantId;
        Guid ambientTenant = tenantContext.TenantId;
        if (selectedTenant == Guid.Empty || lifetime.AccountKey != subject.AccountKey)
            throw new UnauthorizedAccessException("Footer source selection does not match its original human.");
        Guid? postedTenant = request switch
        {
            CreateFooterLinkCommand command => command.TenantId,
            CreateFooterLinkGroupCommand command => command.TenantId,
            UpdateFooterLinkCommand command => command.TenantId,
            UpdateFooterLinkGroupCommand command => command.TenantId,
            DeleteFooterLinkCommand command => command.TenantId,
            DeleteFooterLinkGroupCommand command => command.TenantId,
            ReorderFooterLinkGroupsCommand command => command.TenantId,
            PatchTenantFooterSettingsCommand command => command.TenantId,
            _ => null
        };
        if (postedTenant.HasValue && postedTenant != selectedTenant)
            throw new UnauthorizedAccessException("A Footer target cannot replace the authenticated tenant.");
        void Custody()
        {
            resolveScope().RequireCurrentOwner(selection);
            if (!ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(httpContext.HttpContext, http) || !ReferenceEquals(http.User, authenticated)
                || tenantContext.TenantId != ambientTenant || machine.Current is not null)
                throw new UnauthorizedAccessException("The original Footer request custody changed.");
        }
        Custody();
        if (!lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original Footer credential expired.");

        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var principalFacts = await principalSource.ObserveAsync(cancellationToken);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var originalSupport = await supportSource.ObserveAsync(cancellationToken);
        void Facts(HumanPrincipalSourceFacts principal, HumanAuthorizationRouteFacts route)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, principal);
            if (principal.SelectedTenantId != selectedTenant || route.SelectedTenantId != selectedTenant
                || route.PrincipalId != subject.PrincipalId)
                throw new UnauthorizedAccessException("Footer requires its exact original ordinary source owner.");
        }
        Facts(principalFacts, routeFacts);
        bool mutation = request is ISecureRequest;
        var check = new AuthorizationRequest(instance ? ResourceKinds.InstanceSetting : ResourceKinds.Tenant,
            selectedTenant?.ToString("D") ?? "footer-governance",
            request is GetFooterGovernanceSettingsQuery or GetTenantFooterSettingsQuery
                or GetFooterLinkGroupListQuery or GetFooterLinkGroupDetailsQuery
                ? AuthorizationActions.View : AuthorizationActions.Update,
            Facts: selectedTenant is { } tenant ? new TenantScopedAuthorizationFacts(tenant) : null);
        // All eight annotated Footer mutations are Tenant/update. They retain tenant BYO
        // routing; an administrator predicate never replaces the selected provider decision.
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var route = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, settingsGovernance: instance,
            localDecisionSourcesCoLocated: lifetime.LocalSession is null || principalFacts.LocalSession is not null);
        var projection = CerbosPrincipalBuilder.BuildPrincipalFromSources(principalFacts);
        var serialized = CerbosPrincipalBuilder.CapturePrincipal(projection);
        var localInputs = FallbackAuthorizationService.PreparedTenantInputs(principalFacts, check, ambientTenant);
        if (instance && !InstanceAdministrator(principalFacts))
            throw new AuthorizationException(check.ResourceKind, check.Action);
        RequireSupport(originalSupport, clock.GetUtcNow(), check);
        if (mutation)
        {
            bool allowed;
            if (route.Provider == HumanAuthorizationRouteProvider.Local)
                allowed = FallbackAuthorizationService.EvaluatePreparedTenant(principalFacts, check, ambientTenant, false);
            else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
            {
                var decision = await cerbos.AuthorizePreparedHumanAsync(projection, check, ambientTenant,
                    deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
                if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                    throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
                allowed = decision.IsAllowed;
            }
            else allowed = false;
            if (!allowed) throw new AuthorizationException(check.ResourceKind, check.Action);
        }

        void Compare(HumanPrincipalSourceFacts principal, HumanAuthorizationRouteFacts currentRoute)
        {
            Facts(principal, currentRoute);
            var current = HumanAuthorizationRouteProjector.Project(currentRoute, deployment, instance,
                lifetime.LocalSession is null || principal.LocalSession is not null);
            bool principalMatches = !mutation || route.Provider == HumanAuthorizationRouteProvider.Local
                ? localInputs == FallbackAuthorizationService.PreparedTenantInputs(principal, check, ambientTenant)
                : serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                    CerbosPrincipalBuilder.BuildPrincipalFromSources(principal)));
            if (!route.IsSemanticallyEquivalentTo(current) || !principalMatches
                || instance && !InstanceAdministrator(principal))
                throw new UnauthorizedAccessException("The original Footer policy inputs changed.");
        }
        void Time(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original Footer credential expired.");
            RequireSupport(originalSupport, now, check);
            RequireSupport(support, now, check);
        }
        Time(originalSupport, clock.GetUtcNow());
        try
        {
            Task<T> RunOwner(CancellationToken ownerToken) => resolveOwner().ExecuteAsync(async token =>
            {
                var exclusion = await resolveExclusion().AcquireAsync(token);
                var principal = await principalSource.AcquireAsync(exclusion, token);
                var currentRoute = await routeSource.AcquireAsync(exclusion, token);
                var support = await supportSource.AcquireAsync(exclusion, principal, cancellationToken: token);
                Compare(principal.Facts, currentRoute.Facts);
                Time(support.Facts, clock.GetUtcNow());
                HumanSupportAccessSourceFacts? finalSupport = null;
                async Task Sources(CancellationToken finalToken)
                {
                    await exclusion.ValidateAsync(finalToken);
                    var finalPrincipal = await principal.ValidateAsync(finalToken);
                    var finalRoute = await currentRoute.ValidateAsync(finalToken);
                    finalSupport = await support.ValidateAsync(finalToken);
                    Compare(finalPrincipal, finalRoute);
                }
                active = new(request, subject, async currentToken =>
                {
                    await Sources(currentToken);
                    Time(finalSupport!, clock.GetUtcNow());
                });
                resolveFinalizer().Register(Sources, now => Time(finalSupport
                    ?? throw new InvalidOperationException("Footer terminal sources have not been admitted."), now));
                var result = await body(token);
                await Sources(token);
                Time(finalSupport!, clock.GetUtcNow());
                return result;
            }, ownerToken);
            string[] keys = request switch
            {
                UpdateFooterGovernanceSettingsCommand =>
                [
                    FooterSettingDefinitions.LockTenantTemplate.Key, FooterSettingDefinitions.LockTenantLinkGroups.Key,
                    FooterSettingDefinitions.LockTenantSocialLinks.Key, FooterSettingDefinitions.LockTenantDescription.Key,
                    FooterSettingDefinitions.LockTenantCopyright.Key
                ],
                PatchTenantFooterSettingsCommand =>
                [
                    FooterSettingDefinitions.LockTenantTemplate.Key, FooterSettingDefinitions.LockTenantDescription.Key,
                    FooterSettingDefinitions.LockTenantSocialLinks.Key, FooterSettingDefinitions.LockTenantCopyright.Key
                ],
                ISecureRequest => [FooterSettingDefinitions.LockTenantLinkGroups.Key],
                _ => []
            };
            var result = keys.Length == 0
                ? await RunOwner(cancellationToken)
                : await resolveLocks().ExecuteOrderedGroupsAsync(request is PatchTenantFooterSettingsCommand
                    ? [keys, FooterSettingDefinitions.All.Where(definition => definition.MaxScope == SettingScope.Tenant)
                        .Select(definition => definition.Key)]
                    : [keys], RunOwner, cancellationToken);
            if (request is UpdateFooterGovernanceSettingsCommand)
                resolveSettings().InvalidateCache(SettingScope.Instance, null);
            else if (request is PatchTenantFooterSettingsCommand)
                resolveSettings().InvalidateCache(SettingScope.Tenant, selectedTenant);
            return result;
        }
        finally { active = null; }
    }

    public async Task<HumanCommitSubject?> RequireOwnedAsync(object request, CancellationToken cancellationToken = default)
    {
        if (machine.Current is not null)
            return null;
        if (active is not { } admission || !ReferenceEquals(admission.Request, request))
            throw new UnauthorizedAccessException("The actual original Footer producer must own this body.");
        await admission.Validate(cancellationToken);
        return admission.Subject;
    }

    public async Task RequireWriteAsync(object entity, FooterWriteKind kind, CancellationToken cancellationToken = default)
    {
        if (machine.Current is not null)
            return;
        if (active is not { Subject: not null } admission)
            throw new UnauthorizedAccessException("A Footer write requires its actual original human producer.");
        bool allowed = (admission.Request, entity, kind) switch
        {
            (CreateFooterLinkGroupCommand command, TenantFooterLinkGroup group, FooterWriteKind.Create) =>
                group.TenantId == command.TenantId,
            (UpdateFooterLinkGroupCommand command, TenantFooterLinkGroup group, FooterWriteKind.Update) =>
                group.TenantId == command.TenantId && group.Id == command.GroupId,
            (DeleteFooterLinkGroupCommand command, TenantFooterLinkGroup group, FooterWriteKind.Delete) =>
                group.TenantId == command.TenantId && group.Id == command.GroupId,
            (ReorderFooterLinkGroupsCommand command, TenantFooterLinkGroup group, FooterWriteKind.Update) =>
                group.TenantId == command.TenantId && command.OrderedGroupIds.Contains(group.Id),
            (CreateFooterLinkCommand command, TenantFooterLink link, FooterWriteKind.Create) =>
                link.FooterLinkGroupId == command.GroupId,
            (UpdateFooterLinkCommand command, TenantFooterLink link, FooterWriteKind.Update) =>
                link.Id == command.LinkId,
            (DeleteFooterLinkCommand command, TenantFooterLink link, FooterWriteKind.Delete) =>
                link.Id == command.LinkId,
            _ => false
        };
        if (!allowed)
            throw new UnauthorizedAccessException("The Footer row and action do not belong to the original operation.");
        await admission.Validate(cancellationToken);
    }

    public async Task RequireGroupDeletionAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        if (machine.Current is not null)
            return;
        if (active is not { Subject: not null, Request: DeleteFooterLinkGroupCommand command } admission
            || command.GroupId != groupId)
            throw new UnauthorizedAccessException("Bulk Footer deletion requires its original group-deletion producer.");
        await admission.Validate(cancellationToken);
    }

    private static bool InstanceAdministrator(HumanPrincipalSourceFacts principal) =>
        principal.PlatformGrants.Any(grant => principal.Roles.Any(role => role.Id == grant.RoleId
            && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode));

    private static void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now, AuthorizationRequest check)
    {
        if (HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.ResourceKind, check.Action,
                AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
            throw new UnauthorizedAccessException("The original support horizon does not permit the Footer operation.");
    }

    private sealed record Admission(object Request, HumanCommitSubject? Subject, Func<CancellationToken, Task> Validate);
}
