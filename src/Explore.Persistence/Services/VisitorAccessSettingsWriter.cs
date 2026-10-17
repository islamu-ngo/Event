
using System.Collections.Immutable;
using Explore.Persistence.Database.ProviderPrimitives;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Notifications;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Services;

public sealed class VisitorAccessSettingsWriter : IVisitorAccessSettingsWriter
{
    private readonly ExploreDbContext context;
    private readonly ISettingMutationLock mutationLock;
    private readonly IUnitOfWork unitOfWork;
    private readonly IEventParticipationConfigurationRepository participation;
    private readonly IConfiguration configuration;
    private readonly IInstanceAdministratorCommitAuthority? instanceAuthority;
    private readonly ITenantAdministratorCommitAuthority? tenantAuthority;
    private readonly IHumanActionCommitFinalizer? finalizer;
    private readonly HumanCommitSubject? originalSubject;
    private readonly IHumanSettingsMutationPreparation? settingsPreparation;
    private readonly IHumanConfigurationImportAdmission? importAdmission;
    private ProviderConfigurationSettingsWriter.ProviderHandoff? validatedProvider;

    internal async Task<VisitorAccessSettingsWriteResult> ApplyPlanAsync(
        TypedSettingsOriginalStore plan, CancellationToken token)
    {
        if (!ReferenceEquals(plan, TypedSettingsOriginalStore.CurrentPlan(context))
            || !ReferenceEquals(originalSubject, plan.PlanSubject))
            throw new UnauthorizedAccessException("The original selected plan visitor subject is required.");
        var mutations = plan.PlanVisitorMutations;
        await plan.RequirePlanVisitorAsync(this, mutations, token);
        return await ApplyCoreAsync(mutations, originalSubject, SettingsMutationOperation.InstanceTenantSettings,
            plan.PlanActor, token);
    }

    internal void RequireProviderValidated(ProviderConfigurationSettingsWriter.ProviderHandoff handoff)
    {
        handoff.RequireCurrent();
        if (!ReferenceEquals(validatedProvider, handoff))
            throw new UnauthorizedAccessException("The original visitor validator has not admitted this provider handoff.");
    }

    public VisitorAccessSettingsWriter(
        ExploreDbContext context, ISettingMutationLock mutationLock, IUnitOfWork unitOfWork,
        IEventParticipationConfigurationRepository participation, IConfiguration configuration)
    {
        this.context = context;
        this.mutationLock = mutationLock;
        this.unitOfWork = unitOfWork;
        this.participation = participation;
        this.configuration = configuration;
    }

    public VisitorAccessSettingsWriter(
        ExploreDbContext context, ISettingMutationLock mutationLock, IUnitOfWork unitOfWork,
        IEventParticipationConfigurationRepository participation, IConfiguration configuration,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        ITenantAdministratorCommitAuthority tenantAuthority, IHumanActionCommitFinalizer finalizer,
        HumanCommitSubject originalSubject, IHumanSettingsMutationPreparation settingsPreparation,
        IHumanConfigurationImportAdmission? importAdmission = null)
        : this(context, mutationLock, unitOfWork, participation, configuration)
    {
        this.instanceAuthority = instanceAuthority;
        this.tenantAuthority = tenantAuthority;
        this.finalizer = finalizer;
        this.originalSubject = originalSubject;
        this.settingsPreparation = settingsPreparation;
        this.importAdmission = importAdmission;
    }

