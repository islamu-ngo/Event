namespace Explore.Persistence.Repositories;

using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

public sealed class CoordinatedSettingMutationRepository(ExploreDbContext dbContext)
    : ICoordinatedSettingMutationStore
{
    private const string ActiveTransactionRequiredMessage =
        "Coordinated setting writes require an active transaction.";

    private const string TenantFilterBypassReason =
        "Coordinated publication-policy snapshots enumerate only bounded canonical setting keys across tenants; tenant-specific reads also apply an exact tenant predicate.";

    private static readonly string[] GuardedKeys = PublicationPolicySettingKeys.All.ToArray();

    public async Task<PublicationPolicyMutationSnapshot> ReadTenantSnapshotAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        List<SystemSetting> systemRows = await QuerySystemRows()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        List<TenantSetting> tenantRows = await QueryTenantRows()
            .AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return CreateSnapshot(systemRows, tenantRows);
    }

    public async Task<PublicationPolicyMutationSnapshot> ReadInstanceSnapshotAsync(
        CancellationToken cancellationToken)
    {
        List<SystemSetting> systemRows = await QuerySystemRows()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        List<TenantSetting> tenantRows = await QueryTenantRows()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return CreateSnapshot(systemRows, tenantRows);
    }

    public async Task<CoordinatedSettingMutationWriteResult> WriteTenantAsync(
        Guid tenantId,
        ImmutableArray<PublicationPolicySettingMutation> mutations,
        Guid? actorUserId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        EnsureActiveTransaction();
        ValidateTenantMutations(tenantId, mutations);

        if (mutations.IsEmpty)
            return new CoordinatedSettingMutationWriteResult([]);

        string[] requestedKeys = mutations.Select(mutation => mutation.Key).ToArray();
        List<TenantSetting> existingRows = await QueryTenantRows()
            .Where(row => row.TenantId == tenantId && requestedKeys.Contains(row.SettingKey))
            .ToListAsync(cancellationToken);
        Dictionary<string, TenantSetting> existingByKey = existingRows
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var changes = ImmutableArray.CreateBuilder<CoordinatedSettingValueChange>();

        foreach (PublicationPolicySettingMutation mutation in OrderMutations(mutations))
        {
            existingByKey.TryGetValue(mutation.Key, out TenantSetting? existing);
            if (mutation.Kind == PublicationPolicyMutationKind.Remove)
            {
                if (existing is null)
                    continue;

                dbContext.TenantSettingOverrides.Remove(existing);
                changes.Add(new CoordinatedSettingValueChange(mutation.Key, existing.Value, null));
                continue;
            }

            string newValue = mutation.JsonValue!;
            if (existing is null)
            {
                dbContext.TenantSettingOverrides.Add(new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    SettingKey = mutation.Key,
                    Value = newValue,
                    IsLocked = false,
                    CreatedAt = occurredAtUtc,
                    CreatedBy = actorUserId
                });
                changes.Add(new CoordinatedSettingValueChange(mutation.Key, null, newValue));
                continue;
            }

            if (string.Equals(existing.Value, newValue, StringComparison.Ordinal))
                continue;

            string oldValue = existing.Value;
            existing.Value = newValue;
            existing.UpdatedAt = occurredAtUtc;
            existing.UpdatedBy = actorUserId;
            changes.Add(new CoordinatedSettingValueChange(mutation.Key, oldValue, newValue));
        }

        return await SaveChangesIfNeededAsync(changes, cancellationToken);
    }

    public async Task<CoordinatedSettingMutationWriteResult> WriteInstanceAsync(
        ImmutableArray<PublicationPolicySettingMutation> mutations,
        Guid actorUserId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        EnsureActiveTransaction();
        ValidateInstanceMutations(mutations);

        if (mutations.IsEmpty)
            return new CoordinatedSettingMutationWriteResult([]);

        string[] requestedKeys = mutations.Select(mutation => mutation.Key).ToArray();
        List<SystemSetting> existingRows = await QuerySystemRows()
            .Where(row => requestedKeys.Contains(row.SettingKey))
            .ToListAsync(cancellationToken);
        Dictionary<string, SystemSetting> existingByKey = existingRows
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var changes = ImmutableArray.CreateBuilder<CoordinatedSettingValueChange>();

        foreach (PublicationPolicySettingMutation mutation in OrderMutations(mutations))
        {
            existingByKey.TryGetValue(mutation.Key, out SystemSetting? existing);
            if (mutation.Kind == PublicationPolicyMutationKind.Remove)
            {
                if (existing is null)
                    continue;

                dbContext.SystemSettings.Remove(existing);
                changes.Add(new CoordinatedSettingValueChange(mutation.Key, existing.Value, null));
                continue;
            }

            SettingDefinition definition = SettingRegistry.Get(mutation.Key)!;
            string newValue = mutation.JsonValue!;
            bool newLockState = mutation.IsLocked!.Value;
            string? allowedValues = definition.AllowedValues is null
                ? null
                : JsonSerializer.Serialize(definition.AllowedValues);

            if (existing is null)
            {
                dbContext.SystemSettings.Add(new SystemSetting
                {
                    Id = CanonicalSystemSettingId(mutation.Key),
                    SettingKey = mutation.Key,
                    Value = newValue,
                    ValueType = definition.ValueType,
                    IsLocked = newLockState,
                    AllowedValues = allowedValues,
                    Description = definition.Description,
                    Category = definition.Category,
                    DisplayOrder = CanonicalDisplayOrder(mutation.Key),
                    CreatedAt = occurredAtUtc,
                    CreatedBy = actorUserId
                });
                changes.Add(new CoordinatedSettingValueChange(mutation.Key, null, newValue));
                continue;
            }

            bool hasChange = !string.Equals(existing.Value, newValue, StringComparison.Ordinal)
                || existing.IsLocked != newLockState
                || existing.ValueType != definition.ValueType
                || !string.Equals(existing.AllowedValues, allowedValues, StringComparison.Ordinal)
                || !string.Equals(existing.Description, definition.Description, StringComparison.Ordinal)
                || !string.Equals(existing.Category, definition.Category, StringComparison.Ordinal)
                || existing.DisplayOrder != CanonicalDisplayOrder(mutation.Key);
            if (!hasChange)
                continue;

            string oldValue = existing.Value;
            existing.Value = newValue;
            existing.ValueType = definition.ValueType;
            existing.IsLocked = newLockState;
            existing.AllowedValues = allowedValues;
            existing.Description = definition.Description;
            existing.Category = definition.Category;
            existing.DisplayOrder = CanonicalDisplayOrder(mutation.Key);
            existing.UpdatedAt = occurredAtUtc;
            existing.UpdatedBy = actorUserId;
            changes.Add(new CoordinatedSettingValueChange(mutation.Key, oldValue, newValue));
        }

        return await SaveChangesIfNeededAsync(changes, cancellationToken);
    }

    private IQueryable<SystemSetting> QuerySystemRows() =>
        dbContext.SystemSettings
            .Where(row => GuardedKeys.Contains(row.SettingKey));

    private IQueryable<TenantSetting> QueryTenantRows() =>
        dbContext.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReason)
            .Where(row => GuardedKeys.Contains(row.SettingKey));

    private static PublicationPolicyMutationSnapshot CreateSnapshot(
        IEnumerable<SystemSetting> systemRows,
        IEnumerable<TenantSetting> tenantRows)
    {
        ImmutableArray<PublicationPolicySystemValueSnapshot> systemValues = systemRows
            .OrderBy(row => CanonicalOrder(row.SettingKey))
            .Select(row => new PublicationPolicySystemValueSnapshot(
                row.SettingKey,
                row.Value,
                row.IsLocked))
            .ToImmutableArray();
        ImmutableArray<PublicationPolicyTenantValueSnapshot> tenantValues = tenantRows
            .OrderBy(row => row.TenantId)
            .ThenBy(row => CanonicalOrder(row.SettingKey))
            .Select(row => new PublicationPolicyTenantValueSnapshot(
                row.TenantId,
                row.SettingKey,
                row.Value))
            .ToImmutableArray();

        return new PublicationPolicyMutationSnapshot(systemValues, tenantValues);
    }

    private void EnsureActiveTransaction()
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException(ActiveTransactionRequiredMessage);
    }

    private static void ValidateTenantMutations(
        Guid tenantId,
        ImmutableArray<PublicationPolicySettingMutation> mutations)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A requested tenant is required.", nameof(tenantId));
        if (mutations.IsDefault)
            throw new ArgumentException("The mutation batch must be initialized.", nameof(mutations));

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (PublicationPolicySettingMutation? mutation in mutations)
        {
            if (mutation is null)
                throw new ArgumentException("The mutation batch cannot contain null elements.", nameof(mutations));

            ValidateCommonMutation(mutation, keys, mutations);
            if (mutation.TenantId != tenantId)
                throw new ArgumentException("Every tenant mutation must target the requested tenant.", nameof(mutations));

            bool validShape = mutation.Kind switch
            {
                PublicationPolicyMutationKind.Set =>
                    mutation.IsLocked is null && TryParseBoolean(mutation.JsonValue),
                PublicationPolicyMutationKind.Remove =>
                    mutation.JsonValue is null && mutation.IsLocked is null,
                _ => false
            };
            if (!validShape)
                throw new ArgumentException("The tenant mutation shape is invalid.", nameof(mutations));
        }
    }

    private static void ValidateInstanceMutations(
        ImmutableArray<PublicationPolicySettingMutation> mutations)
    {
        if (mutations.IsDefault)
            throw new ArgumentException("The mutation batch must be initialized.", nameof(mutations));

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (PublicationPolicySettingMutation? mutation in mutations)
        {
            if (mutation is null)
                throw new ArgumentException("The mutation batch cannot contain null elements.", nameof(mutations));

            ValidateCommonMutation(mutation, keys, mutations);
            if (mutation.TenantId is not null)
                throw new ArgumentException("Instance mutations cannot target a tenant.", nameof(mutations));

            bool validShape = mutation.Kind switch
            {
                PublicationPolicyMutationKind.Set =>
                    mutation.IsLocked is not null && TryParseBoolean(mutation.JsonValue),
                PublicationPolicyMutationKind.Remove =>
                    mutation.JsonValue is null && mutation.IsLocked is null,
                _ => false
            };
            if (!validShape)
                throw new ArgumentException("The instance mutation shape is invalid.", nameof(mutations));
        }
    }

    private static void ValidateCommonMutation(
        PublicationPolicySettingMutation mutation,
        HashSet<string> keys,
        ImmutableArray<PublicationPolicySettingMutation> mutations)
    {
        if (string.IsNullOrWhiteSpace(mutation.Key)
            || !GuardedKeys.Contains(mutation.Key, StringComparer.Ordinal))
        {
            throw new ArgumentException("Every mutation must use a guarded setting key.", nameof(mutations));
        }

        if (!keys.Add(mutation.Key))
            throw new ArgumentException("A mutation batch cannot contain duplicate keys.", nameof(mutations));

        if (!Enum.IsDefined(mutation.Kind))
            throw new ArgumentException("The mutation kind is invalid.", nameof(mutations));
    }

    private static bool TryParseBoolean(string? jsonValue)
    {
        if (jsonValue is null)
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(jsonValue);
            return document.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<CoordinatedSettingMutationWriteResult> SaveChangesIfNeededAsync(
        ImmutableArray<CoordinatedSettingValueChange>.Builder changes,
        CancellationToken cancellationToken)
    {
        ImmutableArray<CoordinatedSettingValueChange> result = changes.ToImmutable();
        if (!result.IsEmpty)
            await dbContext.SaveChangesAsync(cancellationToken);

        return new CoordinatedSettingMutationWriteResult(result);
    }

    private static IEnumerable<PublicationPolicySettingMutation> OrderMutations(
        ImmutableArray<PublicationPolicySettingMutation> mutations) =>
        mutations.OrderBy(mutation => CanonicalOrder(mutation.Key));

    private static int CanonicalOrder(string key) => Array.IndexOf(GuardedKeys, key);

    private static Guid CanonicalSystemSettingId(string key) => CanonicalOrder(key) switch
    {
        0 => SeedIds.SystemSettingEventReportingIntakeEnabledId,
        1 => SeedIds.SystemSettingRequireApprovalId,
        2 => SeedIds.SystemSettingUserSubmissionEnabledId,
        3 => SeedIds.SystemSettingOrgSubmissionEnabledId,
        4 => SeedIds.SystemSettingGroupSubmissionEnabledId,
        _ => throw new ArgumentException("The setting key is not guarded.", nameof(key))
    };

    private static int CanonicalDisplayOrder(string key) => CanonicalOrder(key) + 1;
}
