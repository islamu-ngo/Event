using System.Diagnostics.CodeAnalysis;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Explore.Blazor.Client.Tests.Services;

public class EventModerationServiceTests
{
    private readonly IEventModerationClient _client;
    private readonly ILogger<EventModerationService> _logger;
    private readonly EventModerationService _service;

    public EventModerationServiceTests()
    {
        _client = Substitute.For<IEventModerationClient>();
        _logger = Substitute.For<ILogger<EventModerationService>>();
        _service = new EventModerationService(_client, _logger);
    }

    [Test]
    public async Task ModerateEventLightAsync_SendsReasonMetadata()
    {
        var eventId = Guid.NewGuid();
        var expectedResponse = new BaseCommandResponseOfGuid { Id = eventId, Success = true };

        _client.ModerateEventLightAsync(
                Arg.Any<Guid>(),
                Arg.Any<EventModerationRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        var result = await _service.ModerateEventLightAsync(eventId, reasonCode: "policy_review", correlationId: "case-1");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await _client.Received(1).ModerateEventLightAsync(
            eventId,
            Arg.Is<EventModerationRequestDto>(request =>
                request.ReasonCode == "policy_review" && request.CorrelationId == "case-1"),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ModerateEventHeavyAsync_SendsReasonMetadata()
    {
        var eventId = Guid.NewGuid();
        var expectedResponse = new BaseCommandResponseOfGuid { Id = eventId, Success = true };

        _client.ModerateEventHeavyAsync(
                Arg.Any<Guid>(),
                Arg.Any<EventModerationRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        var result = await _service.ModerateEventHeavyAsync(eventId, reasonCode: "illegal_image", correlationId: "case-heavy-1");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await _client.Received(1).ModerateEventHeavyAsync(
            eventId,
            Arg.Is<EventModerationRequestDto>(request =>
                request.ReasonCode == "illegal_image" && request.CorrelationId == "case-heavy-1"),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnmoderateEventAsync_SendsReasonMetadata()
    {
        var eventId = Guid.NewGuid();
        var expectedResponse = new BaseCommandResponseOfGuid { Id = eventId, Success = true };

        _client.UnmoderateEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<EventModerationRequestDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        var result = await _service.UnmoderateEventAsync(eventId, reasonCode: "appeal_approved", correlationId: "case-restore-1");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await _client.Received(1).UnmoderateEventAsync(
            eventId,
            Arg.Is<EventModerationRequestDto>(request =>
                request.ReasonCode == "appeal_approved" && request.CorrelationId == "case-restore-1"),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
