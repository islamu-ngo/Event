using Explore.Blazor.Client.Clients;
using System.Collections.ObjectModel;

namespace Explore.Blazor.Client.Models;

/// <summary>
/// Represents a paginated result set for Blazor UI consumption.
/// Normalizes generated paginated and HAL responses for UI list state.
/// </summary>
/// <typeparam name="T">The type of items in the result set.</typeparam>
public sealed record PaginatedResult<T>
{
    private IReadOnlyList<T> _items = Array.Empty<T>();
    private IReadOnlyDictionary<string, HalLink>? _links;

    /// <summary>
    /// Gets the items in the current page.
    /// </summary>
    public required IReadOnlyList<T> Items
    {
        get => _items;
        init => _items = Array.AsReadOnly(value.ToArray());
    }

    /// <summary>
    /// Gets the current page number (1-based).
    /// </summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// Gets the number of items per page.
    /// </summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// Gets the total number of items across all pages.
    /// </summary>
    public required int TotalCount { get; init; }

    public IReadOnlyDictionary<string, HalLink>? Links
    {
        get => _links;
        init => _links = value is null
            ? null
            : new ReadOnlyDictionary<string, HalLink>(new Dictionary<string, HalLink>(value, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    /// <summary>
    /// Gets whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Gets whether there is a next page.
    /// </summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>
    /// Creates an empty paginated result.
    /// </summary>
    public static PaginatedResult<T> Empty(int pageNumber = 1, int pageSize = 20) => new()
    {
        Items = [],
        PageNumber = pageNumber,
        PageSize = pageSize,
        TotalCount = 0
    };

    public bool HasHalLink(string rel) =>
        !string.IsNullOrWhiteSpace(rel) && Links?.ContainsKey(rel) == true;
}
