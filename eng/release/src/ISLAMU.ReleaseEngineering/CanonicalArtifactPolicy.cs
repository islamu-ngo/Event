using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record CanonicalArtifactResult(bool IsValid, byte[]? Bytes, IReadOnlyList<string> Diagnostics);

public sealed record CanonicalTextResult(bool IsValid, string? Text, string? Diagnostic);

public static class CanonicalArtifactPolicy
{
    public const int MaximumFieldUtf8Bytes = 4_096;
    public const int MaximumDocumentUtf8Bytes = 1_048_576;
    public const int MaximumCollectionItems = 1_024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex SecretPattern = new("(?:-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----|\\bBearer\\s+[A-Za-z0-9._~+/=-]{16,}|\\b(?:gh[pousr]_|github_pat_|glpat-|xox[baprs]-)[A-Za-z0-9_-]{8,}|\\b(?:password|passwd|client_secret|api[_-]?key|access[_-]?token|refresh[_-]?token)\\s*[:=]\\s*\\S+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly Regex IdentityOrProviderPattern = new("(?:[\\p{L}\\p{N}._%+-]+@[\\p{L}\\p{N}.-]+\\.[\\p{L}]{2,}|https?://(?:www\\.)?(?:github\\.com|gitlab\\.com|codeberg\\.org|bitbucket\\.org)/\\S+|(?<![\\w@])@[A-Za-z0-9][A-Za-z0-9-]{0,38}(?![\\w-])|\\b(?:workflow|pipeline|job|run)[ _-]?id\\s*[:=]\\s*[0-9]{3,}\\b)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly Regex RawHtmlPattern = new("<\\s*(?:!|/?[A-Za-z])[^>]*>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex MarkdownAutolinkPattern = new("<(?:(?:https?|mailto):[^>\\s]+|[\\p{L}\\p{N}._%+-]+@[\\p{L}\\p{N}.-]+\\.[\\p{L}]{2,})>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> ForbiddenPropertyNames = new(StringComparer.Ordinal)
    {
        "author",
        "authoremail",
        "apikey",
        "accesstoken",
        "clientsecret",
        "committer",
        "committeremail",
        "credential",
        "identity",
        "password",
        "provider",
        "providerid",
        "providername",
        "providerurl",
        "refreshtoken",
        "secret",
        "token",
        "runid",
        "workflowid",
    };

    public static CanonicalArtifactResult CanonicalizeJson(string json)
    {
        if (!TryGetUtf8ByteCount(json, out int byteCount))
        {
            return Invalid("canonical_json_invalid_unicode");
        }

        if (byteCount > MaximumDocumentUtf8Bytes)
        {
            return Invalid("canonical_json_too_large");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = true,
            }))
            {
                WriteCanonical(document.RootElement, writer, null);
            }

            stream.WriteByte((byte)'\n');
            byte[] bytes = stream.ToArray();
            return bytes.Length <= MaximumDocumentUtf8Bytes
                ? new CanonicalArtifactResult(true, bytes, [])
                : Invalid("canonical_json_too_large");
        }
        catch (JsonException)
        {
            return Invalid("canonical_json_malformed");
        }
        catch (CanonicalJsonException exception)
        {
            return Invalid(exception.Diagnostic);
        }
    }

    public static CanonicalArtifactResult CanonicalizeText(string text)
    {
        if (!TryNormalize(text, out string? normalized))
        {
            return Invalid("canonical_text_invalid_unicode");
        }

        string canonical = normalized.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n') + "\n";
        byte[] bytes = StrictUtf8.GetBytes(canonical);
        return bytes.Length <= MaximumDocumentUtf8Bytes
            ? new CanonicalArtifactResult(true, bytes, [])
            : Invalid("canonical_text_too_large");
    }

    public static CanonicalArtifactResult RenderMarkdown(string title, IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        CanonicalTextResult safeTitle = EscapeUntrustedMarkdown(title, rejectRawHtml: false);
        if (!safeTitle.IsValid)
        {
            return Invalid($"markdown_title_invalid:{safeTitle.Diagnostic}");
        }

        var safeEntries = new List<string>();
        int index = 0;
        int utf8Bytes = StrictUtf8.GetByteCount(safeTitle.Text!);
        foreach (string entry in entries)
        {
            if (index >= MaximumCollectionItems)
            {
                return Invalid("markdown_collection_too_large");
            }

            CanonicalTextResult safeEntry = EscapeUntrustedMarkdown(entry, rejectRawHtml: false);
            if (!safeEntry.IsValid)
            {
                return Invalid($"markdown_entry_invalid:{index}:{safeEntry.Diagnostic}");
            }

            utf8Bytes += StrictUtf8.GetByteCount(safeEntry.Text!) + 3;
            if (utf8Bytes > MaximumDocumentUtf8Bytes)
            {
                return Invalid("markdown_document_too_large");
            }

            safeEntries.Add(safeEntry.Text!);
            index++;
        }

        string markdown = $"# {safeTitle.Text}\n\n{string.Join('\n', safeEntries.Order(StringComparer.Ordinal).Select(entry => $"- {entry}"))}\n";
        return CanonicalizeText(markdown);
    }

    public static CanonicalTextResult EscapeUntrustedMarkdown(string value) => EscapeUntrustedMarkdown(value, rejectRawHtml: true);

    public static IReadOnlyDictionary<string, string> CreateDeterministicEnvironment(string isolationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(isolationDirectory);
        if (!Path.IsPathFullyQualified(isolationDirectory))
        {
            throw new ArgumentException("isolation_directory_must_be_absolute", nameof(isolationDirectory));
        }

        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_GLOBAL"] = Path.Combine(isolationDirectory, "global.gitconfig"),
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_OPTIONAL_LOCKS"] = "0",
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["LANG"] = "C",
            ["LC_ALL"] = "C",
            ["TZ"] = "UTC",
        };
    }

    private static CanonicalTextResult EscapeUntrustedMarkdown(string value, bool rejectRawHtml)
    {
        if (!TryGetUtf8ByteCount(value, out int byteCount) || !TryNormalize(value, out string? normalized))
        {
            return new CanonicalTextResult(false, null, "untrusted_text_invalid_unicode");
        }

        if (byteCount > MaximumFieldUtf8Bytes)
        {
            return new CanonicalTextResult(false, null, "untrusted_text_too_large");
        }

        if (normalized.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format))
        {
            return new CanonicalTextResult(false, null, "untrusted_text_ambiguous_unicode");
        }

        if (SecretPattern.IsMatch(normalized))
        {
            return new CanonicalTextResult(false, null, "untrusted_text_secret_material");
        }

        if (IdentityOrProviderPattern.IsMatch(normalized))
        {
            return new CanonicalTextResult(false, null, "untrusted_text_identity_or_provider");
        }

        if (rejectRawHtml && RawHtmlPattern.IsMatch(normalized))
        {
            return new CanonicalTextResult(false, null, "untrusted_text_raw_html");
        }

        if (rejectRawHtml && MarkdownAutolinkPattern.IsMatch(normalized))
        {
            return new CanonicalTextResult(false, null, "untrusted_text_markdown_autolink");
        }

        var escaped = new StringBuilder(normalized.Length);
        foreach (char character in normalized)
        {
            switch (character)
            {
                case '&': escaped.Append("&amp;"); break;
                case '<': escaped.Append("&lt;"); break;
                case '>': escaped.Append("&gt;"); break;
                case '\\':
                case '`':
                case '*':
                case '_':
                case '{':
                case '}':
                case '[':
                case ']':
                case '(':
                case ')':
                case '#':
                case '+':
                case '-':
                case '.':
                case '!':
                case '|':
                case '~':
                    escaped.Append('\\').Append(character);
                    break;
                default:
                    escaped.Append(character);
                    break;
            }
        }

        return new CanonicalTextResult(true, escaped.ToString(), null);
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer, string? propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                (string Name, JsonElement Value)[] properties = element.EnumerateObject()
                    .Select(property => (property.Name.Normalize(NormalizationForm.FormC), property.Value))
                    .OrderBy(property => property.Item1, StringComparer.Ordinal)
                    .ToArray();
                if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                {
                    throw new CanonicalJsonException("canonical_json_duplicate_property");
                }

                foreach ((string name, JsonElement propertyValue) in properties)
                {
                    ValidatePropertyName(name);
                    writer.WritePropertyName(name);
                    WriteCanonical(propertyValue, writer, name);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray().OrderBy(item => CanonicalSortKey(item, propertyName), StringComparer.Ordinal))
                {
                    WriteCanonical(item, writer, propertyName);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                string stringValue = element.GetString()!.Normalize(NormalizationForm.FormC);
                ValidateCanonicalString(stringValue, propertyName);
                writer.WriteStringValue(IsPathProperty(propertyName) ? NormalizePath(stringValue) : stringValue);
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out long integer))
                {
                    writer.WriteNumberValue(integer);
                }
                else if (element.TryGetDecimal(out decimal number))
                {
                    writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                }
                else
                {
                    throw new CanonicalJsonException("canonical_json_invalid_number");
                }

                break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new CanonicalJsonException("canonical_json_unsupported_value");
        }
    }

    private static string CanonicalSortKey(JsonElement element, string? propertyName)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteCanonical(element, writer, propertyName);
        }

        return StrictUtf8.GetString(stream.ToArray());
    }

    private static bool IsPathProperty(string? propertyName)
    {
        if (propertyName is null)
        {
            return false;
        }

        string compact = NormalizePropertyName(propertyName);
        return compact.EndsWith("path", StringComparison.Ordinal) || compact.EndsWith("paths", StringComparison.Ordinal);
    }

    private static string NormalizePath(string value) => value.Replace('\\', '/');

    private static void ValidatePropertyName(string propertyName)
    {
        if (propertyName.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format))
        {
            throw new CanonicalJsonException("canonical_json_ambiguous_unicode");
        }

        string compact = NormalizePropertyName(propertyName);
        if (ForbiddenPropertyNames.Contains(compact))
        {
            throw new CanonicalJsonException("canonical_json_identity_or_provider_property");
        }

        if (IsClockProperty(compact))
        {
            throw new CanonicalJsonException("canonical_json_clock_property");
        }
    }

    private static bool IsClockProperty(string compactPropertyName)
    {
        return compactPropertyName is
            "now" or
            "nowutc" or
            "current" or
            "currentdate" or
            "currenttime" or
            "currentutc" or
            "currentdatetime" or
            "currentdatetimeutc" or
            "generated" or
            "generatedat" or
            "generatedatutc" or
            "generateddate" or
            "generateddateutc" or
            "generatedtime" or
            "generatedtimeutc" or
            "generatedutc" or
            "timestamp" or
            "timestamputc" or
            "buildtimestamp" or
            "buildtimestamputc";
    }

    private static string NormalizePropertyName(string propertyName)
    {
        var builder = new StringBuilder(propertyName.Length);
        foreach (char character in propertyName.Normalize(NormalizationForm.FormC))
        {
            if (character is not '_' and not '-')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static void ValidateCanonicalString(string value, string? propertyName)
    {
        if (value.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format))
        {
            throw new CanonicalJsonException("canonical_json_ambiguous_unicode");
        }

        if (SecretPattern.IsMatch(value))
        {
            throw new CanonicalJsonException("canonical_json_secret_material");
        }

        if (IdentityOrProviderPattern.IsMatch(value))
        {
            throw new CanonicalJsonException("canonical_json_identity_or_provider");
        }

        if (RawHtmlPattern.IsMatch(value))
        {
            throw new CanonicalJsonException("canonical_json_raw_html");
        }

        if (propertyName is not null &&
            (propertyName.Equals("date", StringComparison.OrdinalIgnoreCase) || propertyName.EndsWith("Date", StringComparison.OrdinalIgnoreCase)) &&
            !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new CanonicalJsonException("canonical_json_invalid_release_date");
        }
    }

    private static bool TryGetUtf8ByteCount(string? value, out int byteCount)
    {
        try
        {
            byteCount = StrictUtf8.GetByteCount(value ?? string.Empty);
            return value is not null;
        }
        catch (EncoderFallbackException)
        {
            byteCount = 0;
            return false;
        }
    }

    private static bool TryNormalize(string? value, out string normalized)
    {
        if (value is null)
        {
            normalized = string.Empty;
            return false;
        }

        try
        {
            StrictUtf8.GetByteCount(value);
            normalized = value.Normalize(NormalizationForm.FormC);
            return true;
        }
        catch (EncoderFallbackException)
        {
            normalized = string.Empty;
            return false;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static CanonicalArtifactResult Invalid(string diagnostic) => new(false, null, [diagnostic]);

    private sealed class CanonicalJsonException(string diagnostic) : Exception
    {
        public string Diagnostic { get; } = diagnostic;
    }
}
