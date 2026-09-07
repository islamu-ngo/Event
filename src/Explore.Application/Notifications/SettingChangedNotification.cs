using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Notifications;

public sealed record SettingChangedNotification : INotification
{
    public const string RedactedValue = "[REDACTED]";

    public SettingChangedNotification(
        string key,
        string? oldValue,
        string? newValue,
        SettingSource scope,
        Guid? tenantId,
        Guid? actorUserId,
        DateTime changedAt)
    {
        Key = key;
        IsSensitive = SettingRegistry.Get(key)?.IsSensitive == true;
        OldValue = Redact(oldValue);
        NewValue = Redact(newValue);
        Scope = scope;
        TenantId = tenantId;
        ActorUserId = actorUserId;
        ChangedAt = changedAt;
    }

    public string Key { get; }
    public string? OldValue { get; }
    public string? NewValue { get; }
    public SettingSource Scope { get; }
    public Guid? TenantId { get; }
    public Guid? ActorUserId { get; }
    public DateTime ChangedAt { get; }
    public bool IsSensitive { get; }

    private string? Redact(string? value) => IsSensitive && value is not null ? RedactedValue : value;
}
