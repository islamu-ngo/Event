using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Serialization;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationProfileSubjectConsentContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter(), new JsonStringEnumConverter() }
    };

    [Test]
    [Arguments(nameof(ModerationProfileSubjectConsentsController.Grant), null, RouteNames.GrantModerationProfileSubjectConsent)]
    [Arguments(nameof(ModerationProfileSubjectConsentsController.Withdraw), "{consentId:guid}/withdraw", RouteNames.WithdrawModerationProfileSubjectConsent)]
    public async Task SeparateConsentWritesHaveNamedRoutesAndCannotRetainReplayResponses(string action, string? route, string name)
    {
        var method = typeof(ModerationProfileSubjectConsentsController).GetMethod(action)!;
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Template).IsEqualTo(route);
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name).IsEqualTo(name);
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
    }

    [Test]
    public async Task PrivateConsentBoundaryRequiresAuthenticationNoStoreAndBoundedWriteBody()
    {
        var controller = typeof(ModerationProfileSubjectConsentsController);
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsEqualTo(true);
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsEqualTo(true);
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())
            ?.MaxRequestBodySize).IsEqualTo(64L * 1024);
        await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
    }

    [Test]
    [Arguments("subjectHumanId")]
    [Arguments("principal")]
    [Arguments("grantedAt")]
    [Arguments("required")]
    [Arguments("dateOfBirth")]
    [Arguments("providerKey")]
    [Arguments("$type")]
    public async Task CallerAuthorityAndSensitiveDataCannotBindAtAnyNominalObjectLevel(string member)
    {
        for (var level = 0; level < 3; level++)
        {
            var payload = JsonSerializer.SerializeToNode(Grant(), Options)!.AsObject();
            var target = level switch
            {
                0 => payload,
                1 => payload["assessment"]!.AsObject(),
                _ => payload["assessment"]!["original"]!.AsObject()
            };
            target[member] = "unsupported assertion";
            await Assert.That(() => JsonSerializer.Deserialize<GrantModerationProfileSubjectConsentCommand>(
                payload.ToJsonString(), Options)).Throws<JsonException>();
        }
        var withdrawal = JsonSerializer.SerializeToNode(Withdraw(), Options)!.AsObject();
        withdrawal[member] = "unsupported assertion";
        await Assert.That(() => JsonSerializer.Deserialize<WithdrawModerationProfileSubjectConsentCommand>(
            withdrawal.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    [Arguments(false, "assessment")]
    [Arguments(false, "consentId")]
    [Arguments(false, "expectedGeneration")]
    [Arguments(false, "validUntil")]
    [Arguments(true, "consentId")]
    [Arguments(true, "expectedConsentRevision")]
    [Arguments(true, "expectedConsentGeneration")]
    [Arguments(true, "expectedGeneration")]
    public async Task EveryOuterNominationIsExplicit(bool withdraw, string member)
    {
        object command = withdraw ? Withdraw() : Grant();
        var payload = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
        payload.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize(payload.ToJsonString(), command.GetType(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("original")]
    [Arguments("sourceId")]
    [Arguments("expectedSourceRevision")]
    [Arguments("expectedSourceGeneration")]
    [Arguments("adoptionId")]
    [Arguments("expectedAdoptionRevision")]
    [Arguments("expectedAdoptionGeneration")]
    public async Task EveryAcceptedAssessmentCoordinateIsExplicit(string member)
    {
        var payload = JsonSerializer.SerializeToNode(Grant(), Options)!.AsObject();
        payload["assessment"]!.AsObject().Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<GrantModerationProfileSubjectConsentCommand>(
            payload.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    [Arguments("observationId")]
    [Arguments("expectedObservationRevision")]
    [Arguments("expectedObservationGeneration")]
    [Arguments("jurisdiction")]
    [Arguments("policyId")]
    [Arguments("policyRevision")]
    [Arguments("publicationId")]
    [Arguments("profileId")]
    [Arguments("mode")]
    [Arguments("procedureSourceId")]
    [Arguments("expectedProcedureSourceRevision")]
    [Arguments("expectedProcedureSourceWriterGeneration")]
    [Arguments("procedureAdoptionId")]
    [Arguments("expectedProcedureAdoptionRevision")]
    [Arguments("expectedProcedureAdoptionWriterGeneration")]
    public async Task OriginalSubjectAndParentCoordinatesCannotBeOmittedFromConsent(string member)
    {
        var payload = JsonSerializer.SerializeToNode(Grant(), Options)!.AsObject();
        payload["assessment"]!["original"]!.AsObject().Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<GrantModerationProfileSubjectConsentCommand>(
            payload.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    public async Task TypedJsonRetainsOriginalCoordinatesAndSeparateHistoricalCas()
    {
        var grant = Grant();
        var copy = JsonSerializer.Deserialize<GrantModerationProfileSubjectConsentCommand>(
            JsonSerializer.Serialize(grant, Options), Options)!;
        await Assert.That(copy.ToGrant()).IsEqualTo(grant.ToGrant());
        var withdrawal = Withdraw() with { ExpectedConsentGeneration = 3, ExpectedGeneration = 5 };
        var correction = JsonSerializer.Deserialize<WithdrawModerationProfileSubjectConsentCommand>(
            JsonSerializer.Serialize(withdrawal, Options), Options)!;
        await Assert.That(correction.ToWithdrawal()).IsEqualTo(withdrawal.ToWithdrawal());
    }

    [Test]
    public async Task WithdrawalRouteOverridesBodyBeforeRealHandlerValidation()
    {
        var controller = new ModerationProfileSubjectConsentsController(null!,
            new WithdrawModerationProfileSubjectConsentCommandHandler(null!));
        var response = await controller.Withdraw(Guid.Empty, Withdraw(), default);
        await Assert.That(response.Result).IsTypeOf<BadRequestObjectResult>();
    }

    private static GrantModerationProfileSubjectConsentCommand Grant() => new()
    {
        Assessment = new()
        {
            Original = new()
            {
                ObservationId = Id(1), ExpectedObservationRevision = 1, ExpectedObservationGeneration = 2,
                Jurisdiction = AuthorityScope.Tenant(AuthorityScope.Instance(
                    AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(30)), Id(31)), Id(32)), Id(33)),
                PolicyId = Id(2), PolicyRevision = 4, PublicationId = Id(3), ProfileId = Id(4),
                Mode = ModerationNativeMode.Embedded, ProcedureSourceId = Id(5), ExpectedProcedureSourceRevision = 1,
                ExpectedProcedureSourceWriterGeneration = 7, ProcedureAdoptionId = Id(6),
                ExpectedProcedureAdoptionRevision = 1, ExpectedProcedureAdoptionWriterGeneration = 8
            },
            SourceId = Id(7), ExpectedSourceRevision = 1, ExpectedSourceGeneration = 9,
            AdoptionId = Id(8), ExpectedAdoptionRevision = 1, ExpectedAdoptionGeneration = 10
        },
        ConsentId = Id(9), ExpectedGeneration = 3,
        ValidUntil = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)
    };

    private static WithdrawModerationProfileSubjectConsentCommand Withdraw() => new()
    {
        ConsentId = Id(9), ExpectedConsentRevision = 1, ExpectedConsentGeneration = 4, ExpectedGeneration = 4
    };

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
