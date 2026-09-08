namespace Explore.Blazor.Client.Tests.Services;

public class EventCreationEligibilityServiceTests
{
    private readonly IEventService _eventService;
    private readonly EventCreationEligibilityService _service;

    public EventCreationEligibilityServiceTests()
    {
        _eventService = Substitute.For<IEventService>();
        var logger = Substitute.For<ILogger<EventCreationEligibilityService>>();
        _service = new EventCreationEligibilityService(_eventService, logger);
    }

    [Test]
    public async Task GetEligibilityAsync_WhenPersonalPublisherCanPublish_ReturnsUserSubmissionRoute()
    {
        _eventService.GetEventCreationContextAsync(Arg.Any<CancellationToken>()).Returns(new EventCreationContextDto
        {
            CanCreate = true,
            PublisherOptions =
            [
                new EventCreationPublisherOptionDto
                {
                    PublisherMode = "personal",
                    DisplayName = "Personal profile",
                    CanPublish = true
                }
            ]
        });

        var result = await _service.GetEligibilityAsync();

        await Assert.That(result.CanCreate).IsTrue();
        await Assert.That(result.IsUserSubmissionMode).IsTrue();
        await Assert.That(result.CreateEventRoute).IsEqualTo("/events/create");
    }

    [Test]
    public async Task GetEligibilityAsync_WhenOrganizationPublisherCanPublish_ReturnsOrganizationRoute()
    {
        var blockedOrganizationId = Guid.CreateVersion7();
        var allowedOrganizationId = Guid.CreateVersion7();
        _eventService.GetEventCreationContextAsync(Arg.Any<CancellationToken>()).Returns(new EventCreationContextDto
        {
            CanCreate = true,
            PublisherOptions =
            [
                new EventCreationPublisherOptionDto
                {
                    PublisherMode = "organization",
                    PublisherId = blockedOrganizationId,
                    DisplayName = "Blocked org",
                    CanPublish = false
                },
                new EventCreationPublisherOptionDto
                {
                    PublisherMode = "organization",
                    PublisherId = allowedOrganizationId,
                    DisplayName = "Allowed org",
                    CanPublish = true
                }
            ]
        });

        var result = await _service.GetEligibilityAsync();

        await Assert.That(result.CanCreate).IsTrue();
        await Assert.That(result.EligibleOrganizationId).IsEqualTo(allowedOrganizationId);
        await Assert.That(result.CreateEventRoute).IsEqualTo($"/organizations/{allowedOrganizationId}/events/create");
    }

    [Test]
    public async Task GetEligibilityAsync_WhenGroupPublisherCanPublish_ReturnsGroupRoute()
    {
        var groupId = Guid.CreateVersion7();
        _eventService.GetEventCreationContextAsync(Arg.Any<CancellationToken>()).Returns(new EventCreationContextDto
        {
            CanCreate = true,
            PublisherOptions =
            [
                new EventCreationPublisherOptionDto
                {
                    PublisherMode = "group",
                    PublisherId = groupId,
                    DisplayName = "Allowed group",
                    CanPublish = true
                }
            ]
        });

        var result = await _service.GetEligibilityAsync();

        await Assert.That(result.CanCreate).IsTrue();
        await Assert.That(result.EligibleGroupId).IsEqualTo(groupId);
        await Assert.That(result.CreateEventRoute).IsEqualTo($"/groups/{groupId}/events/create");
    }

    [Test]
    public async Task GetEligibilityAsync_WhenNoPublisherCanPublish_ReturnsNotEligible()
    {
        _eventService.GetEventCreationContextAsync(Arg.Any<CancellationToken>()).Returns(new EventCreationContextDto
        {
            CanCreate = true,
            PublisherOptions =
            [
                new EventCreationPublisherOptionDto
                {
                    PublisherMode = "organization",
                    PublisherId = Guid.CreateVersion7(),
                    DisplayName = "Blocked org",
                    CanPublish = false
                }
            ]
        });

        var result = await _service.GetEligibilityAsync();

        await Assert.That(result.CanCreate).IsFalse();
    }
}
