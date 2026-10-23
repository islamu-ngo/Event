using Explore.Domain.Settings;

namespace Explore.Domain;

/// <summary>
/// Nonsecret provisioning policy image. Presence is explicit so missing projection
/// coverage never becomes an unlocked default. It conveys data, not worker authority.
/// </summary>
public sealed class ManagedTenantProvisioningPolicySetting
{
    private ManagedTenantProvisioningPolicySetting() { }

    public Guid Id { get; private set; }
    public string SettingKey { get; private set; } = string.Empty;
    public bool IsPresent { get; private set; }
    public bool IsLocked { get; private set; }
    public string? Value { get; private set; }
    public long Revision { get; private set; }

    public static ManagedTenantProvisioningPolicySetting CreateAbsent(string settingKey)
    {
        if (!ManagedTenantProvisioningPolicyInventory.Keys.Contains(settingKey, StringComparer.Ordinal))
            throw new ArgumentException("The provisioning policy key is not declared.", nameof(settingKey));
        return new() { Id = Guid.CreateVersion7(), SettingKey = settingKey };
    }

    public void Project(SystemSetting? source)
    {
        RequireValid();
        if (source is not null && (source.SettingKey != SettingKey || source.Value is null))
            throw new ArgumentException("The source must retain the declared provisioning policy key.", nameof(source));
        long revision = checked(Revision + 1);
        IsPresent = source is not null;
        IsLocked = source?.IsLocked ?? false;
        Value = source is not null && ManagedTenantProvisioningPolicyInventory.ValueKeys.Contains(SettingKey, StringComparer.Ordinal)
            ? source.Value : null;
        Revision = revision;
    }

    /// <summary>Validates a materialized image without treating it as action authority.</summary>
    public void RequireValid()
    {
        bool storesValue = ManagedTenantProvisioningPolicyInventory.ValueKeys.Contains(SettingKey, StringComparer.Ordinal);
        if (Id.Version != 7 || Revision < 0
            || !ManagedTenantProvisioningPolicyInventory.Keys.Contains(SettingKey, StringComparer.Ordinal)
            || !IsPresent && (IsLocked || Value is not null)
            || !storesValue && Value is not null
            || IsPresent && storesValue && Value is null)
            throw new InvalidOperationException("provisioning_policy_image_invalid");
    }
}
