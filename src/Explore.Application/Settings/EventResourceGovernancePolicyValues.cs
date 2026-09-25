using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Settings;

/// <summary>Strictly decodes the closed native resource-setting contract without convenience fallbacks.</summary>
public static class EventResourceGovernancePolicyValues
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters =
        {
            new JsonStringEnumConverter<EventResourceDeliveryTypeEnum>(allowIntegerValues: false),
            new JsonStringEnumConverter<EventResourceAudienceKindEnum>(allowIntegerValues: false)
        }
    };

    public static EventResourceGovernancePolicy Parse(
        IReadOnlyDictionary<string, string> values, long storageMaxUploadBytes)
    {
        T Read<T>(SettingDefinition definition)
        {
            var result = JsonSerializer.Deserialize<T>(
                values.GetValueOrDefault(definition.Key) ?? definition.DefaultValue, Options);
            return result is null ? throw new JsonException("Resource policy values cannot be null.") : result;
        }

        return EventResourceGovernancePolicy.Create(
            Read<EventResourceDeliveryTypeEnum[]>(EventResourceSettingDefinitions.EnabledDeliveryTypes),
            Read<EventResourceAudienceKindEnum[]>(EventResourceSettingDefinitions.EnabledAudiences),
            Read<string[]>(EventResourceSettingDefinitions.PermittedFileTypes),
            Read<long>(EventResourceSettingDefinitions.MaxUploadBytes),
            Read<bool>(EventResourceSettingDefinitions.AllowUnscannedDocuments),
            Read<string[]>(EventResourceSettingDefinitions.ExternalOrigins),
            Read<int>(EventResourceSettingDefinitions.AuditRetentionDays),
            Read<int>(EventResourceSettingDefinitions.MaxActiveResources),
            storageMaxUploadBytes);
    }
}
