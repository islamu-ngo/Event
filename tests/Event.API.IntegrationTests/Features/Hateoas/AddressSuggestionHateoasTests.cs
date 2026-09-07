using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class AddressSuggestionHateoasTests
{
    [Test]
    public async Task ScopedSuggestion_AdvertisesNamedAuthorizedApprovalAction()
    {
        Guid tenantId = Guid.CreateVersion7();
        var policy = new AddressSuggestionCollectionLinkPolicy();

        LinkDefinition approval = policy
            .GetItemLinks(
                Suggestion(tenantId, LocationAddressVisibilityEnum.OrganizationScoped),
                user: null)
            .Single(link => link.Rel == LinkRelations.ApproveTenantAddress);

        await Assert.That(approval.RouteName).IsEqualTo(RouteNames.ApproveTenantAddress);
        await Assert.That(approval.Method).IsEqualTo("POST");
        await Assert.That(approval.RequiresAuth).IsTrue();
        await Assert.That(approval.PermissionResourceKind).IsEqualTo(ResourceKinds.Location);
        await Assert.That(approval.PermissionAction)
            .IsEqualTo(AuthorizationActions.Locations.ApproveTenantAddress);
        var facts = approval.PermissionFacts as TenantScopedAuthorizationFacts;
        await Assert.That(facts).IsNotNull();
        await Assert.That(facts!.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task TenantApprovedSuggestion_OmitsApprovalAction()
    {
        var policy = new AddressSuggestionCollectionLinkPolicy();

        LinkDefinition[] links = policy
            .GetItemLinks(
                Suggestion(
                    Guid.CreateVersion7(),
                    LocationAddressVisibilityEnum.TenantApproved),
                user: null)
            .ToArray();

        await Assert.That(links.Any(link =>
            link.Rel == LinkRelations.ApproveTenantAddress)).IsFalse();
        await Assert.That(links.Single(link => link.Rel == LinkRelations.Self).Method)
            .IsEqualTo("GET");
    }

    private static AddressSuggestionDto Suggestion(
        Guid tenantId,
        LocationAddressVisibilityEnum visibility) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Synthetic venue",
            "Synthetic address",
            "0000",
            LocationAddressSourceEnum.Manual,
            visibility)
        {
            TenantId = tenantId
        };
}
