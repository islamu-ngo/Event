using System.Text.Json;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Settings;

/// <summary>Strictly decodes the closed native resource-setting contract without convenience fallbacks.</summary>
public static class EventResourceGovernancePolicyValues
{
    public static EventResourceGovernancePolicy Parse(
        IReadOnlyDictionary<string, string> values, long storageMaxUploadBytes)
    {
        T Read<T>(SettingDefinition definition)
        {
            var result = JsonSerializer.Deserialize<T>(
                values.GetValueOrDefault(definition.Key) ?? definition.DefaultValue);
            return result is null ? throw new JsonException("Resource policy values cannot be null.") : result;
        }

        TEnum[] ReadEnums<TEnum>(SettingDefinition definition) where TEnum : struct, Enum =>
            Read<string[]>(definition).Select(token =>
                Enum.TryParse(token, ignoreCase: false, out TEnum parsed)
                && string.Equals(token, Enum.GetName(parsed), StringComparison.Ordinal)
                    ? parsed
                    : throw new JsonException("Resource policy enum values must be exact names."))
                .ToArray();

        return EventResourceGovernancePolicy.Create(
            ReadEnums<EventResourceDeliveryTypeEnum>(EventResourceSettingDefinitions.EnabledDeliveryTypes),
            ReadEnums<EventResourceAudienceKindEnum>(EventResourceSettingDefinitions.EnabledAudiences),
            Read<string[]>(EventResourceSettingDefinitions.PermittedFileTypes),
            Read<long>(EventResourceSettingDefinitions.MaxUploadBytes),
            Read<bool>(EventResourceSettingDefinitions.AllowUnscannedDocuments),
            Read<string[]>(EventResourceSettingDefinitions.ExternalOrigins),
            Read<int>(EventResourceSettingDefinitions.AuditRetentionDays),
            Read<int>(EventResourceSettingDefinitions.MaxActiveResources),
            storageMaxUploadBytes);
    }
}
