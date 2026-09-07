namespace Explore.Blazor.Client.Tests.Services;

public class PublicExperienceServiceTests
{
    private readonly IPublicExperienceClient _apiClient;
    private readonly PublicExperienceService _service;

    public PublicExperienceServiceTests()
    {
        _apiClient = Substitute.For<IPublicExperienceClient>();
        _service = new PublicExperienceService(
            _apiClient,
            Substitute.For<ILogger<PublicExperienceService>>());
    }

    [Test]
    public async Task GetSettingsAsync_ReturnsGeneratedSettings_WhenApiSucceeds()
    {
        var expected = new PublicExperienceSettingsDto
        {
            TenantId = Guid.NewGuid(),
            Mode = PublicExperienceMode.OrganizationCentric,
            PreferredHomePage = "LandingPage"
        };
        _apiClient.GetPublicExperienceSettingsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _service.GetSettingsAsync();

        await Assert.That(result).IsSameReferenceAs(expected);
        await Assert.That(result!.Mode).IsEqualTo(PublicExperienceMode.OrganizationCentric);
        await Assert.That(result.PreferredHomePage).IsEqualTo("LandingPage");
    }

    [Test]
    public async Task GetSettingsAsync_ReturnsNull_WhenApiThrows()
    {
        _apiClient.GetPublicExperienceSettingsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<PublicExperienceSettingsDto>>(_ => throw new HttpRequestException("factory failure"));

        var result = await _service.GetSettingsAsync();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetShellAsync_ReturnsGeneratedShell_WhenApiSucceeds()
    {
        var expected = new PublicExperienceShellDto
        {
            Mode = PublicExperienceMode.OrganizationCentric,
            PrimaryOrganization = new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.Available,
                DisplayName = "Community Center"
            },
            EventCatalog = new PublicExperienceEventCatalogDto
            {
                Label = "Programs",
                Url = "/events?ActorId=11111111-1111-1111-1111-111111111111"
            }
        };
        _apiClient.GetPublicExperienceShellAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _service.GetShellAsync();

        await Assert.That(result).IsSameReferenceAs(expected);
        await Assert.That(result!.PrimaryOrganization!.State)
            .IsEqualTo(PublicExperiencePrimaryOrganizationState.Available);
        await Assert.That(result.EventCatalog!.Label).IsEqualTo("Programs");
    }

    [Test]
    public async Task GetShellAsync_ReturnsNull_WhenApiRejectsRequest()
    {
        _apiClient.GetPublicExperienceShellAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<PublicExperienceShellDto>>(_ => throw new ApiException(
                "Server error",
                500,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                null));

        var result = await _service.GetShellAsync();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCachedSettingsAsync_ReusesSuccessfulResponse()
    {
        var expected = SettingsWithIdentity("Cached directory");
        _apiClient.GetPublicExperienceSettingsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var first = await _service.GetCachedSettingsAsync();
        var second = await _service.GetCachedSettingsAsync();

        await Assert.That(first).IsSameReferenceAs(expected);
        await Assert.That(second).IsSameReferenceAs(expected);
        await _apiClient.Received(1).GetPublicExperienceSettingsAsync(
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExpiredSettings503_DoesNotReturnStaleSentinelIdentityA()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero));
        var service = new PublicExperienceService(_apiClient, Substitute.For<ILogger<PublicExperienceService>>(), clock);
        PublicExperienceSettingsDto sentinel = SettingsWithIdentity("Sentinel identity A");
        int calls = 0;
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? Task.FromResult(sentinel)
                : Task.FromException<PublicExperienceSettingsDto>(new ApiException("Unavailable", 503, string.Empty, new Dictionary<string, IEnumerable<string>>(), null)));

        await Assert.That(await service.GetCachedSettingsAsync()).IsSameReferenceAs(sentinel);
        clock.Advance(TimeSpan.FromMinutes(6));

        await Assert.That(await service.GetCachedSettingsAsync()).IsNull();
        await Assert.That(service.SettingsAvailability).IsEqualTo(PublicExperienceAvailability.Unavailable);
    }

    [Test]
    public async Task ExpiredShellMissingIdentity_DoesNotReturnStaleSentinelIdentityA()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero));
        var service = new PublicExperienceService(_apiClient, Substitute.For<ILogger<PublicExperienceService>>(), clock);
        PublicExperienceShellDto sentinel = ShellWithIdentities("Sentinel identity A");
        _apiClient.GetPublicExperienceShellAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(sentinel), Task.FromResult(new PublicExperienceShellDto()));

        await Assert.That(await service.GetCachedShellAsync()).IsSameReferenceAs(sentinel);
        clock.Advance(TimeSpan.FromMinutes(6));

        await Assert.That(await service.GetCachedShellAsync()).IsNull();
        await Assert.That(service.ShellAvailability).IsEqualTo(PublicExperienceAvailability.Unavailable);
    }

    private static PublicExperienceSettingsDto SettingsWithIdentity(string name) => new()
    {
        PreferredHomePage = "LandingPage",
        DirectoryOperator = new TenantDirectoryOperatorPublicDto { PublicName = name, LegalName = name }
    };

    private static PublicExperienceShellDto ShellWithIdentities(string name) => new()
    {
        DirectoryOperator = SettingsWithIdentity(name).DirectoryOperator,
        InstanceOperator = new InstanceOperatorPublicDto { PublicName = "Instance A", LegalName = "Instance A legal" }
    };

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    [Test]
    [Arguments("LandingPage", "/home")]
    [Arguments("landingpage", "/home")]
    [Arguments("EventList", "/events")]
    public async Task ResolveHomeRoute_UsesPreferredHomePage(string preferredHomePage, string expectedRoute)
    {
        var route = _service.ResolveHomeRoute(new PublicExperienceSettingsDto
        {
            PreferredHomePage = preferredHomePage
        });

        await Assert.That(route).IsEqualTo(expectedRoute);
    }

    [Test]
    public async Task ResolveHomeRoute_ReturnsHome_ForAvailableOrganizationCentricShell()
    {
        var shell = new PublicExperienceShellDto
        {
            Mode = PublicExperienceMode.OrganizationCentric,
            PrimaryOrganization = new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.Available
            }
        };

        var route = _service.ResolveHomeRoute(shell);

        await Assert.That(route).IsEqualTo("/home");
    }

    [Test]
    public async Task ResolveHomeRoute_ReturnsHome_ForShellLandingPagePreference()
    {
        var shell = new PublicExperienceShellDto
        {
            Mode = PublicExperienceMode.DiscoveryCentric,
            Home = new PublicExperienceHomeDto { PreferredHomePage = "LandingPage" }
        };

        var route = _service.ResolveHomeRoute(shell);

        await Assert.That(route).IsEqualTo("/home");
    }

    [Test]
    public async Task ResolveHomeRoute_ReturnsEvents_ForMissingOrganizationOrNullShell()
    {
        var shell = new PublicExperienceShellDto
        {
            Mode = PublicExperienceMode.OrganizationCentric,
            PrimaryOrganization = new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.Missing
            }
        };

        await Assert.That(_service.ResolveHomeRoute(shell)).IsEqualTo("/events");
        await Assert.That(_service.ResolveHomeRoute((PublicExperienceShellDto?)null)).IsEqualTo("/events");
    }
}
