using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Serialization;
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

public sealed class ModerationClassificationTaxonomiesControllerContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter(), new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Test]
    [Arguments(nameof(ModerationClassificationTaxonomiesController.Record), "sources",
        RouteNames.RecordModerationClassificationTaxonomy)]
    [Arguments(nameof(ModerationClassificationTaxonomiesController.Adopt), "adoptions",
        RouteNames.AdoptModerationClassificationTaxonomy)]
    [Arguments(nameof(ModerationClassificationTaxonomiesController.Withdraw), "adoptions/{adoptionId:guid}/withdraw",
        RouteNames.WithdrawModerationClassificationTaxonomy)]
    public async Task Taxonomy_writes_are_authenticated_bounded_named_and_not_response_cached(
        string action, string template, string name)
    {
        var controller = typeof(ModerationClassificationTaxonomiesController);
        var method = controller.GetMethod(action)!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)controller.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(2L * 1024 * 1024);
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Template).IsEqualTo(template);
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name).IsEqualTo(name);
        await Assert.That(method.GetCustomAttributes<ProducesResponseTypeAttribute>().Any(response =>
            response.StatusCode == StatusCodes.Status200OK && response.Type == typeof(ModerationQualificationResponse))).IsTrue();
    }

    [Test]
    [Arguments("record", "isApproved")]
    [Arguments("record", "isQualified")]
    [Arguments("record", "$type")]
    [Arguments("adopt", "isApproved")]
    [Arguments("adopt", "isQualified")]
    [Arguments("adopt", "$type")]
    [Arguments("withdraw", "isApproved")]
    [Arguments("withdraw", "isQualified")]
    [Arguments("withdraw", "$type")]
    public async Task Claimed_authority_or_polymorphic_command_members_cannot_bind(string kind, string member)
    {
        var command = Command(kind);
        var document = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
        document[member] = "caller assertion";
        await Assert.That(() => JsonSerializer.Deserialize(document.ToJsonString(), command.GetType(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("taxonomyId")]
    [Arguments("expectedTaxonomyGeneration")]
    [Arguments("expectedPolicyRevision")]
    [Arguments("publicationId")]
    [Arguments("profileId")]
    [Arguments("mandate")]
    [Arguments("categories")]
    [Arguments("validUntil")]
    public async Task Missing_required_proposal_coordinates_cannot_bind(string member)
    {
        var document = JsonSerializer.SerializeToNode(Command("record"), typeof(RecordModerationClassificationTaxonomyCommand), Options)!.AsObject();
        document.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<RecordModerationClassificationTaxonomyCommand>(document.ToJsonString(), Options))
            .Throws<JsonException>();
    }

    [Test]
    public async Task Maximum_category_inventory_fits_the_declared_request_budget_even_with_json_escaping()
    {
        var command = (RecordModerationClassificationTaxonomyCommand)Command("record");
        command = command with
        {
            Categories = Enumerable.Range(1, 64).Select(id => new ModerationClassificationTaxonomyCategoryTerms(
                Guid.CreateVersion7(), id, $"category-{id}", EventContentClassificationVersion.CategoryKind.General,
                6, new string('\u4e00', 2048), new string('\u4e00', 2048))).ToImmutableArray()
        };
        var limit = ((IRequestSizeLimitMetadata)typeof(ModerationClassificationTaxonomiesController)
            .GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize;
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(command, Options).LongLength <= limit).IsTrue();
    }

    private static ModerationQualificationCommand Command(string kind)
    {
        var target = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var owner = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        return kind switch
        {
            "record" => new RecordModerationClassificationTaxonomyCommand
            {
                OperationId = Guid.CreateVersion7(), Target = target, ExpectedQualificationRevision = 1,
                SourceId = Guid.CreateVersion7(), TaxonomyId = Guid.CreateVersion7(), ExpectedTaxonomyGeneration = 0,
                PolicyId = Guid.CreateVersion7(), ExpectedPolicyRevision = 1, PublicationId = Guid.CreateVersion7(),
                ProfileId = Guid.CreateVersion7(), Mode = ModerationNativeMode.Embedded,
                Mandate = new(owner, owner, reviewer, reviewer, "Accountable taxonomy owner.", "Independent floor review."),
                Categories = [new(Guid.CreateVersion7(), 1, "general", EventContentClassificationVersion.CategoryKind.General,
                    6, "Reviewed meaning.", "Reviewed floor.")],
                ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
            },
            "adopt" => new AdoptModerationClassificationTaxonomyCommand
            {
                OperationId = Guid.CreateVersion7(), Target = target, ExpectedQualificationRevision = 2,
                SourceId = Guid.CreateVersion7(), ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = 2,
                AdoptionId = Guid.CreateVersion7(), ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
            },
            "withdraw" => new WithdrawModerationClassificationTaxonomyCommand
            {
                OperationId = Guid.CreateVersion7(), Target = target, ExpectedQualificationRevision = 3,
                AdoptionId = Guid.CreateVersion7(), ExpectedAdoptionRevision = 1
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
