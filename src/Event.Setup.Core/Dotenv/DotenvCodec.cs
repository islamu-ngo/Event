namespace ISLAMU.Event.Setup.Core.Environment;

using System.Text;

public static class DotenvCodec
{
    public const int MaximumFileUtf8Bytes = 1_048_576;
    public const int MaximumLineUtf8Bytes = 16_384;
    public const int MaximumKeyCharacters = 128;
    public const int MaximumValueUtf8Bytes = 8_192;
    public const int MaximumEntryCount = 2_048;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static DotenvParseResult Parse(ReadOnlyMemory<byte> input)
    {
        if (input.Length > MaximumFileUtf8Bytes)
            return FailedParse("dotenv-file-too-large", "$.document");
        if (input.Length >= 3 && input.Span[0] == 0xEF
            && input.Span[1] == 0xBB && input.Span[2] == 0xBF)
            return FailedParse("dotenv-bom-forbidden", "$.document");

        string text;
        try
        {
            text = StrictUtf8.GetString(input.Span);
        }
        catch (DecoderFallbackException)
        {
            return FailedParse("dotenv-utf8-invalid", "$.document");
        }

        var diagnostics = new List<EnvironmentDiagnostic>();
        var entries = new List<DotenvEntry>();
        var exactKeys = new HashSet<string>(StringComparer.Ordinal);
        var foldedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int start = 0;
        int lineNumber = 1;
        while (start <= text.Length)
        {
            int newline = text.IndexOf('\n', start);
            int end = newline < 0 ? text.Length : newline;
            ReadOnlySpan<char> line = text.AsSpan(start, end - start);
            string path = $"$.lines[{lineNumber}]";
            if (line.IndexOf('\r') >= 0)
                Add(diagnostics, "dotenv-carriage-return-forbidden", path);
            else if (HasControl(line))
                Add(diagnostics, "dotenv-control-character", path);
            else if (StrictUtf8.GetByteCount(line) > MaximumLineUtf8Bytes)
                Add(diagnostics, "dotenv-line-too-large", path);
            else if (!line.IsEmpty && line[0] != '#')
            {
                bool quoteContinues = newline >= 0
                    && line.IndexOf('=') >= 0
                    && line[(line.IndexOf('=') + 1)..].StartsWith("\"", StringComparison.Ordinal)
                    && !line[(line.IndexOf('=') + 2)..].Contains('"')
                    && text.AsSpan(newline + 1).IndexOf('"') >= 0;
                if (quoteContinues) Add(diagnostics, "dotenv-multiline-forbidden", path);
                else ParseLine(line, path, entries, exactKeys, foldedKeys, diagnostics);
            }

            if (newline < 0) break;
            start = newline + 1;
            lineNumber++;
        }

        if (entries.Count > MaximumEntryCount)
            Add(diagnostics, "dotenv-count-exceeded", "$.document");
        return diagnostics.Count == 0
            ? new DotenvParseResult(new DotenvDocument(entries), [])
            : new DotenvParseResult(null, Ordered(diagnostics));
    }

    public static DotenvRenderResult Render(DotenvDocument document, bool finalNewline)
    {
        ArgumentNullException.ThrowIfNull(document);
        DotenvEntry?[] entries = document.Entries.Cast<DotenvEntry?>().ToArray();
        var diagnostics = new List<EnvironmentDiagnostic>();
        var exactKeys = new HashSet<string>(StringComparer.Ordinal);
        var foldedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < entries.Length; index++)
            ValidateEntry(entries[index], $"$.entries[{index}]", exactKeys, foldedKeys, diagnostics);
        if (entries.Length > MaximumEntryCount)
            Add(diagnostics, "dotenv-count-exceeded", "$.document");
        if (diagnostics.Count != 0)
            return new DotenvRenderResult([], Ordered(diagnostics));

