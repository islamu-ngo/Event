using System.Collections;

namespace Explore.Blazor.Client.Tests.Common;

public static class HalLinkTestFactory
{
    public static TResource WithLinks<TResource>(TResource resource, params HalLinkTestLink[] links)
    {
        var linksProperty = typeof(TResource).GetProperty("_links")
            ?? throw new InvalidOperationException($"{typeof(TResource).Name} does not expose HAL links.");
        var linkType = linksProperty.PropertyType.GetGenericArguments()[1];
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), linkType);
        var dictionary = (IDictionary)Activator.CreateInstance(dictionaryType)!;

        foreach (var spec in links)
        {
            var link = Activator.CreateInstance(linkType)!;
            SetIfPresent(linkType, link, nameof(HalLink.Href), spec.Href);
            SetIfPresent(linkType, link, nameof(HalLink.Method), spec.Method);
            SetIfPresent(linkType, link, nameof(HalLink.Title), spec.Title);
            SetIfPresent(linkType, link, nameof(HalLink.Templated), spec.Templated);
            dictionary[spec.Rel] = link;
        }

        linksProperty.SetValue(resource, dictionary);
        return resource;
    }

    public static TResource WithLinks<TResource>(
        TResource resource,
        IEnumerable<string> rels,
        string href,
        string method)
    {
        return WithLinks(
            resource,
            rels.Select(rel => new HalLinkTestLink(rel, href, method)).ToArray());
    }

    private static void SetIfPresent(Type linkType, object link, string propertyName, object? value)
    {
        if (value is null)
        {
            return;
        }

        linkType.GetProperty(propertyName)?.SetValue(link, value);
    }
}

public sealed record HalLinkTestLink(
    string Rel,
    string Href,
    string? Method = null,
    string? Title = null,
    bool? Templated = null);
