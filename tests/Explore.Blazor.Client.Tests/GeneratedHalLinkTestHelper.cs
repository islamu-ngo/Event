using System.Collections;
using System.Reflection;

namespace Explore.Blazor.Client.Tests;

internal static class GeneratedHalLinkTestHelper
{
    public static void SetLinks<TResource>(
        TResource resource,
        params (string Rel, string Href, string Method)[] links)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(resource);

        var linksProperty = typeof(TResource).GetProperty("_links", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"{typeof(TResource).Name} does not expose a generated _links property.");
        var linkType = ResolveLinkType(linksProperty);
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), linkType);
        var dictionary = (IDictionary)(Activator.CreateInstance(dictionaryType)
            ?? throw new InvalidOperationException($"Could not create generated HAL link dictionary for {typeof(TResource).Name}."));

        foreach (var (rel, href, method) in links)
        {
            var link = Activator.CreateInstance(linkType)
                ?? throw new InvalidOperationException($"Could not create generated HAL link item {linkType.Name}.");
            SetStringProperty(link, linkType, "Href", href);
            SetStringProperty(link, linkType, "Method", method);
            dictionary.Add(rel, link);
        }

        linksProperty.SetValue(resource, dictionary);
    }

    private static Type ResolveLinkType(PropertyInfo linksProperty)
    {
        var linkDictionary = linksProperty.PropertyType.GetInterfaces()
            .Append(linksProperty.PropertyType)
            .FirstOrDefault(type => type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                && type.GetGenericArguments()[0] == typeof(string));

        return linkDictionary?.GetGenericArguments()[1]
            ?? throw new InvalidOperationException($"{linksProperty.Name} is not a string-keyed HAL link dictionary.");
    }

    private static void SetStringProperty(object target, Type targetType, string propertyName, string value)
    {
        var property = targetType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"{targetType.Name} does not expose {propertyName}.");

        if (property.PropertyType != typeof(string))
        {
            throw new InvalidOperationException($"{targetType.Name}.{propertyName} must be a string property.");
        }

        property.SetValue(target, value);
    }
}
