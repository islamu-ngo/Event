using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Security.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Explore.Persistence.Security;

/// <summary>Classifies tracked changes to the fixed human-source projection, not business authorization.</summary>
internal static class HumanSecuritySourceMutationClassifier
{
    private static readonly ImmutableDictionary<Type, ImmutableHashSet<string>> Sources =
        HumanPrincipalSourceSql.Sources.Append((typeof(Actor), new[]
            { nameof(Actor.Id), nameof(Actor.UserId), nameof(Actor.ActorTypeId), nameof(Actor.IsDeleted), nameof(Actor.IsSuspended) }))
            .ToImmutableDictionary(source => source.Item1,
                source => source.Item2.ToImmutableHashSet(StringComparer.Ordinal));
    private static readonly ImmutableHashSet<string> SystemRouteFields = new[]
    {
        nameof(SystemSetting.SettingKey), nameof(SystemSetting.Value), nameof(SystemSetting.SettingValueTypeId),
        nameof(SystemSetting.IsLocked), nameof(SystemSetting.Description), nameof(SystemSetting.Category), nameof(SystemSetting.AllowedValues)
    }.ToImmutableHashSet(StringComparer.Ordinal);
    private static readonly ImmutableHashSet<string> TenantRouteFields = new[]
    {
        nameof(TenantSetting.TenantId), nameof(TenantSetting.SettingKey), nameof(TenantSetting.Value), nameof(TenantSetting.IsLocked)
    }.ToImmutableHashSet(StringComparer.Ordinal);

    internal static bool RequiresGate(EntityEntry entry)
    {
        bool setting = entry.Entity is SystemSetting or TenantSetting;
        return RequiresGate(entry.Metadata.ClrType, entry.State,
            entry.Properties.Where(property => property.IsModified).Select(property => property.Metadata.Name),
            setting ? entry.Property(nameof(SystemSetting.SettingKey)).OriginalValue as string : null,
            setting ? entry.Property(nameof(SystemSetting.SettingKey)).CurrentValue as string : null);
    }

    internal static bool RequiresGate(Type entityType, EntityState state, IEnumerable<string> modifiedProperties,
        string? originalKey = null, string? currentKey = null)
    {
        if (state is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) return false;
        if (Sources.TryGetValue(entityType, out var fields))
            return state != EntityState.Modified || modifiedProperties.Any(fields.Contains);
        if (entityType != typeof(SystemSetting) && entityType != typeof(TenantSetting)) return false;
        bool route = HumanAuthorizationRouteFacts.Keys.Contains(originalKey, StringComparer.Ordinal)
            || HumanAuthorizationRouteFacts.Keys.Contains(currentKey, StringComparer.Ordinal);
        bool email = entityType == typeof(SystemSetting)
            && (originalKey == GovernanceSettingKeys.Email.DeliveryEnabled || currentKey == GovernanceSettingKeys.Email.DeliveryEnabled);
        if (!route && !email) return false;
        if (state != EntityState.Modified) return true;
        return route
            ? modifiedProperties.Any((entityType == typeof(SystemSetting) ? SystemRouteFields : TenantRouteFields).Contains)
            : modifiedProperties.Any(property => property is nameof(SystemSetting.SettingKey) or nameof(SystemSetting.Value));
    }
}
