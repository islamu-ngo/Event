using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Features.Moderation.Handlers.Queries;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationReferenceCatalogueLinkPolicyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReferenceMetadataAdvertisesOnlyReadsNotPublicationOrActivation(bool authenticated)
    {
        var view = await new GetModerationReferenceCatalogueQueryHandler().QueryAsync(new());
        var principal = new ClaimsPrincipal(authenticated ? new ClaimsIdentity("Testing") : new ClaimsIdentity());
        var links = new ModerationReferenceCatalogueLinkPolicy().GetLinks(view, principal).ToArray();

        await Assert.That(links.Select(link => link.Rel)).IsEquivalentTo(["self", "refresh"]);
        await Assert.That(links.All(link => link.RouteName == RouteNames.GetModerationReferenceCatalogue
            && (link.Method is null or "GET") && !link.RequiresAuth)).IsTrue();
    }
}
