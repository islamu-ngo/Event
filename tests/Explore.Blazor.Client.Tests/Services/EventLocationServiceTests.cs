using System.Text.Json;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Services;

public class EventLocationServiceTests
{
    private readonly IEventLocationClient _apiClient = Substitute.For<IEventLocationClient>();
    private readonly EventLocationService _service;

    public EventLocationServiceTests() =>
        _service = new EventLocationService(
            _apiClient,
            Substitute.For<ILogger<EventLocationService>>());

    // ========== Public purpose ==========

    [Test]
    public async Task GetPublicAsync_CallsTheAnonymousPublicOperationOnly()
    {
        var eventId = Guid.NewGuid();
        _apiClient.GetPublicEventLocationsAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([new EventLocationPublicDto
            {
                EventLocationId = Guid.NewGuid(),
                State = EventLocationDisclosureState.Available,
                Fields = new EventLocationPublicFieldsDto { City = "Brussels", Country = "BE" }
            }]);

        IReadOnlyList<EventLocationPublicDto> result = await _service.GetPublicAsync(eventId);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Fields!.City).IsEqualTo("Brussels");
        await _apiClient.DidNotReceive().GetAttendeeEventLocationsAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _apiClient.DidNotReceive().GetManagementEventLocationAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetPublicAsync_ReturnsEmpty_WhenTheEventIsWithheld()
    {
        _apiClient.GetPublicEventLocationsAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new ApiException("Not found", 404, null, null!, null));

        IReadOnlyList<EventLocationPublicDto> result = await _service.GetPublicAsync(Guid.NewGuid());

