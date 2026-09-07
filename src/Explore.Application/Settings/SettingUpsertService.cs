// ABOUTME: Centralized upsert logic for SystemSetting records with audit trail.
// ABOUTME: Replaces copy-pasted UpsertSystemSettingAsync methods across 3+ services.

namespace Explore.Application.Settings;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Notifications;
using Explore.Domain;
using MediatR;

/// <summary>
/// Centralized service for upserting SystemSetting records.
/// Handles create-or-update logic with proper audit fields.
/// </summary>
public class SettingUpsertService
{
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly IMediator _mediator;
    private readonly IPublicationPolicyMutationBoundary _publicationPolicyMutationBoundary;
    private readonly ILocationPrivacyGovernanceMutationService? _locationPrivacyMutations;
    private readonly IEmailDeliverySettingsWriter _emailSettingsWriter;

    public SettingUpsertService(
        ISystemSettingRepository systemSettingRepository,
        IMediator mediator,
        IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary,
        IEmailDeliverySettingsWriter emailSettingsWriter,
        ILocationPrivacyGovernanceMutationService? locationPrivacyMutations = null)
    {
        _systemSettingRepository = systemSettingRepository;
        _mediator = mediator;
        _publicationPolicyMutationBoundary = publicationPolicyMutationBoundary;
        _locationPrivacyMutations = locationPrivacyMutations;
        _emailSettingsWriter = emailSettingsWriter;
    }

    public Task<PublicationPolicyMutationResult> ApplyInstancePublicationPolicyAsync(
        PublicationPolicyInstanceMutationRequest request,
        CancellationToken cancellationToken = default) =>
        _publicationPolicyMutationBoundary.ApplyInstanceAsync(request, cancellationToken);

    /// <summary>
    /// Creates or updates a system setting by key. Sets audit timestamps automatically.
    /// </summary>
    public async Task UpsertSystemSettingAsync(
        string settingKey,
        string value,
        SettingValueType valueType,
        bool isLocked,
        string category,
        int displayOrder,
        string description,
        Guid? actorId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureUnguarded(settingKey);
        SettingPersistenceResult persistence = await PersistAsync(new SystemSetting
        {
            SettingKey = settingKey,
            Value = value,
            ValueType = valueType,
            IsLocked = isLocked,
            Description = description,
            Category = category,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = actorId
        }, actorId, cancellationToken);

        await InvalidateCommittedMutationAsync(persistence.Mutation);
        await _mediator.Publish(new SettingChangedNotification(
            settingKey, EmailDeliverySettingsWriteResultExtensions.AuditValue(settingKey, persistence.PreviousStoredValue),
            EmailDeliverySettingsWriteResultExtensions.AuditValue(settingKey, value),
            SettingSource.SystemDefault, null, actorId, DateTime.UtcNow), CancellationToken.None);
    }

    /// <summary>
    /// Upserts a system setting with minimal parameters (uses existing metadata if updating).
    /// </summary>
    public async Task UpsertValueAsync(
        string settingKey,
        string value,
        Guid? actorId = null,
        CancellationToken cancellationToken = default)
        => await UpsertValueAsync(settingKey, value, isLocked: false, actorId, cancellationToken);

    /// <summary>
    /// Upserts a system setting with lock control. Pulls metadata from SettingRegistry (Guardrail 2).
    /// </summary>
    public async Task UpsertValueAsync(
        string settingKey,
        string value,
        bool isLocked,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        EnsureUnguarded(settingKey);
        _ = await UpsertValueCoreAsync(
            settingKey,
            value,
            isLocked,
            actorId,
            invalidateAfterCommit: true,
            cancellationToken);
    }

