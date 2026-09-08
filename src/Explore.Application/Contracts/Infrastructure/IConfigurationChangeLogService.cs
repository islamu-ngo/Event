// Every administrative settings change is logged with who, what, old/new values, and scope.

using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>
/// Service for recording audit log entries when configuration settings are changed.
/// Captures the full context: who changed what, the before/after values, and at which scope level.
/// </summary>
public interface IConfigurationChangeLogService
{
    /// <summary>
    /// Records a configuration change in the audit log.
    /// </summary>
    /// <param name="userId">The user who made the change.</param>
    /// <param name="settingKey">The setting key that was changed.</param>
    /// <param name="oldValue">The previous value (null for new settings).</param>
    /// <param name="newValue">The new value.</param>
    /// <param name="scope">The hierarchy level (Instance, Tenant, Organization).</param>
    /// <param name="scopeId">The scoped entity ID (TenantId or OrganizationId). Null for Instance/System scope.</param>
    /// <param name="actionType">The action performed ("Create", "Update", "Delete").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogChangeAsync(
        Guid userId,
        string settingKey,
        string? oldValue,
        string newValue,
        ConfigurationScopeEnum scope,
        Guid? scopeId = null,
        string actionType = "Update",
        CancellationToken cancellationToken = default);
}