        await Assert.That(result).IsEmpty();
    }

    // ========== Attendee purpose ==========

    [Test]
    public async Task GetMyAccessAsync_CallsTheAuthenticatedAttendeeOperationOnly()
    {
        var eventId = Guid.NewGuid();
        _apiClient.GetAttendeeEventLocationsAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([new EventLocationAttendeeDto
            {
                EventLocationId = Guid.NewGuid(),
                State = EventLocationDisclosureState.Available,
                Fields = new EventLocationAttendeeFieldsDto { StreetAddress = "Rue Neuve 1" }
            }]);

        IReadOnlyList<EventLocationAttendeeDto> result = await _service.GetMyAccessAsync(eventId);

        await Assert.That(result[0].Fields!.StreetAddress).IsEqualTo("Rue Neuve 1");
        await _apiClient.Received(1).GetAttendeeEventLocationsAsync(
            eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _apiClient.DidNotReceive().GetPublicEventLocationsAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetMyAccessAsync_ReturnsEmpty_WhenAccessIsDenied()
    {
        _apiClient.GetAttendeeEventLocationsAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new ApiException("Unauthorized", 401, null, null!, null));

        await Assert.That(await _service.GetMyAccessAsync(Guid.NewGuid())).IsEmpty();
    }

    // ========== Management purpose ==========

    [Test]
    public async Task GetManagementAsync_PreservesHalLinksAndTypedPolicy()
    {
        var eventId = Guid.NewGuid();
        var eventLocationId = Guid.NewGuid();
        _apiClient.GetManagementEventLocationAsync(
                eventId, eventLocationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfEventLocationManagementDto
            {
                EventLocationId = eventLocationId,
                State = EventLocationDisclosureState.Private_venue,
                PolicyVersion = 4,
                ConcurrencyStamp = Guid.NewGuid(),
                Policy = new EventLocationDisclosurePolicyDto { ShowCity = true, FullDetailsAudienceId = 3 },
                _links = new Dictionary<string, HalLink>
                {
                    ["self"] = new() { Href = "/api/events/x/locations/y/management" },
                    ["edit"] = new() { Href = "/api/events/x/locations/y/disclosure" }
                }
            });

        HalResourceOfEventLocationManagementDto? result =
            await _service.GetManagementAsync(eventId, eventLocationId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasLink(EventLocationLinkRelations.Edit)).IsTrue();
        await Assert.That(result.HasLink(EventLocationLinkRelations.Remediate)).IsFalse();
        await Assert.That(result.Policy!.ShowCity).IsEqualTo(true);
        await Assert.That(result.PolicyVersion).IsEqualTo(4);
    }

    [Test]
    public async Task GetManagementAsync_ReturnsNull_WhenManagementIsForbidden()
    {
        _apiClient.GetManagementEventLocationAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new ApiException("Forbidden", 403, null, null!, null));

        await Assert.That(await _service.GetManagementAsync(Guid.NewGuid(), Guid.NewGuid())).IsNull();
    }

    // ========== Review queue ==========

    [Test]
    public async Task GetReviewQueueAsync_KeepsPerRowAffordancesIndependent()
    {
        var eventId = Guid.NewGuid();
        _apiClient.GetEventLocationReviewQueueAsync(eventId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventLocationManagementDto
            {
                _embedded = new HalCollectionEmbeddedOfEventLocationManagementDto
                {
                    Items =
                    [
                        new HalResourceOfEventLocationManagementDto
                        {
                            EventLocationId = Guid.NewGuid(),
                            NeedsPrivacyReview = true,
                            _links = new Dictionary<string, HalLink>
                            {
                                ["remediate-location"] = new() { Href = "/remediate/1" }
                            }
                        },
                        new HalResourceOfEventLocationManagementDto
                        {
                            EventLocationId = Guid.NewGuid(),
                            NeedsPrivacyReview = true,
                            _links = new Dictionary<string, HalLink>()
                        }
                    ]
                }
            });

        IReadOnlyList<HalResourceOfEventLocationManagementDto> queue =
            await _service.GetReviewQueueAsync(eventId);

        await Assert.That(queue.Count).IsEqualTo(2);
        await Assert.That(queue[0].HasLink(EventLocationLinkRelations.Remediate)).IsTrue();
        await Assert.That(queue[1].HasLink(EventLocationLinkRelations.Remediate)).IsFalse();
    }

    [Test]
    public async Task GetReviewQueueAsync_ReturnsEmpty_WhenNothingIsEmbedded()
    {
        _apiClient.GetEventLocationReviewQueueAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventLocationManagementDto());

        await Assert.That(await _service.GetReviewQueueAsync(Guid.NewGuid())).IsEmpty();
    }

    // ========== Writes ==========

    [Test]
    public async Task UpdateDisclosureAsync_SendsTheConcurrencyTokensUnchanged()
    {
        var eventId = Guid.NewGuid();
        var eventLocationId = Guid.NewGuid();
        var stamp = Guid.NewGuid();
        var request = new UpdateEventLocationDisclosureDto
        {
            ExpectedPolicyVersion = 7,
            ExpectedConcurrencyStamp = stamp,
            Fields = new UpdateEventLocationDisclosureFieldsDto { ShowCity = true }
        };
        _apiClient.UpdateEventLocationDisclosureAsync(
                eventId, eventLocationId, request, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        BaseCommandResponseOfGuid response =
            await _service.UpdateDisclosureAsync(eventId, eventLocationId, request);

        await Assert.That(response.Success).IsEqualTo(true);
        await _apiClient.Received(1).UpdateEventLocationDisclosureAsync(
            eventId,
            eventLocationId,
            Arg.Is<UpdateEventLocationDisclosureDto>(body =>
                body.ExpectedPolicyVersion == 7 && body.ExpectedConcurrencyStamp == stamp),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateDisclosureAsync_ReturnsFailedCommand_OnConflict()
    {
        _apiClient.UpdateEventLocationDisclosureAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateEventLocationDisclosureDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Throws(new ApiException("Conflict", 409, null, null!, null));

        BaseCommandResponseOfGuid response = await _service.UpdateDisclosureAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UpdateEventLocationDisclosureDto());

        await Assert.That(response.Success).IsEqualTo(false);
        await Assert.That(response.Message).Contains("API error");
    }

    [Test]
    public async Task ConfirmRemediationAsync_PostsTheRemediationConfirmation()
    {
        var eventId = Guid.NewGuid();
        var eventLocationId = Guid.NewGuid();
        var request = new ConfirmEventLocationRemediationDto
        {
            ExpectedPolicyVersion = 2,
            ExpectedConcurrencyStamp = Guid.NewGuid()
        };
        _apiClient.ConfirmEventLocationRemediationAsync(
                eventId, eventLocationId, request, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        BaseCommandResponseOfGuid response =
            await _service.ConfirmRemediationAsync(eventId, eventLocationId, request);

        await Assert.That(response.Success).IsEqualTo(true);
        await _apiClient.Received(1).ConfirmEventLocationRemediationAsync(
            eventId, eventLocationId, request, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ========== Cancellation ==========

    [Test]
    public async Task GetManagementAsync_PropagatesCallerCancellation()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _apiClient.GetManagementEventLocationAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), source.Token)
            .Returns<Task<HalResourceOfEventLocationManagementDto>>(_ =>
                throw new OperationCanceledException(source.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _service.GetManagementAsync(Guid.NewGuid(), Guid.NewGuid(), source.Token));
    }

    // ========== Wire contract ==========

    [Test]
    [Arguments("hidden", EventLocationDisclosureState.Hidden)]
    [Arguments("to_be_announced", EventLocationDisclosureState.To_be_announced)]
    [Arguments("available", EventLocationDisclosureState.Available)]
    [Arguments("private_venue", EventLocationDisclosureState.Private_venue)]
    [Arguments("unavailable", EventLocationDisclosureState.Unavailable)]
    [Arguments("needs_privacy_review", EventLocationDisclosureState.Needs_privacy_review)]
    public async Task DisclosureState_DeserializesEveryServerWireValue(
        string wireValue,
        EventLocationDisclosureState expected)
    {
        var json = $$"""{"eventLocationId":"{{Guid.Empty}}","state":"{{wireValue}}"}""";

        EventLocationPublicDto? dto = JsonSerializer.Deserialize<EventLocationPublicDto>(json);

        await Assert.That(dto!.State).IsEqualTo(expected);
    }
}
