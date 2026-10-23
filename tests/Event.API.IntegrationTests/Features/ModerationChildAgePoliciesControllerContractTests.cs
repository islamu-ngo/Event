using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Serialization;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationChildAgePoliciesControllerContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter(), new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Test]
    [Arguments(nameof(ModerationChildAgePoliciesController.Record), "sources", RouteNames.RecordModerationChildAgePolicy)]
    [Arguments(nameof(ModerationChildAgePoliciesController.Adopt), "adoptions", RouteNames.AdoptModerationChildAgePolicy)]
    [Arguments(nameof(ModerationChildAgePoliciesController.Withdraw), "adoptions/{adoptionId:guid}/withdraw",
        RouteNames.WithdrawModerationChildAgePolicy)]
    public async Task GovernanceWritesAreAuthenticatedBoundedNamedAndUncached(string action, string template, string name)
    {
        var controller = typeof(ModerationChildAgePoliciesController);
        var method = controller.GetMethod(action)!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(256L * 1024);
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Template).IsEqualTo(template);
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name).IsEqualTo(name);
        await Assert.That(method.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(response =>
            response.StatusCode == StatusCodes.Status200OK && response.Type == typeof(ModerationQualificationResponse))).IsTrue();
    }

    [Test]
    [Arguments("record", "dateOfBirth")]
    [Arguments("record", "isQualified")]
    [Arguments("record", "$type")]
    [Arguments("adopt", "dateOfBirth")]
    [Arguments("adopt", "isQualified")]
    [Arguments("adopt", "$type")]
    [Arguments("withdraw", "dateOfBirth")]
    [Arguments("withdraw", "isQualified")]
    [Arguments("withdraw", "$type")]
    public async Task IndividualDataClaimedAuthorityOrPolymorphicMembersCannotBind(string kind, string member)
    {
        var command = Command(kind);
        var document = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
        document[member] = "unrequested caller assertion";
        await Assert.That(() => JsonSerializer.Deserialize(document.ToJsonString(), command.GetType(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("sourceId")]
    [Arguments("policyId")]
    [Arguments("expectedPolicyRevision")]
    [Arguments("publicationId")]
    [Arguments("profileId")]
    [Arguments("mode")]
    [Arguments("terms")]
    [Arguments("ownership")]
    [Arguments("validUntil")]
    public async Task MissingRequiredGovernanceCoordinatesCannotBind(string member)
    {
        var document = JsonSerializer.SerializeToNode(Command("record"),
            typeof(RecordModerationChildAgePolicyCommand), Options)!.AsObject();
        document.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<RecordModerationChildAgePolicyCommand>(document.ToJsonString(), Options))
            .Throws<JsonException>();
    }

    [Test]
    public async Task MaximumEscapedPolicyGroundsFitTheDeclaredRequestCeiling()
    {
        var command = (RecordModerationChildAgePolicyCommand)Command("record");
        var limit = ((IRequestSizeLimitMetadata)typeof(ModerationChildAgePoliciesController)
            .GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize;
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(command, Options).LongLength <= limit).IsTrue();
    }

    [Test]
    public async Task WithdrawalRouteCannotBeReplacedByValidBodyAdoptionIntent()
    {
        var controller = new ModerationChildAgePoliciesController(null!, null!,
            new WithdrawModerationChildAgePolicyCommandHandler(null!))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var result = await controller.Withdraw(Guid.Empty,
            (WithdrawModerationChildAgePolicyCommand)Command("withdraw"), CancellationToken.None);
        var failure = (ObjectResult)result.Result!;
        await Assert.That(failure.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(failure.Value is ProblemDetails).IsTrue();
    }

    private static ModerationQualificationCommand Command(string kind)
    {
        var target = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var grounds = new string('\u4e00', 2048);
        return kind switch
        {
            "record" => new RecordModerationChildAgePolicyCommand
            {
                OperationId = Guid.CreateVersion7(), Target = target, ExpectedQualificationRevision = 1,
                SourceId = Guid.CreateVersion7(), PolicyId = Guid.CreateVersion7(), ExpectedPolicyRevision = 1,
                PublicationId = Guid.CreateVersion7(), ProfileId = Guid.CreateVersion7(), Mode = ModerationNativeMode.Embedded,
                Terms = new(ModerationScopeKind.Instance,
                    ModerationChildAgeCollection.LawfulMinimumBeforeSensitiveBodyHandling,
                    ModerationChildAgeConsent.ActualSubjectPurposePolicyBoundPrerequisites,
                    ModerationChildAgeAssurance.QualifiedLocalManualThresholdNotDeclaredDob,
                    ModerationChildAgeProviderAssurance.OptionalReviewedThresholdOnly,
                    ModerationChildAgeEvidence.UnknownContradictoryExpiredRemainRestrictedNotGuilty,
                    ModerationChildAgeClassification.ReviewedContentNotEventFormat, 18,
                    ModerationChildAgeAudience.WholeEventUnlessCompleteSeparatelyQualifiedSegregation,
                    ModerationChildAgeParticipant.ActualViewerAndParticipantNeverPurchaser,
                    ModerationChildAgeCorrection.InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration,
                    ModerationChildAgeProtectedAccess.CatalogueRemedyOrdinaryErasureIndependent,
                    ModerationChildAgeProhibitedInference.NoChildReligionBehaviourOrUniversalIdentityFace,
                    ModerationChildAgeCalendar.Gregorian, ModerationChildAgeTimeZone.Utc,
                    ModerationChildAgeLeapDay.MarchFirstInNonLeapYear,
                    ModerationChildAgeAdulthood.ReconsiderGuardianGrantsAndPurposes, 18,
                    grounds, grounds, grounds, grounds, grounds, grounds, grounds, grounds),
                Ownership = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    Guid.CreateVersion7(), Guid.CreateVersion7(), grounds, grounds, grounds, grounds),
                ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
            },
            "adopt" => new AdoptModerationChildAgePolicyCommand
            {
                OperationId = Guid.CreateVersion7(), Target = target, ExpectedQualificationRevision = 2,
                SourceId = Guid.CreateVersion7(), ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = 2,
                AdoptionId = Guid.CreateVersion7(), ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
            },
            "withdraw" => new WithdrawModerationChildAgePolicyCommand
            {
                OperationId = Guid.CreateVersion7(), Target = target, ExpectedQualificationRevision = 3,
                AdoptionId = Guid.CreateVersion7(), ExpectedAdoptionRevision = 1
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
