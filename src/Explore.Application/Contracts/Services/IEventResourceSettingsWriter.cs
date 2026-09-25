using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;

namespace Explore.Application.Contracts.Services;

public enum EventResourceSettingMutationKind { SetValue, SetLock, Remove }

public sealed record EventResourceSettingMutation(
    Guid? TenantId,
    string Key,
    EventResourceSettingMutationKind Kind,
    string? Value = null,
    bool? IsLocked = null);

public sealed record EventResourceSettingsWriteResult(
    bool Success,
    string? FailureCode,
    ImmutableArray<SettingChangedNotification> DeferredNotifications)
{
    public void EnsureAccepted()
    {
        if (!Success)
            throw new ConcurrencyConflictException(FailureCode!,
                "The resource policy change conflicts with the current instance ceilings or setting locks.");
    }

    public async Task<BaseCommandResponse<Guid>> CompleteAsync(
        IHierarchicalSettingsResolver resolver,
        IEnumerable<INotificationHandler<SettingChangedNotification>> notificationHandlers,
        SettingScope scope, Guid scopeId)
    {
        if (!Success)
            return BaseCommandResponse.Failure<Guid>(FailureCode!,
                "The resource policy change conflicts with the current instance ceilings or setting locks.");
        resolver.InvalidateCache(scope, scopeId);
        foreach (var notification in DeferredNotifications)
            await notificationHandlers.HandleAsync(notification, CancellationToken.None);
        return BaseCommandResponse.Success(scopeId, "Resource policy updated.");
    }
}

public static class EventResourceSettingMutationGuard
{
    public static ImmutableArray<string> Keys { get; } =
        EventResourceSettingDefinitions.All.Select(definition => definition.Key).ToImmutableArray();

    public static bool Handles(string key) => Keys.Contains(key, StringComparer.Ordinal);

    public static void RejectGenericMutation(string key)
    {
        if (Handles(key))
            throw new InvalidOperationException("Resource governance settings require coordinated mutation.");
    }
}

public interface IEventResourceSettingsWriter
{
    /// <summary>
    /// Validates the complete proposed state and writes atomically. Joins a caller-owned transaction
    /// when present; the caller must dispatch returned notifications only after its commit.
    /// </summary>
    Task<EventResourceSettingsWriteResult> ApplyAsync(
        ImmutableArray<EventResourceSettingMutation> mutations,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);
}
