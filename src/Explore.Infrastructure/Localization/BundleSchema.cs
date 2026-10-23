using System.Text.Json;
using Explore.Domain.Common.Localization;

namespace Explore.Infrastructure.Localization;

internal static class BundleSchema
{
    public static SortedDictionary<string, string> Read(Stream stream)
    {
        using var bounded = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer, 0,
            Math.Min(buffer.Length, LocalizationBundlePolicy.MaximumBytes - (int)bounded.Length + 1))) > 0)
        {
            if (bounded.Length + count > LocalizationBundlePolicy.MaximumBytes)
                throw new JsonException("Localization bundle input limit exceeded.");
            bounded.Write(buffer, 0, count);
        }
        bounded.Position = 0;
        using var document = JsonDocument.Parse(bounded, new JsonDocumentOptions { MaxDepth = 2 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Localization bundle root must be a JSON object.");
        }

        var translations = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (translations.Count >= LocalizationBundlePolicy.MaximumEntries
                || property.Value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Localization bundle entry limit or shape is invalid.");
            }

            if (!translations.TryAdd(property.Name, property.Value.GetString()!))
                throw new JsonException("Localization bundle has duplicate translation keys.");
        }

        return ValidateAndSort(translations);
    }

    public static SortedDictionary<string, string> ValidateAndSort(IReadOnlyDictionary<string, string> translations)
    {
        try
        {
            LocalizationBundlePolicy.Validate(translations);
        }
        catch (ArgumentException)
        {
            throw new JsonException("Localization bundle must contain bounded flat translation text.");
        }

        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in translations)
            sorted.Add(key, value);
        return sorted;
    }
}
