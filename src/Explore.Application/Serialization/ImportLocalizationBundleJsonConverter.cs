using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.DTOs.Localization;
using Explore.Domain.Common.Localization;

namespace Explore.Application.Serialization;

/// <summary>
/// Decodes only this flat import contract before dictionary materialization can
/// discard duplicate names. No runtime type names or nested values are accepted.
/// </summary>
public sealed class ImportLocalizationBundleJsonConverter : JsonConverter<ImportLocalizationBundleDto>
{
    public override bool HandleNull => true;

    public override ImportLocalizationBundleDto Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Localization bundle import is invalid.");
        long start = reader.TokenStartIndex;
        Utf8JsonReader boundary = reader;
        if (!boundary.TrySkip()
            || boundary.BytesConsumed - start > LocalizationBundlePolicy.MaximumBytes)
            throw new JsonException("Localization bundle input limit exceeded.");
        Utf8JsonReader depth = reader;
        int initialDepth = depth.CurrentDepth;
        while (depth.Read())
        {
            if (depth.CurrentDepth - initialDepth > 2)
                throw new JsonException("Localization bundle nesting limit exceeded.");
            if (depth.TokenType == JsonTokenType.EndObject && depth.CurrentDepth == initialDepth)
                break;
        }
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Localization bundle import is invalid.");

        string languageMember = options.PropertyNamingPolicy?.ConvertName(nameof(ImportLocalizationBundleDto.LanguageCode))
            ?? nameof(ImportLocalizationBundleDto.LanguageCode);
        string translationsMember = options.PropertyNamingPolicy?.ConvertName(nameof(ImportLocalizationBundleDto.Translations))
            ?? nameof(ImportLocalizationBundleDto.Translations);
        StringComparer comparer = options.PropertyNameCaseInsensitive
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var names = new HashSet<string>(comparer);
        string? language = null;
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new JsonException("Localization bundle import has duplicate members.");
            if (comparer.Equals(property.Name, languageMember))
            {
                if (property.Value.ValueKind != JsonValueKind.String
                    || property.Value.GetString() is not { Length: > 0 and <= 35 } languageCode)
                    throw new JsonException("Localization bundle language is invalid.");
                language = languageCode;
            }
            else if (comparer.Equals(property.Name, translationsMember))
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Localization bundle translations must be flat text.");
                int characters = 0;
                foreach (JsonProperty translation in property.Value.EnumerateObject())
                {
                    if (translation.Name.Length > LocalizationBundlePolicy.MaximumKeyCharacters
                        || translation.Value.ValueKind != JsonValueKind.String
                        || translation.Value.GetString() is not { } value
                        || value.Length > LocalizationBundlePolicy.MaximumValueCharacters
                        || translations.Count >= LocalizationBundlePolicy.MaximumEntries)
                        throw new JsonException("Localization bundle translation limit or shape is invalid.");
                    characters += translation.Name.Length + value.Length;
                    if (characters > LocalizationBundlePolicy.MaximumAggregateCharacters
                        || !translations.TryAdd(translation.Name, value))
                        throw new JsonException("Localization bundle translations are ambiguous or excessive.");
                }
            }
            else
            {
                throw new JsonException("Localization bundle import has an unsupported member.");
            }
        }

        if (language is null || names.Count != 2)
            throw new JsonException("Localization bundle import requires language and translations.");
        try
        {
            LocalizationBundlePolicy.Validate(translations);
        }
        catch (ArgumentException)
        {
            throw new JsonException("Localization bundle translations are invalid.");
        }
        return new ImportLocalizationBundleDto { LanguageCode = language, Translations = translations };
    }

    public override void Write(
        Utf8JsonWriter writer, ImportLocalizationBundleDto value, JsonSerializerOptions options)
    {
        if (value is null)
            throw new JsonException("Localization bundle import is invalid.");
        LocalizationBundlePolicy.Validate(value.Translations);
        writer.WriteStartObject();
        writer.WriteString(options.PropertyNamingPolicy?.ConvertName(nameof(value.LanguageCode))
            ?? nameof(value.LanguageCode), value.LanguageCode);
        writer.WritePropertyName(options.PropertyNamingPolicy?.ConvertName(nameof(value.Translations))
            ?? nameof(value.Translations));
        writer.WriteStartObject();
        foreach ((string key, string translation) in value.Translations)
            writer.WriteString(key, translation);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
