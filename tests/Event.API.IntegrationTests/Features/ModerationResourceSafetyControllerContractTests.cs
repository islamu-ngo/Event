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

namespace Event.API.IntegrationTests.Features;

public sealed class ModerationResourceSafetyControllerContractTests
{
    [Test]
    public async Task CurrentReviewStateRequiresProtectedNativeCoordinatesAndBypassesResponseStorage()
    {
        var controller = typeof(ModerationResourceSafetyController);
        var method = controller.GetMethod(nameof(ModerationResourceSafetyController.ReadReviewState))!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()!.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        var route = method.GetCustomAttribute<HttpPostAttribute>();
        await Assert.That(route?.Template).IsEqualTo("quarantine-causes/{quarantineId:guid}/review-state");
        await Assert.That(route?.Name).IsEqualTo(RouteNames.ReadModerationResourceQuarantineReviewState);
        await Assert.That(method.GetCustomAttribute<ProducesAttribute>()!.ContentTypes)
            .Contains("application/hal+json");
    }

    [Test]
    public async Task OperationalStateIsAProtectedUncachedNativeQueryWithoutMaterialOrReleaseAffordance()
    {
        var controller = typeof(ModerationResourceSafetyController);
        var method = controller.GetMethod(nameof(ModerationResourceSafetyController.ReadState))!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()!.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        var route = method.GetCustomAttribute<HttpPostAttribute>();
        await Assert.That(route?.Template).IsEqualTo("quarantine-state");
        await Assert.That(route?.Name).IsEqualTo(RouteNames.ReadModerationResourceQuarantineState);
        await Assert.That(method.GetCustomAttribute<ProducesAttribute>()!.ContentTypes)
            .Contains("application/hal+json");
    }

    [Test]
    public async Task InspectionIsAnAuthenticatedUncachedNativePurposeQueryNotAPublicRedirect()
    {
        var controller = typeof(ModerationResourceSafetyController);
        var method = controller.GetMethod(nameof(ModerationResourceSafetyController.Inspect))!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()!.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        var route = method.GetCustomAttribute<HttpPostAttribute>();
        await Assert.That(route?.Template).IsEqualTo("quarantine-causes/{quarantineId:guid}/inspection");
        await Assert.That(route?.Name).IsEqualTo(RouteNames.InspectModerationResourceQuarantine);
        await Assert.That(method.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(response =>
            response.StatusCode == StatusCodes.Status200OK && response.Type == typeof(FileContentResult))).IsTrue();
        await Assert.That(method.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(response =>
            response.StatusCode is StatusCodes.Status301MovedPermanently or StatusCodes.Status302Found
                or StatusCodes.Status307TemporaryRedirect or StatusCodes.Status308PermanentRedirect)).IsFalse();
    }

    [Test]
    [Arguments(nameof(ModerationResourceSafetyController.RecordReview), "quarantine-reviews",
        RouteNames.RecordModerationResourceSafetyReview)]
    [Arguments(nameof(ModerationResourceSafetyController.ReleaseCause), "quarantine-causes/{quarantineId:guid}/release",
        RouteNames.ReleaseModerationResourceQuarantine)]
    public async Task WritesRequireAuthenticationAndNativeReceiptOwnership(string action, string template, string name)
    {
        var controller = typeof(ModerationResourceSafetyController);
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()!.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore).IsTrue();
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(32L * 1024);
        var method = controller.GetMethod(action)!;
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        var route = method.GetCustomAttribute<HttpPostAttribute>();
        await Assert.That(route?.Template).IsEqualTo(template);
        await Assert.That(route?.Name).IsEqualTo(name);
        await Assert.That(method.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(response =>
            response.StatusCode == StatusCodes.Status200OK && response.Type == typeof(ModerationResourceSafetyResponse))).IsTrue();
    }
}