        var builder = new StringBuilder();
        DotenvEntry[] ordered = entries.OfType<DotenvEntry>()
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        for (int index = 0; index < ordered.Length; index++)
        {
            DotenvEntry entry = ordered[index];
            builder.Append(entry.Key).Append('=').Append(RenderValue(entry.Value));
            if (index < ordered.Length - 1 || finalNewline) builder.Append('\n');
        }
        try
        {
            byte[] bytes = StrictUtf8.GetBytes(builder.ToString());
            if (bytes.Length > MaximumFileUtf8Bytes)
                return new DotenvRenderResult([], [Diagnostic("dotenv-file-too-large", "$.document")]);
            return new DotenvRenderResult(bytes, []);
        }
        catch (EncoderFallbackException)
        {
            return new DotenvRenderResult([], [Diagnostic("dotenv-utf16-invalid", "$.document")]);
        }
    }

    private static void ParseLine(
        ReadOnlySpan<char> line,
        string path,
        List<DotenvEntry> entries,
        HashSet<string> exactKeys,
        HashSet<string> foldedKeys,
        List<EnvironmentDiagnostic> diagnostics)
    {
        if (line.StartsWith("export ", StringComparison.Ordinal))
        {
            Add(diagnostics, "dotenv-export-forbidden", path);
            return;
        }
        if (char.IsWhiteSpace(line[0]))
        {
            Add(diagnostics, "dotenv-whitespace-forbidden", path);
            return;
        }
        int equals = line.IndexOf('=');
        if (equals < 0)
        {
            Add(diagnostics, "dotenv-equals-missing", path);
            return;
        }
        ReadOnlySpan<char> keySpan = line[..equals];
        string key = keySpan.ToString();
        if (keySpan.Length > 0 && char.IsWhiteSpace(keySpan[^1]))
            Add(diagnostics, "dotenv-whitespace-forbidden", path, key);
        if (!IsKey(key)) Add(diagnostics, "dotenv-key-invalid", path);
        if (!exactKeys.Add(key)) Add(diagnostics, "dotenv-duplicate-key", path, key);
        else if (!foldedKeys.Add(key)) Add(diagnostics, "dotenv-key-case-collision", path);

        ReadOnlySpan<char> source = line[(equals + 1)..];
        if (!TryParseValue(source, path, diagnostics, out string? value)) return;
        if (StrictUtf8.GetByteCount(value) > MaximumValueUtf8Bytes)
        {
            Add(diagnostics, "dotenv-value-too-large", path, key);
            return;
        }
        if (IsKey(key) && exactKeys.Contains(key))
            entries.Add(new DotenvEntry(key, value.Length == 0 ? null : value,
                value.Length == 0 ? DotenvEntryKind.EmptyPlaceholder : DotenvEntryKind.LocalHumanValue,
                false, DotenvProvenance.UserInput));
    }

    private static bool TryParseValue(
        ReadOnlySpan<char> source,
        string path,
        List<EnvironmentDiagnostic> diagnostics,
        out string value)
    {
        value = string.Empty;
        if (source.IsEmpty) return true;
        if (ContainsForbiddenShell(source))
        {
            Add(diagnostics, source.IndexOfAny('$', '`') >= 0
                ? "dotenv-expansion-forbidden" : "dotenv-trailing-syntax", path);
            return false;
        }
        if (source[0] == '\'')
        {
            Add(diagnostics, "dotenv-quote-invalid", path);
            return false;
        }
        if (source[0] != '"')
        {
            if (source.IndexOfAny('"', '#') >= 0 || !source.All(IsSafeUnquoted))
            {
                Add(diagnostics, source.IndexOf('"') >= 0 ? "dotenv-quote-invalid" : "dotenv-trailing-syntax", path);
                return false;
            }
            value = source.ToString();
            return true;
        }

        var builder = new StringBuilder(source.Length);
        bool escaped = false;
        for (int index = 1; index < source.Length; index++)
        {
            char character = source[index];
            if (escaped)
            {
                if (character is not '\\' and not '"')
                {
                    Add(diagnostics, "dotenv-escape-invalid", path);
                    return false;
                }
                builder.Append(character);
                escaped = false;
            }
            else if (character == '\\') escaped = true;
            else if (character == '"')
            {
                if (index != source.Length - 1)
                {
                    Add(diagnostics, "dotenv-trailing-syntax", path);
                    return false;
                }
                value = builder.ToString();
                return true;
            }
            else builder.Append(character);
        }
        Add(diagnostics, escaped ? "dotenv-escape-invalid" : "dotenv-quote-invalid", path);
        return false;
    }

    private static void ValidateEntry(
        DotenvEntry? entry,
        string path,
        HashSet<string> exactKeys,
        HashSet<string> foldedKeys,
        List<EnvironmentDiagnostic> diagnostics)
    {
        if (entry is null)
        {
            Add(diagnostics, "dotenv-entry-null", path);
            return;
        }
        if (!IsKey(entry.Key)) Add(diagnostics, "dotenv-key-invalid", path);
        if (!exactKeys.Add(entry.Key)) Add(diagnostics, "dotenv-duplicate-key", path, entry.Key);
        else if (!foldedKeys.Add(entry.Key)) Add(diagnostics, "dotenv-key-case-collision", path);
        if (!Enum.IsDefined(entry.Kind)) Add(diagnostics, "dotenv-entry-kind-invalid", path, entry.Key);
        if (!Enum.IsDefined(entry.Provenance)) Add(diagnostics, "dotenv-provenance-invalid", path, entry.Key);
        if (entry.Kind == DotenvEntryKind.EmptyPlaceholder && entry.Value is not null
            || entry.Kind != DotenvEntryKind.EmptyPlaceholder && string.IsNullOrEmpty(entry.Value))
            Add(diagnostics, "dotenv-entry-shape-invalid", path, entry.Key);
        if (entry.Value is { } value)
        {
            try
            {
                if (StrictUtf8.GetByteCount(value) > MaximumValueUtf8Bytes)
                    Add(diagnostics, "dotenv-value-too-large", path, entry.Key);
            }
            catch (EncoderFallbackException)
            {
                Add(diagnostics, "dotenv-utf16-invalid", path, entry.Key);
                return;
            }
            if (HasControl(value.AsSpan()) || value.Contains('\r') || value.Contains('\n')
                || ContainsForbiddenShell(value))
                Add(diagnostics, "dotenv-value-forbidden", path, entry.Key);
        }
    }

    private static string RenderValue(string? value)
    {
        if (value is null) return string.Empty;
        if (value.All(IsSafeUnquoted)) return value;
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static bool IsKey(string key) => key.Length is > 0 and <= MaximumKeyCharacters
        && key[0] is >= 'A' and <= 'Z'
        && key.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    private static bool IsSafeUnquoted(char character) => character is >= 'A' and <= 'Z'
        or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or '/' or ':' or ',' or '@' or '+';

    private static bool ContainsForbiddenShell(ReadOnlySpan<char> value) =>
        value.IndexOfAny("$`;|&<>".AsSpan()) >= 0;

    private static bool HasControl(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
            if (character < ' ') return true;
        return false;
    }

    private static DotenvParseResult FailedParse(string code, string path) =>
        new(null, [Diagnostic(code, path)]);

    private static void Add(List<EnvironmentDiagnostic> values, string code, string path, string? key = null) =>
        values.Add(Diagnostic(code, path, key));

    private static EnvironmentDiagnostic Diagnostic(string code, string path, string? key = null) =>
        new(code, path, key is not null && IsKey(key) ? key : null, "dotenv");

    private static IEnumerable<EnvironmentDiagnostic> Ordered(IEnumerable<EnvironmentDiagnostic> values) =>
        values.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Code, StringComparer.Ordinal);
}

internal static class DotenvSpanExtensions
{
    internal static bool All(this ReadOnlySpan<char> value, Func<char, bool> predicate)
    {
        foreach (char character in value)
            if (!predicate(character)) return false;
        return true;
    }
}
