using System.Security.Claims;
using System.Text.Json;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Constants;
using Explore.Application.Features.Moderation.Notices;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class ModerationNoticeStatusLinkPolicyTests
{
    [Test]
    [Arguments("Proven")]
    [Arguments("Unproven")]
    [Arguments("Unavailable")]
    [Arguments("PurposePurged")]
    public async Task Every_qualified_status_has_only_the_real_stable_self_route(string proof)
    {
        var dto = Status(proof);
        var user = Principal(dto.QualifiedRecipientId, "status-witness");
        var detail = new ModerationNoticeStatusDetailLinkPolicy().GetLinks(dto, user).ToArray();
        var collection = new ModerationNoticeStatusCollectionLinkPolicy().GetItemLinks(dto, user).ToArray();
        await Assert.That(detail.Length).IsEqualTo(1);
        await Assert.That(collection.Length).IsEqualTo(1);
        await Assert.That(detail[0].RouteName).IsEqualTo(RouteNames.GetModerationNoticeStatus);
        await Assert.That(collection[0].RouteName).IsEqualTo(RouteNames.GetModerationNoticeStatus);
        await Assert.That(JsonSerializer.Serialize(dto)).DoesNotContain("QualifiedRecipientId");
    }

    [Test]
    [Arguments(ApiAuthenticationSchemeNames.PrivacyErasureReceipt)]
    [Arguments(ApiAuthenticationSchemeNames.ApiKey)]
    [Arguments(ApiAuthenticationSchemeNames.SetupSecret)]
    public async Task Purpose_bound_principal_cannot_emit_status_affordances(string scheme)
    {
        var dto = Status("PurposePurged");
        var user = Principal(dto.QualifiedRecipientId, scheme);
        await Assert.That(new ModerationNoticeStatusDetailLinkPolicy().GetLinks(dto, user).Any()).IsFalse();
        await Assert.That(new ModerationNoticeStatusCollectionLinkPolicy().GetItemLinks(dto, user).Any()).IsFalse();
    }

    [Test]
    public async Task Unqualified_value_cannot_emit_status_links_even_for_a_human()
    {
        var dto = Status("Unavailable") with { QualifiedRecipientId = Guid.Empty };
        var user = Principal(Guid.CreateVersion7(), "status-witness");
        await Assert.That(new ModerationNoticeStatusDetailLinkPolicy().GetLinks(dto, user).Any()).IsFalse();
    }

    private static ModerationNoticeStatusDto Status(string proof) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), proof, null, null, null,
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))
        { QualifiedRecipientId = Guid.CreateVersion7() };

    private static ClaimsPrincipal Principal(Guid id, string scheme) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], scheme));
}
