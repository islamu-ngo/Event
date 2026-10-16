using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Explore.Application.Serialization;

namespace Explore.Application.DTOs.Localization;

[JsonConverter(typeof(ImportLocalizationBundleJsonConverter))]
public sealed record ImportLocalizationBundleDto
{
    private IReadOnlyDictionary<string, string> _translations =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    public string LanguageCode { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Translations
    {
        get => _translations;
        init => _translations = value is null
            ? null!
            : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(value, StringComparer.Ordinal));
    }
}
