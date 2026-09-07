// ABOUTME: Maps closed SMTP writer outcomes to existing application failures and deferred setting notifications.
// ABOUTME: Redacts transport values before audit publication while retaining bounded delivery and delegation booleans.

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using FluentValidation.Results;

namespace Explore.Application.Settings;

public static class EmailDeliverySettingsWriteResultExtensions
{
    public static bool IsAccepted(this EmailDeliverySettingsWriteResult result) =>
        result.Status is EmailDeliverySettingsWriteStatus.Applied or EmailDeliverySettingsWriteStatus.NoChange;

    public static string FailureMessage(this EmailDeliverySettingsWriteStatus status) => status switch
    {
        EmailDeliverySettingsWriteStatus.RequiresDisableConfirmation =>
            "This change would disable email delivery. Preview and confirm the affected scope's disable operation, then retry.",
        EmailDeliverySettingsWriteStatus.Locked => "SMTP settings are locked by current governance.",
        EmailDeliverySettingsWriteStatus.NotFound => "Email delivery scope was not found.",
        EmailDeliverySettingsWriteStatus.ConfirmationConflict => "Email delivery confirmation is no longer valid. Request a new preview.",
        EmailDeliverySettingsWriteStatus.InvalidConfirmation => "The exact acknowledgement DISABLE EMAIL DELIVERY is required.",
        _ => "The proposed SMTP settings change is invalid."
    };

    public static void EnsureAccepted(this EmailDeliverySettingsWriteResult result)
    {
        if (result.IsAccepted())
            return;
        string message = result.Status.FailureMessage();
        if (result.Status == EmailDeliverySettingsWriteStatus.ConfirmationConflict)
            throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate, message);
        if (result.Status == EmailDeliverySettingsWriteStatus.NotFound)
            throw new NotFoundException("EmailDeliverySettings", "scope");
        throw new ValidationException(new ValidationResult([new ValidationFailure("EmailDeliverySettings", message)]));
    }

    public static BaseCommandResponse<Guid> ToCommandResponse(
        this EmailDeliverySettingsWriteResult result, Guid scopeId, string successMessage) => result.Status switch
    {
        EmailDeliverySettingsWriteStatus.Applied or EmailDeliverySettingsWriteStatus.NoChange =>
            BaseCommandResponse.Success(scopeId, successMessage),
        EmailDeliverySettingsWriteStatus.ConfirmationConflict =>
            BaseCommandResponse.Conflict(scopeId, result.Status.FailureMessage()),
        EmailDeliverySettingsWriteStatus.NotFound =>
            BaseCommandResponse.NotFound<Guid>(result.Status.FailureMessage()),
        _ => BaseCommandResponse.Validation<Guid>([result.Status.FailureMessage()])
    };

    public static ImmutableArray<SettingChangedNotification> ToNotifications(
        this EmailDeliverySettingsWriteResult result, Guid? actorUserId)
    {
        result.EnsureAccepted();
        return result.Changes.Select(change => new SettingChangedNotification(
            key: change.Key,
            oldValue: AuditValue(change.Key, change.PreviousValue),
            newValue: AuditValue(change.Key, change.Value),
            scope: change.TenantId.HasValue
                ? change.IsLocked ? SettingSource.TenantLocked : SettingSource.TenantOverride
                : change.IsLocked ? SettingSource.SystemLocked : SettingSource.SystemDefault,
            tenantId: change.TenantId,
            actorUserId: actorUserId,
            changedAt: change.ChangedAtUtc)).ToImmutableArray();
    }

    public static string? AuditValue(string key, string? value) =>
        value is null || !EmailDeliverySettingKeys.Contains(key)
            || key is GovernanceSettingKeys.Email.DeliveryEnabled or GovernanceSettingKeys.TenantDelegation.LockSmtp
            ? value : SettingChangedNotification.RedactedValue;
}
