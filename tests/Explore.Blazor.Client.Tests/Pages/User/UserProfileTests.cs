using Explore.Blazor.Client.Pages.User;

namespace Explore.Blazor.Client.Tests.Pages.User;

public class UserProfileTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IUserService _userService;
    private readonly IEventService _eventService;
    private readonly IOrganizationReviewService _reviewService;
    private readonly IUserSettingsService _userSettingsService;

    public UserProfileTests()
    {
        _ctx = new BlazorTestContext();
        _userSettingsService = Substitute.For<IUserSettingsService>();
        _ctx.AddShellStateMocks();
        _userService = Substitute.For<IUserService>();
        _eventService = Substitute.For<IEventService>();
        _reviewService = Substitute.For<IOrganizationReviewService>();

        _ctx.Services.AddSingleton(_userService);
        _ctx.Services.AddSingleton(_eventService);
        _ctx.Services.AddSingleton(_reviewService);
        _ctx.Services.AddSingleton(_userSettingsService);
        _ctx.Services.AddSingleton(Substitute.For<IContactShareConsentService>());
        _ctx.Services.AddSingleton(Substitute.For<ILogger<UserProfile>>());

        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User", "test@example.com");
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task UserProfile_ShowsLoadingState_WhileUserLookupIsPending()
    {
        // Arrange
        var pendingUser = new TaskCompletionSource<UserDto?>();
        _userService.GetCurrentUserAsync().Returns(pendingUser.Task);

        // Act
        var cut = _ctx.RenderMudComponent<UserProfile>();

        // Assert
        await Assert.That(cut.Markup).Contains("Loading profile...");

        // Cleanup
        pendingUser.TrySetResult(new UserDto { Id = Guid.NewGuid(), FirstName = "Test", LastName = "User" });
    }

    [Test]
    public async Task UserProfile_ShowsErrorState_WhenLoadThrows()
    {
        // Arrange
        _userService.GetCurrentUserAsync().ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var cut = _ctx.RenderMudComponent<UserProfile>();
        cut.WaitForState(() => cut.Markup.Contains("Unable to load your profile", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Unable to load your profile. Please try again.");
        await Assert.That(cut.Markup).Contains("Retry");
    }

    [Test]
    public async Task UserProfile_WhenLoadThrows_AnnouncesSafeErrorWithoutProviderDetail()
    {
        const string providerDetail = "pds-provider-private-detail";
        _userService.GetCurrentUserAsync().ThrowsAsync(new InvalidOperationException(providerDetail));

        var cut = _ctx.RenderMudComponent<UserProfile>();
        cut.WaitForState(() => cut.Markup.Contains("Retry", StringComparison.Ordinal));

        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("Unable to load your profile");
        await Assert.That(cut.Markup).DoesNotContain(providerDetail);
    }

    [Test]
    public async Task UserProfile_ShowsFallbackError_WhenUserStillNullAfterSync()
    {
        // Arrange
        _userService.GetCurrentUserAsync().Returns((UserDto?)null);
        _userService.SyncUserAsync().Returns(new BaseCommandResponseOfGuid { Success = false, Message = "sync failed" });

        // Act
        var cut = _ctx.RenderMudComponent<UserProfile>();
        cut.WaitForState(() => cut.Markup.Contains("Unable to load user profile", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Unable to load user profile. Please try refreshing the page.");
    }

    [Test]
    public async Task UserProfile_ShowsUserAndStats_WhenDataLoadsSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _userService.GetCurrentUserAsync().Returns(new UserDto
        {
            Id = userId,
            FirstName = "Amina",
            LastName = "Rahman",
            ActorHandle = "amina",
            Email = "amina@example.com",
            EmailVerified = true
        });

        _reviewService.GetReviewsByUserId(userId).Returns(
        [
            new OrganizationReviewDto
            {
                Id = Guid.NewGuid(),
                OrganizationFullName = "Community Center",
                Comment = "Great event",
                Rating = 5,
                CreatedAt = TestTime.UtcNow
            }
        ]);

        // Act
        var cut = _ctx.RenderMudComponent<UserProfile>();
        cut.WaitForState(() => cut.Markup.Contains("Amina Rahman", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Amina Rahman");
        await Assert.That(cut.Markup).Contains("amina@example.com");
        await Assert.That(cut.Markup).Contains("Email Verified");
        cut.FindAll("[role='tab']")
            .First(tab => tab.TextContent.Contains("Reviews", StringComparison.OrdinalIgnoreCase))
            .Click();

        await Assert.That(cut.Markup).Contains("Community Center");
    }

    [Test]
    public async Task UserProfile_AtprotoConsent_UpdatesOnlyPersonalPublicationSetting()
    {
        var userId = Guid.NewGuid();
        ConfigureUser(userId);
        _userSettingsService.GetSettingsAsync("AtprotoFederation", Arg.Any<CancellationToken>())
            .Returns(CreateAtprotoSettings(eventsEnabled: true, consentCanEdit: true));
        _userSettingsService.UpdateSettingAsync(
                "federation.atproto_publish_my_events",
                "true",
                Arg.Any<CancellationToken>())
            .Returns(true);

        var cut = _ctx.RenderMudComponent<UserProfile>();
        cut.WaitForState(() => cut.Markup.Contains("Amina Rahman", StringComparison.OrdinalIgnoreCase));
        cut.FindAll("[role='tab']")
            .Single(tab => tab.TextContent.Contains("AT Protocol", StringComparison.OrdinalIgnoreCase))
            .Click();

        var consent = cut.Find("input[type='checkbox']");
        await Assert.That(consent.HasAttribute("checked")).IsFalse();
        await Assert.That(consent.Closest("label")?.TextContent)
            .Contains("Publish my eligible events to my PDS");

        consent.Change(true);

        await _userSettingsService.Received(1).UpdateSettingAsync(
            "federation.atproto_publish_my_events",
            "true",
            Arg.Any<CancellationToken>());
        await _userSettingsService.DidNotReceive().UpdateSettingAsync(
            "federation.atproto_events_enabled",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await Assert.That(cut.Markup).Contains("local event is always committed first", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Find("[role='status']").TextContent)
            .Contains("publication preference saved", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task UserProfile_AtprotoConsent_WhenLocked_DisablesControlAndExplainsWhy()
    {
        ConfigureUser(Guid.NewGuid());
        _userSettingsService.GetSettingsAsync("AtprotoFederation", Arg.Any<CancellationToken>())
            .Returns(CreateAtprotoSettings(
                eventsEnabled: false,
                consentCanEdit: false,
                reason: "Publication consent is locked by policy."));

        var cut = _ctx.RenderMudComponent<UserProfile>();
        cut.WaitForState(() => cut.Markup.Contains("Amina Rahman", StringComparison.OrdinalIgnoreCase));
        cut.FindAll("[role='tab']")
            .Single(tab => tab.TextContent.Contains("AT Protocol", StringComparison.OrdinalIgnoreCase))
            .Click();

        await Assert.That(cut.Find("input[type='checkbox']").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Markup).Contains("Publication consent is locked by policy.");
        await Assert.That(cut.Markup).Contains("no events are fetched or published", StringComparison.OrdinalIgnoreCase);
        await _userSettingsService.DidNotReceive().UpdateSettingAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UserProfile_AtprotoConsent_WithMalformedValue_DefaultsOff()
    {
        ConfigureUser(Guid.NewGuid());
        _userSettingsService.GetSettingsAsync("AtprotoFederation", Arg.Any<CancellationToken>())
            .Returns(CreateAtprotoSettings(
                eventsEnabled: true,
                consentCanEdit: true,
                consentValue: "not-a-boolean"));

        var cut = _ctx.RenderMudComponent<UserProfile>();
        cut.WaitForState(() => cut.Markup.Contains("Amina Rahman", StringComparison.OrdinalIgnoreCase));
        cut.FindAll("[role='tab']")
            .Single(tab => tab.TextContent.Contains("AT Protocol", StringComparison.OrdinalIgnoreCase))
            .Click();

        await Assert.That(cut.Find("input[type='checkbox']").HasAttribute("checked")).IsFalse();
        await _userSettingsService.DidNotReceive().UpdateSettingAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UserProfile_AtprotoConsent_WhenDisposed_CancelsPendingSave()
    {
        ConfigureUser(Guid.NewGuid());
        _userSettingsService.GetSettingsAsync("AtprotoFederation", Arg.Any<CancellationToken>())
            .Returns(CreateAtprotoSettings(eventsEnabled: true, consentCanEdit: true));
        var saveCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedToken = CancellationToken.None;
        _userSettingsService.UpdateSettingAsync(
                "federation.atproto_publish_my_events",
                "true",
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observedToken = call.ArgAt<CancellationToken>(2);
                return saveCompletion.Task;
            });

        var cut = _ctx.RenderMudComponent<UserProfile>();
        cut.WaitForState(() => cut.Markup.Contains("Amina Rahman", StringComparison.OrdinalIgnoreCase));
        cut.FindAll("[role='tab']")
            .Single(tab => tab.TextContent.Contains("AT Protocol", StringComparison.OrdinalIgnoreCase))
            .Click();

        Task? changeTask = null;
        var componentDisposed = false;
        try
        {
            changeTask = cut.Find("input[type='checkbox']")
                .ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = true });

            await Assert.That(observedToken.CanBeCanceled).IsTrue();
            cut.Instance.Dispose();
            componentDisposed = true;
            await Assert.That(observedToken.IsCancellationRequested).IsTrue();
        }
        finally
        {
            if (!componentDisposed)
            {
                cut.Instance.Dispose();
            }
            saveCompletion.TrySetResult(false);
            if (changeTask is not null)
            {
                await changeTask;
            }
        }
    }

    private void ConfigureUser(Guid userId)
    {
        _userService.GetCurrentUserAsync().Returns(new UserDto
        {
            Id = userId,
            FirstName = "Amina",
            LastName = "Rahman",
            Email = "amina@example.com"
        });
        _eventService.GetMyEventsAsync().Returns([]);
        _reviewService.GetReviewsByUserId(userId).Returns([]);
    }

    private static SettingGroupResponseDto CreateAtprotoSettings(
        bool eventsEnabled,
        bool consentCanEdit,
        string? reason = null,
        string consentValue = "false") =>
        new()
        {
            Category = "AtprotoFederation",
            Settings =
            [
                new EffectiveSettingDto
                {
                    Key = "federation.atproto_events_enabled",
                    Value = eventsEnabled ? "true" : "false",
                    CanEdit = false
                },
                new EffectiveSettingDto
                {
                    Key = "federation.atproto_publish_my_events",
                    Value = consentValue,
                    CanEdit = consentCanEdit,
                    Reason = reason
                }
            ]
        };
}
