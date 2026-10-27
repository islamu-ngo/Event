using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationProfileCollectionSubjectObservationContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public async Task ActualSelfNominationIsAuthenticatedBoundedAndNeverResponseCached()
    {
        var controller = typeof(ModerationProfileCollectionSubjectObservationsController);
        var method = controller.GetMethod(nameof(ModerationProfileCollectionSubjectObservationsController.Observe))!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(64L * 1024);
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name)
            .IsEqualTo(RouteNames.ObserveModerationProfileCollectionSubject);
    }

    [Test]
    [Arguments("subjectHumanId")]
    [Arguments("observedAt")]
    [Arguments("validUntil")]
    [Arguments("principal")]
    [Arguments("isQualified")]
    [Arguments("providerKey")]
    [Arguments("dateOfBirth")]
    [Arguments("$type")]
    public async Task ClientCannotReplaceSubjectObservationTimeOrAuthority(string member)
    {
        var payload = JsonSerializer.SerializeToNode(Command(), Options)!.AsObject();
        payload[member] = "unsupported assertion";
        await Assert.That(() => JsonSerializer.Deserialize<ObserveModerationProfileCollectionSubjectCommand>(
            payload.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    [Arguments("profileId")]
    [Arguments("mode")]
    public async Task ExplicitNominationCoordinatesCannotBeOmitted(string member)
    {
        var payload = JsonSerializer.SerializeToNode(Command(), Options)!.AsObject();
        payload.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<ObserveModerationProfileCollectionSubjectCommand>(
            payload.ToJsonString(), Options)).Throws<JsonException>();
    }

    private static ObserveModerationProfileCollectionSubjectCommand Command() => new()
    {
        ProfileId = Guid.Parse("019a0000-0000-7000-8000-000000000001"),
        Mode = ModerationNativeMode.Embedded
    };
}
