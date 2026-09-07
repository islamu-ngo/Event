using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;

namespace Explore.Application.Webhooks;

public sealed class WebhookEventTypeCatalogSyncService(
    IWebhookEventTypeRegistry eventTypeRegistry,
    IWebhookEventSchemaProvider schemaProvider,
    IWebhookEventTypeRepository eventTypeRepository)
    : IWebhookEventTypeCatalogSyncService
{
    public async Task<WebhookEventTypeCatalogSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
        var descriptors = eventTypeRegistry
            .GetAll()
            .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToList();
        var names = descriptors.Select(descriptor => descriptor.Name).ToArray();
        var existingByName = (await eventTypeRepository.GetByNamesAsync(names, cancellationToken))
            .ToDictionary(eventType => eventType.Name, StringComparer.Ordinal);

        var created = 0;
        var updated = 0;
        var unchanged = 0;

        foreach (var descriptor in descriptors)
        {
            var schemaJson = schemaProvider.CreateSchemaJson(descriptor);
            if (!existingByName.TryGetValue(descriptor.Name, out var eventType))
            {
                await eventTypeRepository.CreateAsync(
                    new WebhookEventType
                    {
                        Id = Guid.CreateVersion7(),
                        Name = descriptor.Name,
                        GroupName = descriptor.GroupName,
                        Description = descriptor.Description,
                        SchemaJson = schemaJson,
                        SchemaVersion = descriptor.SchemaVersion,
                        IsPublic = descriptor.IsPublic,
                        IsEnabled = descriptor.IsEnabled,
                        PayloadRetentionDays = descriptor.PayloadRetentionDays
                    },
                    cancellationToken);
                created++;
                continue;
            }

            if (!ApplyDescriptor(eventType, descriptor, schemaJson))
            {
                unchanged++;
                continue;
            }

            await eventTypeRepository.UpdateAsync(eventType, cancellationToken);
            updated++;
        }

        return new WebhookEventTypeCatalogSyncResult(created, updated, unchanged);
    }

    private static bool ApplyDescriptor(
        WebhookEventType eventType,
        WebhookEventTypeDescriptor descriptor,
        string schemaJson)
    {
        var changed = false;

        changed |= SetIfChanged(eventType.GroupName, descriptor.GroupName, value => eventType.GroupName = value);
        changed |= SetIfChanged(eventType.Description, descriptor.Description, value => eventType.Description = value);
        changed |= SetIfChanged(eventType.SchemaJson, schemaJson, value => eventType.SchemaJson = value);
        changed |= SetIfChanged(eventType.SchemaVersion, descriptor.SchemaVersion, value => eventType.SchemaVersion = value);
        changed |= SetIfChanged(eventType.IsPublic, descriptor.IsPublic, value => eventType.IsPublic = value);
        changed |= SetIfChanged(eventType.IsEnabled, descriptor.IsEnabled, value => eventType.IsEnabled = value);
        changed |= SetIfChanged(
            eventType.PayloadRetentionDays,
            descriptor.PayloadRetentionDays,
            value => eventType.PayloadRetentionDays = value);

        return changed;
    }

    private static bool SetIfChanged<T>(T current, T next, Action<T> assign)
        where T : IEquatable<T>
    {
        if (current.Equals(next))
        {
            return false;
        }

        assign(next);
        return true;
    }
}
