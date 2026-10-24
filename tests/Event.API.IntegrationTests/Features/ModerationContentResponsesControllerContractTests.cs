using System.Reflection;
using System.Security.Claims;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Constants;
using Explore.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationContentResponsesControllerContractTests
{
    [Test]
    public async Task Protected_response_transport_authenticates_and_disables_generic_receipt_storage()
    {
        var method = typeof(ModerationContentResponsesController).GetMethod(nameof(ModerationContentResponsesController.Submit))!;
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name).IsEqualTo(RouteNames.SubmitModerationContentResponse);
        await Assert.That(method.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        await Assert.That(((IRequestSizeLimitMetadata?)method.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(64L * 1024);
    }

    [Test]
    [Arguments("response-witness", true)]
    [Arguments(ApiAuthenticationSchemeNames.PrivacyErasureReceipt, false)]
    [Arguments(ApiAuthenticationSchemeNames.ApiKey, false)]
    public async Task Only_current_qualified_native_response_offers_submission_without_an_acknowledgement(string scheme, bool allowed)
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var availability = new ModerationContentResponseAvailability(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, new string('a', 64),
            now.AddMonths(7), 6, null, null, now.AddMinutes(1));
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())], scheme));
        var links = new ModerationContentResponseDetailLinkPolicy(new Clock(now)).GetLinks(availability, user).ToArray();
        await Assert.That(links.Any(link => link.RouteName == RouteNames.SubmitModerationContentResponse)).IsEqualTo(allowed);
        await Assert.That(links.Any(link => link.RouteName == RouteNames.AcknowledgeModerationNotice)).IsFalse();
        await Assert.That(new ModerationContentResponseDetailLinkPolicy(new Clock(availability.QualifiedUntil))
            .GetLinks(availability, user).Any()).IsFalse();
        await Assert.That(new ModerationContentResponseCollectionLinkPolicy(new Clock(now))
            .GetItemLinks(availability, user).Any(link => link.RouteName == RouteNames.SubmitModerationContentResponse)).IsFalse();
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
