using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Features.Moderation.Requests.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using ISLAMU.Moderation.Domain;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationClassificationMaterialReviewContractTests
{
    [Test]
    public async Task CandidateReadIsNamedAnonymousDispatchWithNativeQualificationAndNoCache()
    {
        var controller = typeof(ModerationClassificationMaterialReviewController);
        var method = controller.GetMethod(nameof(ModerationClassificationMaterialReviewController.Read))!;
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<HttpGetAttribute>()?.Template).IsEqualTo("sources/{sourceId:guid}/review");
        await Assert.That(method.GetCustomAttribute<HttpGetAttribute>()?.Name).IsEqualTo(RouteNames.GetModerationClassificationMaterialReview);
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
    }

    [Test]
    public async Task RoleClaimsCannotAdvertiseAcceptanceWithoutNativeReviewerQualification()
    {
        var privilegedClaims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Admin"), new Claim(ClaimTypes.Role, "Moderator")], "test"));
        var links = new ModerationClassificationMaterialReviewLinkPolicy().GetLinks(View(), privilegedClaims).ToArray();
        await Assert.That(links.Any(link => link.RouteName == RouteNames.AcceptModerationClassificationMaterial)).IsFalse();
        await Assert.That(links.Any(link => link.RouteName == RouteNames.GetModerationClassificationMaterialReview)).IsTrue();
    }

    [Test]
    public async Task WireCannotExportNativeReviewerMarkerOrAnApprovalAssertion()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(View(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await Assert.That(json.RootElement.TryGetProperty("nativeReviewerQualified", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("isApproved", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("dateOfBirth", out _)).IsFalse();
    }

    private static ModerationClassificationMaterialReviewView View()
    {
        var target = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var owner = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        return new(target, 8, Guid.CreateVersion7(), 1, 8, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            new(owner, owner, Guid.NewGuid(), 1, reviewer, reviewer,
                "Current Event assignment.", "Independent complete material review."),
            [], [], [], new(2026, 10, 14, 12, 3, 0, TimeSpan.Zero));
    }
}
