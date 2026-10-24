using System.Reflection;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Features.Moderation.Requests.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationCaseNarrativePoliciesControllerContractTests
{
    [Test]
    [Arguments(nameof(ModerationCaseNarrativePoliciesController.Record), "sources",
        RouteNames.RecordModerationCaseNarrativePolicy)]
    [Arguments(nameof(ModerationCaseNarrativePoliciesController.Adopt), "adoptions",
        RouteNames.AdoptModerationCaseNarrativePolicy)]
    [Arguments(nameof(ModerationCaseNarrativePoliciesController.Withdraw), "adoptions/{adoptionId:guid}/withdraw",
        RouteNames.WithdrawModerationCaseNarrativePolicy)]
    public async Task CustodyWritesRequireAuthenticationAndRemainBoundedUncachedNamedActions(
        string action, string template, string name)
    {
        var controller = typeof(ModerationCaseNarrativePoliciesController);
        var method = controller.GetMethod(action)!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(32L * 1024);
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        var route = method.GetCustomAttribute<HttpPostAttribute>();
        await Assert.That(route?.Template).IsEqualTo(template);
        await Assert.That(route?.Name).IsEqualTo(name);
        await Assert.That(method.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(response =>
            response.StatusCode == StatusCodes.Status200OK && response.Type == typeof(ModerationQualificationResponse))).IsTrue();
    }
}
