using Explore.Domain.Settings;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Notifications;

public sealed record PolicyChangedNotification(
    SettingScope Scope,
    Guid? ScopeId,
    string ChangedBy,
    DateTimeOffset ChangedAt) : INotification;
