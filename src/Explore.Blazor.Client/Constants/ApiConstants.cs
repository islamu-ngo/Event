namespace Explore.Blazor.Client.Constants;

/// <summary>
/// Constants for API request defaults.
/// </summary>
public static class ApiConstants
{
    /// <summary>
    /// Default page size for paginated API calls.
    /// Used when loading lookup data (categories, tags, locations, etc.)
    /// where full lists are needed.
    /// </summary>
    public const int DefaultPageSize = 100;

    /// <summary>
    /// Default first page number for paginated API calls.
    /// </summary>
    public const int FirstPage = 1;
}
