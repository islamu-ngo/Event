
using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.Settings;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using static Explore.Persistence.Services.EmailDeliverySettingsMutationOwner;

namespace Explore.Persistence.Services;

public sealed class EmailDeliverySettingsWriter(
    ExploreDbContext context,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    IEmailDeliveryDisableImpactReader impactReader,
    IEmailDeliveryDisableTokenService tokenService,
    IServiceProvider selectedServices,
    IHumanActionCommitFinalizer finalizer) : IEmailDeliverySettingsWriter, IMixedEmailDeliverySettingsWriter
{
    private readonly EmailDeliverySettingsMutationOwner owner = new(context, mutationLock, unitOfWork);
    private IHumanEmailMutationPreparation Admission => HumanEmailDataOwner.Require(context, selectedServices);
    internal async Task<EmailDeliverySettingsWriteResult> ApplyGovernanceAsync(
        TypedSettingsOriginalStore governance, CancellationToken token)
    {
        await governance.RequireGovernanceEmailAsync(this, token);
        var mutations = governance.GovernanceEmailMutations;
        if (mutations.Length != 1 || mutations[0].Key != GovernanceSettingKeys.TenantDelegation.LockSmtp
            || mutations[0].TenantId is not null || !IsValid(mutations[0]))
            throw new UnauthorizedAccessException("The exact original SMTP delegation slice is required.");
        var prepared = await owner.PrepareAsync(mutations, governance.GovernanceActor, token, governance: governance);
        if (prepared.Before is { } before && prepared.After is { } after
            && (before.Instance.Enabled && !after.Instance.Enabled
                || before.Tenants.Any(pair => pair.Value.Enabled && !after.Tenants[pair.Key].Enabled)))
            return Result(EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation);
        await governance.RequireGovernanceEmailAsync(this, token);
        var result = await owner.PersistAsync(prepared, governance.GovernanceActor, token, governance: governance);
        if (result.IsAccepted()) await governance.CompleteGovernanceEmailAsync(this, token);
        return result;
    }

    internal async Task<EmailDeliverySettingsWriteResult> ApplyPlanAsync(
        TypedSettingsOriginalStore plan, CancellationToken token)
    {
        if (!ReferenceEquals(plan, TypedSettingsOriginalStore.CurrentPlan(context)))
            throw new UnauthorizedAccessException("The original selected plan owner is required.");
        await plan.RequirePlanEmailAsync(this, token);
        var mutations = plan.PlanEmailMutations;
        if (mutations.IsEmpty || mutations.Any(mutation => !IsValid(mutation)))
            return Result(EmailDeliverySettingsWriteStatus.InvalidMutation);
        var prepared = await owner.PrepareAsync(mutations, plan.PlanActor, token, plan: plan);
        if (prepared.Before is { } before && prepared.After is { } after
            && (before.Instance.Enabled && !after.Instance.Enabled
                || before.Tenants.Any(pair => pair.Value.Enabled && !after.Tenants[pair.Key].Enabled)))
            return Result(EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation);
        await plan.RequirePlanEmailAsync(this, token);
        var result = await owner.PersistAsync(prepared, plan.PlanActor, token, plan: plan);
        if (result.IsAccepted()) await plan.CompletePlanEmailAsync(this, token);
        return result;
    }
    public async Task<EmailDeliverySettingsWriteResult> ApplyMixedAsync(
        MixedSettingsBatchMutation mutation, CancellationToken cancellationToken = default)
    {
        var mixed = MixedSettingsBatchStore.RequireOriginal(context, selectedServices, mutation);
        using var slice = mixed.EnterEmail(this);
        var effects = mutation.Accepted.Where(pair => EmailDeliverySettingKeys.Contains(pair.Key))
            .Select(pair => new EmailDeliverySettingMutation(mutation.TenantId, pair.Key,
                EmailDeliverySettingMutationKind.SetValue, pair.Value)).ToImmutableArray();
        if (effects.IsEmpty || effects.Any(effect => !IsValid(effect)))
            throw new UnauthorizedAccessException("The original complete Email slice is required.");
        var prepared = await owner.PrepareAsync(effects, mutation.Actor, cancellationToken, mixed);
        if (prepared.Before is { } before && prepared.After is { } after
            && (before.Instance.Enabled && !after.Instance.Enabled
                || before.Tenants.Any(pair => pair.Value.Enabled && !after.Tenants[pair.Key].Enabled)))
            return Result(EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation);
        var result = await owner.PersistAsync(prepared, mutation.Actor, cancellationToken, mixed);
        if (result.IsAccepted())
            await mixed.CompleteEmailAsync(this, cancellationToken);
        return result;
    }

    public Task<EmailDeliverySettingsWriteResult> ApplyAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(mutations, actorUserId, instanceTenantAction: false, cancellationToken);

    public Task<EmailDeliverySettingsWriteResult> ApplyInstanceTenantAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(mutations, actorUserId, instanceTenantAction: true, cancellationToken);

    private async Task<EmailDeliverySettingsWriteResult> ApplyCoreAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        bool instanceTenantAction, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || mutations.IsDefault || mutations.Any(mutation => !IsValid(mutation))
            || mutations.Select(mutation => (mutation.TenantId, mutation.Key)).Distinct().Count() != mutations.Length)
            return Result(EmailDeliverySettingsWriteStatus.InvalidMutation);
        return await Admission.ExecuteApplyAsync(mutations, actorUserId, instanceTenantAction, async token =>
        {
            Guid actor = await Admission.RequireApplyAsync(mutations, actorUserId, instanceTenantAction, token);
            if (mutations.IsEmpty)
                return Result(EmailDeliverySettingsWriteStatus.NoChange);
            var prepared = await owner.PrepareAsync(mutations, actor, token);
            if (prepared.Before is { } before && prepared.After is { } after
                && (before.Instance.Enabled && !after.Instance.Enabled
                    || before.Tenants.Any(pair => pair.Value.Enabled && !after.Tenants[pair.Key].Enabled)))
                return Result(EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation);
            await Admission.RequireApplyAsync(mutations, actorUserId, instanceTenantAction, token);
            return await owner.PersistAsync(prepared, actor, token);
        }, cancellationToken);
    }

    public async Task<EmailDeliverySettingsWriteResult> DisableAsync(
        EmailDeliveryDisableConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        return await Admission.ExecuteDisableAsync(confirmation, async token =>
        {
            Guid actor = await Admission.RequireDisableAsync(confirmation, token);
            if (confirmation.ActorUserId == Guid.Empty || confirmation.TenantId == Guid.Empty
                || !string.Equals(confirmation.Acknowledgement, EmailDeliveryDisableConfirmation.RequiredAcknowledgement,
                    StringComparison.Ordinal))
                return Result(EmailDeliverySettingsWriteStatus.InvalidConfirmation);
            var snapshot = await impactReader.ReadAsync(confirmation.TenantId, token);
            if (snapshot is null)
                return Result(EmailDeliverySettingsWriteStatus.NotFound);
            if (snapshot.IsLocked)
                return Result(EmailDeliverySettingsWriteStatus.Locked);
            if (!snapshot.CanDisable || snapshot.Revision != confirmation.ExpectedRevision
                || !tokenService.Matches(confirmation.ConfirmationToken, confirmation.ActorUserId, snapshot))
                return Result(EmailDeliverySettingsWriteStatus.ConfirmationConflict);
            // Retain the preview's original snapshot. The successful disable changes policy
            // revision itself; final time admission must not compare against that own write.
            finalizer.Register(_ => Task.CompletedTask, _ =>
            {
                if (!tokenService.Matches(confirmation.ConfirmationToken, actor, snapshot))
                    throw new UnauthorizedAccessException("The original email disable confirmation has expired.");
            });
            var prepared = await owner.PrepareAsync(
                [new(TenantId: confirmation.TenantId, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "false")],
                confirmation.ActorUserId, token);
            await Admission.RequireDisableAsync(confirmation, token);
            return await owner.PersistAsync(prepared, confirmation.ActorUserId, token);
        }, cancellationToken);
    }
}