    public async Task<SettingChangedNotification> UpsertLockAsync(
        string settingKey,
        string fallbackValue,
        bool isLocked,
        Guid? actorId,
        CancellationToken cancellationToken = default)
    {
        EnsureUnguarded(settingKey);
        if (EmailDeliverySettingKeys.Contains(settingKey))
        {
            var result = await _emailSettingsWriter.ApplyAsync(
                [new EmailDeliverySettingMutation(TenantId: null, Key: settingKey,
                    Kind: EmailDeliverySettingMutationKind.SetLock, IsLocked: isLocked)],
                actorUserId: actorId, cancellationToken: cancellationToken);
            result.EnsureAccepted();
            return result.ToNotifications(actorId).FirstOrDefault() ?? new SettingChangedNotification(
                key: settingKey, oldValue: null, newValue: null,
                scope: isLocked ? SettingSource.SystemLocked : SettingSource.SystemDefault,
                tenantId: null, actorUserId: actorId, changedAt: DateTime.UtcNow);
        }
        var definition = Domain.Settings.SettingRegistry.Get(settingKey);
        var changedAt = DateTime.UtcNow;
        string? previousStoredValue = await _systemSettingRepository.UpsertLockAsync(new SystemSetting
        {
            SettingKey = settingKey,
            Value = fallbackValue,
            ValueType = definition?.ValueType ?? SettingValueType.String,
            IsLocked = isLocked,
            AllowedValues = definition?.AllowedValues is null
                ? null
                : SettingValueSerializer.Serialize(definition.AllowedValues),
            Description = definition?.Description,
            Category = definition?.Category ?? "Unknown",
            DisplayOrder = 0,
            CreatedAt = changedAt,
            CreatedBy = actorId,
            UpdatedAt = changedAt,
            UpdatedBy = actorId
        }, cancellationToken);

        var persistedValue = previousStoredValue ?? fallbackValue;
        return new SettingChangedNotification(
            settingKey,
            previousStoredValue,
            persistedValue,
            isLocked ? SettingSource.SystemLocked : SettingSource.SystemDefault,
            null,
            actorId,
            changedAt);
    }

