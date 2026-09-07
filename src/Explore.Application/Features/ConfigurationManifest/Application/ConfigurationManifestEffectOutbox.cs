namespace Explore.Application.Features.ConfigurationManifest.Application;

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;
using MediatR;

public static class ConfigurationManifestEffectOutbox
{
    public const string AggregateType = "ConfigurationManifestOperation";
    public const string EventType = "ConfigurationManifestEffectsRequested";
    public const int MaxRetries = 5;

    public static OutboxMessage Create(
        Guid messageId,
        Guid operationId,
        DateTime occurredAtUtc)
    {
        if (messageId == Guid.Empty || messageId.Version != 7
            || operationId == Guid.Empty || operationId.Version != 7)
        {
            throw new ArgumentException("Manifest effect identities must be UUIDv7.");
        }

        if (occurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Manifest effect timestamp must use UTC kind.", nameof(occurredAtUtc));
        }

        return new OutboxMessage
        {
            Id = messageId,
            AggregateType = AggregateType,
            AggregateId = operationId,
            EventType = EventType,
            Payload = null,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = occurredAtUtc,
            MaxRetries = MaxRetries
        };
    }
}

public interface IConfigurationManifestEffectDispatcher
{
    Task DispatchAsync(Guid operationId, CancellationToken cancellationToken);
}

public sealed class ConfigurationManifestEffectDispatcher(
    IConfigurationManifestOperationRepository operationRepository,
    IHierarchicalSettingsResolver settingsResolver,
    ITypedSettingsDocumentResolver typedSettingsDocumentResolver,
    IPublisher publisher)
    : IConfigurationManifestEffectDispatcher
{
    public async Task DispatchAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ConfigurationManifestOperation operation =
            await operationRepository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("Manifest effect operation was not found.");
        if (operation.Status != ConfigurationManifestOperationStatus.Applied)
        {
            throw new InvalidOperationException("Manifest effects require an applied operation.");
        }

        IReadOnlyList<ConfigurationManifestTenantResult> results =
            await operationRepository.GetResultsByOperationIdAsync(
                operationId,
                cancellationToken);
        ConfigurationManifestTenantResult[] created = results
            .Where(result =>
                result.Status == ConfigurationManifestTenantResultStatus.Created)
            .OrderBy(result => result.TenantId)
            .ToArray();
        var failures = new List<Exception>();
        if (operation.InstanceChangedSettingKeyNames.Count > 0)
        {
            TryEffect(
                () => settingsResolver.InvalidateCache(SettingScope.Instance),
                failures);
        }

        foreach (ConfigurationManifestTenantResult result in created)
        {
            TryEffect(
                () => settingsResolver.InvalidateCache(SettingScope.Tenant, result.TenantId),
                failures);
            TryEffect(
                () => typedSettingsDocumentResolver.InvalidateTenantDocumentCache(
                    result.TenantId,
                    SettingsDocumentKeys.Tenant.Branding),
                failures);
        }

        ImmutableArray<SettingChangedNotification> notifications =
        [
            .. operation.InstanceChangedSettingKeyNames.Select(key =>
                new SettingChangedNotification(
                    key,
                    oldValue: null,
                    newValue: null,
                    SettingSource.SystemDefault,
                    tenantId: null,
                    actorUserId: null,
                    operation.StartedAt)),
            .. created.SelectMany(result => result.ChangedSettingKeyNames.Select(key =>
                new SettingChangedNotification(
                    key,
                    oldValue: null,
                    newValue: null,
                    SettingSource.TenantOverride,
                    result.TenantId,
                    actorUserId: null,
                    operation.StartedAt)))
        ];
        foreach (SettingChangedNotification notification in notifications)
        {
            try
            {
                await publisher.Publish(notification, cancellationToken);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count == 0)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested
            && failures.Any(exception => exception is OperationCanceledException))
        {
            throw new OperationCanceledException(cancellationToken);
        }

        throw new AggregateException("One or more configuration-manifest post-commit effects failed.", failures);
    }

    private static void TryEffect(Action effect, ICollection<Exception> failures)
    {
        try
        {
            effect();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }
}
