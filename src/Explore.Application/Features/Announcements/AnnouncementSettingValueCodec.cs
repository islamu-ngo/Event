using System.Text.Json;
using Explore.Domain.Settings;

namespace Explore.Application.Features.Announcements;

public static class AnnouncementSettingValueCodec
{
    public static string SerializeGroup(AnnouncementGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return JsonSerializer.Serialize(new
        {
            selectionEpoch = group.SelectionEpoch,
            enabled = group.Enabled,
            publication = group.Publication is { } publication ? new
            {
                edition = publication.Edition,
                message = publication.Material.Message,
                linkText = publication.Material.LinkText,
                linkUrl = publication.Material.LinkUrl
            } : null
        });
    }

    public static AnnouncementGroup? DeserializeGroup(string? value) => Read(value, root =>
    {
        RequireProperties(root, "selectionEpoch", "enabled", "publication");
        var publication = root.GetProperty("publication");
        AnnouncementPublication? material = null;
        if (publication.ValueKind != JsonValueKind.Null)
        {
            RequireProperties(publication, "edition", "message", "linkText", "linkUrl");
            material = new(publication.GetProperty("edition").GetGuid(), new(
                RequiredString(publication, "message"),
                RequiredString(publication, "linkText"),
                RequiredString(publication, "linkUrl")));
        }
        return new AnnouncementGroup(root.GetProperty("selectionEpoch").GetGuid(),
            root.GetProperty("enabled").GetBoolean(), material);
    });

    public static string SerializeMarker(AnnouncementDismissalMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        var observation = marker.Observation;
        var witness = observation.SourceWitness;
        return JsonSerializer.Serialize(new
        {
            tenantId = observation.TenantId,
            owner = observation.Owner.TenantId.HasValue ? "tenant" : "instance",
            edition = observation.Edition,
            sourceWitness = new
            {
                instanceSelectionEpoch = witness.InstanceSelectionEpoch,
                instanceIsLocked = witness.InstanceIsLocked,
                tenantSelectionEpoch = witness.TenantSelectionEpoch,
                tenantIsLocked = witness.TenantIsLocked
            }
        });
    }

    public static AnnouncementDismissalMarker? DeserializeMarker(string? value) => Read(value, root =>
    {
        RequireProperties(root, "tenantId", "owner", "edition", "sourceWitness");
        Guid tenant = root.GetProperty("tenantId").GetGuid();
        var owner = RequiredString(root, "owner") switch
        {
            "instance" => AnnouncementOwner.Instance,
            "tenant" => AnnouncementOwner.ForTenant(tenant),
            _ => throw new ArgumentException("The stored announcement owner is invalid.", nameof(value))
        };
        var witness = root.GetProperty("sourceWitness");
        RequireProperties(witness, "instanceSelectionEpoch", "instanceIsLocked", "tenantSelectionEpoch", "tenantIsLocked");
        return new AnnouncementDismissalMarker(new(tenant, owner, root.GetProperty("edition").GetGuid(), new(
            OptionalGuid(witness.GetProperty("instanceSelectionEpoch")),
            witness.GetProperty("instanceIsLocked").GetBoolean(),
            OptionalGuid(witness.GetProperty("tenantSelectionEpoch")),
            witness.GetProperty("tenantIsLocked").GetBoolean())));
    });

    internal static void RequireProperties(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("An announcement value must be an object.", nameof(value));
        var remaining = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!remaining.Remove(property.Name))
                throw new ArgumentException("An announcement value has an unknown or duplicate property.", nameof(value));
        if (remaining.Count != 0)
            throw new ArgumentException("An announcement value is incomplete.", nameof(value));
    }

    internal static string RequiredString(JsonElement value, string name) =>
        value.GetProperty(name).GetString()
            ?? throw new ArgumentException("An announcement string cannot be null.", nameof(value));

    private static Guid? OptionalGuid(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();

    private static T? Read<T>(string? value, Func<JsonElement, T> parse) where T : class
    {
        if (value is null)
            return null;
        try
        {
            using var document = JsonDocument.Parse(value);
            return parse(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or FormatException or KeyNotFoundException)
        {
            throw new ArgumentException("The stored announcement value is invalid.", nameof(value), error);
        }
    }
}
