using System.Collections.Immutable;
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

public sealed class ModerationClassificationMaterialsControllerContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new ModerationAuthorityScopeJsonConverter(), new ModerationClassificationMaterialJsonConverter(),
            new System.Text.Json.Serialization.JsonStringEnumConverter()
        }
    };

    [Test]
    [Arguments(nameof(ModerationClassificationMaterialsController.Record), "sources", RouteNames.RecordModerationClassificationMaterial)]
    [Arguments(nameof(ModerationClassificationMaterialsController.Accept), "reviews", RouteNames.AcceptModerationClassificationMaterial)]
    [Arguments(nameof(ModerationClassificationMaterialsController.End), "reviews/{reviewId:guid}/end",
        RouteNames.EndModerationClassificationMaterial)]
    public async Task MaterialWritesAreAuthenticatedBoundedNamedAndUncached(string action, string template, string name)
    {
        var controller = typeof(ModerationClassificationMaterialsController);
        var method = controller.GetMethod(action)!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(16L * 1024 * 1024);
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Template).IsEqualTo(template);
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name).IsEqualTo(name);
    }

    [Test]
    [Arguments("record", "isComplete")]
    [Arguments("record", "isApproved")]
    [Arguments("record", "$type")]
    [Arguments("accept", "isComplete")]
    [Arguments("accept", "isApproved")]
    [Arguments("accept", "$type")]
    [Arguments("end", "isComplete")]
    [Arguments("end", "isApproved")]
    [Arguments("end", "$type")]
    public async Task ClaimedCompletenessApprovalOrPolymorphismCannotEnterCommand(string kind, string member)
    {
        var command = Command(kind);
        var document = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
        document[member] = "caller assertion";
        await Assert.That(() => JsonSerializer.Deserialize(document.ToJsonString(), command.GetType(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("eventGenerationId")]
    [Arguments("expectedMaterialGeneration")]
    [Arguments("expectedTaxonomySourceId")]
    [Arguments("expectedTaxonomyAdoptionId")]
    [Arguments("mandate")]
    [Arguments("materials")]
    [Arguments("components")]
    [Arguments("validUntil")]
    public async Task MissingRequiredInventoryCoordinatesCannotBind(string member)
    {
        var document = JsonSerializer.SerializeToNode(Command("record"),
            typeof(RecordModerationClassificationMaterialCommand), Options)!.AsObject();
        document.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<RecordModerationClassificationMaterialCommand>(document.ToJsonString(), Options))
            .Throws<JsonException>();
    }

    [Test]
    public async Task EndingUsesTheRouteReviewRatherThanSubstitutedBodyIntent()
    {
        var controller = new ModerationClassificationMaterialsController(null!, null!,
            new EndModerationClassificationMaterialCommandHandler(null!))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var action = await controller.End(Guid.Empty,
            (EndModerationClassificationMaterialCommand)Command("end"), CancellationToken.None);
        var failure = (ObjectResult)action.Result!;
        await Assert.That(failure.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(failure.Value is ProblemDetails).IsTrue();
    }

    [Test]
    public async Task MaximumVersionInventoryAndEscapedGroundsFitTheRequestCeiling()
    {
        var command = (RecordModerationClassificationMaterialCommand)Command("record");
        var root = command.Materials[0].Material;
        var session = root with { SessionId = Guid.NewGuid(), SessionStamp = Guid.NewGuid() };
        var categories = Enumerable.Range(0, 64).Select(_ => Guid.CreateVersion7()).ToImmutableArray();
        command = command with
        {
            Materials = Enumerable.Range(0, 4096).Select(index => new ModerationClassificationMaterialSelection(
                Guid.CreateVersion7(), index == 0 ? root : index == 1 ? session
                    : session with { ResourceId = Guid.NewGuid(), ResourceStamp = Guid.NewGuid() }, categories)).ToImmutableArray(),
            Components = Enumerable.Range(0, 4096).Select(_ => new ModerationClassificationMaterialComponentVersion(
                ModerationClassificationMaterialComponentKind.ProviderConnection, Guid.NewGuid(), Guid.NewGuid(),
                new string('a', 64))).ToImmutableArray(),
            Mandate = command.Mandate with
            {
                OwnershipGrounds = new string('\u4e00', 2048),
                ReviewGrounds = new string('\u4e00', 2048)
            }
        };
        var limit = ((IRequestSizeLimitMetadata)typeof(ModerationClassificationMaterialsController)
            .GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize;
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(command, Options).LongLength <= limit).IsTrue();
    }

    private static ModerationQualificationCommand Command(string kind)
    {
        var target = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
        var owner = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        var material = new ModerationClassificationMaterial(Guid.NewGuid(), Guid.NewGuid(), null, null, null, null);
        return kind switch
        {
            "record" => new RecordModerationClassificationMaterialCommand
            {
                OperationId = Guid.CreateVersion7(),
                Target = target,
                ExpectedQualificationRevision = 1,
                SourceId = Guid.CreateVersion7(),
                ExpectedMaterialGeneration = 0,
                EventGenerationId = Guid.CreateVersion7(),
                PolicyId = Guid.CreateVersion7(),
                ExpectedPolicyRevision = 1,
                PublicationId = Guid.CreateVersion7(),
                ProfileId = Guid.CreateVersion7(),
                Mode = ModerationNativeMode.Embedded,
                TaxonomyId = Guid.CreateVersion7(),
                ExpectedTaxonomySourceId = Guid.CreateVersion7(),
                ExpectedTaxonomyAdoptionId = Guid.CreateVersion7(),
                Mandate = new(owner, owner, Guid.CreateVersion7(), 1, reviewer, reviewer,
                    "Actual event assignment.", "Independent material review."),
                Materials = [new(Guid.CreateVersion7(), material, [Guid.CreateVersion7()])],
                Components = [new(ModerationClassificationMaterialComponentKind.Event, material.EventId, material.EventStamp,
                    new string('a', 64))],
                ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
            },
            "accept" => new AcceptModerationClassificationMaterialCommand
            {
                OperationId = Guid.CreateVersion7(),
                Target = target,
                ExpectedQualificationRevision = 2,
                SourceId = Guid.CreateVersion7(),
                ExpectedSourceRevision = 1,
                ExpectedSourceWriterGeneration = 2,
                ReviewId = Guid.CreateVersion7(),
                ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
            },
            "end" => new EndModerationClassificationMaterialCommand
            {
                OperationId = Guid.CreateVersion7(),
                Target = target,
                ExpectedQualificationRevision = 3,
                ReviewId = Guid.CreateVersion7(),
                ExpectedReviewRevision = 1,
                End = ModerationClassificationMaterialEnd.Corrected,
                Grounds = "Current accountable correction."
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
