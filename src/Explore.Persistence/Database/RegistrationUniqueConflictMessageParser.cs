namespace Explore.Persistence.Database;

internal static class RegistrationUniqueConflictMessageParser
{
    private const int MySqlDuplicateEntry = 1062;

    internal static bool IsMySqlDuplicate(
        int number,
        string physicalKeyName) =>
        number == MySqlDuplicateEntry &&
        !string.IsNullOrWhiteSpace(physicalKeyName);

    internal static bool MatchesQuotedConstraint(
        string message,
        IEnumerable<string> expected) =>
        message.Split(
                ['\'', '`', '"', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Any(actual => MatchesConstraint(actual, expected));

    internal static bool MatchesSqliteColumns(
        string message,
        IEnumerable<IReadOnlyList<string>> expectedColumnSets)
    {
        const string prefix = "UNIQUE constraint failed:";
        int prefixIndex = message.IndexOf(
            prefix,
            StringComparison.OrdinalIgnoreCase);
        if (prefixIndex < 0)
        {
            return false;
        }

        string columns = message[(prefixIndex + prefix.Length)..]
            .Trim()
            .TrimEnd('.', '\'', '"');
        string[] actualColumns = columns.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(NormalizeSqliteColumn)
            .ToArray();

        return expectedColumnSets.Any(expectedColumns =>
        {
            var expected = new HashSet<string>(
                expectedColumns.Select(NormalizeSqliteColumn),
                StringComparer.Ordinal);
            return actualColumns.Length == expected.Count &&
                expected.SetEquals(actualColumns);
        });
    }

    internal static bool MatchesConstraint(
        string? actual,
        IEnumerable<string> expected) =>
        actual is not null &&
        expected.Any(candidate => string.Equals(
            NormalizeConstraintIdentifier(actual),
            NormalizeConstraintIdentifier(candidate),
            StringComparison.Ordinal));

    private static string NormalizeConstraintIdentifier(string identifier)
    {
        string normalized = NormalizeSqliteColumn(identifier);
        int qualifier = normalized.LastIndexOf('.');
        return qualifier < 0
            ? normalized
            : normalized[(qualifier + 1)..];
    }

    private static string NormalizeSqliteColumn(string identifier)
    {
        string normalized = identifier.Trim().ToLowerInvariant();
        return normalized.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '_' or '.')
            ? normalized
            : string.Empty;
    }
}
