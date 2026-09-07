namespace Explore.Application.Contracts.Webhooks;

public static class WebhookJsonSchemaTypes
{
    public const string Text = "string";
    public const string WholeNumber = "integer";
    public const string Numeric = "number";
    public const string Flag = "boolean";
    public const string Structured = "object";
}

public sealed record WebhookEventDataFieldDescriptor(
    string Name,
    string JsonType,
    string Description,
    object? Example,
    bool Required = true);

public sealed record WebhookEventTypeDescriptor(
    string Name,
    string GroupName,
    string Description,
    int SchemaVersion,
    bool IsPublic,
    bool IsEnabled,
    int PayloadRetentionDays,
    IReadOnlyList<WebhookEventDataFieldDescriptor> DataFields,
    bool ApplyStrictPayloadAllowList = true)
{
    public bool AllowsPayloadField(string fieldName) =>
        DataFields.Any(field => string.Equals(field.Name, fieldName, StringComparison.Ordinal));
}
