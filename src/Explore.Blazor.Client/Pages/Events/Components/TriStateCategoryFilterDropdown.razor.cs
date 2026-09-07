// Manages category states (Neutral/Include/Exclude), search, badge counts, and mode toggles.

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Pages.Events.Components;

public partial class TriStateCategoryFilterDropdown
{
    [Parameter] public ICollection<CategoryTypeWithCategoriesDto> CategoryGroups { get; set; } = [];
    [Parameter] public EventCallback<CategoryFilterChangedEventArgs> OnFilterChanged { get; set; }

    private readonly Dictionary<Guid, TagFilterState> _categoryStates = new();
    private string _searchTerm = string.Empty;
    private bool _isOpen;
    private string _inclusionMode = "and";
    private string _exclusionMode = "or";

    private void TogglePopover() => _isOpen = !_isOpen;
    private void ClosePopover() => _isOpen = false;

    private void ToggleCategoryState(Guid categoryId)
    {
        var current = GetCategoryState(categoryId);
        var next = current switch
        {
            TagFilterState.Neutral => TagFilterState.Include,
            TagFilterState.Include => TagFilterState.Exclude,
            TagFilterState.Exclude => TagFilterState.Neutral,
            _ => TagFilterState.Neutral
        };

        if (next == TagFilterState.Neutral)
            _categoryStates.Remove(categoryId);
        else
            _categoryStates[categoryId] = next;
    }

    private TagFilterState GetCategoryState(Guid categoryId) =>
        _categoryStates.TryGetValue(categoryId, out var state) ? state : TagFilterState.Neutral;

    /// <summary>
    /// Global Reset: clears ALL categories back to neutral regardless of search visibility.
    /// </summary>
    public void ResetAll()
    {
        _categoryStates.Clear();
        _searchTerm = string.Empty;
    }

    /// <summary>
    /// Contextual Clear: clears only categories currently visible in the search results.
    /// Non-matching categories retain their state.
    /// </summary>
    private void ClearVisible()
    {
        var visibleCategoryIds = GetFilteredGroups()
            .SelectMany(g => g.Categories)
            .Where(c => c.Id.HasValue)
            .Select(c => c.Id!.Value)
            .ToHashSet();

        foreach (var categoryId in visibleCategoryIds)
        {
            _categoryStates.Remove(categoryId);
        }
    }

    private int GetIncludeCount() =>
        _categoryStates.Count(kv => kv.Value == TagFilterState.Include);

    private int GetExcludeCount() =>
        _categoryStates.Count(kv => kv.Value == TagFilterState.Exclude);

    private bool HasActiveFilters() => _categoryStates.Count > 0;

    private string GetBadgeText()
    {
        var inc = GetIncludeCount();
        var exc = GetExcludeCount();
        if (inc == 0 && exc == 0) return "Filter Categories";

        var parts = new List<string>();
        if (inc > 0) parts.Add($"+{inc}");
        if (exc > 0) parts.Add($"-{exc}");
        return $"Filter Categories {string.Join(" ", parts)}";
    }

    private List<FilteredCategoryGroup> GetFilteredGroups()
    {
        if (string.IsNullOrWhiteSpace(_searchTerm))
        {
            return CategoryGroups
                .Where(g => g.Categories is { Count: > 0 })
                .Select(g => new FilteredCategoryGroup(g.Id ?? 0, g.FullName, g.Categories!.ToList()))
                .ToList();
        }

        var term = _searchTerm.Trim();
        return CategoryGroups
            .Select(g => new FilteredCategoryGroup(
                g.Id ?? 0,
                g.FullName,
                (g.Categories ?? [])
                    .Where(c => c.FullName.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .ToList()))
            .Where(g => g.Categories.Count > 0)
            .ToList();
    }

    private sealed record FilteredCategoryGroup(int Id, string FullName, List<CategoryListDto> Categories);

    private MudBlazor.Color GetChipColor(TagFilterState state) => state switch
    {
        TagFilterState.Include => MudBlazor.Color.Success,
        TagFilterState.Exclude => MudBlazor.Color.Error,
        _ => MudBlazor.Color.Default
    };

    private MudBlazor.Variant GetChipVariant(TagFilterState state) => state switch
    {
        TagFilterState.Include => MudBlazor.Variant.Filled,
        TagFilterState.Exclude => MudBlazor.Variant.Outlined,
        _ => MudBlazor.Variant.Outlined
    };

    private string GetChipIcon(TagFilterState state) => state switch
    {
        TagFilterState.Include => MudBlazor.Icons.Material.Filled.Add,
        TagFilterState.Exclude => MudBlazor.Icons.Material.Filled.Remove,
        _ => string.Empty
    };

    public CategoryFilterChangedEventArgs GetCurrentFilter() => new()
    {
        IncludedCategoryIds = _categoryStates
            .Where(kv => kv.Value == TagFilterState.Include)
            .Select(kv => kv.Key)
            .ToList(),
        ExcludedCategoryIds = _categoryStates
            .Where(kv => kv.Value == TagFilterState.Exclude)
            .Select(kv => kv.Key)
            .ToList(),
        InclusionMode = _inclusionMode,
        ExclusionMode = _exclusionMode
    };
}