    public async Task<VisitorAccessSettingsWriteResult> ApplyAsync(
        ImmutableArray<VisitorAccessSettingMutation> mutations,
        HumanCommitSubject subject,
        SettingsMutationOperation operation,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (finalizer is null || !ReferenceEquals(subject, originalSubject)
            || !context.HasHumanCommitContextLease
            || !ReferenceEquals(context.HumanCommitAuditSubject, subject)
            || actorUserId is { } actor && actor != subject.PrincipalId)
            throw new UnauthorizedAccessException("Human visitor writes require a prepared committing owner.");
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        // Instance import has no visitor native mutation contract. Never promote its Global owner.
        if (context.IsConfigurationInstanceImportOwner
            || operation == SettingsMutationOperation.ConfigurationInstanceImport)
            throw new InvalidOperationException("Visitor settings are not an instance import mutation family.");
        var preparation = settingsPreparation
            ?? throw new UnauthorizedAccessException("Original visitor source preparation is required.");
        if (operation == SettingsMutationOperation.ConfigurationTenantImport)
        {
            var producer = importAdmission
                ?? throw new UnauthorizedAccessException("The original acquired import producer is required.");
            var slice = mutations.Select(mutation => new HumanConfigurationImportSettingEffect(
                SettingScope.Tenant, mutation.TenantId, mutation.Key, mutation.Kind switch
                {
                    VisitorAccessSettingMutationKind.SetValue => HumanSettingsMutationKind.SetValue,
                    VisitorAccessSettingMutationKind.SetLock => HumanSettingsMutationKind.SetLock,
                    VisitorAccessSettingMutationKind.Remove => HumanSettingsMutationKind.Remove,
                    _ => throw new ArgumentOutOfRangeException(nameof(mutations))
                }, mutation.Value, mutation.IsLocked)).ToImmutableArray();
            return await producer.ExecuteJoinedAsync(operation, slice,
                (_, token) => ApplyCoreAsync(mutations, subject, operation, actorUserId, token), cancellationToken);
        }
        var effects = mutations.Select(mutation => new HumanSettingsMutation(
            operation switch
            {
                SettingsMutationOperation.InstanceSettings => HumanSettingsOperation.InstanceSettings,
                SettingsMutationOperation.TenantSettings => HumanSettingsOperation.TenantSettings,
                SettingsMutationOperation.InstanceTenantSettings => HumanSettingsOperation.InstanceTenantSettings,
                _ => throw new InvalidOperationException("The original visitor Settings action is required.")
            }, mutation.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance,
            mutation.TenantId, null, mutation.Key, mutation.Kind switch
            {
                VisitorAccessSettingMutationKind.SetValue => HumanSettingsMutationKind.SetValue,
                VisitorAccessSettingMutationKind.SetLock => HumanSettingsMutationKind.SetLock,
                VisitorAccessSettingMutationKind.Remove => HumanSettingsMutationKind.Remove,
                _ => throw new ArgumentOutOfRangeException(nameof(mutations))
            }, mutation.Value, mutation.IsLocked)).ToImmutableArray();
        if (context.Database.CurrentTransaction is not null)
            return await preparation.ExecuteCurrentJoinedAsync(effects,
                (_, token) => ApplyCoreAsync(mutations, subject, operation, actorUserId, token), cancellationToken);
        var prepared = await preparation.PrepareVisitorAsync(mutations, operation, actorUserId, cancellationToken);
        return await mutationLock.ExecuteOrderedGroupsAsync([VisitorAccessCapabilityResolver.AuthoritySettingKeys],
            token => preparation.ExecutePreparedAsync(prepared,
                (_, inner) => ApplyCoreAsync(mutations, subject, operation, actorUserId, inner), token),
            cancellationToken);
    }

