using System.Diagnostics.CodeAnalysis;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Explore.Blazor.Client.Tests.Services;

public class EventOrganizerClaimServiceTests
{
    private readonly IEventOrganizerClaimClient _client;
    private readonly ILogger<EventOrganizerClaimService> _logger;
    private readonly EventOrganizerClaimService _service;

    public EventOrganizerClaimServiceTests()
    {
        _client = Substitute.For<IEventOrganizerClaimClient>();
        _logger = Substitute.For<ILogger<EventOrganizerClaimService>>();
        _service = new EventOrganizerClaimService(_client, _logger);
    }

    [Test]
    public async Task SubmitEventOrganizerClaimAsync_ForwardsGeneratedRequest()
    {
        var eventId = Guid.NewGuid();
        var request = new SubmitEventOrganizerClaimDto
        {
            ClaimantActorId = Guid.NewGuid(),
            EvidenceType = "website",
            EvidenceReference = "https://organizer.test/about"
        };
        _client.SubmitEventOrganizerClaimAsync(
                eventId,
                request,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var result = await _service.SubmitEventOrganizerClaimAsync(eventId, request);

        await Assert.That(result).IsTrue();
        await _client.Received(1).SubmitEventOrganizerClaimAsync(
            eventId,
            request,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WithdrawEventOrganizerClaimAsync_ForwardsParameters()
    {
        var eventId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();

        _client.WithdrawEventOrganizerClaimAsync(
                eventId,
                claimId,
                concurrencyStamp.ToString(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var result = await _service.WithdrawEventOrganizerClaimAsync(eventId, claimId, concurrencyStamp);

        await Assert.That(result).IsTrue();
        await _client.Received(1).WithdrawEventOrganizerClaimAsync(
            eventId,
            claimId,
            concurrencyStamp.ToString(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetClaimantOrganizerClaimsAsync_ReturnsGeneratedCollectionItems()
    {
        var claimantActorId = Guid.NewGuid();
        var claim = new HalResourceOfEventOrganizerClaimDto
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            ClaimantActorId = claimantActorId,
            EvidenceType = "website",
            EvidenceReference = "https://organizer.test"
        };
        _client.GetClaimantOrganizerClaimsAsync(
                claimantActorId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventOrganizerClaimDto
            {
                _embedded = new HalCollectionEmbeddedOfEventOrganizerClaimDto { Items = [claim] }
            });

        var result = await _service.GetClaimantOrganizerClaimsAsync(claimantActorId);

        await Assert.That(result).Contains(claim);
    }
}
