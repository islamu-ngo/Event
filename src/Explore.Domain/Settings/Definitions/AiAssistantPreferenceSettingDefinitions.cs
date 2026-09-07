namespace Explore.Domain.Settings.Definitions;

public static class AiAssistantPreferenceSettingDefinitions
{
    public static readonly SettingDefinition ShowNavbarButton = new(
        Key: "ai_assistant_preferences.show_navbar_button",
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "AiAssistantPreferences",
        Description: "Show the AI assistant button in the navbar for this user",
        MaxScope: SettingScope.User);

    public static IReadOnlyList<SettingDefinition> All => [ShowNavbarButton];
}
