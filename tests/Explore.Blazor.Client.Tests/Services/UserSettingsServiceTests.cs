using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class UserSettingsServiceTests
{
    [Test]
    public async Task GetSettingsAsync_UsesGeneratedClient_WhenAuthenticated()
    {
        var expected = new SettingGroupResponseDto
        {
            Category = "AiAssistantPreferences",
            Settings =
            [
                new EffectiveSettingDto
                {
                    Key = "ai_assistant_preferences.show_navbar_button",
                    Value = "true",
                    SettingValueTypeId = 1,
                    SettingValueTypeCode = "Boolean",
                    SettingValueTypeName = "Boolean",
                    Source = SettingSource.UserPreference,
                    IsLocked = false,
                    CanEdit = true
                }
            ]
        };
        var apiClient = Substitute.For<ISettingsClient>();
        apiClient.GetUserSettingsAsync(
                "AiAssistantPreferences",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var authState = Substitute.For<AuthenticationStateProvider>();
        authState.GetAuthenticationStateAsync().Returns(
            new AuthenticationState(new ClaimsPrincipal(
                new ClaimsIdentity(authenticationType: "TestAuth"))));
        var service = new UserSettingsService(
            apiClient,
            authState,
            Substitute.For<IJSRuntime>(),
            Substitute.For<ILogger<UserSettingsService>>());

        var result = await service.GetSettingsAsync("AiAssistantPreferences");

        await Assert.That(result).IsSameReferenceAs(expected);
        var setting = result!.Settings.Single();
        await Assert.That(setting.Key).IsEqualTo("ai_assistant_preferences.show_navbar_button");
        await Assert.That(setting.Source).IsEqualTo(SettingSource.UserPreference);
    }
}
