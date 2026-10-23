using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Settings;

namespace Explore.Application.Settings;

/// <summary>Maps dedicated federation entities into the existing hierarchy's immutable inputs.</summary>
public static class AtprotoFederationSettingFacts
{
    public static SettingMergeFacts Create(
        string key,
        AtprotoFederationInstanceSetting? instance,
        AtprotoFederationTenantSetting? tenant,
        Guid? tenantId)
    {
        if (!AtprotoFederationSettingPolicy.IsDedicatedKey(key))
            throw new ArgumentException("A dedicated federation setting key is required.", nameof(key));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A selected tenant must have a nonempty identity.", nameof(tenantId));

        if (instance is not null)
        {
            if (!string.Equals(instance.SettingKey, key, StringComparison.Ordinal))
                throw new ArgumentException("The instance setting belongs to another key.", nameof(instance));
            AtprotoFederationSettingPolicy.Validate(instance.SettingKey, instance.Value, SettingScope.Instance);
        }

        if (tenant is not null)
        {
            if (!tenantId.HasValue || tenant.TenantId != tenantId.Value)
                throw new ArgumentException("The tenant setting must belong to the selected tenant.", nameof(tenant));
            if (!string.Equals(tenant.SettingKey, key, StringComparison.Ordinal))
                throw new ArgumentException("The tenant setting belongs to another key.", nameof(tenant));
            AtprotoFederationSettingPolicy.Validate(tenant.SettingKey, tenant.Value, SettingScope.Tenant);
        }

        var definition = SettingRegistry.Get(key)
            ?? throw new InvalidOperationException("Dedicated federation setting metadata is unavailable.");
        return new SettingMergeFacts(
            System: instance is null ? null : new SystemSettingFacts(
                instance.Value, definition.ValueType, instance.IsLocked,
                definition.Description, definition.Category,
                definition.AllowedValues is null ? null : JsonSerializer.Serialize(definition.AllowedValues)),
            Tenant: tenant is null ? null : new TenantSettingFacts(tenant.Value, tenant.IsLocked),
            Organization: null,
            Group: null,
            User: null,
            HasTenantContext: tenantId.HasValue,
            SmtpDelegationValue: null);
    }
}
