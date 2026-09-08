// Part of the 3-tier cascading settings engine (System → Tenant → Event).

using Explore.Domain.Interfaces;

namespace Explore.Domain;

/// <summary>
/// System-wide configuration setting that serves as the default value for all tenants.
/// When IsLocked is true, tenants cannot override this setting.
/// </summary>
public class SystemSetting : IAuditableEntity
{
    /// <summary>
    /// Unique identifier for the setting.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Unique key for the setting (e.g., "events.max_sessions", "email.from_address").
    /// Uses dot notation for namespacing.
    /// </summary>
    public required string SettingKey { get; set; }

    /// <summary>
    /// JSON-serialized value of the setting.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>
    /// Data type of the value for validation purposes.
    /// </summary>
    public int SettingValueTypeId { get; set; }
    public SettingValueTypeLookup SettingValueTypeLookup { get; set; } = null!;

    public SettingValueType ValueType
    {
        get => (SettingValueType)SettingValueTypeId;
        set => SettingValueTypeId = (int)value;
    }

    /// <summary>
    /// When true, tenants cannot override this setting value.
    /// Used for security policies, payment providers, and other critical settings.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// JSON array of allowed values (if constrained). Null means any value is allowed.
    /// Example: ["stripe", "paypal"] for payment providers.
    /// </summary>
    public string? AllowedValues { get; set; }

    /// <summary>
    /// Human-readable description of the setting.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Category for grouping settings in UI (e.g., "Email", "Events", "Security").
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Display order within the category.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// When the setting was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Who created this setting.
    /// </summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>
    /// When the setting was last modified.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Who last modified this setting.
    /// </summary>
    public Guid? UpdatedBy { get; set; }
}

/// <summary>
/// Data type of a setting value for validation.
/// </summary>
public enum SettingValueType
{
    String = 0,
    Integer = 1,
    Boolean = 2,
    Decimal = 3,
    Json = 4,
    DateTime = 5,
    Long = 6
}