/// <summary>
/// The public data object must use the same scoped issuer and physical context as its source
/// proof. Owner labels only select the boundary; its acquired private effect remains mandatory.
/// </summary>
internal static class HumanEmailDataOwner
{
    internal static IHumanEmailMutationPreparation Require(ExploreDbContext dataOwner, IServiceProvider selectedServices)
    {
        if (!ReferenceEquals(dataOwner, selectedServices.GetRequiredService<ExploreDbContext>()))
            throw new UnauthorizedAccessException("Email data must use the original selected context.");
        var lease = selectedServices.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.RequireOriginalOwner(dataOwner);
        if (lease.PolicyPurpose != PolicyMutationPurpose.EmailDelivery
            || lease.HumanScope is not { } human
            || human.Purpose is not (HumanAdministratorCommitPurpose.EmailInstance or HumanAdministratorCommitPurpose.EmailTenant)
            || !ReferenceEquals(dataOwner.HumanCommitAuditSubject, human.Subject)
            || dataOwner.HumanCommitContextLease != dataOwner.ContextId || dataOwner.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("Email data requires the original dedicated human owner.");
        return selectedServices.GetRequiredService<IHumanEmailMutationPreparation>();
    }
}

/// <summary>One SMTP lease/transaction/revision engine shared by distinct human and internal entries.</summary>
internal sealed class EmailDeliverySettingsMutationOwner(
    ExploreDbContext context, ISettingMutationLock mutationLock, IUnitOfWork unitOfWork)
{
    internal Task<EmailDeliverySettingsWriteResult> ExecuteHumanAsync(
        Func<CancellationToken, Task<EmailDeliverySettingsWriteResult>> operation, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            return mutationLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All],
                token => ((ISerializableAttemptOwner)unitOfWork).ExecuteAsync(operation, token), cancellationToken);
        if (context.Database.CurrentTransaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("SMTP policy writes require a serializable transaction.");
        return mutationLock.ExecuteManyAsync(EmailDeliverySettingKeys.All, operation, cancellationToken);
    }

