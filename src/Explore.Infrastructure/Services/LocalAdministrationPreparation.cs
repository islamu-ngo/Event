using System.Collections.Immutable;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// One original handler-owned administrator question across separate Local lifecycle
/// transactions. No annotation, selected-provider decision, refreshed login or role
/// is substituted for that question. Each physical owner reacquires the original sources.
/// </summary>
public sealed class LocalAdministrationPreparation(
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
    Func<ILocalAdministrationOwnerSource> resolveOwner) : ILocalAdministrationPreparation
{
    public async Task<ILocalAdministrationInvocation> PrepareAsync(object request, Guid administrator,
        CancellationToken cancellationToken)
    {
        if (request is not (CreateLocalIdentityCommand or ResetLocalCredentialCommand
            or ReconcileLocalCredentialOperationCommand or ListLocalIdentitiesQuery or GetLocalCredentialOperationQuery)
            || administrator == Guid.Empty || machine.IsMachineCaller)
            throw new UnauthorizedAccessException("An original Local administration request is required.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("The original Local administrator is required.");
        var context = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var authenticated = context.User;
        var lifetime = await lifetimeObserver.ObserveAsync(authenticated, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic original credential lifetime is required.");
        Guid tenant = tenantContext.TenantId;
        if (subject.PrincipalId != administrator || subject.AccountKey != lifetime.AccountKey)
            throw new UnauthorizedAccessException("The administrator is not the original account.");
        var prepared = new Prepared(this, request, administrator);
        void Custody()
        {
            if (prepared.Rejected || machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(httpContext.HttpContext, context) || !ReferenceEquals(context.User, authenticated)
                || tenantContext.TenantId != tenant)
                throw new UnauthorizedAccessException("Original Local administration custody changed.");
        }
        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var exclusionSource = resolveExclusion();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var original = await principalSource.ObserveAsync(cancellationToken);
        var originalRoute = await routeSource.ObserveAsync(cancellationToken);
        var originalSupport = await supportSource.ObserveAsync(cancellationToken);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var route = HumanAuthorizationRouteProjector.Project(originalRoute, deployment, settingsGovernance: true,
            localDecisionSourcesCoLocated: lifetime.LocalSession is null || original.LocalSession is not null);
        HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, original);
        if (!Administrator(original) || originalRoute.PrincipalId != administrator
            || original.SelectedTenantId != originalRoute.SelectedTenantId
            || original.SelectedTenantId.HasValue && original.SelectedTenantId != tenant)
            throw new UnauthorizedAccessException("The original persisted platform administrator is required.");
        RequireTime(originalSupport, clock.GetUtcNow());

        prepared.Acquire = async (stage, token) =>
        {
            Custody();
            resolveOwner().RequireOwner(prepared, stage);
            var exclusion = await exclusionSource.AcquireAsync(token);
            var principal = await principalSource.AcquireAsync(exclusion, token);
            var currentRoute = await routeSource.AcquireAsync(exclusion, token);
            var support = await supportSource.AcquireAsync(exclusion, principal, cancellationToken: token);
            await Compare(principal.Facts, currentRoute.Facts, token);
            RequireSupport(support.Facts, clock.GetUtcNow());
            RequireTime(support.Facts, clock.GetUtcNow());
            HumanSupportAccessSourceFacts? finalSupport = null;
            resolveFinalizer().Register(async finalToken =>
            {
                resolveOwner().RequireOwner(prepared, stage);
                await exclusion.ValidateAsync(finalToken);
                var finalPrincipal = await principal.ValidateAsync(finalToken);
                var finalRoute = await currentRoute.ValidateAsync(finalToken);
                finalSupport = await support.ValidateAsync(finalToken);
                await Compare(finalPrincipal, finalRoute, finalToken);
            }, now => RequireTime(finalSupport
                ?? throw new InvalidOperationException("Local terminal sources were not read."), now));
        };
        return prepared;

        async Task Compare(HumanPrincipalSourceFacts current, HumanAuthorizationRouteFacts currentRoute,
            CancellationToken token)
        {
            Custody();
            var expectedLocal = original.LocalSession;
            var reset = lifetime.LocalSession is not null
                && request is ResetLocalCredentialCommand self && self.LocalSubjectId == administrator
                ? await resolveOwner().ValidateAsync(prepared, token) : null;
            if (reset is null)
                HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current);
            else
            {
                // The actual writer has validated every persisted scalar against its
                // private postimage. This changes only the original credential's closed
                // reset image, never the authenticated lifetime or administrator inputs.
                var command = (ResetLocalCredentialCommand)request;
                if (lifetime.LocalSession is null || expectedLocal is null
                    || reset.Operation.OperationId != command.OperationId
                    || reset.Operation.LocalSubjectId != administrator
                    || reset.Operation.InitiatingApplicationUserId != administrator
                    || reset.PreviousOperationId != command.ExpectedCurrentOperationId
                    || reset.PreviousOperationConcurrencyStamp != command.ExpectedCurrentOperationConcurrencyStamp
                    || expectedLocal.CredentialState?.OperationId != reset.PreviousOperationId)
                    throw new UnauthorizedAccessException("The self-reset is not the original authorized transition.");
                expectedLocal = expectedLocal with
                {
                    Accounts = expectedLocal.Accounts.Select(row => row with { StampMatches = false }).ToImmutableArray(),
                    CredentialState = new(LocalCredentialStateMetadata.CurrentVersion,
                        LocalCredentialState.ChangeRequired, command.OperationId, administrator),
                    Operations = expectedLocal.Operations.Select(row => row.Id == reset.PreviousOperationId
                        ? row with { Stage = (int)LocalCredentialOperationStage.Superseded } : row)
                        .Append(new HumanPrincipalLocalOperationFact(command.OperationId, administrator,
                            (int)LocalCredentialOperationKind.Reset, (int)LocalCredentialOperationStage.ChangeRequired,
                            reset.Operation.PersonalActorId, reset.Operation.ExternalLoginId)).ToImmutableArray()
                };
            }
            var projected = HumanAuthorizationRouteProjector.Project(currentRoute, deployment, settingsGovernance: true,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || current.LocalSession is not null);
            if (current.PrincipalId != administrator || current.SelectedTenantId != original.SelectedTenantId
                || currentRoute.PrincipalId != administrator || currentRoute.SelectedTenantId != original.SelectedTenantId
                || !route.IsSemanticallyEquivalentTo(projected) || !Same(original.Users, current.Users)
                || !Same(original.Bindings, current.Bindings) || !Same(original.PlatformGrants, current.PlatformGrants)
                || !Same(original.Roles, current.Roles) || !Same(original.Permissions, current.Permissions)
                || !Same(original.TenantMemberships, current.TenantMemberships)
                || !Same(original.TenantGrants, current.TenantGrants) || !Same(original.Tenants, current.Tenants)
                || !Same(original.Organizations, current.Organizations) || !Same(original.Groups, current.Groups)
                || original.PermissionMappingsExist != current.PermissionMappingsExist
                || !original.EventCreateOrganizations.SequenceEqual(current.EventCreateOrganizations)
                || !original.EventCreateGroups.SequenceEqual(current.EventCreateGroups)
                || !original.EventFinanceOrganizations.SequenceEqual(current.EventFinanceOrganizations)
                || !original.EventFinanceGroups.SequenceEqual(current.EventFinanceGroups)
                || !SameLocal(expectedLocal, current.LocalSession))
                throw new UnauthorizedAccessException("Original Local administration sources changed.");
        }

        void RequireTime(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original Local administrator credential expired.");
            RequireSupport(originalSupport, now);
            RequireSupport(support, now);
        }

        void RequireSupport(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            if (!ReferenceEquals(support.Forwarding, originalSupport.Forwarding)
                || !Same(originalSupport.Sessions, support.Sessions) || !Same(originalSupport.Settings, support.Settings))
                throw new UnauthorizedAccessException("Original administrative support sources changed.");
            var access = HumanSupportAccessProjection.Context(support, now);
            if (support.Forwarding.WasForwarded && !access.IsActive
                || access.IsActive && request is not (ListLocalIdentitiesQuery or GetLocalCredentialOperationQuery)
                    && !access.AllowsWrites)
                throw new UnauthorizedAccessException("Original support does not permit Local administration.");
        }
    }

    public async Task AcquireAsync(ILocalAdministrationInvocation invocation, object originalRequest,
        LocalAdministrationStage stage, CancellationToken cancellationToken)
    {
        var prepared = Require(invocation);
        try
        {
            if (!ReferenceEquals(prepared.Request, originalRequest) || prepared.Active is not null
                || !Allowed(prepared.Request, prepared.Last, stage))
                throw new UnauthorizedAccessException("Local administration cannot replace or replay its original stage.");
            prepared.Active = stage;
            await prepared.Acquire(stage, cancellationToken);
        }
        catch { prepared.Rejected = true; throw; }
    }

    public void Settle(ILocalAdministrationInvocation invocation, LocalAdministrationStage stage, bool committed)
    {
        var prepared = Require(invocation, allowRejected: true);
        if (prepared.Active != stage || !committed) prepared.Rejected = true;
        prepared.Active = null;
        prepared.Last = stage;
    }

    private Prepared Require(ILocalAdministrationInvocation invocation, bool allowRejected = false) =>
        invocation is Prepared prepared && ReferenceEquals(prepared.Issuer, this)
            && (allowRejected || !prepared.Rejected) ? prepared
            : throw new UnauthorizedAccessException("Local administration requires its original private invocation.");

    private static bool Allowed(object request, LocalAdministrationStage? previous, LocalAdministrationStage next) =>
        (request, previous, next) switch
        {
            (CreateLocalIdentityCommand, null, LocalAdministrationStage.CreatePending) => true,
            (CreateLocalIdentityCommand, LocalAdministrationStage.CreatePending,
                LocalAdministrationStage.ReadProvisioning or LocalAdministrationStage.Disclose) => true,
            (ReconcileLocalCredentialOperationCommand, null, LocalAdministrationStage.ReadProvisioning) => true,
            (CreateLocalIdentityCommand or ReconcileLocalCredentialOperationCommand,
                LocalAdministrationStage.ReadProvisioning, LocalAdministrationStage.Bind) => true,
            (CreateLocalIdentityCommand or ReconcileLocalCredentialOperationCommand,
                LocalAdministrationStage.Bind, LocalAdministrationStage.Activate) => true,
            (CreateLocalIdentityCommand, LocalAdministrationStage.Activate, LocalAdministrationStage.Disclose) => true,
            (ResetLocalCredentialCommand, null, LocalAdministrationStage.Reset) => true,
            (ResetLocalCredentialCommand, LocalAdministrationStage.Reset, LocalAdministrationStage.Disclose) => true,
            (ListLocalIdentitiesQuery or GetLocalCredentialOperationQuery, null, LocalAdministrationStage.Disclose) => true,
            _ => false
        };

    private static bool Administrator(HumanPrincipalSourceFacts facts) =>
        facts.PlatformGrants.Any(grant => facts.Roles.Any(role => role.Id == grant.RoleId
            && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode));

    private static bool Same<T>(IEnumerable<T> left, IEnumerable<T> right) where T : notnull
    {
        var expected = left.GroupBy(row => row).ToDictionary(group => group.Key, group => group.Count());
        var actual = right.GroupBy(row => row).ToArray();
        return expected.Count == actual.Length
            && actual.All(group => expected.TryGetValue(group.Key, out int count) && count == group.Count());
    }

    private static bool SameLocal(HumanPrincipalLocalSessionFacts? left, HumanPrincipalLocalSessionFacts? right) =>
        left is null ? right is null : right is not null
            && Same(left.Accounts, right.Accounts) && left.CredentialState == right.CredentialState
            && Same(left.Operations, right.Operations) && Same(left.PersonalActors, right.PersonalActors)
            && left.EmailDeliveryEnabled == right.EmailDeliveryEnabled;

    private sealed class Prepared(LocalAdministrationPreparation issuer, object request, Guid principalId)
        : ILocalAdministrationInvocation
    {
        internal LocalAdministrationPreparation Issuer { get; } = issuer;
        internal object Request { get; } = request;
        public Guid PrincipalId { get; } = principalId;
        internal bool Rejected { get; set; }
        internal LocalAdministrationStage? Active { get; set; }
        internal LocalAdministrationStage? Last { get; set; }
        internal Func<LocalAdministrationStage, CancellationToken, Task> Acquire { get; set; } = null!;
    }
}
