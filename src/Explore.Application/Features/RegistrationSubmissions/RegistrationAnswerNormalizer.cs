using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Explore.Domain.Enums;

namespace Explore.Application.Features.RegistrationSubmissions;

public sealed record RegistrationFieldNormalizationSpec(
    RegistrationFieldTypeEnum FieldType,
    decimal? MinNumber,
    decimal? MaxNumber,
    int? MinLength,
    int? MaxLength,
    string? RegexPattern,
    DateTimeOffset? MinDateTime,
    DateTimeOffset? MaxDateTime,
    string? AllowedUrlSchemes);

public sealed record NormalizedRegistrationValue(
    string Canonical,
    string? Text = null,
    long? IntegerValue = null,
    decimal? DecimalValue = null,
    bool? Boolean = null,
    DateOnly? Date = null,
    TimeOnly? Time = null,
    DateTime? Instant = null,
    IReadOnlyList<Guid>? OptionIds = null);

public sealed record RegistrationValueNormalizationResult(
    NormalizedRegistrationValue? Value,
    string? IssueCode)
{
    public bool IsValid => Value is not null;

    public static RegistrationValueNormalizationResult Accepted(NormalizedRegistrationValue value) => new(value, null);
    public static RegistrationValueNormalizationResult Rejected(string code) => new(null, code);
}

