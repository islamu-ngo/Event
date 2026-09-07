using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Pages.Events.Components;

public partial class TriStateTagFilterDropdown
{
    [Parameter] public ICollection<TagTypeWithTagsDto> TagGroups { get; set; } = [];
    [Parameter] public EventCallback<TagFilterChangedEventArgs> OnFilterChanged { get; set; }

    private readonly Dictionary<Guid, TagFilterState> _tagStates = new();
    private string _searchTerm = string.Empty;
    private bool _isOpen;
    private string _inclusionMode = "and";
    private string _exclusionMode = "or";

    private void TogglePopover() => _isOpen = !_isOpen;
    private void ClosePopover() => _isOpen = false;

    private void ToggleTagState(Guid tagId)
    {
        var current = GetTagState(tagId);
        var next = current switch
        {
            TagFilterState.Neutral => TagFilterState.Include,
            TagFilterState.Include => TagFilterState.Exclude,
            TagFilterState.Exclude => TagFilterState.Neutral,
            _ => TagFilterState.Neutral
        };

        if (next == TagFilterState.Neutral)
            _tagStates.Remove(tagId);
        else
            _tagStates[tagId] = next;
    }

    private TagFilterState GetTagState(Guid tagId) =>
        _tagStates.TryGetValue(tagId, out var state) ? state : TagFilterState.Neutral;

    /// <summary>
    /// Global Reset: clears ALL tags back to neutral regardless of search visibility.
    /// </summary>
    public void ResetAll()
    {
        _tagStates.Clear();
        _searchTerm = string.Empty;
    }

    /// <summary>
    /// Contextual Clear: clears only tags currently visible in the search results.
    /// Non-matching tags retain their state.
    /// </summary>
    private void ClearVisible()
    {
        var visibleTagIds = GetFilteredGroups()
            .SelectMany(g => g.Tags)
            .Where(t => t.Id.HasValue)
            .Select(t => t.Id!.Value)
            .ToHashSet();

        foreach (var tagId in visibleTagIds)
        {
            _tagStates.Remove(tagId);
        }
    }

    private int GetIncludeCount() =>
        _tagStates.Count(kv => kv.Value == TagFilterState.Include);

    private int GetExcludeCount() =>
        _tagStates.Count(kv => kv.Value == TagFilterState.Exclude);

    private bool HasActiveFilters() => _tagStates.Count > 0;

    private string GetBadgeText()
    {
        var inc = GetIncludeCount();
        var exc = GetExcludeCount();
        if (inc == 0 && exc == 0) return "Filter Tags";

        var parts = new List<string>();
        if (inc > 0) parts.Add($"+{inc}");
        if (exc > 0) parts.Add($"-{exc}");
        return $"Filter Tags {string.Join(" ", parts)}";
    }

    private List<FilteredTagGroup> GetFilteredGroups()
    {
        if (string.IsNullOrWhiteSpace(_searchTerm))
        {
            return TagGroups
                .Where(g => g.Tags is { Count: > 0 })
                .Select(g => new FilteredTagGroup(g.Id ?? 0, g.FullName, g.Tags!.ToList()))
                .ToList();
        }

        var term = _searchTerm.Trim();
        return TagGroups
            .Select(g => new FilteredTagGroup(
                g.Id ?? 0,
                g.FullName,
                (g.Tags ?? [])
                    .Where(t => t.FullName.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .ToList()))
            .Where(g => g.Tags.Count > 0)
            .ToList();
    }

    private sealed record FilteredTagGroup(int Id, string FullName, List<TagListDto> Tags);

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

    public TagFilterChangedEventArgs GetCurrentFilter() => new()
    {
        IncludedTagIds = _tagStates
            .Where(kv => kv.Value == TagFilterState.Include)
            .Select(kv => kv.Key)
            .ToList(),
        ExcludedTagIds = _tagStates
            .Where(kv => kv.Value == TagFilterState.Exclude)
            .Select(kv => kv.Key)
            .ToList(),
        InclusionMode = _inclusionMode,
        ExclusionMode = _exclusionMode
    };
}
