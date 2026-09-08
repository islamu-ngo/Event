// Called by settings update handlers to maintain a complete audit trail.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Records configuration change audit log entries.
/// Every administrative settings change is captured with full context:
/// who changed what, before/after values, and at which hierarchy scope level.
/// </summary>
public class ConfigurationChangeLogService : IConfigurationChangeLogService
{
    private readonly IConfigurationChangeLogRepository _repository;
    private readonly ILogger<ConfigurationChangeLogService> _logger;

    public ConfigurationChangeLogService(
        IConfigurationChangeLogRepository repository,
        ILogger<ConfigurationChangeLogService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task LogChangeAsync(
        Guid userId,
        string settingKey,
        string? oldValue,
        string newValue,
        ConfigurationScopeEnum scope,
        Guid? scopeId = null,
        string actionType = "Update",
        CancellationToken cancellationToken = default)
    {
        var entry = new ConfigurationChangeLog
        {
            UserId = userId,
            Timestamp = DateTime.UtcNow,
            SettingKey = settingKey,
            OldValue = oldValue,
            NewValue = newValue,
            Scope = scope,
            ScopeId = scopeId,
            ActionType = actionType
        };

        await _repository.Create(entry);

        _logger.LogInformation(
            "Configuration change logged: {ActionType} {SettingKey} at {Scope} scope by user {UserId}",
            actionType, settingKey, scope, userId);
    }
}
