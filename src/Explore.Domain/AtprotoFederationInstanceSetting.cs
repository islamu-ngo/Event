using Explore.Domain.Interfaces;
using Explore.Domain.Settings;

namespace Explore.Domain;

/// <summary>
/// Instance-owned nonsecret federation policy, mapped directly by persistence.
/// </summary>
public sealed class AtprotoFederationInstanceSetting : IAuditableEntity
{
    private AtprotoFederationInstanceSetting()
    {
    }

    public Guid Id { get; private set; }
    public string SettingKey { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public bool IsLocked { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static AtprotoFederationInstanceSetting Create(
        string settingKey, string value, bool isLocked, DateTime createdAt, Guid? createdBy)
    {
        AtprotoFederationSettingPolicy.Validate(settingKey, value, SettingScope.Instance);
        AtprotoFederationSettingPolicy.ValidateAuditTime(createdAt);

        return new AtprotoFederationInstanceSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = settingKey,
            Value = value,
            IsLocked = isLocked,
            CreatedAt = createdAt,
            CreatedBy = createdBy
        };
    }

    public void SetValue(string value, DateTime updatedAt, Guid? updatedBy)
    {
        AtprotoFederationSettingPolicy.Validate(SettingKey, value, SettingScope.Instance);
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
