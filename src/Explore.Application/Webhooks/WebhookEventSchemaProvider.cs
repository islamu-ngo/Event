using System.Text.Json;
using Explore.Application.Contracts.Webhooks;

namespace Explore.Application.Webhooks;

public sealed class WebhookEventSchemaProvider : IWebhookEventSchemaProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string CreateSchemaJson(WebhookEventTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var dataProperties = descriptor.DataFields.ToDictionary(
            field => field.Name,
            field => (object)new Dictionary<string, object?>
            {
                ["type"] = field.JsonType,
                ["description"] = field.Description
            },
            StringComparer.Ordinal);

        var schema = new Dictionary<string, object?>
        {
            ["$schema"] = "http://json-schema.org/draft-07/schema#",
            ["title"] = descriptor.Name,
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[] { "id", "type", "version", "occurredAt", "tenantId", "data" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["id"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid" },
                ["type"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["const"] = descriptor.Name
                },
                ["version"] = new Dictionary<string, object?>
                {
                    ["type"] = "integer",
                    ["const"] = descriptor.SchemaVersion
                },
                ["occurredAt"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["format"] = "date-time"
                },
                ["tenantId"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["format"] = "uuid"
                },
                ["data"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["additionalProperties"] = !descriptor.ApplyStrictPayloadAllowList,
                    ["required"] = descriptor.DataFields
                        .Where(field => field.Required)
                        .Select(field => field.Name)
                        .ToArray(),
                    ["properties"] = dataProperties
                }
            }
        };

        return JsonSerializer.Serialize(schema, JsonOptions);
    }

    public string CreateExamplePayloadJson(WebhookEventTypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var data = descriptor.DataFields.ToDictionary(
            field => field.Name,
            field => field.Example,
            StringComparer.Ordinal);
        var envelope = new WebhookEventEnvelope(
            Guid.Parse("018f0000-0000-7000-8000-000000000999"),
            descriptor.Name,
            descriptor.SchemaVersion,
            new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero),
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000001"),
            data);

        return JsonSerializer.Serialize(envelope, JsonOptions);
    }
}