public static partial class RegistrationAnswerNormalizer
{
    private static readonly HashSet<string> CountryCodes = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => new RegionInfo(culture.Name).TwoLetterISORegionName)
        .ToHashSet(StringComparer.Ordinal);

    public static RegistrationValueNormalizationResult Normalize(
        RegistrationFieldNormalizationSpec spec,
        JsonElement raw) => spec.FieldType switch
        {
            RegistrationFieldTypeEnum.ShortText or RegistrationFieldTypeEnum.LongText => Text(spec, raw),
            RegistrationFieldTypeEnum.Integer => Integer(spec, raw),
            RegistrationFieldTypeEnum.Decimal => Decimal(spec, raw),
            RegistrationFieldTypeEnum.Boolean => Boolean(raw),
            RegistrationFieldTypeEnum.Date => Date(spec, raw),
            RegistrationFieldTypeEnum.Time => Time(raw),
            RegistrationFieldTypeEnum.Instant => Instant(spec, raw),
            RegistrationFieldTypeEnum.Email => Email(spec, raw),
            RegistrationFieldTypeEnum.Phone => Phone(spec, raw),
            RegistrationFieldTypeEnum.Url => Url(spec, raw),
            RegistrationFieldTypeEnum.CountryCode => CountryCode(raw),
            RegistrationFieldTypeEnum.LanguageTag => LanguageTag(raw),
            RegistrationFieldTypeEnum.SingleChoice => SingleChoice(raw),
            RegistrationFieldTypeEnum.MultipleChoice => MultipleChoice(raw),
            RegistrationFieldTypeEnum.Rating => Integer(spec, raw),
            RegistrationFieldTypeEnum.Consent => Consent(raw),
            _ => RegistrationValueNormalizationResult.Rejected("UNSUPPORTED_FIELD_TYPE")
        };

    private static RegistrationValueNormalizationResult Text(RegistrationFieldNormalizationSpec spec, JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.String)
        {
            return RegistrationValueNormalizationResult.Rejected("TYPE_MISMATCH");
        }

        string value = NormalizeText(raw.GetString()!);
        if (value.Length == 0 || value.Contains('<', StringComparison.Ordinal) || value.Contains('>', StringComparison.Ordinal))
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_TEXT");
        }

        if (spec.MinLength is { } min && value.Length < min || spec.MaxLength is { } max && value.Length > max)
        {
            return RegistrationValueNormalizationResult.Rejected("LENGTH_OUT_OF_RANGE");
        }

        try
        {
            if (spec.RegexPattern is { Length: > 0 } pattern &&
                !Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                return RegistrationValueNormalizationResult.Rejected("PATTERN_MISMATCH");
            }
        }
        catch (ArgumentException)
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_PATTERN");
        }
        catch (RegexMatchTimeoutException)
        {
            return RegistrationValueNormalizationResult.Rejected("PATTERN_TIMEOUT");
        }

        return RegistrationValueNormalizationResult.Accepted(new(value, Text: value));
    }

    private static RegistrationValueNormalizationResult Integer(RegistrationFieldNormalizationSpec spec, JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetInt64(out long value) ||
            spec.MinNumber is { } min && value < min || spec.MaxNumber is { } max && value > max)
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_INTEGER");
        }

        return RegistrationValueNormalizationResult.Accepted(new(value.ToString(CultureInfo.InvariantCulture), IntegerValue: value));
    }

    private static RegistrationValueNormalizationResult Decimal(RegistrationFieldNormalizationSpec spec, JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Number || !raw.TryGetDecimal(out decimal value) ||
            spec.MinNumber is { } min && value < min || spec.MaxNumber is { } max && value > max)
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_DECIMAL");
        }

        return RegistrationValueNormalizationResult.Accepted(new(value.ToString(CultureInfo.InvariantCulture), DecimalValue: value));
    }

    private static RegistrationValueNormalizationResult Boolean(JsonElement raw) => raw.ValueKind switch
    {
        JsonValueKind.True => RegistrationValueNormalizationResult.Accepted(new("true", Boolean: true)),
        JsonValueKind.False => RegistrationValueNormalizationResult.Accepted(new("false", Boolean: false)),
        _ => RegistrationValueNormalizationResult.Rejected("INVALID_BOOLEAN")
    };

    private static RegistrationValueNormalizationResult Date(RegistrationFieldNormalizationSpec spec, JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(raw.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly value))
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_DATE");
        }

        DateTimeOffset instant = new(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (spec.MinDateTime is { } min && instant < min || spec.MaxDateTime is { } max && instant > max)
        {
            return RegistrationValueNormalizationResult.Rejected("DATE_OUT_OF_RANGE");
        }

        return RegistrationValueNormalizationResult.Accepted(new(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Date: value));
    }

    private static RegistrationValueNormalizationResult Time(JsonElement raw)
    {
        string[] formats = ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"];
        if (raw.ValueKind != JsonValueKind.String || !TimeOnly.TryParseExact(raw.GetString(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly value))
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_TIME");
        }

        return RegistrationValueNormalizationResult.Accepted(new(value.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.'), Time: value));
    }

    private static RegistrationValueNormalizationResult Instant(RegistrationFieldNormalizationSpec spec, JsonElement raw)
    {
        string? text = raw.ValueKind == JsonValueKind.String ? raw.GetString() : null;
        if (text is null || !Rfc3339Instant().IsMatch(text) || !DateTimeOffset.TryParseExact(
                text, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset parsed))
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_INSTANT");
        }

        if (spec.MinDateTime is { } min && parsed < min || spec.MaxDateTime is { } max && parsed > max)
        {
            return RegistrationValueNormalizationResult.Rejected("INSTANT_OUT_OF_RANGE");
        }

        DateTime utc = parsed.UtcDateTime;
        return RegistrationValueNormalizationResult.Accepted(new(utc.ToString("O", CultureInfo.InvariantCulture), Instant: utc));
    }

    private static RegistrationValueNormalizationResult Email(RegistrationFieldNormalizationSpec spec, JsonElement raw)
    {
        RegistrationValueNormalizationResult text = Text(spec, raw);
        if (!text.IsValid)
        {
            return text;
        }

        string value = text.Value!.Text!;
        try
        {
            MailAddress address = new(value);
            return string.Equals(address.Address, value, StringComparison.Ordinal)
                ? text
                : RegistrationValueNormalizationResult.Rejected("INVALID_EMAIL");
        }
        catch (FormatException)
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_EMAIL");
        }
    }

    private static RegistrationValueNormalizationResult Phone(RegistrationFieldNormalizationSpec spec, JsonElement raw)
    {
        RegistrationValueNormalizationResult text = Text(spec, raw);
        return text.IsValid && E164Phone().IsMatch(text.Value!.Text!)
            ? text
            : RegistrationValueNormalizationResult.Rejected("INVALID_PHONE");
    }

    private static RegistrationValueNormalizationResult Url(RegistrationFieldNormalizationSpec spec, JsonElement raw)
    {
        RegistrationValueNormalizationResult text = Text(spec, raw);
        if (!text.IsValid || !Uri.TryCreate(text.Value!.Text, UriKind.Absolute, out Uri? uri))
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_URL");
        }

        string[] schemes = string.IsNullOrWhiteSpace(spec.AllowedUrlSchemes)
            ? [Uri.UriSchemeHttp, Uri.UriSchemeHttps]
            : spec.AllowedUrlSchemes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
            ? RegistrationValueNormalizationResult.Accepted(new(uri.AbsoluteUri, Text: uri.AbsoluteUri))
            : RegistrationValueNormalizationResult.Rejected("URL_SCHEME_NOT_ALLOWED");
    }

    private static RegistrationValueNormalizationResult CountryCode(JsonElement raw)
    {
        string value = raw.ValueKind == JsonValueKind.String ? NormalizeText(raw.GetString()!).ToUpperInvariant() : string.Empty;
        return CountryCodes.Contains(value)
            ? RegistrationValueNormalizationResult.Accepted(new(value, Text: value))
            : RegistrationValueNormalizationResult.Rejected("INVALID_COUNTRY_CODE");
    }

    private static RegistrationValueNormalizationResult LanguageTag(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.String)
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_LANGUAGE_TAG");
        }

        try
        {
            string value = CultureInfo.GetCultureInfo(NormalizeText(raw.GetString()!)).Name;
            return value.Length > 0 && !value.Contains('_', StringComparison.Ordinal)
                ? RegistrationValueNormalizationResult.Accepted(new(value, Text: value))
                : RegistrationValueNormalizationResult.Rejected("INVALID_LANGUAGE_TAG");
        }
        catch (CultureNotFoundException)
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_LANGUAGE_TAG");
        }
    }

    private static RegistrationValueNormalizationResult SingleChoice(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.String || !Guid.TryParseExact(raw.GetString(), "D", out Guid id) || id == Guid.Empty)
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_OPTION");
        }

        return RegistrationValueNormalizationResult.Accepted(new(id.ToString("D"), OptionIds: [id]));
    }

    private static RegistrationValueNormalizationResult MultipleChoice(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Array)
        {
            return RegistrationValueNormalizationResult.Rejected("INVALID_OPTIONS");
        }

        List<Guid> ids = [];
        foreach (JsonElement item in raw.EnumerateArray())
        {
            RegistrationValueNormalizationResult option = SingleChoice(item);
            if (!option.IsValid || ids.Contains(option.Value!.OptionIds![0]))
            {
                return RegistrationValueNormalizationResult.Rejected("INVALID_OPTIONS");
            }

            ids.Add(option.Value.OptionIds[0]);
        }

        return ids.Count == 0
            ? RegistrationValueNormalizationResult.Rejected("INVALID_OPTIONS")
            : RegistrationValueNormalizationResult.Accepted(new(string.Join(',', ids), OptionIds: ids));
    }

    private static RegistrationValueNormalizationResult Consent(JsonElement raw) => raw.ValueKind == JsonValueKind.True
        ? RegistrationValueNormalizationResult.Accepted(new("true", Boolean: true))
        : RegistrationValueNormalizationResult.Rejected("CONSENT_NOT_GRANTED");

    private static string NormalizeText(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n').Trim().Normalize(NormalizationForm.FormC);

    [GeneratedRegex("^\\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164Phone();

    [GeneratedRegex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d{1,7})?(?:Z|[+-]\\d{2}:\\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex Rfc3339Instant();
}
