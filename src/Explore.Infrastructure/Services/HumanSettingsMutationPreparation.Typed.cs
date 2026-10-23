using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Infrastructure.SupportAccess;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Services;

public sealed partial class HumanSettingsMutationPreparation
{
    public TypedSettingsOriginal? CurrentTypedOriginal =>
        _currentOwner.Value is { Active: true } current ? current.TypedOriginal : null;

    public async Task<IPreparedHumanTenantPlanAssignment> PrepareAsync(ApplyInstanceTenantPlanAssignmentCommand command,
        CancellationToken cancellationToken = default) =>
        (IPreparedHumanTenantPlanAssignment)await PrepareTypedAsync(new(command), cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateEventPolicyCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareTypedAsync(new(command), cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateModuleSettingsCommand command,
        CancellationToken cancellationToken = default) => PrepareTypedAsync(new(command), cancellationToken);
    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateOrganizationPolicyCommand command,
        CancellationToken cancellationToken = default) => PrepareTypedAsync(new(command), cancellationToken);
    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateBrandingSettingsCommand command,
        CancellationToken cancellationToken = default) => PrepareTypedAsync(new(command), cancellationToken);
    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateDomainSettingsCommand command,
        CancellationToken cancellationToken = default) => PrepareTypedAsync(new(command), cancellationToken);
    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateTenantDelegationSettingsCommand command,
        CancellationToken cancellationToken = default) => PrepareTypedAsync(new(command), cancellationToken);
    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateAdminPortalSettingsCommand command,
        CancellationToken cancellationToken = default) => PrepareTypedAsync(new(command), cancellationToken);
    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateMcpGovernanceSettingsCommand command,
        CancellationToken cancellationToken = default) => PrepareTypedAsync(new(command), cancellationToken);
    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateAiAssistantGovernanceSettingsCommand command,
        CancellationToken cancellationToken = default) => PrepareTypedAsync(new(command), cancellationToken);
    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateRenderPolicySettingsCommand command,
        CancellationToken cancellationToken = default) => PrepareTypedAsync(new(command), cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(CompleteTenantOnboardingCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareTypedAsync(new(command, tenantContext.TenantId), cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateTenantReportingIntakePolicyCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareTypedAsync(new(command), cancellationToken);

    public void RequireTypedSource(IHumanMixedSettingsSource source)
    {
        if (source is not TypedSource issued || !ReferenceEquals(issued.Issuer, this))
            throw new UnauthorizedAccessException("The original scoped typed source issuer is required.");
        issued.RequireCustody();
    }

    private async Task<IPreparedHumanSettingsMutation> PrepareTypedAsync(
        TypedSettingsOriginal original, CancellationToken cancellationToken)
    {
        await humanScope.PrepareAsync(cancellationToken);
        var protocol = await services.GetRequiredService<IHumanMixedSettingsSourceFactory>()
            .CreateAsync(original, cancellationToken);
        var request = httpContext.HttpContext ?? throw new UnauthorizedAccessException("An original authenticated request is required.");
        var principal = request.User;
        var subject = protocol.Subject;
        var lifetime = protocol.Lifetime;
        Guid ambient = tenantContext.TenantId;
        if (!ReferenceEquals(humanScope.Subject, subject) || original.Actor != subject.PrincipalId
            || !ReferenceEquals(await lifetimeObserver.ObserveAsync(principal, cancellationToken), lifetime)
            || original.TenantId != protocol.TenantId || original.TenantId.HasValue && original.TenantId != ambient)
            throw new UnauthorizedAccessException("Original typed actor, target and credential custody is required.");
        void Custody()
        {
            if (!ReferenceEquals(httpContext.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || !ReferenceEquals(humanScope.Subject, subject) || tenantContext.TenantId != ambient)
                throw new UnauthorizedAccessException("Original typed execution custody changed.");
        }
        var observed = await protocol.ObserveAsync(cancellationToken);
        var current = observed;
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        string principalImage = JsonSerializer.Serialize(observed.Principal);
        string supportImage = JsonSerializer.Serialize(observed.Support);
        var route = HumanAuthorizationRouteProjector.Project(observed.Route
                ?? throw new UnauthorizedAccessException("Complete original typed route sources are required."),
            deployment, settingsGovernance: true,
            localDecisionSourcesCoLocated: lifetime.LocalSession is null || observed.Principal.LocalSession is not null);
        bool TenantAdministrator(HumanPrincipalSourceFacts facts) => original.TenantId is { } tenant
            && facts.Tenants.Any(currentTenant => currentTenant.Id == tenant && currentTenant.IsActiveState)
            && facts.TenantGrants.Any(grant => grant.TenantId == tenant
                && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RevokedAt is null
                && grant.RoleScopeId == (int)RoleScopeEnum.Tenant
                && facts.Roles.Any(role => role.Id == grant.RoleId
                    && role.RoleScopeId == (int)RoleScopeEnum.Tenant)
                && facts.TenantMemberships.Any(member => member.Id == grant.TenantUserId
                    && member.TenantId == tenant && member.StatusId == (int)TenantUserStatusEnum.Active && !member.IsDeleted));
        void Check(HumanEmailSourceFacts facts)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts.Principal);
            if (facts.Principal.SelectedTenantId != original.TenantId
                || !ReferenceEquals(facts.Support.Forwarding.Subject, subject)
                || !SameForwarding(protocol.Forwarding, facts.Support.Forwarding)
                || principalImage != JsonSerializer.Serialize(facts.Principal)
                || supportImage != JsonSerializer.Serialize(facts.Support)
                || facts.Route is null || facts.Route.SelectedTenantId != original.TenantId
                || facts.Route.PrincipalId != original.Actor
                || !route.IsSemanticallyEquivalentTo(HumanAuthorizationRouteProjector.Project(facts.Route, deployment,
                    settingsGovernance: true,
                    localDecisionSourcesCoLocated: lifetime.LocalSession is null || facts.Principal.LocalSession is not null)))
                throw new UnauthorizedAccessException("Original typed human or route sources changed.");
            bool administrator = original.Governance is not null || original.EventPolicy is not null || original.PlanAssignment is not null ? InstancePolicyInput(facts.Principal)
                : original.Onboarding is not null ? TenantAdministrator(facts.Principal) || InstancePolicyInput(facts.Principal)
                : route.Provider != HumanAuthorizationRouteProvider.Local || TenantAdministrator(facts.Principal);
            if (!administrator)
            {
                if (original.PlanAssignment is not null)
                    throw new TenantPlanOriginalRejectedException(
                        Explore.Application.Responses.BaseCommandResponse.Authorization<Guid>(
                            "Current instance administrator authority is required."));
                throw new AuthorizationException(original.TenantId.HasValue ? ResourceKinds.TenantSetting : ResourceKinds.InstanceSetting,
                    AuthorizationActions.Update);
            }
        }
        var supportCheck = new SupportCheck(original.TenantId.HasValue && original.PlanAssignment is null ? ResourceKinds.TenantSetting : ResourceKinds.InstanceSetting,
            AuthorizationActions.Update, original.Reporting is { } reporting
                ? ((ISecureRequest)reporting).AuthorizationFacts
                : original.PlanAssignment is { } plan ? ((ISecureRequest)plan).AuthorizationFacts
                : original.TenantId is { } tenant ? new TenantScopedAuthorizationFacts(tenant)
                : InstanceScopedAuthorizationFacts.Instance);
        void Time(DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original typed credential horizon ended.");
            if ((original.PlanAssignment is not null || original.Governance is not null)
                && (observed.Support.Forwarding.WasForwarded && !HumanSupportAccessProjection.Context(observed.Support, now).IsActive
                    || current.Support.Forwarding.WasForwarded && !HumanSupportAccessProjection.Context(current.Support, now).IsActive))
                throw new UnauthorizedAccessException("The original typed support horizon ended.");
            RequireSupport(observed.Support, now, supportCheck);
            RequireSupport(current.Support, now, supportCheck);
        }
        Check(observed);
        Time(clock.GetUtcNow());
        if (original.PlanAssignment is { } planOriginal)
        {
            ISecureRequest secured = planOriginal;
            var question = new AuthorizationRequest(ResourceKinds.InstanceSetting, secured.ResourceId!,
                AuthorizationActions.InstanceSettings.Update, Facts: secured.AuthorizationFacts);
            if (route.Provider == HumanAuthorizationRouteProvider.CerbosInstance)
            {
                var decision = await cerbos.AuthorizePreparedHumanAsync(
                    CerbosPrincipalBuilder.BuildPrincipalFromSources(observed.Principal), question, ambient,
                    deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
                if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                    throw new AuthorizationProviderUnavailableException(question.ResourceKind, question.Action);
                if (!decision.IsAllowed) throw new AuthorizationException(question.ResourceKind, question.Action);
            }
            else if (route.Provider != HumanAuthorizationRouteProvider.Local)
                throw new AuthorizationException(question.ResourceKind, question.Action);
            Time(clock.GetUtcNow());
        }
        if (original.Reporting is { } reportingOriginal)
        {
            ISecureRequest secured = reportingOriginal;
            var question = new AuthorizationRequest(ResourceKinds.TenantSetting, secured.ResourceId!,
                AuthorizationActions.TenantSettings.Update, Facts: secured.AuthorizationFacts);
            if (route.Provider == HumanAuthorizationRouteProvider.CerbosInstance)
            {
                var decision = await cerbos.AuthorizePreparedHumanAsync(
                    CerbosPrincipalBuilder.BuildPrincipalFromSources(observed.Principal), question, ambient,
                    deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
                if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                    throw new AuthorizationProviderUnavailableException(question.ResourceKind, question.Action);
                if (!decision.IsAllowed) throw new AuthorizationException(question.ResourceKind, question.Action);
            }
            else if (route.Provider != HumanAuthorizationRouteProvider.Local)
                throw new AuthorizationException(question.ResourceKind, question.Action);
            using var image = JsonDocument.Parse(protocol.OriginalData);
            bool systemLocked = image.RootElement.GetProperty(nameof(SystemSetting)).EnumerateArray().Any(row =>
                row.GetProperty(nameof(SystemSetting.SettingKey)).GetString() == secured.ResourceId
                && row.GetProperty(nameof(SystemSetting.IsLocked)).GetBoolean());
            bool tenantLocked = image.RootElement.GetProperty(nameof(TenantSetting)).EnumerateArray().Any(row =>
                row.GetProperty(nameof(TenantSetting.TenantId)).GetGuid() == reportingOriginal.TenantId
                && row.GetProperty(nameof(TenantSetting.SettingKey)).GetString() == secured.ResourceId
                && row.GetProperty(nameof(TenantSetting.IsLocked)).GetBoolean());
            if (route.Provider == HumanAuthorizationRouteProvider.Local && (systemLocked || tenantLocked))
                throw new AuthorizationException(question.ResourceKind, question.Action);
            Time(clock.GetUtcNow());
        }
        var source = new TypedSource(this, protocol, Custody, async token =>
        {
            current = await protocol.RequireOwnedAsync(token);
            Check(current);
            Time(clock.GetUtcNow());
        }, Time);
        var owner = services.GetRequiredService<ISerializableAttemptOwner>();
        var store = services.GetRequiredService<ITypedSettingsOriginalStore>();
        await store.ObserveAsync(original, source, cancellationToken);
        Time(clock.GetUtcNow());
        Prepared? prepared = null;
        prepared = new Prepared(this, original, [], [], owner, async token =>
        {
            current = await protocol.AcquireAsync(token);
            Check(current);
            Time(clock.GetUtcNow());
            var compiled = await store.AcquireAndCompileAsync(original, token);
            var scope = original.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance;
            var mutations = ImmutableArray.CreateBuilder<HumanSettingsMutation>();
            foreach (var effect in compiled)
            {
                var value = new HumanSettingsMutation(original.PlanAssignment is null ? OperationFor(scope)
                    : HumanSettingsOperation.InstanceTenantSettings, scope, original.TenantId, null,
                    effect.Key, effect.Remove ? HumanSettingsMutationKind.Remove : HumanSettingsMutationKind.SetValue, effect.Value);
                mutations.Add(value);
                if (effect.IsLocked.HasValue)
                    mutations.Add(value with { Kind = HumanSettingsMutationKind.SetLock, Value = null, IsLocked = effect.IsLocked });
            }
            prepared!.Effects = mutations.ToImmutable();
            services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
            {
                await store.RequireCompleteAsync(original, finalToken);
                current = await protocol.RequireOwnedAsync(finalToken);
                Check(current);
            }, Time);
            return async guardToken =>
            {
                await store.VerifyAsync(original, guardToken);
                Time(clock.GetUtcNow());
            };
        })
        { TypedOriginal = original, TypedStore = store };
        return prepared;
    }

    private sealed class TypedSource(HumanSettingsMutationPreparation issuer, IHumanMixedSettingsSourceProtocol protocol,
        Action custody, Func<CancellationToken, Task> validate, Action<DateTimeOffset> time) : IHumanMixedSettingsSource
    {
        internal HumanSettingsMutationPreparation Issuer => issuer;
        public HumanCommitSubject Subject => protocol.Subject;
        public Guid? TenantId => protocol.TenantId;
        public string OriginalData => protocol.OriginalData;
        public void RequireCustody() => custody();
        public Task RequireSourcesAsync(CancellationToken cancellationToken) => validate(cancellationToken);
        public void RequireTime(DateTimeOffset now) => time(now);
    }
}
