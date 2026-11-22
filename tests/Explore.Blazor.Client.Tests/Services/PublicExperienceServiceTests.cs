using NSubstitute.Extensions;

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

    [Test]
    public async Task SettingsNotificationObservesTheInstalledSnapshotWithoutRefetching()
    {
        var settings = SettingsWithIdentity("Current directory") with { TenantId = Guid.CreateVersion7() };
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(settings);
        Task<PublicExperienceSettingsDto?>? observed = null;
        PublicExperienceAvailability availability = PublicExperienceAvailability.Unknown;
        _service.SettingsChanged += () =>
        {
            availability = _service.SettingsAvailability;
            observed = _service.GetCachedSettingsAsync();
        };

        await _service.GetSettingsAsync();

        await Assert.That(observed is not null).IsTrue();
        await Assert.That(await observed!).IsSameReferenceAs(settings);
        await Assert.That(availability).IsEqualTo(PublicExperienceAvailability.Available);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("edition")]
    [Arguments("epoch")]
    [Arguments("message")]
    [Arguments("link-text")]
    [Arguments("link-url")]
    [Arguments("enabled")]
    public async Task RelevantSettingsChangesNotifyOnceAndEquivalentRefreshDoesNotNotify(string field)
    {
        var original = SettingsWithIdentity("Current directory") with
        {
            TenantId = AnnouncementTestData.Tenant,
            Announcement = AnnouncementTestData.Settings(message: "Original").Announcement
        };
        var announcement = original.Announcement
            ?? throw new InvalidOperationException("The fixture requires its public announcement.");
        var changed = field switch
        {
            "tenant" => original with
            {
                TenantId = AnnouncementTestData.OtherTenant,
                Announcement = AnnouncementTestData.Settings(tenant: AnnouncementTestData.OtherTenant, message: "Original").Announcement
            },
            "edition" => original with
            {
                Announcement = AnnouncementTestData.Settings(edition: AnnouncementTestData.OtherEdition, message: "Original").Announcement
            },
            "epoch" => original with
            {
                Announcement = AnnouncementTestData.Settings(epoch: AnnouncementTestData.OtherEpoch, message: "Original").Announcement
            },
            "message" => original with { Announcement = announcement with
            {
                Material = new() { Message = "Replacement", LinkText = string.Empty, LinkUrl = string.Empty }
            } },
            "link-text" => original with { Announcement = announcement with
            {
                Material = new() { Message = "Original", LinkText = "Details", LinkUrl = string.Empty }
            } },
            "link-url" => original with { Announcement = announcement with
            {
                Material = new() { Message = "Original", LinkText = string.Empty, LinkUrl = "/events" }
            } },
            "enabled" => original with { Announcement = announcement with { IsVisible = false } },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(original, changed, changed with { EnabledModules = ["Islamic"] });
        await _service.GetSettingsAsync();
        var notifications = new List<Task<PublicExperienceSettingsDto?>>();
        _service.SettingsChanged += () => notifications.Add(_service.GetCachedSettingsAsync());

        await _service.GetSettingsAsync();
        await _service.GetSettingsAsync();

        await Assert.That(notifications.Count).IsEqualTo(1);
        await Assert.That(await notifications.Single()).IsSameReferenceAs(changed);
    }

    [Test]
    public async Task RepeatedUnavailableSettingsNotifyOnceAndRecoveryInstallsCurrentIdentity()
    {
        var original = SettingsWithIdentity("Original directory");
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(original);
        await _service.GetSettingsAsync();
        var availability = new List<PublicExperienceAvailability>();
        _service.SettingsChanged += () => availability.Add(_service.SettingsAvailability);
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<PublicExperienceSettingsDto>>(_ => throw new HttpRequestException("Unavailable"));

        await _service.GetSettingsAsync();
        await _service.GetSettingsAsync();
        var recovered = SettingsWithIdentity("Recovered directory");
        _apiClient.Configure().GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(recovered);
        await _service.GetSettingsAsync();

        await Assert.That(availability.Count).IsEqualTo(2);
        await Assert.That(availability[0]).IsEqualTo(PublicExperienceAvailability.Unavailable);
        await Assert.That(availability[1]).IsEqualTo(PublicExperienceAvailability.Available);
        await Assert.That(await _service.GetCachedSettingsAsync()).IsSameReferenceAs(recovered);
    }

    [Test]
    public async Task MissingDirectoryIdentityInvalidatesAvailabilityBeforeNotification()
    {
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(SettingsWithIdentity("Original directory"));
        await _service.GetSettingsAsync();
        var observed = new List<PublicExperienceAvailability>();
        _service.SettingsChanged += () => observed.Add(_service.SettingsAvailability);
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(new PublicExperienceSettingsDto());

        await _service.GetSettingsAsync();
        var cached = await _service.GetCachedSettingsAsync();

        await Assert.That(observed.Count).IsEqualTo(1);
        await Assert.That(observed.Single()).IsEqualTo(PublicExperienceAvailability.Unavailable);
        await Assert.That(cached).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OlderSettingsSuccessCannotReplaceNewerSettingsOrReachItsCaller(bool cachedCaller)
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<PublicExperienceSettingsDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var older = SettingsWithIdentity("Earlier directory") with
        {
            TenantId = AnnouncementTestData.Tenant, Announcement = AnnouncementTestData.Settings().Announcement
        };
        var newer = SettingsWithIdentity("Current directory") with
        {
            TenantId = AnnouncementTestData.OtherTenant,
            Announcement = AnnouncementTestData.Settings(tenant: AnnouncementTestData.OtherTenant,
                edition: AnnouncementTestData.OtherEdition).Announcement
        };
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return response.Task;
            }, _ => Task.FromResult(newer));
        int notifications = 0;
        _service.SettingsChanged += () => notifications++;
        var pending = cachedCaller ? _service.GetCachedSettingsAsync() : _service.GetSettingsAsync();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var current = await _service.GetSettingsAsync();
        response.SetResult(older);
        var stale = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(current).IsSameReferenceAs(newer);
        await Assert.That(stale).IsNull();
        await Assert.That(await _service.GetCachedSettingsAsync()).IsSameReferenceAs(newer);
        await Assert.That(_service.SettingsAvailability).IsEqualTo(PublicExperienceAvailability.Available);
        await Assert.That(notifications).IsEqualTo(1);
    }

    [Test]
    [Arguments("missing-identity")]
    [Arguments("api-failure")]
    [Arguments("transport-failure")]
    public async Task OlderSettingsFailureCannotInvalidateANewerVerifiedSnapshot(string outcome)
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<PublicExperienceSettingsDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newer = SettingsWithIdentity("Current directory") with
        {
            TenantId = AnnouncementTestData.OtherTenant,
            Announcement = AnnouncementTestData.Settings(tenant: AnnouncementTestData.OtherTenant,
                edition: AnnouncementTestData.OtherEdition).Announcement
        };
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return response.Task;
            }, _ => Task.FromResult(newer));
        int notifications = 0;
        _service.SettingsChanged += () => notifications++;
        var pending = _service.GetSettingsAsync();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await _service.GetSettingsAsync();
        switch (outcome)
        {
            case "missing-identity":
                response.SetResult(new PublicExperienceSettingsDto());
                break;
            case "api-failure":
                response.SetException(new ApiException("Unavailable", 503, string.Empty,
                    new Dictionary<string, IEnumerable<string>>(), null));
                break;
            case "transport-failure":
                response.SetException(new HttpRequestException("Unavailable"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
        await Assert.That(await _service.GetCachedSettingsAsync()).IsSameReferenceAs(newer);
        await Assert.That(_service.SettingsAvailability).IsEqualTo(PublicExperienceAvailability.Available);
        await Assert.That(notifications).IsEqualTo(1);
    }

    [Test]
    [Arguments("success")]
    [Arguments("missing-identity")]
    [Arguments("api-failure")]
    [Arguments("transport-failure")]
    public async Task ResetRejectsPreResetSettingsCompletionAndKeepsTheNextReadFresh(string outcome)
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<PublicExperienceSettingsDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var earlier = SettingsWithIdentity("Earlier directory") with
        {
            TenantId = AnnouncementTestData.Tenant, Announcement = AnnouncementTestData.Settings().Announcement
        };
        var current = SettingsWithIdentity("Current directory") with
        {
            TenantId = AnnouncementTestData.OtherTenant,
            Announcement = AnnouncementTestData.Settings(tenant: AnnouncementTestData.OtherTenant,
                edition: AnnouncementTestData.OtherEdition).Announcement
        };
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return response.Task;
            }, _ => Task.FromResult(current));
        int notifications = 0;
        _service.SettingsChanged += () => notifications++;
        var pending = _service.GetCachedSettingsAsync();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        _service.ResetCache();
        switch (outcome)
        {
            case "success":
                response.SetResult(earlier);
                break;
            case "missing-identity":
                response.SetResult(new PublicExperienceSettingsDto());
                break;
            case "api-failure":
                response.SetException(new ApiException("Unavailable", 503, string.Empty,
                    new Dictionary<string, IEnumerable<string>>(), null));
                break;
            case "transport-failure":
                response.SetException(new HttpRequestException("Unavailable"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
        await Assert.That(_service.SettingsAvailability).IsEqualTo(PublicExperienceAvailability.Unknown);
        await Assert.That(notifications).IsEqualTo(1);
        await Assert.That(await _service.GetCachedSettingsAsync()).IsSameReferenceAs(current);
        await Assert.That(_service.SettingsAvailability).IsEqualTo(PublicExperienceAvailability.Available);
        await Assert.That(notifications).IsEqualTo(2);
    }

    [Test]
    public async Task ANewerPendingSettingsRequestAlreadyOwnsPublication()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResponse = new TaskCompletionSource<PublicExperienceSettingsDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<PublicExperienceSettingsDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newer = SettingsWithIdentity("Current directory") with
        {
            TenantId = AnnouncementTestData.OtherTenant,
            Announcement = AnnouncementTestData.Settings(tenant: AnnouncementTestData.OtherTenant,
                edition: AnnouncementTestData.OtherEdition).Announcement
        };
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                firstStarted.TrySetResult();
                return firstResponse.Task;
            }, _ =>
            {
                secondStarted.TrySetResult();
                return secondResponse.Task;
            });
        int notifications = 0;
        _service.SettingsChanged += () => notifications++;
        var first = _service.GetSettingsAsync();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = _service.GetSettingsAsync();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        firstResponse.SetResult(SettingsWithIdentity("Earlier directory") with
        {
            TenantId = AnnouncementTestData.Tenant, Announcement = AnnouncementTestData.Settings().Announcement
        });

        await Assert.That(await first.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
        await Assert.That(_service.SettingsAvailability).IsEqualTo(PublicExperienceAvailability.Unknown);
        await Assert.That(notifications).IsEqualTo(0);
        secondResponse.SetResult(newer);
        await Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(10))).IsSameReferenceAs(newer);
        await Assert.That(await _service.GetCachedSettingsAsync()).IsSameReferenceAs(newer);
        await Assert.That(notifications).IsEqualTo(1);
    }

    [Test]
    public async Task ResetDuringSettingsNotificationPreventsReturningPreResetIdentity()
    {
        var settings = SettingsWithIdentity("Earlier directory") with { TenantId = Guid.CreateVersion7() };
        _apiClient.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(settings);
        bool reset = false;
        int notifications = 0;
        _service.SettingsChanged += () =>
        {
            notifications++;
            if (!reset)
            {
                reset = true;
                _service.ResetCache();
            }
        };

        var result = await _service.GetSettingsAsync();

        await Assert.That(result).IsNull();
        await Assert.That(_service.SettingsAvailability).IsEqualTo(PublicExperienceAvailability.Unknown);
        await Assert.That(notifications).IsEqualTo(2);
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
