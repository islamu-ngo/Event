namespace Explore.Domain.Settings.Definitions;

public static class EventListSettingDefinitions
{
    public static readonly SettingDefinition BrowseMode = new(
        Key: "event_list.browse_mode",
        ValueType: SettingValueType.String,
        DefaultValue: "\"pagination\"",
        Category: "EventList",
        Description: "Browse mode for the event list. Allowed values: pagination, infinite-scroll",
        MaxScope: SettingScope.User,
        AllowedValues: ["pagination", "infinite-scroll"]);

    public static readonly SettingDefinition PageSize = new(
        Key: "event_list.page_size",
        ValueType: SettingValueType.Integer,
        DefaultValue: "12",
        Category: "EventList",
        Description: "Number of events displayed per page in pagination mode",
        MaxScope: SettingScope.User);

    public static readonly SettingDefinition DefaultLayout = new(
        Key: "event_list.default_layout",
        ValueType: SettingValueType.String,
        DefaultValue: "\"DetailedList\"",
        Category: "EventList",
        Description: "Default layout mode for the event list. Allowed values: CompactGrid, DetailedList, SingleRow",
        MaxScope: SettingScope.User,
        AllowedValues: ["CompactGrid", "DetailedList", "SingleRow"]);

    public static readonly SettingDefinition CardShowDate = new(
        Key: "event_list.card.show_date",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "EventList",
        Description: "Show event date on the event card",
        MaxScope: SettingScope.User);

    public static readonly SettingDefinition CardShowLocation = new(
        Key: "event_list.card.show_location",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "EventList",
        Description: "Show location information on the event card",
        MaxScope: SettingScope.User);

    public static readonly SettingDefinition CardShowOrganizer = new(
        Key: "event_list.card.show_organizer",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "EventList",
        Description: "Show organizer name on the event card",
        MaxScope: SettingScope.User);

    public static readonly SettingDefinition CardShowDescription = new(
        Key: "event_list.card.show_description",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "EventList",
        Description: "Show description snippet on the event card",
        MaxScope: SettingScope.User);

    public static readonly SettingDefinition CardShowTags = new(
        Key: "event_list.card.show_tags",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "EventList",
        Description: "Show tag chips on the event card",
        MaxScope: SettingScope.User);

    public static readonly SettingDefinition CardShowCategories = new(
        Key: "event_list.card.show_categories",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "EventList",
        Description: "Show category chips on the event card",
        MaxScope: SettingScope.User);

    public static readonly SettingDefinition CardShowCapacity = new(
        Key: "event_list.card.show_capacity",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "EventList",
        Description: "Show capacity and registration count on the event card",
        MaxScope: SettingScope.User);

    public static readonly SettingDefinition CardShowPrice = new(
        Key: "event_list.card.show_price",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "EventList",
        Description: "Show price indicator on the event card",
        MaxScope: SettingScope.User);

    public static readonly SettingDefinition CardShowStatus = new(
        Key: "event_list.card.show_status",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "EventList",
        Description: "Show event status badge on the event card",
        MaxScope: SettingScope.User);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        BrowseMode, PageSize, DefaultLayout,
        CardShowDate, CardShowLocation, CardShowOrganizer, CardShowDescription,
        CardShowTags, CardShowCategories, CardShowCapacity, CardShowPrice, CardShowStatus
    ];
}