    internal Task<EmailDeliverySettingsWriteResult> ExecuteAsync(
        Func<CancellationToken, Task<EmailDeliverySettingsWriteResult>> operation, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            return mutationLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All],
                token => unitOfWork.ExecuteSerializableAsync(operation, token), cancellationToken);
        if (context.Database.CurrentTransaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("SMTP policy writes require a serializable transaction.");
        return mutationLock.ExecuteManyAsync(EmailDeliverySettingKeys.All, operation, cancellationToken);
    }

    internal async Task<PreparedWrite> PrepareAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId, CancellationToken cancellationToken,
        MixedSettingsBatchStore? mixed = null, TypedSettingsOriginalStore? plan = null,
        TypedSettingsOriginalStore? governance = null)
    {
        if (context.ChangeTracker.Entries<SystemSetting>().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && EmailDeliverySettingKeys.Contains(entry.Entity.SettingKey))
            || context.ChangeTracker.Entries<TenantSetting>().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && EmailDeliverySettingKeys.Contains(entry.Entity.SettingKey)))
            return Preparation(EmailDeliverySettingsWriteStatus.InvalidMutation);

        var systemSettings = governance is not null ? governance.GovernanceEmailSystems()
            : plan is not null ? plan.PlanEmailSystems() : mixed is null
            ? await EmailDeliveryPolicyReader.ReadSystemSettingsAsync(context, cancellationToken)
            : mixed.EmailSystemSettings();
        var tenantSettings = governance is not null ? governance.GovernanceEmailTenants()
            : plan is not null ? plan.PlanEmailTenants() : mixed is null
            ? await EmailDeliveryPolicyReader.ReadTenantSettingsAsync(
                context, tenantId: context.BindTenantLease(), cancellationToken)
            : mixed.EmailTenantSettings();
        if (mutations.Any(mutation => mutation.TenantId is { } tenantId && !tenantSettings.ContainsKey(tenantId)))
            return Preparation(EmailDeliverySettingsWriteStatus.NotFound);
        var before = EmailDeliveryPolicyReader.EvaluateAll(systemSettings, tenantSettings);
        DateTime now = governance?.GovernanceOccurredAt ?? plan?.PlanOccurredAt ?? mixed?.OccurredAt ?? DateTime.UtcNow;
        var changes = ImmutableArray.CreateBuilder<EmailDeliverySettingChange>();
        var writes = new List<(object Entity, EntityState State)>();

        foreach (var mutation in mutations.OrderBy(mutation => mutation.TenantId.HasValue))
        {
            SettingDefinition definition = SettingRegistry.Get(mutation.Key)!;
            if (mutation.TenantId is { } tenantId)
            {
                var settings = tenantSettings[tenantId];
                if (HierarchicalSettingMerge.Resolve(mutation.Key, systemSettings, settings)?.Source
                    == Explore.Application.Contracts.Infrastructure.SettingSource.SystemLocked)
                    return Preparation(EmailDeliverySettingsWriteStatus.Locked);
                settings.TryGetValue(mutation.Key, out var setting);
                if (setting is null && mutation.Kind is EmailDeliverySettingMutationKind.Remove or EmailDeliverySettingMutationKind.SetLock)
                    continue;
                string? previousValue = setting?.Value;
                bool previousLock = setting?.IsLocked ?? false;
                bool created = setting is null;
                setting ??= new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    SettingKey = mutation.Key,
                    Value = definition.DefaultValue,
                    CreatedAt = now,
                    CreatedBy = actorUserId
                };
                if (mutation.Kind == EmailDeliverySettingMutationKind.Remove)
                    settings.Remove(mutation.Key);
                else
                {
                    setting.Value = mutation.Value ?? setting.Value;
                    setting.IsLocked = mutation.IsLocked ?? setting.IsLocked;
                    settings[mutation.Key] = setting;
                }
                if (!created && mutation.Kind != EmailDeliverySettingMutationKind.Remove
                    && previousValue == setting.Value && previousLock == setting.IsLocked)
                    continue;
                setting.UpdatedAt = now;
                setting.UpdatedBy = actorUserId;
                writes.Add((setting, mutation.Kind == EmailDeliverySettingMutationKind.Remove
                    ? EntityState.Deleted : created ? EntityState.Added : EntityState.Modified));
                changes.Add(new(TenantId: tenantId, Key: mutation.Key, PreviousValue: previousValue,
                    Value: mutation.Kind == EmailDeliverySettingMutationKind.Remove ? null : setting.Value,
                    IsLocked: mutation.Kind != EmailDeliverySettingMutationKind.Remove && setting.IsLocked, ChangedAtUtc: now));
            }
            else
            {
                systemSettings.TryGetValue(mutation.Key, out var setting);
                if (setting is null && mutation.Kind == EmailDeliverySettingMutationKind.Remove)
                    continue;
                string? previousValue = setting?.Value;
                bool previousLock = setting?.IsLocked ?? false;
                bool created = setting is null;
                setting ??= new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = mutation.Key,
                    Value = definition.DefaultValue,
                    ValueType = definition.ValueType,
                    Category = definition.Category,
                    Description = definition.Description,
                    AllowedValues = definition.AllowedValues is null ? null : JsonSerializer.Serialize(definition.AllowedValues),
                    CreatedAt = now,
                    CreatedBy = actorUserId
                };
                if (mutation.Kind == EmailDeliverySettingMutationKind.Remove)
                    systemSettings.Remove(mutation.Key);
                else
                {
                    setting.Value = mutation.Value ?? setting.Value;
                    setting.IsLocked = mutation.IsLocked ?? setting.IsLocked;
                    systemSettings[mutation.Key] = setting;
                }
                if (!created && mutation.Kind != EmailDeliverySettingMutationKind.Remove
                    && previousValue == setting.Value && previousLock == setting.IsLocked)
                    continue;
                setting.UpdatedAt = now;
                setting.UpdatedBy = actorUserId;
                writes.Add((setting, mutation.Kind == EmailDeliverySettingMutationKind.Remove
                    ? EntityState.Deleted : created ? EntityState.Added : EntityState.Modified));
                changes.Add(new(TenantId: null, Key: mutation.Key, PreviousValue: previousValue,
                    Value: mutation.Kind == EmailDeliverySettingMutationKind.Remove ? null : setting.Value,
                    IsLocked: mutation.Kind != EmailDeliverySettingMutationKind.Remove && setting.IsLocked, ChangedAtUtc: now));
            }
        }
        if (changes.Count == 0)
            return Preparation(EmailDeliverySettingsWriteStatus.NoChange);

        return new(Status: EmailDeliverySettingsWriteStatus.Applied, Before: before,
            After: EmailDeliveryPolicyReader.EvaluateAll(systemSettings, tenantSettings),
            Changes: changes.ToImmutable(), Writes: writes.ToImmutableArray());
    }

    internal async Task<EmailDeliverySettingsWriteResult> PersistAsync(
        PreparedWrite prepared, Guid? actorUserId, CancellationToken cancellationToken,
        MixedSettingsBatchStore? mixed = null, TypedSettingsOriginalStore? plan = null,
        TypedSettingsOriginalStore? governance = null)
    {
        if (prepared.Status != EmailDeliverySettingsWriteStatus.Applied)
            return Result(prepared.Status);

        foreach (var entry in context.ChangeTracker.Entries<SystemSetting>()
                     .Where(entry => EmailDeliverySettingKeys.Contains(entry.Entity.SettingKey)).ToArray())
            entry.State = EntityState.Detached;
        foreach (var entry in context.ChangeTracker.Entries<TenantSetting>()
                     .Where(entry => EmailDeliverySettingKeys.Contains(entry.Entity.SettingKey)).ToArray())
            entry.State = EntityState.Detached;
        if (governance is not null)
            await governance.WriteGovernanceEmailRowsAsync(prepared.Writes, cancellationToken);
        else if (plan is not null)
            await plan.WritePlanSpecializedAsync(prepared.Writes, true, cancellationToken);
        else if (mixed is null)
        {
            foreach (var (entity, state) in prepared.Writes)
                context.Entry(entity).State = state;
            await context.SaveChangesAsync(cancellationToken);
        }
        else
            await mixed.WriteEmailRowsAsync(prepared.Writes, cancellationToken);
        if (prepared.Changes.Any(change => change.TenantId is null))
            await EmailDeliveryPolicyRevisionTracker.RecordInstanceAsync(context, prepared.Before!, actorUserId, cancellationToken, mixed, governance);
        // Transaction baselines deduplicate scopes already reconciled by the instance write.
        foreach (Guid tenantId in prepared.Changes.Where(change => change.TenantId.HasValue)
                     .Select(change => change.TenantId!.Value).Distinct())
            await EmailDeliveryPolicyRevisionTracker.RecordTenantAsync(context, tenantId, prepared.Before!.Tenants[tenantId],
                actorUserId, cancellationToken, mixed, plan);
        foreach (var (entity, _) in prepared.Writes)
            context.Entry(entity).State = EntityState.Detached;
        return new(Status: EmailDeliverySettingsWriteStatus.Applied, Changes: prepared.Changes);
    }

    internal static bool IsValid(EmailDeliverySettingMutation mutation)
    {
        if (mutation is null || mutation.TenantId == Guid.Empty || !EmailDeliverySettingKeys.All.Contains(mutation.Key)
            || !Enum.IsDefined(mutation.Kind)
            || mutation.TenantId.HasValue && mutation.Key == GovernanceSettingKeys.TenantDelegation.LockSmtp)
            return false;
        if (mutation.Kind == EmailDeliverySettingMutationKind.Remove)
            return mutation.Value is null && mutation.IsLocked is null;
        if (mutation.Kind == EmailDeliverySettingMutationKind.SetLock)
            return mutation.Value is null && mutation.IsLocked.HasValue;
        if (mutation.Value is null)
            return false;
        try
        {
            using var value = JsonDocument.Parse(mutation.Value);
            return SettingRegistry.Get(mutation.Key)!.ValueType switch
            {
                SettingValueType.Boolean => value.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False,
                SettingValueType.Integer => value.RootElement.ValueKind == JsonValueKind.Number && value.RootElement.TryGetInt32(out _),
                SettingValueType.String => value.RootElement.ValueKind == JsonValueKind.String,
                _ => false
            };
        }
        catch (JsonException) { return false; }
    }

    internal static EmailDeliverySettingsWriteResult Result(EmailDeliverySettingsWriteStatus status) =>
        new(Status: status, Changes: []);

    private static PreparedWrite Preparation(EmailDeliverySettingsWriteStatus status) =>
        new(Status: status, Before: null, After: null, Changes: [], Writes: []);

    internal sealed record PreparedWrite(
        EmailDeliverySettingsWriteStatus Status,
        EmailDeliveryPolicySet? Before,
        EmailDeliveryPolicySet? After,
        ImmutableArray<EmailDeliverySettingChange> Changes,
        ImmutableArray<(object Entity, EntityState State)> Writes);
}