    public async Task<VisitorAccessSettingsWriteResult> ApplyInternalAsync(
        ImmutableArray<VisitorAccessSettingMutation> mutations,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (originalSubject is not null || context.HumanCommitContextLease.HasValue
            || context.HumanCommitAuditSubject is not null)
            throw new UnauthorizedAccessException("A human visitor writer cannot enter internal setup.");
        if (context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("Instance import cannot use the internal visitor writer.");
        await ProviderConfigurationSettingsWriter.RequireSetupVisitorAsync(
            context, mutations, actorUserId, cancellationToken);
        var result = await ApplyCoreAsync(mutations, null, null, actorUserId, cancellationToken);
        await ProviderConfigurationSettingsWriter.RequireSetupVisitorAsync(
            context, mutations, actorUserId, cancellationToken);
        return result;
    }

    private Task<VisitorAccessSettingsWriteResult> ApplyCoreAsync(
        ImmutableArray<VisitorAccessSettingMutation> mutations,
        HumanCommitSubject? subject,
        SettingsMutationOperation? operation,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        if (mutations.IsDefault || mutations.Any(mutation => !IsValid(mutation))
            || mutations.Select(mutation => (mutation.TenantId, mutation.Key)).Distinct().Count() != mutations.Length)
            return Task.FromResult(Rejected("visitor_access_policy_invalid"));
        if (mutations.IsEmpty && subject is null)
            return Task.FromResult(new VisitorAccessSettingsWriteResult(true, null, []));
        if (context.Database.CurrentTransaction is null && subject is not null)
            return mutationLock.ExecuteOrderedGroupsAsync([VisitorAccessCapabilityResolver.AuthoritySettingKeys],
                token => ((ISerializableAttemptOwner)unitOfWork).ExecuteAsync(
                    inner => ApplyInsideTransactionAsync(mutations, subject, operation, actorUserId, inner), token),
                cancellationToken);
        if (context.Database.CurrentTransaction is null)
            return mutationLock.ExecuteOrderedGroupsAsync([VisitorAccessCapabilityResolver.AuthoritySettingKeys],
                token => unitOfWork.ExecuteSerializableAsync(
                    innerToken => ApplyInsideTransactionAsync(mutations, subject, operation, actorUserId, innerToken), token), cancellationToken);
        if (context.Database.CurrentTransaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Visitor policy writes require a serializable transaction.");
        return mutationLock.ExecuteManyAsync(VisitorAccessCapabilityResolver.AuthoritySettingKeys,
            token => ApplyInsideTransactionAsync(mutations, subject, operation, actorUserId, token), cancellationToken);
    }

    private async Task<VisitorAccessSettingsWriteResult> ApplyInsideTransactionAsync(
        ImmutableArray<VisitorAccessSettingMutation> mutations, HumanCommitSubject? subject,
        SettingsMutationOperation? operation, Guid? actorUserId, CancellationToken token)
    {
        var mixed = MixedSettingsBatchStore.Current(context);
        var plan = TypedSettingsOriginalStore.CurrentPlan(context);
        async Task RequireCurrentAuthorityAsync(CancellationToken cancellationToken)
        {
            if (plan is not null)
            {
                await plan.RequirePlanVisitorAsync(this, mutations, cancellationToken);
                return;
            }
            if (mixed is not null)
            {
                await mixed.RequireSpecializedAsync(subject, operation, actorUserId,
                    mutations.Select(mutation => mutation.Key), cancellationToken);
                return;
            }
            if (subject is null)
                return;
            context.ValidateTenantLease();
            bool tenantAction = operation is SettingsMutationOperation.TenantSettings
                or SettingsMutationOperation.ConfigurationTenantImport;
            bool targetsMatch = tenantAction || operation == SettingsMutationOperation.InstanceTenantSettings
                ? context.TenantFilterTenantId is { } tenant
                    && mutations.All(mutation => mutation.TenantId == tenant)
                : context.TenantFilterTenantId is null
                    && (operation != SettingsMutationOperation.InstanceSettings
                        || mutations.All(mutation => mutation.TenantId is null));
            bool allowed = targetsMatch && (tenantAction
                ? tenantAuthority is not null && await tenantAuthority.IsTenantAdministratorForCommitAsync(
                    subject.PrincipalId, context.TenantFilterTenantId!.Value, cancellationToken)
                : instanceAuthority is not null && await instanceAuthority.IsInstanceAdministratorForCommitAsync(
                    subject.PrincipalId, cancellationToken));
            if (!allowed)
                throw new UnauthorizedAccessException("Current visitor settings administration is required.");
        }
        await RequireCurrentAuthorityAsync(token);
        if (subject is not null && plan is null)
            finalizer!.Register(RequireCurrentAuthorityAsync, _ => { });
        if (mutations.IsEmpty)
            return new(true, null, []);
        var providerHandoff = mixed is null && plan is null
            ? ProviderConfigurationSettingsWriter.GetHandoff(context, mutations, subject, actorUserId) : null;
        string[] keys = VisitorAccessCapabilityResolver.AuthoritySettingKeys.ToArray();
        bool instanceChanged = mutations.Any(mutation => mutation.TenantId is null);
        if (context.ChangeTracker.Entries<SystemSetting>().Any(entry => keys.Contains(entry.Entity.SettingKey)
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || context.ChangeTracker.Entries<TenantSetting>().Any(entry => keys.Contains(entry.Entity.SettingKey)
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return Rejected("visitor_access_policy_invalid");
        var systems = plan is not null
            ? plan.PlanRows<SystemSetting>().Where(row => keys.Contains(row.SettingKey))
                .ToDictionary(row => row.SettingKey, StringComparer.Ordinal)
            : mixed is not null
            ? mixed.Rows<SystemSetting>().Where(row => keys.Contains(row.SettingKey))
                .ToDictionary(row => row.SettingKey, StringComparer.Ordinal)
            : providerHandoff is not null ? providerHandoff.Systems
            : await context.SystemSettings.AsNoTracking()
                .Where(row => keys.Contains(row.SettingKey)).ToDictionaryAsync(row => row.SettingKey, token);
        var overrides = (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql) && instanceChanged
            ? VisitorPolicyAuthorityQueries.Overrides(context)
            : context.TenantSettingOverrides;
        var tenants = plan is not null
            ? plan.PlanRows<TenantSetting>().Where(row => keys.Contains(row.SettingKey)).ToList()
            : mixed is not null
            ? mixed.Rows<TenantSetting>().Where(row => keys.Contains(row.SettingKey)).ToList()
            : providerHandoff is not null ? providerHandoff.Tenants : await overrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.VisitorPolicyAuthorityInheritanceSafetyRead)
            .AsNoTracking().Where(row => keys.Contains(row.SettingKey)).ToListAsync(token);
        var tenantRows = tenants.ToDictionary(row => (row.TenantId, row.SettingKey));
        var writes = new List<(object Entity, EntityState State)>();
        var notifications = ImmutableArray.CreateBuilder<SettingChangedNotification>();
        DateTime now = plan?.PlanOccurredAt ?? mixed?.OccurredAt ?? providerHandoff?.Mutation.OccurredAt ?? DateTime.UtcNow;
        foreach (var mutation in mutations.OrderBy(mutation => mutation.TenantId.HasValue))
        {
            SettingDefinition definition = SettingRegistry.Get(mutation.Key)!;
            if (mutation.TenantId is { } tenantId)
            {
                if (systems.GetValueOrDefault(mutation.Key)?.IsLocked == true)
                    return Rejected("setting_system_locked");
                tenantRows.TryGetValue((tenantId, mutation.Key), out TenantSetting? row);
                if (row is null && mutation.Kind == VisitorAccessSettingMutationKind.Remove)
                    continue;
                if (mutation.Kind == VisitorAccessSettingMutationKind.SetLock)
                {
                    if (row is null) return Rejected("setting_override_not_found");
                    if (row.IsLocked == mutation.IsLocked) return Rejected("setting_state_conflict");
                }
                string? oldValue = row?.Value;
                bool created = row is null;
                row ??= new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    SettingKey = mutation.Key,
                    Value = definition.DefaultValue,
                    CreatedAt = now,
                    CreatedBy = actorUserId
                };
                if (mutation.Kind == VisitorAccessSettingMutationKind.Remove)
                    tenantRows.Remove((tenantId, mutation.Key));
                else
                {
                    row.Value = mutation.Value ?? row.Value;
                    row.IsLocked = mutation.IsLocked ?? row.IsLocked;
                    tenantRows[(tenantId, mutation.Key)] = row;
                }
                row.UpdatedAt = now;
                row.UpdatedBy = actorUserId;
                writes.Add((row, mutation.Kind == VisitorAccessSettingMutationKind.Remove ? EntityState.Deleted
                    : created ? EntityState.Added : EntityState.Modified));
                notifications.Add(new(mutation.Key, oldValue,
                    mutation.Kind == VisitorAccessSettingMutationKind.Remove ? null : row.Value,
                    SettingSource.TenantOverride, tenantId, actorUserId, now));
            }
            else
            {
                systems.TryGetValue(mutation.Key, out SystemSetting? row);
                if (row is null && mutation.Kind == VisitorAccessSettingMutationKind.Remove)
                    continue;
                if (mutation.Kind == VisitorAccessSettingMutationKind.SetLock && row is null)
                    return Rejected("setting_not_found");
                string? oldValue = row?.Value;
                bool created = row is null;
                row ??= providerHandoff?.Canonical(mutation.Key) ?? new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = mutation.Key,
                    Value = definition.DefaultValue,
                    ValueType = definition.ValueType,
                    Category = definition.Category,
                    Description = definition.Description,
                    CreatedAt = now,
                    CreatedBy = actorUserId
                };
                if (mutation.Kind == VisitorAccessSettingMutationKind.Remove)
                    systems.Remove(mutation.Key);
                else
                {
                    row.Value = mutation.Value ?? row.Value;
                    row.IsLocked = mutation.IsLocked ?? row.IsLocked;
                    systems[mutation.Key] = row;
                }
                row.UpdatedAt = now;
                row.UpdatedBy = actorUserId;
                writes.Add((row, mutation.Kind == VisitorAccessSettingMutationKind.Remove ? EntityState.Deleted
                    : created ? EntityState.Added : EntityState.Modified));
                notifications.Add(new(mutation.Key, oldValue,
                    mutation.Kind == VisitorAccessSettingMutationKind.Remove ? null : row.Value,
                    row.IsLocked ? SettingSource.SystemLocked : SettingSource.SystemDefault, null, actorUserId, now));
            }
        }

        var affectedTenantIds = mutations.Where(mutation => mutation.TenantId.HasValue)
            .Select(mutation => mutation.TenantId!.Value).ToHashSet();
        // Global validation stays on the owning serializable transaction, including caller-owned writes.
        var affected = plan is not null ? plan.PlanAccountRequiredTenants()
            : mixed is not null ? mixed.AccountRequiredTenants()
            : providerHandoff is not null ? providerHandoff.Affected : (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql) && instanceChanged
            ? await VisitorPolicyAuthorityQueries.AccountRequiredTenantsAsync(context, token)
            : (await participation.GetAccountRequiredAsync(
                !instanceChanged && affectedTenantIds.Count == 1 ? affectedTenantIds.Single() : null, token))
                .Select(item => item.TenantId).Distinct().ToList();
        IReadOnlyList<VisitorAccessProviderState> providers;
        try
        {
            providers = AuthProviderConfigurationService.ProjectVisitorProviders(systems,
                plan?.PlanDeployment ?? mixed?.Deployment ?? providerHandoff?.Mutation.Source.Deployment ?? configuration);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Rejected("visitor_access_policy_invalid");
        }
        foreach (Guid tenantId in affected
                     .Where(id => instanceChanged || affectedTenantIds.Contains(id)))
        {
            VisitorAccessMode mode = VisitorAccessCapabilityResolver.ResolveMode(
                systems.GetValueOrDefault(GovernanceSettingKeys.PublicExperience.VisitorAccessMode),
                tenantRows.GetValueOrDefault((tenantId, GovernanceSettingKeys.PublicExperience.VisitorAccessMode)));
            VisitorAccessCapability capability = VisitorAccessCapabilityResolver.EvaluateProposedState(new VisitorAccessPolicyState(mode, providers));
            if (!capability.AllowsAccountRequiredParticipation)
                return Rejected("visitor_access_account_required_conflict");
        }

        if (plan is not null)
        {
            await plan.RequirePlanVisitorAsync(this, mutations, token);
            await plan.WritePlanSpecializedAsync(writes.ToImmutableArray(), false, token);
            return new(true, null, notifications.ToImmutable());
        }
        if (mixed is not null)
        {
            await mixed.WriteVisitorAsync(this, mutations, writes.ToImmutableArray(), token);
            return new(true, null, notifications.ToImmutable());
        }
        if (providerHandoff is not null)
        {
            validatedProvider = providerHandoff;
            try { return await providerHandoff.ApplyValidatedAsync(this, systems, notifications.ToImmutable(), token); }
            finally { validatedProvider = null; }
        }

        // No persistent or tracked state is changed until every affected scope has passed.
        foreach (var entry in context.ChangeTracker.Entries<SystemSetting>()
                     .Where(entry => keys.Contains(entry.Entity.SettingKey)).ToArray())
            entry.State = EntityState.Detached;
        foreach (var entry in context.ChangeTracker.Entries<TenantSetting>()
                     .Where(entry => keys.Contains(entry.Entity.SettingKey)).ToArray())
            entry.State = EntityState.Detached;
        foreach (var (entity, state) in writes)
            context.Entry(entity).State = state;
        await context.SaveChangesAsync(token);
        foreach (var (entity, _) in writes)
            context.Entry(entity).State = EntityState.Detached;
        return new(true, null, notifications.ToImmutable());
    }

    private static bool IsValid(VisitorAccessSettingMutation mutation)
    {
        if (mutation is null || mutation.TenantId == Guid.Empty
            || !VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains(mutation.Key)
            || !Enum.IsDefined(mutation.Kind))
            return false;
        SettingDefinition definition = SettingRegistry.Get(mutation.Key)!;
        if (mutation.TenantId.HasValue && definition.MaxScope < SettingScope.Tenant)
            return false;
        if (mutation.Kind == VisitorAccessSettingMutationKind.Remove)
            return mutation.Value is null && mutation.IsLocked is null;
        if (mutation.Kind == VisitorAccessSettingMutationKind.SetLock)
            return mutation.Value is null && mutation.IsLocked.HasValue && definition.IsLockable;
        if (mutation.Value is null)
            return false;
        try
        {
            using JsonDocument json = JsonDocument.Parse(mutation.Value);
            return definition.ValueType switch
            {
                SettingValueType.Boolean => json.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False,
                SettingValueType.Integer => json.RootElement.ValueKind == JsonValueKind.Number && json.RootElement.TryGetInt32(out _),
                SettingValueType.String => json.RootElement.ValueKind == JsonValueKind.String
                    && (definition.AllowedValues is null || definition.AllowedValues.Contains(json.RootElement.GetString()!)),
                _ => false
            };
        }
        catch (JsonException) { return false; }
    }

    private static VisitorAccessSettingsWriteResult Rejected(string code) => new(false, code, []);
}
