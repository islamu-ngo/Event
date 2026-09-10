
using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

public sealed class EmailDeliverySettingsWriter(
    ExploreDbContext context,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    IEmailDeliveryDisableImpactReader impactReader,
    IEmailDeliveryDisableTokenService tokenService) : IEmailDeliverySettingsWriter
{
    public Task<EmailDeliverySettingsWriteResult> ApplyAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty || mutations.IsDefault || mutations.Any(mutation => !IsValid(mutation))
            || mutations.Select(mutation => (mutation.TenantId, mutation.Key)).Distinct().Count() != mutations.Length)
            return Task.FromResult(Result(EmailDeliverySettingsWriteStatus.InvalidMutation));
        if (mutations.IsEmpty)
            return Task.FromResult(Result(EmailDeliverySettingsWriteStatus.NoChange));
        return ExecuteAsync(async token =>
        {
            var prepared = await PrepareAsync(mutations, actorUserId, token);
            if (prepared.Before is { } before && prepared.After is { } after
                && (before.Instance.Enabled && !after.Instance.Enabled
                    || before.Tenants.Any(pair => pair.Value.Enabled && !after.Tenants[pair.Key].Enabled)))
                return Result(EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation);
            return await PersistAsync(prepared, actorUserId, token);
        }, cancellationToken);
    }

    public Task<EmailDeliverySettingsWriteResult> DisableAsync(
        EmailDeliveryDisableConfirmation confirmation, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async token =>
        {
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
            var prepared = await PrepareAsync(
                [new(TenantId: confirmation.TenantId, Key: GovernanceSettingKeys.Email.DeliveryEnabled,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: "false")],
                confirmation.ActorUserId, token);
            return await PersistAsync(prepared, confirmation.ActorUserId, token);
        }, cancellationToken);

    private Task<EmailDeliverySettingsWriteResult> ExecuteAsync(
        Func<CancellationToken, Task<EmailDeliverySettingsWriteResult>> operation, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            return mutationLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All],
                token => unitOfWork.ExecuteSerializableAsync(operation, token), cancellationToken);
        if (context.Database.CurrentTransaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("SMTP policy writes require a serializable transaction.");
        return mutationLock.ExecuteManyAsync(EmailDeliverySettingKeys.All, operation, cancellationToken);
    }

    private async Task<PreparedWrite> PrepareAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (context.ChangeTracker.Entries<SystemSetting>().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && EmailDeliverySettingKeys.Contains(entry.Entity.SettingKey))
            || context.ChangeTracker.Entries<TenantSetting>().Any(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && EmailDeliverySettingKeys.Contains(entry.Entity.SettingKey)))
            return Preparation(EmailDeliverySettingsWriteStatus.InvalidMutation);

        var systemSettings = await EmailDeliveryPolicyReader.ReadSystemSettingsAsync(context, cancellationToken);
        var tenantSettings = await EmailDeliveryPolicyReader.ReadTenantSettingsAsync(context, tenantId: null, cancellationToken);
        if (mutations.Any(mutation => mutation.TenantId is { } tenantId && !tenantSettings.ContainsKey(tenantId)))
            return Preparation(EmailDeliverySettingsWriteStatus.NotFound);
        var before = EmailDeliveryPolicyReader.EvaluateAll(systemSettings, tenantSettings);
        DateTime now = DateTime.UtcNow;
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

    private async Task<EmailDeliverySettingsWriteResult> PersistAsync(
        PreparedWrite prepared, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (prepared.Status != EmailDeliverySettingsWriteStatus.Applied)
            return Result(prepared.Status);

        foreach (var entry in context.ChangeTracker.Entries<SystemSetting>()
                     .Where(entry => EmailDeliverySettingKeys.Contains(entry.Entity.SettingKey)).ToArray())
            entry.State = EntityState.Detached;
        foreach (var entry in context.ChangeTracker.Entries<TenantSetting>()
                     .Where(entry => EmailDeliverySettingKeys.Contains(entry.Entity.SettingKey)).ToArray())
            entry.State = EntityState.Detached;
        foreach (var (entity, state) in prepared.Writes)
            context.Entry(entity).State = state;
        await context.SaveChangesAsync(cancellationToken);
        if (prepared.Changes.Any(change => change.TenantId is null))
            await EmailDeliveryPolicyRevisionTracker.RecordInstanceAsync(context, prepared.Before!, actorUserId, cancellationToken);
        // Transaction baselines deduplicate scopes already reconciled by the instance write.
        foreach (Guid tenantId in prepared.Changes.Where(change => change.TenantId.HasValue)
                     .Select(change => change.TenantId!.Value).Distinct())
            await EmailDeliveryPolicyRevisionTracker.RecordTenantAsync(context, tenantId, prepared.Before!.Tenants[tenantId],
                actorUserId, cancellationToken);
        foreach (var (entity, _) in prepared.Writes)
            context.Entry(entity).State = EntityState.Detached;
        return new(Status: EmailDeliverySettingsWriteStatus.Applied, Changes: prepared.Changes);
    }

    private static bool IsValid(EmailDeliverySettingMutation mutation)
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

    private static EmailDeliverySettingsWriteResult Result(EmailDeliverySettingsWriteStatus status) =>
        new(Status: status, Changes: []);

    private static PreparedWrite Preparation(EmailDeliverySettingsWriteStatus status) =>
        new(Status: status, Before: null, After: null, Changes: [], Writes: []);

    private sealed record PreparedWrite(
        EmailDeliverySettingsWriteStatus Status,
        EmailDeliveryPolicySet? Before,
        EmailDeliveryPolicySet? After,
        ImmutableArray<EmailDeliverySettingChange> Changes,
        ImmutableArray<(object Entity, EntityState State)> Writes);
}
