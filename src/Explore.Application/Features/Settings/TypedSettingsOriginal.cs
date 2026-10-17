using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Settings;

namespace Explore.Application.Features.Settings;

/// <summary>
/// Closed original requests, captured before the selected physical attempt. This is input,
/// never authority; the selected source and private writer independently retain its identity.
/// </summary>
public sealed partial class TypedSettingsOriginal
{
    internal TypedSettingsOriginal(UpdateEventPolicyCommand command)
    {
        EventPolicy = command with { Patch = command.Patch with { } };
        Actor = command.UserId;
    }

    internal TypedSettingsOriginal(CompleteTenantOnboardingCommand command, Guid tenant)
    {
        Onboarding = command with
        {
            Settings = command.Settings with
            {
                AiAssistantAllowedModelIds = command.Settings.AiAssistantAllowedModelIds.ToImmutableArray()
            },
            DirectoryOperatorIdentity = command.DirectoryOperatorIdentity with { }
        };
        Actor = command.UserId;
        TenantId = tenant;
    }

    internal TypedSettingsOriginal(UpdateTenantReportingIntakePolicyCommand command)
    {
        Reporting = command with { Policy = command.Policy with { } };
        Actor = command.ActorUserId;
        TenantId = command.TenantId;
    }

    public UpdateEventPolicyCommand? EventPolicy { get; }
    public CompleteTenantOnboardingCommand? Onboarding { get; }
    public UpdateTenantReportingIntakePolicyCommand? Reporting { get; }
    public Guid Actor { get; }
    public Guid? TenantId { get; }
    public string Producer => Governance?.Producer ?? (EventPolicy is not null ? "event-policy"
        : Onboarding is not null ? "tenant-onboarding" : PlanAssignment is not null ? "tenant-plan-assignment" : "reporting-intake");
    public string Serialize() => JsonSerializer.Serialize(this);
}

/// <summary>A single final value/lock transition, or removal, compiled from acquired data.</summary>
public sealed record TypedSettingsEffect(string Key, string? Value, bool? IsLocked = null)
{
    public bool Remove => Value is null;
}

/// <summary>
/// The selected writer owns compilation and completion. No callback, arbitrary effect list,
/// database after-image, or public completion receipt is accepted by this port.
/// </summary>
public interface ITypedSettingsOriginalStore
{
    Task ObserveAsync(TypedSettingsOriginal original, IHumanMixedSettingsSource source,
        CancellationToken cancellationToken);
    Task<ImmutableArray<TypedSettingsEffect>> AcquireAndCompileAsync(
        TypedSettingsOriginal original, CancellationToken cancellationToken);
    Task<PublicationPolicyMutationResult> ApplyEventPolicyAsync(
        TypedSettingsOriginal original, CancellationToken cancellationToken);
    Task<IReadOnlyList<SettingChangedNotification>> ApplyGovernanceAsync(
        TypedSettingsOriginal original, CancellationToken cancellationToken);
    void InvalidateGovernanceAfterCommit(TypedSettingsOriginal original);
    Task<IReadOnlyList<SettingChangedNotification>> ApplyOnboardingAsync(
        TypedSettingsOriginal original, CancellationToken cancellationToken);
    Task<PublicationPolicyMutationResult> ApplyReportingAsync(
        TypedSettingsOriginal original, CancellationToken cancellationToken);
    Task RequireCompleteAsync(TypedSettingsOriginal original, CancellationToken cancellationToken);
    Task VerifyAsync(TypedSettingsOriginal original, CancellationToken cancellationToken);
    void RequireObservedData(TypedSettingsOriginal original, string observedData);
    Task RequirePublicationAsync(TypedSettingsPublication publication, CancellationToken cancellationToken);
    Task<CoordinatedSettingMutationWriteResult> WritePublicationAsync(
        TypedSettingsPublication publication, CancellationToken cancellationToken);
    void Close(TypedSettingsOriginal original);
}

/// <summary>Data for the real publication boundary; only its selected store can recognize it.</summary>
public sealed record TypedSettingsPublication(
    TypedSettingsOriginal Original,
    ITypedSettingsOriginalStore Store,
    PublicationPolicyMutationSnapshot Snapshot,
    ImmutableArray<PublicationPolicySettingMutation> Mutations,
    DateTime OccurredAt);
