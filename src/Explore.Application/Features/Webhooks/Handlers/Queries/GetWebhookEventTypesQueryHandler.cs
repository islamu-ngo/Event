using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Webhooks.Handlers.Queries;

public sealed class GetWebhookEventTypesQueryHandler(
    IWebhookEventTypeRegistry eventTypeRegistry,
    IWebhookEventSchemaProvider schemaProvider,
    IWebhookEventTypeRepository eventTypeRepository)
    : IRequestHandler<GetWebhookEventTypesQuery, IReadOnlyList<WebhookEventTypeDto>>
{
    public async Task<IReadOnlyList<WebhookEventTypeDto>> Handle(
        GetWebhookEventTypesQuery request,
        CancellationToken cancellationToken)
    {
        var descriptors = eventTypeRegistry
            .GetAll()
            .OrderBy(descriptor => descriptor.GroupName, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToList();
        var descriptorNames = descriptors.Select(descriptor => descriptor.Name).ToArray();
        var persistedByName = (await eventTypeRepository.GetByNamesAsync(descriptorNames, cancellationToken))
            .ToDictionary(eventType => eventType.Name, StringComparer.Ordinal);

        return descriptors
            .Select(descriptor => Map(descriptor, persistedByName.GetValueOrDefault(descriptor.Name)))
            .ToList();
    }

    private WebhookEventTypeDto Map(WebhookEventTypeDescriptor descriptor, WebhookEventType? persisted) =>
        new()
        {
            Id = persisted?.Id,
            Name = descriptor.Name,
            GroupName = persisted?.GroupName ?? descriptor.GroupName,
            Description = persisted?.Description ?? descriptor.Description,
            SchemaVersion = persisted?.SchemaVersion ?? descriptor.SchemaVersion,
            IsPublic = persisted?.IsPublic ?? descriptor.IsPublic,
            IsEnabled = persisted?.IsEnabled ?? descriptor.IsEnabled,
            PayloadRetentionDays = persisted?.PayloadRetentionDays ?? descriptor.PayloadRetentionDays,
            SchemaJson = schemaProvider.CreateSchemaJson(descriptor),
            ExamplePayloadJson = schemaProvider.CreateExamplePayloadJson(descriptor),
            DataFields = descriptor.DataFields.Select(MapField).ToList()
        };

    private static WebhookEventDataFieldDto MapField(WebhookEventDataFieldDescriptor field) =>
        new()
        {
            Name = field.Name,
            JsonType = field.JsonType,
            Description = field.Description,
            ExampleJson = JsonSerializer.Serialize(field.Example),
            Required = field.Required
        };
}
