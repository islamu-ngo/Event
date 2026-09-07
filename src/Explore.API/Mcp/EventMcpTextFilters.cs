namespace Explore.API.Mcp;

internal static class EventMcpTextFilters
{
    /// <summary>
    /// Removes blank entries and trims the rest. Used before applying a bound so a collection is not counted
    /// against its ceiling by values an assistant would see as empty.
    /// </summary>
    public static IEnumerable<string> WhereNotBlank(this IEnumerable<string?> values)
        => values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim());
}
