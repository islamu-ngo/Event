
using System.Collections.Immutable;
using Explore.Application.Notifications;

namespace Explore.Application.Contracts.Services;

public enum VisitorAccessSettingMutationKind { SetValue, SetLock, Remove }

public sealed record VisitorAccessSettingMutation(
    Guid? TenantId,
    string Key,
    VisitorAccessSettingMutationKind Kind,
    string? Value = null,
    bool? IsLocked = null);

public sealed record VisitorAccessSettingsWriteResult(
    bool Success,
    string? FailureCode,
    ImmutableArray<SettingChangedNotification> DeferredNotifications)
{
    public void EnsureAccepted()
    {
        if (!Success)
            throw new Explore.Application.Exceptions.ConcurrencyConflictException(FailureCode!,
                "The visitor policy change conflicts with the current participation configuration.");
    }

    public Explore.Application.Responses.BaseCommandResponse<Guid> ToCommandResponse(Guid scopeId) =>
        Success ? Explore.Application.Responses.BaseCommandResponse.Success(scopeId, "Visitor policy updated.")
            : Explore.Application.Responses.BaseCommandResponse.Failure<Guid>(FailureCode!,
                "The visitor policy change conflicts with the current participation configuration.");

    public async Task<Explore.Application.Responses.BaseCommandResponse<Guid>> CompleteAsync(
        Explore.Application.Contracts.Infrastructure.IHierarchicalSettingsResolver resolver,
        MediatR.IMediator mediator, Explore.Domain.Settings.SettingScope scope, Guid scopeId)
    {
        if (!Success)
            return Explore.Application.Responses.BaseCommandResponse.Failure<Guid>(FailureCode!,
                "The visitor policy change conflicts with the current participation configuration.");
        resolver.InvalidateCache(scope, scopeId);
        foreach (var notification in DeferredNotifications)
            await mediator.Publish(notification, CancellationToken.None);
        return Explore.Application.Responses.BaseCommandResponse.Success(scopeId, "Visitor policy updated.");
    }
}

public static class VisitorAccessSettingMutationGuard
{
    public static bool Handles(string key) =>
        Explore.Application.Services.VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains(key, StringComparer.Ordinal);

    public static void RejectGenericMutation(string key)
    {
        if (Handles(key))
            throw new InvalidOperationException("Visitor authority settings require coordinated mutation.");
    }
}

public interface IVisitorAccessSettingsWriter
{
    Task<VisitorAccessSettingsWriteResult> ApplyAsync(
        ImmutableArray<VisitorAccessSettingMutation> mutations,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);
}
