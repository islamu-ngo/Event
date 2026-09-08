using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Pages.Events.Components;

public partial class EventListCustomizationDrawer : ComponentBase
{
    /// <summary>Current user's effective settings for the event-list category.</summary>
    [Parameter] public ICollection<EffectiveSettingDto>? Settings { get; set; }

    /// <summary>Fired when the user changes a setting. Key=setting key, Value=new value.</summary>
    [Parameter] public EventCallback<Dictionary<string, string>> OnSettingsChanged { get; set; }

    /// <summary>Fired when the user requests a reset to defaults.</summary>
    [Parameter] public EventCallback OnResetRequested { get; set; }

    /// <summary>Fired when the user clicks the close button.</summary>
    [Parameter] public EventCallback OnCloseRequested { get; set; }

    /// <summary>Whether a save operation is in progress (debounced autosave).</summary>
    [Parameter] public bool IsSaving { get; set; }

    // ── Setting Keys (matches GovernanceSettingKeys.EventList) ──
    private const string KeyBrowseMode = "event_list.browse_mode";
    private const string KeyPageSize = "event_list.page_size";
    private const string KeyDefaultLayout = "event_list.default_layout";
    private const string KeyShowDate = "event_list.card.show_date";
    private const string KeyShowLocation = "event_list.card.show_location";
    private const string KeyShowOrganizer = "event_list.card.show_organizer";
    private const string KeyShowDescription = "event_list.card.show_description";
    private const string KeyShowTags = "event_list.card.show_tags";
    private const string KeyShowCategories = "event_list.card.show_categories";
    private const string KeyShowCapacity = "event_list.card.show_capacity";
    private const string KeyShowPrice = "event_list.card.show_price";
    private const string KeyShowStatus = "event_list.card.show_status";

    private static readonly int[] PageSizeOptions = [12, 20, 50];

    private sealed record CardFieldInfo(string Key, string Label, string Icon);

    private static readonly CardFieldInfo[] CardFields =
    [
        new(KeyShowDate, "Date", Icons.Material.Outlined.CalendarToday),
        new(KeyShowLocation, "Location", Icons.Material.Outlined.LocationOn),
        new(KeyShowOrganizer, "Organizer", Icons.Material.Outlined.Person),
        new(KeyShowDescription, "Description", Icons.Material.Outlined.Description),
        new(KeyShowPrice, "Price", Icons.Material.Outlined.Sell),
        new(KeyShowStatus, "Status", Icons.Material.Outlined.Circle),
        new(KeyShowTags, "Tags", Icons.Material.Outlined.Label),
        new(KeyShowCategories, "Categories", Icons.Material.Outlined.Category),
        new(KeyShowCapacity, "Capacity", Icons.Material.Outlined.People),
    ];

    // ── Computed Values ──

    private string BrowseModeValue => GetStringValue(KeyBrowseMode, "pagination");
    private bool IsPaginationMode => string.Equals(BrowseModeValue, "pagination", StringComparison.OrdinalIgnoreCase);
    private int PageSizeValue => int.TryParse(GetStringValue(KeyPageSize, "20"), out var ps) ? ps : 20;
    private string LayoutValue => GetStringValue(KeyDefaultLayout, "DetailedList");

    // ── Helpers ──

    private EffectiveSettingDto? GetSetting(string key)
        => Settings?.FirstOrDefault(s => s.Key == key);

    private string GetStringValue(string key, string defaultValue)
    {
        var setting = GetSetting(key);
        return !string.IsNullOrEmpty(setting?.Value) ? setting.Value : defaultValue;
    }

    private bool GetBoolValue(string key, bool defaultValue = true)
    {
        var setting = GetSetting(key);
        if (setting == null || string.IsNullOrEmpty(setting.Value)) return defaultValue;
        return bool.TryParse(setting.Value, out var result) ? result : defaultValue;
    }

    private bool IsLocked(string key)
    {
        var setting = GetSetting(key);
        return setting?.IsLocked == true;
    }

    private string? GetLockReason(string key)
    {
        var setting = GetSetting(key);
        if (setting?.IsLocked != true) return null;
        return setting.Reason ?? "This setting is locked by an administrator";
    }

    // ── Handlers ──

    private Task HandleClose()
        => OnCloseRequested.InvokeAsync();

    private Task HandleBrowseModeChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Task.CompletedTask;
        return EmitChange(KeyBrowseMode, value);
    }

    private Task HandlePageSizeChanged(int value)
        => EmitChange(KeyPageSize, value.ToString());

    private Task HandleLayoutChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Task.CompletedTask;
        return EmitChange(KeyDefaultLayout, value);
    }

    private Task HandleFieldToggled(string key, bool value)
        => EmitChange(key, value.ToString().ToLowerInvariant());

    private Task HandleReset()
        => OnResetRequested.InvokeAsync();

    private Task EmitChange(string key, string value)
        => OnSettingsChanged.InvokeAsync(new Dictionary<string, string> { [key] = value });
}
