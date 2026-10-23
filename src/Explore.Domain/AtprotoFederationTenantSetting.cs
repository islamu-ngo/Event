using Explore.Domain.Interfaces;
using Explore.Domain.Settings;

namespace Explore.Domain;

/// <summary>
/// Tenant-owned nonsecret federation override, mapped directly by persistence.
/// </summary>
public sealed class AtprotoFederationTenantSetting : ITenantEntity, IAuditableEntity
{
    private AtprotoFederationTenantSetting()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; private set; } = null!;
    public string SettingKey { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public bool IsLocked { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static AtprotoFederationTenantSetting Create(
        Guid tenantId, string settingKey, string value, bool isLocked, DateTime createdAt, Guid? createdBy)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant identity is required for a federation override.", nameof(tenantId));
        AtprotoFederationSettingPolicy.Validate(settingKey, value, SettingScope.Tenant);
        AtprotoFederationSettingPolicy.ValidateAuditTime(createdAt);

        return new AtprotoFederationTenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            SettingKey = settingKey,
            Value = value,
            IsLocked = isLocked,
            CreatedAt = createdAt,
            CreatedBy = createdBy
        };
    }

    public void SetValue(string value, DateTime updatedAt, Guid? updatedBy)
    {
        AtprotoFederationSettingPolicy.Validate(SettingKey, value, SettingScope.Tenant);
        AtprotoFederationSettingPolicy.ValidateAuditTime(updatedAt);
        Value = value;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    public void SetLock(bool isLocked, DateTime updatedAt, Guid? updatedBy)
    {
        AtprotoFederationSettingPolicy.ValidateAuditTime(updatedAt);
        IsLocked = isLocked;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }
}
