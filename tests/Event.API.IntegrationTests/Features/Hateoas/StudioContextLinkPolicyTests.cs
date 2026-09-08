using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.DTOs.Studio;
using Explore.Application.Hateoas;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class StudioContextLinkPolicyTests
{
    [Test]
    public async Task GetLinks_EmitsOrderNavigationOnlyWhenTheHandlerProvidedTheCandidate()
    {
        var actorId = Guid.CreateVersion7();
        var dto = new StudioContextDto { SelectedActorId = actorId };

        var withoutCandidate = new StudioContextLinkPolicy().GetLinks(dto, null).ToList();
        var withCandidateDto = dto with
        {
            AllowedLinkRelations = new HashSet<string>(StringComparer.Ordinal)
            {
                LinkRelations.ViewRegistrationOrders,
                LinkRelations.ViewParticipants,
            },
        };
        var withCandidate = new StudioContextLinkPolicy().GetLinks(withCandidateDto, null).ToList();

        await Assert.That(withoutCandidate.Select(link => link.Rel)).IsEquivalentTo([LinkRelations.Self]);
        var orderLink = withCandidate.Single(link => link.Rel == LinkRelations.ViewRegistrationOrders);
        await Assert.That(orderLink.RouteName).IsEqualTo(RouteNames.GetManagedEventsByActor);
        await Assert.That(new Microsoft.AspNetCore.Routing.RouteValueDictionary(orderLink.RouteValues)["actorId"])
            .IsEqualTo(actorId);
        await Assert.That(orderLink.RequiresAuth).IsTrue();
        await Assert.That(withCandidate.Select(link => link.Rel)).Contains(LinkRelations.ViewParticipants);
    }
}
