using System.Text.Json;
using Explore.Application.Features.Announcements;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

namespace Explore.Application.Features.ConfigurationManifest.Importing;

public static class AnnouncementConfigurationProjection
{
    public static AnnouncementChange Parse(AnnouncementOwner owner, JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return ParseForScope(owner.TenantId.HasValue ? ConfigurationManifestScope.Tenant : ConfigurationManifestScope.Instance, value);
    }

    internal static AnnouncementChange ParseForScope(ConfigurationManifestScope scope, JsonElement value)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentException("The announcement portability scope is invalid.", nameof(scope));
        try
        {
            if (value.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("An announcement import requires an object.", nameof(value));
            switch (AnnouncementSettingValueCodec.RequiredString(value, "operation"))
            {
                case "publish":
                    RequireIntentProperties(value,
                        "operation", "enabled", "message", "linkText", "linkUrl", "redisplay");
                    return new AnnouncementChange.Publish(new(
                        AnnouncementSettingValueCodec.RequiredString(value, "message"),
                        AnnouncementSettingValueCodec.RequiredString(value, "linkText"),
                        AnnouncementSettingValueCodec.RequiredString(value, "linkUrl")),
                        value.GetProperty("enabled").GetBoolean(), value.GetProperty("redisplay").GetBoolean());
                case "inherit":
                    RequireIntentProperties(value, "operation");
                    if (scope == ConfigurationManifestScope.Instance)
                        throw new ArgumentException("Only a tenant can inherit an announcement.", nameof(value));
                    return new AnnouncementChange.RemoveOverride();
                case "disable":
                    RequireIntentProperties(value, "operation");
                    return new AnnouncementChange.SetEnabled(false);
                case "setLock":
                    RequireIntentProperties(value, "operation", "isLocked");
                    return new AnnouncementChange.SetLock(value.GetProperty("isLocked").GetBoolean());
                default:
                    throw new ArgumentException("The announcement import operation is invalid.", nameof(value));
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or FormatException or KeyNotFoundException)
        {
            throw new ArgumentException("The announcement import intent is invalid.", nameof(value), error);
        }
    }

    public static JsonElement Export(AnnouncementSourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Group?.Publication is not { } publication)
            return JsonSerializer.SerializeToElement(new
            {
                operation = source.Owner.TenantId.HasValue ? "inherit" : "disable",
                currentLock = source.IsLocked
            });
        return JsonSerializer.SerializeToElement(new
        {
            operation = "publish",
            enabled = source.Group.Enabled,
            message = publication.Material.Message,
            linkText = publication.Material.LinkText,
            linkUrl = publication.Material.LinkUrl,
            redisplay = false,
            currentLock = source.IsLocked
        });
    }

    internal static bool MatchesSettings(
        JsonElement requested, JsonElement actual, ConfigurationManifestScope scope, bool afterApply)
    {
        if (requested.ValueKind != actual.ValueKind)
            return false;
        if (requested.ValueKind == JsonValueKind.Object)
        {
            const string key = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
            var source = requested.EnumerateObject().Where(property => property.Name != key)
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            var target = actual.EnumerateObject().Where(property => property.Name != key)
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            if (source.Any(pair =>
                !target.TryGetValue(pair.Key, out var value) || !MatchesSettings(pair.Value, value, scope, afterApply)))
                return false;
            if (!requested.TryGetProperty(key, out var intent))
                return true;
            return actual.TryGetProperty(key, out var state) && MatchesIntent(intent, state, scope, afterApply);
        }
        if (requested.ValueKind == JsonValueKind.Array)
            return requested.GetArrayLength() == actual.GetArrayLength()
                && requested.EnumerateArray().Zip(actual.EnumerateArray())
                    .All(pair => MatchesSettings(pair.First, pair.Second, scope, afterApply));
        return requested.ValueKind == JsonValueKind.String
            ? requested.GetString() == actual.GetString()
            : requested.GetRawText() == actual.GetRawText();
    }

    private static bool MatchesIntent(
        JsonElement intent, JsonElement state, ConfigurationManifestScope scope, bool afterApply)
    {
        var desired = ParseForScope(scope, intent);
        var current = ParseForScope(scope, state);
        return desired switch
        {
            AnnouncementChange.Publish publish => (afterApply || !publish.Redisplay)
                && current is AnnouncementChange.Publish existing
                && publish.Material == existing.Material && publish.Enabled == existing.Enabled,
            AnnouncementChange.RemoveOverride => current is AnnouncementChange.RemoveOverride,
            AnnouncementChange.SetEnabled { Enabled: false } => current is AnnouncementChange.SetEnabled { Enabled: false }
                or AnnouncementChange.RemoveOverride or AnnouncementChange.Publish { Enabled: false },
            AnnouncementChange.SetLock locked => state.TryGetProperty("currentLock", out var value)
                && (value.ValueKind is JsonValueKind.True or JsonValueKind.False) && value.GetBoolean() == locked.IsLocked,
            _ => false
        };
    }

    private static void RequireIntentProperties(JsonElement value, params string[] properties)
    {
        if (value.TryGetProperty("currentLock", out var locked))
        {
            if (locked.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArgumentException("Exported lock metadata must be Boolean.", nameof(value));
            AnnouncementSettingValueCodec.RequireProperties(value, [.. properties, "currentLock"]);
        }
        else
            AnnouncementSettingValueCodec.RequireProperties(value, properties);
    }
}
