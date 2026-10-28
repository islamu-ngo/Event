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

public sealed class ModerationCaseIntakeControllerContractTests
{
    [Test]
    public async Task Receipt_and_allegation_transport_requires_current_authentication_and_bounded_uncached_json()
    {
        var controller = typeof(ModerationCaseIntakeController);
        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()?.Template)
            .IsEqualTo("api/moderation/cases");
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(controller.GetCustomAttribute<ConsumesAttribute>()?.ContentTypes)
            .Contains("application/json");
        await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(192L * 1024);
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
    }

    [Test]
    [Arguments(nameof(ModerationCaseIntakeController.IssueSubmissionReceipt), "submission-receipts",
        RouteNames.IssueModerationSubmissionReceipt, typeof(ModerationSubmissionReceiptResponse))]
    [Arguments(nameof(ModerationCaseIntakeController.SubmitAllegation), "allegations",
        RouteNames.SubmitModerationAllegation, typeof(ModerationAllegationResponse))]
    [Arguments(nameof(ModerationCaseIntakeController.IssueReadReceipt), "read-receipts",
        RouteNames.IssueModerationReadReceipt, typeof(ModerationReadReceiptResponse))]
    [Arguments(nameof(ModerationCaseIntakeController.ReadNarrative), "{caseId:guid}/narrative",
        RouteNames.ReadModerationCaseNarrative, typeof(FileContentResult))]
    public async Task Named_intake_action_never_persists_generic_bearer_response_or_exposes_untyped_failures(
        string action, string template, string routeName, Type responseType)
    {
        var controller = typeof(ModerationCaseIntakeController);
        var method = controller.GetMethod(action)!;
        var route = method.GetCustomAttribute<HttpPostAttribute>();
        await Assert.That(route?.Template).IsEqualTo(template);
        await Assert.That(route?.Name).IsEqualTo(routeName);
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        var responses = controller.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Concat(method.GetCustomAttributes<ProducesResponseTypeAttribute>()).ToArray();
        await Assert.That(responses.Any(response =>
            response.StatusCode == StatusCodes.Status200OK && response.Type == responseType)).IsTrue();
        foreach (var status in new[]
                 {
                     StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized,
                     StatusCodes.Status403Forbidden, StatusCodes.Status409Conflict,
                     StatusCodes.Status429TooManyRequests, StatusCodes.Status503ServiceUnavailable
                 })
            await Assert.That(responses.Any(response => response.StatusCode == status
                && response.Type == typeof(ProblemDetails))).IsTrue();
    }
}
