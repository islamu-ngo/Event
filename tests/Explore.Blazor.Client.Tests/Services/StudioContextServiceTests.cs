using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class StudioContextServiceTests
{
    [Test]
    public async Task GetEventAttendeesAsync_ExcludesOrdersWithoutViewParticipantsRelation()
    {
        var orderClient = Substitute.For<IRegistrationOrderClient>();
        var authenticatedClient = Substitute.For<IAuthenticatedRegistrationOrderClient>();
        var service = new StudioContextService(
            Substitute.For<IStudioClient>(),
            orderClient,
            authenticatedClient);
        var eventId = Guid.CreateVersion7();
        var visibleOrderId = Guid.CreateVersion7();
        var hiddenOrderId = Guid.CreateVersion7();
        orderClient.GetEventRegistrationOrdersAsync(eventId, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfRegistrationOrderDto
            {
                _embedded = new HalCollectionEmbeddedOfRegistrationOrderDto
                {
                    Items =
                    [
                        new HalResourceOfRegistrationOrderDto
                        {
                            Id = visibleOrderId,
                            _links = new Dictionary<string, HalLink>
                            {
                                ["view-participants"] = new() { Href = $"/orders/{visibleOrderId}/participants" }
                            }
                        },
                        new HalResourceOfRegistrationOrderDto
                        {
                            Id = hiddenOrderId,
                            _links = new Dictionary<string, HalLink>()
                        }
                    ]
                }
            });
        authenticatedClient.GetAuthenticatedRegistrationOrderParticipantsAsync(
                eventId,
                visibleOrderId,
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfRegistrationOrderParticipantsDto
            {
                RegistrationOrderId = visibleOrderId,
                Lines = [],
                Participants = [],
                Assignments = []
            });

        IReadOnlyList<StudioAttendeeOrder> result = await service.GetEventAttendeesAsync(eventId);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Order.Id).IsEqualTo(visibleOrderId);
        await authenticatedClient.DidNotReceive().GetAuthenticatedRegistrationOrderParticipantsAsync(
            eventId,
            hiddenOrderId,
            cancellationToken: Arg.Any<CancellationToken>());
    }
}
