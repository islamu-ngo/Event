using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Notifications;

public sealed record PolicyChangedNotification(
    SettingScope Scope,
    Guid? ScopeId,
    string ChangedBy,
    DateTimeOffset ChangedAt) : INotification;
