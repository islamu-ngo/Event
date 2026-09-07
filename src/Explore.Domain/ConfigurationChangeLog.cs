// Captures who changed what setting, the old and new values, and at which scope level.

using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class ConfigurationChangeLog : IAuditableEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// The user who made the change.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// When the change occurred.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// The setting key that was changed (e.g., "events.require_approval").
    /// </summary>
    public string SettingKey { get; set; } = string.Empty;

    /// <summary>
    /// The previous value (null for new settings).
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// The new value after the change.
    /// </summary>
    public string NewValue { get; set; } = string.Empty;

    /// <summary>
    /// The hierarchy level at which the change was made.
    /// </summary>
    public int SettingScopeId { get; set; }
    public SettingScopeLookup SettingScope { get; set; } = null!;

    public ConfigurationScopeEnum Scope
    {
        get => (ConfigurationScopeEnum)SettingScopeId;
        set => SettingScopeId = (int)value;
    }

    /// <summary>
    /// The ID of the scoped entity (TenantId or OrganizationId). Null for Instance/System scope.
    /// </summary>
    public Guid? ScopeId { get; set; }

    /// <summary>
    /// The type of action performed (Create, Update, Delete).
    /// </summary>
    public string ActionType { get; set; } = string.Empty;

    // IAuditableEntity
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
