namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

public class EventListSettingGroup : ISettingGroup
{
    public string BrowseMode { get; private set; } = "pagination";
    public int PageSize { get; private set; } = 12;
    public string DefaultLayout { get; private set; } = "DetailedList";
    public bool CardShowDate { get; private set; } = true;
    public bool CardShowLocation { get; private set; } = true;
    public bool CardShowOrganizer { get; private set; } = true;
    public bool CardShowDescription { get; private set; } = true;
    public bool CardShowTags { get; private set; } = true;
    public bool CardShowCategories { get; private set; } = true;
    public bool CardShowCapacity { get; private set; }
    public bool CardShowPrice { get; private set; } = true;
    public bool CardShowStatus { get; private set; } = true;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.EventList.BrowseMode,
        GovernanceSettingKeys.EventList.PageSize,
        GovernanceSettingKeys.EventList.DefaultLayout,
        GovernanceSettingKeys.EventList.Card.ShowDate,
        GovernanceSettingKeys.EventList.Card.ShowLocation,
        GovernanceSettingKeys.EventList.Card.ShowOrganizer,
        GovernanceSettingKeys.EventList.Card.ShowDescription,
        GovernanceSettingKeys.EventList.Card.ShowTags,
        GovernanceSettingKeys.EventList.Card.ShowCategories,
        GovernanceSettingKeys.EventList.Card.ShowCapacity,
        GovernanceSettingKeys.EventList.Card.ShowPrice,
        GovernanceSettingKeys.EventList.Card.ShowStatus
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.EventList.BrowseMode, out var browseMode))
        {
            var mode = SettingValueSerializer.DeserializeString(browseMode.Value, "pagination");
            BrowseMode = mode is "pagination" or "infinite-scroll" ? mode : "pagination";
        }

        if (settings.TryGetValue(GovernanceSettingKeys.EventList.PageSize, out var pageSize))
            PageSize = SettingValueSerializer.DeserializeInt(pageSize.Value, 12);

        if (settings.TryGetValue(GovernanceSettingKeys.EventList.DefaultLayout, out var layout))
        {
            var l = SettingValueSerializer.DeserializeString(layout.Value, "DetailedList");
            DefaultLayout = l is "CompactGrid" or "DetailedList" or "SingleRow" ? l : "DetailedList";
        }

        if (settings.TryGetValue(GovernanceSettingKeys.EventList.Card.ShowDate, out var showDate))
            CardShowDate = SettingValueSerializer.Deserialize(showDate.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.EventList.Card.ShowLocation, out var showLocation))
            CardShowLocation = SettingValueSerializer.Deserialize(showLocation.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.EventList.Card.ShowOrganizer, out var showOrganizer))
            CardShowOrganizer = SettingValueSerializer.Deserialize(showOrganizer.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.EventList.Card.ShowDescription, out var showDescription))
            CardShowDescription = SettingValueSerializer.Deserialize(showDescription.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.EventList.Card.ShowTags, out var showTags))
            CardShowTags = SettingValueSerializer.Deserialize(showTags.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.EventList.Card.ShowCategories, out var showCategories))
            CardShowCategories = SettingValueSerializer.Deserialize(showCategories.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.EventList.Card.ShowCapacity, out var showCapacity))
            CardShowCapacity = SettingValueSerializer.Deserialize(showCapacity.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.EventList.Card.ShowPrice, out var showPrice))
            CardShowPrice = SettingValueSerializer.Deserialize(showPrice.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.EventList.Card.ShowStatus, out var showStatus))
            CardShowStatus = SettingValueSerializer.Deserialize(showStatus.Value, true);
    }
}
