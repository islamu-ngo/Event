using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Constants;
using Explore.Application.Features.Moderation.Notices;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class ModerationNoticeLinkPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task CurrentQualifiedExplanationOffersOnlyImplementedReadRoutes()
    {
        var dto = Notice();
        var user = Principal(dto.QualifiedRecipientId, "notice-witness");
        var links = new ModerationNoticeDetailLinkPolicy(new Clock(Now)).GetLinks(dto, user).ToArray();
        var itemLinks = new ModerationNoticeCollectionLinkPolicy(new Clock(Now)).GetItemLinks(dto, user).ToArray();
        await Assert.That(links.Length).IsEqualTo(2);
        await Assert.That(itemLinks.Length).IsEqualTo(1);
        await Assert.That(links.Select(link => link.RouteName).Order().ToArray())
            .IsEquivalentTo(new[] { RouteNames.GetModerationNotice, RouteNames.GetModerationNotices }.Order().ToArray());
        await Assert.That(itemLinks.Single().RouteName).IsEqualTo(RouteNames.GetModerationNotice);
    }

    [Test]
    public async Task ExactRetentionBoundaryRemovesAllCurrentAffordances()
    {
        var dto = Notice();
        var user = Principal(dto.QualifiedRecipientId, "notice-witness");
        await Assert.That(new ModerationNoticeDetailLinkPolicy(new Clock(dto.RetainUntil))
            .GetLinks(dto, user).Any()).IsFalse();
        await Assert.That(new ModerationNoticeCollectionLinkPolicy(new Clock(dto.RetainUntil))
            .GetItemLinks(dto, user).Any()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Only_the_qualified_original_subject_receives_the_real_notification_status_route(bool subject)
    {
        var dto = Notice() with
        {
            QualifiedDecisionId = subject ? Guid.CreateVersion7() : Guid.Empty,
            QualifiedEffectId = subject ? Guid.CreateVersion7() : Guid.Empty
        };
        var user = Principal(dto.QualifiedRecipientId, "notice-witness");
        var detail = new ModerationNoticeDetailLinkPolicy(new Clock(Now)).GetLinks(dto, user).ToArray();
        var collection = new ModerationNoticeCollectionLinkPolicy(new Clock(Now)).GetItemLinks(dto, user).ToArray();
        await Assert.That(detail.Count(link => link.RouteName == RouteNames.GetModerationNoticeStatus))
            .IsEqualTo(subject ? 1 : 0);
        await Assert.That(collection.Count(link => link.RouteName == RouteNames.GetModerationNoticeStatus))
            .IsEqualTo(subject ? 1 : 0);
    }

    [Test]
    public async Task OnlyAnUnnotifiedSubjectWithinTheOriginalWindowCanAcknowledge()
    {
        var dto = Notice() with { CanAcknowledge = true };
        var user = Principal(dto.QualifiedRecipientId, "notice-witness");
        var links = new ModerationNoticeDetailLinkPolicy(new Clock(Now)).GetLinks(dto, user).ToArray();
        await Assert.That(links.Any(link => link.RouteName == RouteNames.AcknowledgeModerationNotice)).IsTrue();
        await Assert.That(new ModerationNoticeDetailLinkPolicy(new Clock(Now.AddMonths(1).AddTicks(1)))
            .GetLinks(dto, user).Any(link => link.RouteName == RouteNames.AcknowledgeModerationNotice)).IsFalse();
        await Assert.That(new ModerationNoticeDetailLinkPolicy(new Clock(Now))
            .GetLinks(dto with { CanAcknowledge = false }, user)
            .Any(link => link.RouteName == RouteNames.AcknowledgeModerationNotice)).IsFalse();
    }

    [Test]
    [Arguments(ApiAuthenticationSchemeNames.PrivacyErasureReceipt)]
    [Arguments(ApiAuthenticationSchemeNames.ApiKey)]
    [Arguments(ApiAuthenticationSchemeNames.SetupSecret)]
    public async Task PurposeBoundPrincipalCannotTurnNoticeMetadataIntoAccountAffordances(string scheme)
    {
        var dto = Notice();
        var user = Principal(dto.QualifiedRecipientId, scheme);
        await Assert.That(new ModerationNoticeDetailLinkPolicy(new Clock(Now)).GetLinks(dto, user).Any()).IsFalse();
        await Assert.That(new ModerationNoticeCollectionLinkPolicy(new Clock(Now)).GetItemLinks(dto, user).Any()).IsFalse();
    }

    private static ClaimsPrincipal Principal(Guid id, string scheme) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], scheme));

    private static ModerationNoticeDto Notice()
    {
        var scope = new ModerationNoticeScope(ModerationScopeKind.TrustDomain, Guid.CreateVersion7(), null);
        var rule = new ModerationNoticeRule(scope.Id, "notice.read", 1);
        return new(Guid.CreateVersion7(), "Subject", Now, "Active", "Active", scope,
            Now, Now.AddHours(1), null, rule, rule, "Approved rule", "Approved reason", [],
            "Original remaining actions", "Original recourse", "No automation", "Bounded retention",
            new string('a', 64), 6, null, null, Now.AddMonths(7), [], [])
        { QualifiedRecipientId = Guid.CreateVersion7() };
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
