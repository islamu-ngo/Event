using System.Text.Json;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Studio.RegistrationForms;

public static class RegistrationFormHal
{
    public static HalLink? Link(IDictionary<string, object> properties, string relation)
    {
        if (!properties.TryGetValue("_links", out object? value)) return null;
        JsonElement links = value is JsonElement element
            ? element
            : JsonSerializer.SerializeToElement(value);
        if (links.ValueKind != JsonValueKind.Object || !links.TryGetProperty(relation, out JsonElement link)) return null;
        return link.Deserialize<HalLink>();
    }

    public static void Require(HalLink? link, string method, string expectedPath)
    {
        if (link is null || !string.Equals(link.Method, method, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The server did not advertise this action.");

        string path = Uri.TryCreate(link.Href, UriKind.Absolute, out Uri? absolute)
            ? absolute.AbsolutePath
            : link.Href.Split('?', '#')[0];
        if (!string.Equals(path.TrimEnd('/'), expectedPath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The server action is stale or targets another resource.");
    }
}
