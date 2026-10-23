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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationProfileCollectionAssessmentWithdrawalContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter(), new JsonStringEnumConverter() }
    };

    [Test]
    public async Task CorrectionIsAuthenticatedBoundedUncachedAndNeverStoresAnIdempotencyResponse()
    {
        var controller = typeof(ModerationProfileCollectionAssessmentsController);
        var method = controller.GetMethod(nameof(ModerationProfileCollectionAssessmentsController.Withdraw))!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(64L * 1024);
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Template).IsEqualTo("adoptions/{adoptionId:guid}/withdraw");
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name)
            .IsEqualTo(RouteNames.WithdrawModerationProfileCollectionAssessment);
    }

    [Test]
    [Arguments("dateOfBirth")]
    [Arguments("isQualified")]
    [Arguments("required")]
    [Arguments("validUntil")]
    [Arguments("principal")]
    [Arguments("providerKey")]
    [Arguments("$type")]
    public async Task CorrectionCannotBindSensitiveInputsRenewalOrCallerAuthority(string member)
    {
        var payload = JsonSerializer.SerializeToNode(Command(), Options)!.AsObject();
        payload[member] = "unsupported assertion";
        await Assert.That(() => JsonSerializer.Deserialize<WithdrawModerationProfileCollectionAssessmentCommand>(
            payload.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    [Arguments("jurisdiction")]
    [Arguments("subjectHumanId")]
    [Arguments("policyId")]
    [Arguments("policyRevision")]
    [Arguments("publicationId")]
    [Arguments("profileId")]
    [Arguments("mode")]
    [Arguments("sourceId")]
    [Arguments("expectedSourceGeneration")]
    [Arguments("adoptionId")]
    [Arguments("expectedAdoptionGeneration")]
    [Arguments("expectedAdoptionRevision")]
    public async Task OriginalIntentCoordinatesMustBePresent(string member)
    {
        var payload = JsonSerializer.SerializeToNode(Command(), Options)!.AsObject();
        payload.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<WithdrawModerationProfileCollectionAssessmentCommand>(
            payload.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    public async Task RouteAdoptionOverridesAnOtherwiseValidBodyBeforeValidation()
    {
        var controller = new ModerationProfileCollectionAssessmentsController(
            null!, null!, new WithdrawModerationProfileCollectionAssessmentCommandHandler(null!))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var command = Command();
        var response = await controller.Withdraw(Guid.Empty, command, default);
        var problem = response.Result as ObjectResult;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(command.AdoptionId).IsEqualTo(Id(5));
    }

    private static WithdrawModerationProfileCollectionAssessmentCommand Command() => new()
    {
        Jurisdiction = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(30)), Id(31)), Id(32)), Id(33)),
        SubjectHumanId = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab"),
        PolicyId = Id(1), PolicyRevision = 4, PublicationId = Id(2), ProfileId = Id(3),
        Mode = ModerationNativeMode.Embedded, SourceId = Id(4), ExpectedSourceGeneration = 2,
        AdoptionId = Id(5), ExpectedAdoptionGeneration = 3, ExpectedAdoptionRevision = 1
    };

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
