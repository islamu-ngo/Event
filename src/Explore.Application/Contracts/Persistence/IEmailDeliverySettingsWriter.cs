
using System.Collections.Immutable;
using Explore.Application.Features.Settings;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Only a privately compiled active mixed batch can borrow the selected Email writer.</summary>
public interface IMixedEmailDeliverySettingsWriter
{
    Task<EmailDeliverySettingsWriteResult> ApplyMixedAsync(
        MixedSettingsBatchMutation mutation, CancellationToken cancellationToken = default);
}

public interface IEmailDeliverySettingsWriter
{
    Task<EmailDeliverySettingsWriteResult> ApplyAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);

    Task<EmailDeliverySettingsWriteResult> DisableAsync(
        EmailDeliveryDisableConfirmation confirmation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fixed instance-administrator action on the selected tenant. Ordinary tenant
    /// settings use ApplyAsync and never inherit this administrator disjunction.
    /// </summary>
    Task<EmailDeliverySettingsWriteResult> ApplyInstanceTenantAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations,
        Guid? actorUserId,
        CancellationToken cancellationToken = default);
}

public enum EmailDeliverySettingMutationKind { SetValue, Remove, SetLock }

public sealed record EmailDeliverySettingMutation(
    Guid? TenantId,
    string Key,
    EmailDeliverySettingMutationKind Kind,
    string? Value = null,
    bool? IsLocked = null);

public sealed record EmailDeliveryDisableConfirmation(
    Guid? TenantId,
    Guid ActorUserId,
    long ExpectedRevision,
    string? ConfirmationToken,
    string? Acknowledgement)
{
    public const string RequiredAcknowledgement = "DISABLE EMAIL DELIVERY";
}

public enum EmailDeliverySettingsWriteStatus
{
    Applied, NoChange, RequiresDisableConfirmation, Locked, NotFound,
    InvalidMutation, ConfirmationConflict, InvalidConfirmation
}

public sealed record EmailDeliverySettingChange(
    Guid? TenantId,
    string Key,
    string? PreviousValue,
    string? Value,
    bool IsLocked,
    DateTime ChangedAtUtc);

public sealed record EmailDeliverySettingsWriteResult(
    EmailDeliverySettingsWriteStatus Status,
    ImmutableArray<EmailDeliverySettingChange> Changes);
