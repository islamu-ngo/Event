using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

internal sealed record OrdinarySystemSettingImage(Guid Id, string Key, string Value, int Type, bool Locked,
    string? AllowedValues, string? Description, string? Category, int DisplayOrder,
    DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy)
{
    internal static OrdinarySystemSettingImage From(SystemSetting row) => new(row.Id, row.SettingKey, row.Value,
        row.SettingValueTypeId, row.IsLocked, row.AllowedValues, row.Description, row.Category, row.DisplayOrder,
        row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);

    internal SystemSetting Entity() => new()
    {
        Id = Id,
        SettingKey = Key,
        Value = Value,
        SettingValueTypeId = Type,
        IsLocked = Locked,
        AllowedValues = AllowedValues,
        Description = Description,
        Category = Category,
        DisplayOrder = DisplayOrder,
        CreatedAt = CreatedAt,
        CreatedBy = CreatedBy,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy
    };
}

internal sealed record OrdinaryTenantSettingImage(Guid Id, Guid TenantId, string Key, string Value, bool Locked,
    DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy)
{
    internal static OrdinaryTenantSettingImage From(TenantSetting row) => new(row.Id, row.TenantId,
        row.SettingKey, row.Value, row.IsLocked, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);

    internal TenantSetting Entity() => new()
    {
        Id = Id,
        TenantId = TenantId,
        Tenant = null!,
        SettingKey = Key,
        Value = Value,
        IsLocked = Locked,
        CreatedAt = CreatedAt,
        CreatedBy = CreatedBy,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy
    };
}

internal sealed record OrdinaryUserPreferenceImage(Guid Id, Guid TenantId, Guid UserId, string Key, string Value,
    DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy)
{
    internal static OrdinaryUserPreferenceImage From(UserPreference row) => new(row.Id, row.TenantId,
        row.UserId, row.SettingKey, row.Value, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);

    internal UserPreference Entity() => new()
    {
        Id = Id,
        TenantId = TenantId,
        Tenant = null!,
        UserId = UserId,
        SettingKey = Key,
        Value = Value,
        CreatedAt = CreatedAt,
        CreatedBy = CreatedBy,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy
    };
}

internal sealed record OrdinarySettingsDataImage(
    ImmutableDictionary<string, OrdinarySystemSettingImage?> System,
    ImmutableDictionary<string, OrdinaryTenantSettingImage?> Tenant,
    ImmutableDictionary<string, OrdinaryUserPreferenceImage?> User)
{
    internal static string[] Keys(OrdinarySettingsMutation mutation) =>
        mutation.Effects.Where(effect => !AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key)).Select(effect => effect.Key)
            .Concat(mutation.RequiresTenantVerificationCeiling
                ? [GovernanceSettingKeys.Organizations.TenantCanOmitVerification] : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static async Task<OrdinarySettingsDataImage> ReadAsync(ExploreDbContext context, string[] keys,
        Guid? tenantId, Guid? userId, CancellationToken token)
    {
        // Retain the selected Settings corpus, not just scalar candidates. A callback
        // cannot hide another row's creation, deletion or metadata change beside its
        // admitted value. Tenant and preference images never cross the selected target.
        var system = await context.SystemSettings.AsNoTracking().ToListAsync(token);
        var tenant = tenantId.HasValue
            ? await context.Set<TenantSetting>().AsNoTracking()
                .Where(row => row.TenantId == tenantId).ToListAsync(token) : [];
        var user = userId.HasValue
            ? await context.UserPreferences.AsNoTracking().Where(row => row.TenantId == tenantId
                && row.UserId == userId).ToListAsync(token) : [];
        return new(
            keys.Concat(system.Select(row => row.SettingKey)).Distinct(StringComparer.Ordinal)
                .ToImmutableDictionary(key => key, key => system.SingleOrDefault(row => row.SettingKey == key) is { } row
                ? OrdinarySystemSettingImage.From(row) : null, StringComparer.Ordinal),
            keys.Concat(tenant.Select(row => row.SettingKey)).Distinct(StringComparer.Ordinal)
                .ToImmutableDictionary(key => key, key => tenant.SingleOrDefault(row => row.SettingKey == key) is { } row
                ? OrdinaryTenantSettingImage.From(row) : null, StringComparer.Ordinal),
            keys.Concat(user.Select(row => row.SettingKey)).Distinct(StringComparer.Ordinal)
                .ToImmutableDictionary(key => key, key => user.SingleOrDefault(row => row.SettingKey == key) is { } row
                ? OrdinaryUserPreferenceImage.From(row) : null, StringComparer.Ordinal));
    }

    internal bool Same(OrdinarySettingsDataImage other) =>
        System.Count == other.System.Count && Tenant.Count == other.Tenant.Count && User.Count == other.User.Count
        && System.All(pair => other.System.TryGetValue(pair.Key, out var row) && row == pair.Value)
        && Tenant.All(pair => other.Tenant.TryGetValue(pair.Key, out var row) && row == pair.Value)
        && User.All(pair => other.User.TryGetValue(pair.Key, out var row) && row == pair.Value);
}
