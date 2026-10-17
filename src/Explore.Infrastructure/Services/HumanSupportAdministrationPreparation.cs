using System.Collections.Immutable;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Application.Features.SupportAccess.Requests.Queries;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Retains the original annotated question, account, selected policy and support horizon.
/// Ending the original forwarded session admits only the privately compiled terminal image,
/// never a newly authorized principal or an active-session check against replacement evidence.
/// </summary>
public sealed partial class HumanSupportAdministrationPreparation(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IMachinePrincipalAccessor machine,
    ITenantContext tenantContext,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanAuthorizationRouteSource> resolveRoute,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<ISupportAdministrationSource> resolveTarget,
    Func<IHumanActionCommitFinalizer> resolveFinalizer) : IHumanSupportAdministrationPreparation
{
    public Task<IPreparedHumanSupportAdministration> PrepareAsync(StartSupportAccessSessionCommand request,
        CancellationToken cancellationToken) =>
        Prepare(request, AuthorizationActions.SupportAccessSessions.Start, null, cancellationToken);

    public Task<IPreparedHumanSupportAdministration> PrepareAsync(StopSupportAccessSessionCommand request,
        CancellationToken cancellationToken) =>
        Prepare(request, AuthorizationActions.SupportAccessSessions.Stop, request.SessionId, cancellationToken);

    public Task<IPreparedHumanSupportAdministration> PrepareAsync(ForceStopSupportAccessSessionCommand request,
        CancellationToken cancellationToken) =>
        Prepare(request, AuthorizationActions.SupportAccessSessions.ForceStop, request.SessionId, cancellationToken);

    public Task<IPreparedHumanSupportAdministration> PrepareAsync(ListSupportAccessSessionsQuery request,
        CancellationToken cancellationToken) =>
        Prepare(request, AuthorizationActions.SupportAccessSessions.List, null, cancellationToken);

    public Task<IPreparedHumanSupportAdministration> PrepareAsync(GetSupportAccessAuditEventsQuery request,
        CancellationToken cancellationToken) =>
        Prepare(request, AuthorizationActions.SupportAccessSessions.ViewAudit, request.SessionId, cancellationToken);

    private async Task<IPreparedHumanSupportAdministration> Prepare(object operation, string action, Guid? targetId,
        CancellationToken cancellationToken)
    {
        if (machine.IsMachineCaller)
            throw new UnauthorizedAccessException("Support administration requires original human custody.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("The original support administrator is required.");
        var context = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated support request is required.");
        var authenticated = context.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticated, cancellationToken)
            ?? throw new UnauthorizedAccessException("Support administration requires authentic credential lifetime.");
        Guid ambientTenant = tenantContext.TenantId;
        if (lifetime.AccountKey != subject.AccountKey)
            throw new UnauthorizedAccessException("The support credential belongs to another account.");
        var prepared = new Prepared(operation, subject.PrincipalId);
        void Custody()
        {
            if (prepared.Rejected || machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(httpContext.HttpContext, context) || !ReferenceEquals(context.User, authenticated)
                || tenantContext.TenantId != ambientTenant)
                throw new UnauthorizedAccessException("The original support request custody changed.");
        }
        Custody();
        var targetSource = resolveTarget();
        switch (operation)
        {
            case StartSupportAccessSessionCommand start: targetSource.Select(start); break;
            case StopSupportAccessSessionCommand stop: targetSource.Select(stop); break;
            case ForceStopSupportAccessSessionCommand force: targetSource.Select(force); break;
            case ListSupportAccessSessionsQuery list: targetSource.Select(list); break;
            case GetSupportAccessAuditEventsQuery audit: targetSource.Select(audit); break;
            default: throw new InvalidOperationException("Unknown original support operation.");
        }
        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var original = await principalSource.ObserveAsync(cancellationToken);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var originalSupport = await supportSource.ObserveAsync(cancellationToken);
        var originalTarget = await targetSource.ObserveAsync(targetId, cancellationToken);
        prepared.Original = originalTarget;
        // These requests have no enricher or persisted-context substitution. In particular,
        // stop/force-stop keep their declared empty tenant question; the body still requires
        // exact ownership for stop. Do not invent an administrator or self-service shortcut.
        var secure = (ISecureRequest)operation;
        var check = new AuthorizationRequest(ResourceKinds.SupportAccessSession,
            secure.ResourceId ?? operation.GetType().Name, action, Facts: secure.AuthorizationFacts);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var route = Project(routeFacts, original);
        RequireSources(original, routeFacts);
        RequireTime(clock.GetUtcNow());
        bool allowed;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = LocalAllowed(original, check, ambientTenant);
        else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(CerbosPrincipalBuilder.BuildPrincipalFromSources(original),
                check, ambientTenant, deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(check.ResourceKind, action);
            allowed = decision.IsAllowed;
        }
        else allowed = false;
        if (!allowed) throw new AuthorizationException(check.ResourceKind, action);

        prepared.Acquire = async token =>
        {
            Custody();
            var exclusion = await resolveExclusion().AcquireAsync(token);
            var principal = await principalSource.AcquireAsync(exclusion, token);
            var currentRoute = await routeSource.AcquireAsync(exclusion, token);
            var support = await supportSource.AcquireAsync(exclusion, principal, cancellationToken: token);
            var target = await targetSource.AcquireAsync(targetId, exclusion, token);
            prepared.NativeData = target.NativeData;
            prepared.Settings = HumanSupportAccessProjection.Settings(support.Facts);
            RequireSources(principal.Facts, currentRoute.Facts);
            RequireSupport(support.Facts, completed: false);
            if (target.Original != originalTarget)
                throw new UnauthorizedAccessException("The original support target changed before acquisition.");
            RequireTime(clock.GetUtcNow());
            DateTimeOffset now = clock.GetUtcNow();
            prepared.TransitionAt = new DateTimeOffset(now.Ticks - now.Ticks % 10, now.Offset);
            bool sourcesAdmitted = false;
            resolveFinalizer().Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                var finalPrincipal = await principal.ValidateAsync(finalToken);
                var finalRoute = await currentRoute.ValidateAsync(finalToken);
                var finalSupport = await support.ValidateAsync(finalToken);
                var persisted = await target.ValidateAsync(prepared.Expected?.Id ?? targetId,
                    prepared.Audit?.Id, finalToken);
                RequireSources(finalPrincipal, finalRoute);
                prepared.RequirePersisted(persisted);
                RequireSupport(finalSupport, completed: true);
                await target.CompleteAsync(finalToken);
                Custody();
                sourcesAdmitted = true;
            }, terminalNow =>
            {
                if (!sourcesAdmitted)
                    throw new InvalidOperationException("Terminal support sources were not admitted.");
                RequireTime(terminalNow);
                if (operation is StartSupportAccessSessionCommand && prepared.Expected is { } started
                    && terminalNow >= started.ExpiresAtUtc)
                    throw new UnauthorizedAccessException("The originally created support horizon ended.");
            });
        };
        return prepared;

        HumanAuthorizationRoute Project(HumanAuthorizationRouteFacts facts, HumanPrincipalSourceFacts principal) =>
            HumanAuthorizationRouteProjector.Project(facts, deployment, settingsGovernance: false,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || principal.LocalSession is not null);

        void RequireSources(HumanPrincipalSourceFacts current, HumanAuthorizationRouteFacts currentRoute)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current);
            if (current.SelectedTenantId != original.SelectedTenantId
                || currentRoute.SelectedTenantId != original.SelectedTenantId
                || currentRoute.PrincipalId != subject.PrincipalId
                || current.SelectedTenantId.HasValue && current.SelectedTenantId != ambientTenant
                || !route.IsSemanticallyEquivalentTo(Project(currentRoute, current))
                || !SamePrincipal(original, current))
                throw new UnauthorizedAccessException("Original support administration authority changed.");
        }

        void RequireSupport(HumanSupportAccessSourceFacts current, bool completed)
        {
            var expected = originalSupport.Sessions;
            // The stopped/revoked row cannot pass Context.IsActive. Validate that exact
            // intended row instead, while RequireTime still uses the ORIGINAL active image.
            if (completed && prepared.Completed && prepared.Expected is { } ended
                && operation is StopSupportAccessSessionCommand or ForceStopSupportAccessSessionCommand
                && originalSupport.Forwarding.SessionId == ended.Id && ended.ActorUserId == subject.PrincipalId)
                expected = expected.Select(row => row.Id == ended.Id ? row with
                {
                    StatusId = ended.StatusId,
                    EndedAtUtc = ended.EndedAtUtc,
                    ConcurrencyStamp = ended.ConcurrencyStamp
                } : row).ToImmutableArray();
            if (!ReferenceEquals(current.Forwarding, originalSupport.Forwarding)
                || !Same(originalSupport.Settings, current.Settings) || !Same(expected, current.Sessions))
                throw new UnauthorizedAccessException("Support sources changed outside the exact original transition.");
        }

        void RequireTime(DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now)
                || originalSupport.Forwarding.WasForwarded
                    && !HumanSupportAccessProjection.Context(originalSupport, now).IsActive)
                throw new UnauthorizedAccessException("The original credential or support horizon ended.");
        }
    }

    private static bool LocalAllowed(HumanPrincipalSourceFacts facts, AuthorizationRequest check, Guid ambientTenant)
    {
        // The existing Local support policy permits all five questions for a persisted
        // platform administrator and only list/audit for the target's tenant administrator.
        if (facts.PlatformGrants.Any(grant => facts.Roles.Any(role => role.Id == grant.RoleId
            && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode)))
            return true;
        if (check.Action is not (AuthorizationActions.SupportAccessSessions.List or AuthorizationActions.SupportAccessSessions.ViewAudit))
            return false;
        Guid tenant = check.Facts is SupportAccessSessionAuthorizationFacts target ? target.TenantId : ambientTenant;
        return tenant != Guid.Empty
            && facts.TenantGrants.Any(grant => grant.TenantId == tenant && grant.RoleId == (int)RoleEnum.TenantAdmin
                && grant.RevokedAt is null
                && facts.TenantMemberships.Any(row => row.Id == grant.TenantUserId && row.TenantId == tenant
                    && row.StatusId == (int)TenantUserStatusEnum.Active && !row.IsDeleted));
    }

    private static bool Same<T>(IEnumerable<T> expected, IEnumerable<T> actual) where T : notnull
    {
        var counts = expected.GroupBy(row => row).ToDictionary(group => group.Key, group => group.Count());
        var groups = actual.GroupBy(row => row).ToArray();
        return counts.Count == groups.Length
            && groups.All(group => counts.TryGetValue(group.Key, out int count) && count == group.Count());
    }

    private static bool SamePrincipal(HumanPrincipalSourceFacts original, HumanPrincipalSourceFacts current) =>
        Same(original.Users, current.Users) && Same(original.Bindings, current.Bindings)
        && Same(original.PlatformGrants, current.PlatformGrants) && Same(original.Roles, current.Roles)
        && Same(original.TenantMemberships, current.TenantMemberships) && Same(original.TenantGrants, current.TenantGrants)
        && Same(original.Tenants, current.Tenants) && Same(original.Organizations, current.Organizations)
        && Same(original.Groups, current.Groups) && Same(original.Permissions, current.Permissions)
        && original.PermissionMappingsExist == current.PermissionMappingsExist
        && original.EventCreateOrganizations.SequenceEqual(current.EventCreateOrganizations)
        && original.EventCreateGroups.SequenceEqual(current.EventCreateGroups)
        && original.EventFinanceOrganizations.SequenceEqual(current.EventFinanceOrganizations)
        && original.EventFinanceGroups.SequenceEqual(current.EventFinanceGroups)
        && (original.LocalSession is null ? current.LocalSession is null
            : current.LocalSession is { } local
                && original.LocalSession.CredentialState == local.CredentialState
                && original.LocalSession.EmailDeliveryEnabled == local.EmailDeliveryEnabled
                && Same(original.LocalSession.Accounts, local.Accounts)
                && Same(original.LocalSession.Operations, local.Operations)
                && Same(original.LocalSession.PersonalActors, local.PersonalActors));
}
