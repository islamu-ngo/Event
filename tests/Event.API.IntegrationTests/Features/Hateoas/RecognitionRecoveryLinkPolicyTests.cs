using System.Security.Claims;
using System.Text.Json;
using Explore.API.Authentication;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authentication;
using Explore.Application.Features.Moderation.RecognitionRecovery;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class RecognitionRecoveryLinkPolicyTests
{
    [Test]
    public async Task Source_qualified_owner_links_never_transfer_to_another_principal()
    {
        Guid owner = Guid.CreateVersion7();
        var principal = Principal(owner);
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var policy = new RecognitionRecoveryContextDetailLinkPolicy(new UserContext(accessor));
        var dto = Context(owner);
        var links = policy.GetLinks(dto, principal).ToArray();
        await Assert.That(links.Single(link => link.Rel == "assess").RouteName).IsEqualTo(RouteNames.AssessRecognitionRecovery);
        await Assert.That(links.Any(link => link.Rel == "authorize")).IsFalse();
        accessor.HttpContext.User = Principal(Guid.CreateVersion7());
        await Assert.That(policy.GetLinks(dto, accessor.HttpContext.User)).IsEmpty();
        accessor.HttpContext.User = principal;
        var privacyContext = dto with { CanAssess = false, CanAuthorize = true, DispositionId = Guid.CreateVersion7() };
        await Assert.That(policy.GetLinks(privacyContext, principal).Any(link => link.Rel == "assess")).IsFalse();
        await Assert.That(policy.GetLinks(privacyContext, principal).Single(link => link.Rel == "authorize").RouteName)
            .IsEqualTo(RouteNames.AuthorizeRecognitionRecovery);
    }

    [Test]
    public async Task Undelivered_operational_notice_has_no_acknowledgement_affordance()
    {
        Guid owner = Guid.CreateVersion7();
        var principal = Principal(owner);
        var policy = new RecognitionRecoveryEscalationDetailLinkPolicy(new UserContext(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } }));
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var dto = new RecognitionRecoveryEscalationDto(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), null, 2, ModerationRecognitionEscalationStage.OwnerActionRequired,
            now, now.AddHours(1), null, null) { QualifiedOwnerHumanId = owner };
        await Assert.That(policy.GetLinks(dto, principal).Any(link => link.Rel == "acknowledge")).IsFalse();
        await Assert.That(policy.GetLinks(dto with { DeliveredAt = now }, principal)
            .Single(link => link.Rel == "acknowledge").RouteName).IsEqualTo(RouteNames.AcknowledgeRecognitionRecovery);
    }

    [Test]
    public async Task Protected_correction_proof_has_no_account_status_or_owner_write_capability()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ModerationRecognitionCorrectionReceiptAuthenticationHandler.IntentIdClaim, Guid.CreateVersion7().ToString("D"))],
            ModerationRecognitionCorrectionReceiptAuthenticationHandler.SchemeName));
        await Assert.That(principal.GetPlatformUserId()).IsNull();
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var proof = new RecognitionCorrectionProofDto(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            2, ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable, now, now.AddMonths(6), now.AddYears(1));
        var links = new RecognitionCorrectionProofDetailLinkPolicy().GetLinks(proof, principal).ToArray();
        await Assert.That(links.All(link => link.RouteName == RouteNames.GetRecognitionCorrectionProofs)).IsTrue();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Context(Guid.CreateVersion7())));
        await Assert.That(json.RootElement.TryGetProperty("QualifiedOwnerHumanId", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("CanAssess", out _)).IsFalse();
    }

    private static ClaimsPrincipal Principal(Guid id) =>
        new(new ClaimsIdentity([new Claim("sub", id.ToString("D"))], "Bearer"));

    private static RecognitionRecoveryContextDto Context(Guid owner)
    {
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        return new(Guid.CreateVersion7(), Guid.CreateVersion7(), 2, ModerationRecognitionPendingState.EscalationRequired,
            ModerationRecognitionPendingFailure.KeyUnavailable, now, now.AddHours(1), now.AddMinutes(5),
            2, Guid.CreateVersion7(), 1, 3, Guid.CreateVersion7(), 1, 4, now.AddHours(1),
            ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable,
            null, null, null, null, null, null)
        { QualifiedOwnerHumanId = owner, CanAssess = true };
    }
}
