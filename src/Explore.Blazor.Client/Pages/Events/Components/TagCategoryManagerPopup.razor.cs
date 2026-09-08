using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Forms;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Events.Components;

public enum TagCategoryMode { Tags, Categories }

public partial class TagCategoryManagerPopup : ComponentBase
{
    [Inject] private ITagService TagService { get; set; } = default!;
    [Inject] private ICategoryService CategoryService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private ILogger<TagCategoryManagerPopup> Logger { get; set; } = default!;

    /// <summary>Whether the popup is visible.</summary>
    [Parameter] public bool Visible { get; set; }

    /// <summary>Two-way binding callback for Visible.</summary>
    [Parameter] public EventCallback<bool> VisibleChanged { get; set; }

    /// <summary>Whether to manage tags or categories.</summary>
    [Parameter] public TagCategoryMode Mode { get; set; }

    /// <summary>IDs of currently applied items (tags or categories).</summary>
    [Parameter] public IReadOnlyCollection<Guid> InitialAppliedIds { get; set; } = Array.Empty<Guid>();

    /// <summary>Fires when the user clicks Save with the new set of applied IDs.</summary>
    [Parameter] public EventCallback<IReadOnlyCollection<Guid>> OnSaved { get; set; }

    public record TagCategoryItem(Guid Id, string Name);

    private List<TagCategoryItem> _applied = new();
    private List<TagCategoryItem> _available = new();
    private HashSet<Guid> _originalAppliedIds = new();
    private bool _isLoading;
    private FormSubmitState _submitState = new();
    private bool _previousVisible;

    private bool HasChanges =>
        !_applied.Select(x => x.Id).OrderBy(x => x)
            .SequenceEqual(_originalAppliedIds.OrderBy(x => x));

    protected override async Task OnParametersSetAsync()
    {
        if (Visible && !_previousVisible)
        {
            _previousVisible = true;
            await LoadItemsAsync();
        }
        else if (!Visible && _previousVisible)
        {
            _previousVisible = false;
        }
    }

    private async Task LoadItemsAsync()
    {
        _isLoading = true;
        _submitState = new();

        try
        {
            var initialIds = new HashSet<Guid>(InitialAppliedIds);
            _originalAppliedIds = new HashSet<Guid>(initialIds);

            if (Mode == TagCategoryMode.Tags)
            {
                var allTags = await TagService.GetAllTagsAsync();
                var tagItems = allTags
                    .Where(t => t.Id.HasValue && !string.IsNullOrEmpty(t.FullName))
                    .Select(t => new TagCategoryItem(t.Id!.Value, t.FullName!))
                    .OrderBy(t => t.Name)
                    .ToList();

                _applied = tagItems.Where(t => initialIds.Contains(t.Id)).ToList();
                _available = tagItems.Where(t => !initialIds.Contains(t.Id)).ToList();
            }
            else
            {
                var allCategories = await CategoryService.GetAllCategoriesAsync();
                var catItems = allCategories
                    .Where(c => c.Id.HasValue && !string.IsNullOrEmpty(c.FullName))
                    .Select(c => new TagCategoryItem(c.Id!.Value, c.FullName!))
                    .OrderBy(c => c.Name)
                    .ToList();

                _applied = catItems.Where(c => initialIds.Contains(c.Id)).ToList();
                _available = catItems.Where(c => !initialIds.Contains(c.Id)).ToList();
            }
        }
        catch (Exception ex)
        {
            var label = Mode == TagCategoryMode.Tags ? "tags" : "categories";
            Logger.LogError(ex, "Error loading {Label} for management", label);
            Snackbar.Add($"Failed to load {label}.", Severity.Error);
            await Close();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void RemoveItem(TagCategoryItem item)
    {
        _applied.Remove(item);
        if (!_available.Any(a => a.Id == item.Id))
        {
            _available.Add(item);
            _available = _available.OrderBy(x => x.Name).ToList();
        }
    }

    private void AddItem(TagCategoryItem item)
    {
        _available.Remove(item);
        if (!_applied.Any(a => a.Id == item.Id))
        {
            _applied.Add(item);
        }
    }

    private async Task HandleSave()
    {
        _submitState.Start();

        try
        {
            var newIds = (IReadOnlyCollection<Guid>)_applied.Select(x => x.Id).ToList().AsReadOnly();
            await OnSaved.InvokeAsync(newIds);
            await Close();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving tag/category changes");
            Snackbar.Add("Failed to save changes.", Severity.Error);
        }
        finally
        {
            _submitState.Complete();
        }
    }

    private void HandleOverlayClick() => _ = Close();

    private void HandleClose() => _ = Close();

    private async Task Close()
    {
        Visible = false;
        _previousVisible = false;
        await VisibleChanged.InvokeAsync(false);
    }
}