    public async Task<InstanceSettingMutationResult>
        UpsertInstanceValueInCurrentTransactionAsync(
            InstanceSettingMutationInput input,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Instance setting mutation timestamp must use UTC kind.",
                nameof(input));
        }

        Domain.Settings.SettingDefinition definition =
            Domain.Settings.SettingRegistry.Get(input.Key)
            ?? throw new ArgumentOutOfRangeException(
                nameof(input),
                input.Key,
                "Unknown instance setting key.");
        if (definition.IsSensitive
            || definition.MinScope > Domain.Settings.SettingScope.Instance
            || definition.MaxScope < Domain.Settings.SettingScope.Instance)
        {
            throw new InvalidOperationException(
                "The setting is not eligible for instance mutation.");
        }

        EnsureUnguarded(input.Key);
        if (_locationPrivacyMutations?.Handles(input.Key) == true)
        {
            throw new InvalidOperationException(
                "Location-privacy settings require their specialized current-transaction boundary.");
        }

        DeferredSettingUpsertResult result = await UpsertValueCoreAsync(
            input.Key,
            input.SerializedValue,
            isLocked: false,
            input.ActorUserId,
            invalidateAfterCommit: false,
            cancellationToken,
            input.OccurredAtUtc,
            useCallerTransaction: true);
        return new InstanceSettingMutationResult(result.Notification);
    }

    internal Task<DeferredSettingUpsertResult> UpsertValueWithDeferredInvalidationAsync(
        string settingKey,
        string value,
        Guid? actorId,
        bool isLocked = false,
        CancellationToken cancellationToken = default)
    {
        EnsureUnguarded(settingKey);
        return UpsertValueCoreAsync(
            settingKey,
            value,
            isLocked,
            actorId,
            invalidateAfterCommit: false,
            cancellationToken);
    }

    private static void EnsureUnguarded(string settingKey)
    {
        if (PublicationPolicySettingKeys.All.Contains(settingKey, StringComparer.Ordinal))
            throw new InvalidOperationException($"Guarded publication policy setting '{settingKey}' requires coordinated mutation.");
    }

    private async Task<DeferredSettingUpsertResult> UpsertValueCoreAsync(
        string settingKey,
        string value,
        bool isLocked,
        Guid? actorId,
        bool invalidateAfterCommit,
        CancellationToken cancellationToken,
        DateTime? occurredAtUtc = null,
        bool useCallerTransaction = false)
    {
        DateTime changedAtUtc = occurredAtUtc ?? DateTime.UtcNow;
        var definition = Domain.Settings.SettingRegistry.Get(settingKey);
        SettingPersistenceResult persistence = await PersistAsync(new SystemSetting
        {
            SettingKey = settingKey,
            Value = value,
            ValueType = definition?.ValueType ?? SettingValueType.String,
            IsLocked = isLocked,
            Description = definition?.Description,
            Category = definition?.Category ?? "Unknown",
            DisplayOrder = 0,
            CreatedAt = changedAtUtc,
            CreatedBy = actorId,
            UpdatedAt = changedAtUtc,
            UpdatedBy = actorId
        }, actorId, cancellationToken, useCallerTransaction);

        var notification = new SettingChangedNotification(
            settingKey,
            EmailDeliverySettingsWriteResultExtensions.AuditValue(settingKey, persistence.PreviousStoredValue),
            EmailDeliverySettingsWriteResultExtensions.AuditValue(settingKey, value),
            isLocked ? SettingSource.SystemLocked : SettingSource.SystemDefault,
            null,
            actorId,
            changedAtUtc);

        if (invalidateAfterCommit)
        {
            await InvalidateCommittedMutationAsync(persistence.Mutation);
            await _mediator.Publish(notification, CancellationToken.None);
        }

        return new(notification, persistence.Mutation);
    }

    private async Task<SettingPersistenceResult> PersistAsync(
        SystemSetting setting,
        Guid? actorId,
        CancellationToken cancellationToken,
        bool useCallerTransaction = false)
    {
        if (EmailDeliverySettingKeys.Contains(setting.SettingKey))
        {
            var result = await _emailSettingsWriter.ApplyAsync(
                [new EmailDeliverySettingMutation(TenantId: null, Key: setting.SettingKey,
                    Kind: EmailDeliverySettingMutationKind.SetValue, Value: setting.Value, IsLocked: setting.IsLocked)],
                actorUserId: actorId, cancellationToken: cancellationToken);
            result.EnsureAccepted();
            return new SettingPersistenceResult(
                PreviousStoredValue: result.Changes.IsEmpty ? setting.Value : result.Changes[0].PreviousValue,
                Mutation: null);
        }

        if (_locationPrivacyMutations?.Handles(setting.SettingKey) != true)
        {
            string? previousStoredValue = useCallerTransaction
                ? await _systemSettingRepository
                    .UpsertInCurrentTransactionAsync(
                        setting,
                        cancellationToken)
                : await _systemSettingRepository.UpsertAsync(
                    setting,
                    cancellationToken);
            return new(previousStoredValue, null);
        }

        if (useCallerTransaction)
        {
            throw new InvalidOperationException(
                "Location-privacy settings require their specialized current-transaction boundary.");
        }

        LocationPrivacyGovernanceMutationResult mutation = await _locationPrivacyMutations.ExecuteAsync(
            setting.SettingKey,
            setting.Value,
            Domain.Settings.SettingScope.Instance,
            tenantId: null,
            actorId ?? Guid.Empty,
            token => _systemSettingRepository.UpsertAsync(setting, token),
            cancellationToken);
        if (!mutation.Accepted)
        {
            throw new InvalidOperationException(mutation.Error);
        }

        return new(mutation.PreviousStoredValue, mutation);
    }

    private async Task InvalidateCommittedMutationAsync(
        LocationPrivacyGovernanceMutationResult? mutation)
    {
        if (mutation is not { Accepted: true } || _locationPrivacyMutations is null)
        {
            return;
        }

        await _locationPrivacyMutations.InvalidateMutationAsync(
            Domain.Settings.SettingScope.Instance,
            tenantId: null,
            mutation.CorrectedProjections,
            CancellationToken.None);
    }

    private sealed record SettingPersistenceResult(
        string? PreviousStoredValue,
        LocationPrivacyGovernanceMutationResult? Mutation);
}

internal sealed record DeferredSettingUpsertResult(
    SettingChangedNotification Notification,
    LocationPrivacyGovernanceMutationResult? Mutation);

public sealed record InstanceSettingMutationInput(
    string Key,
    string SerializedValue,
    Guid? ActorUserId,
    DateTime OccurredAtUtc);

public sealed record InstanceSettingMutationResult(
    SettingChangedNotification Notification);
