using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Application.Services.Moderation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationClassificationMaterialInventoryContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments("requestTenantId")]
    [Arguments("humanId")]
    [Arguments("isComplete")]
    [Arguments("isApproved")]
    [Arguments("components")]
    [Arguments("dateOfBirth")]
    public async Task BodyCannotSupplyServerIdentityCompletenessOrPrivateAge(string member)
    {
        var document = JsonSerializer.SerializeToNode(Query(), Options)!.AsObject();
        document[member] = "caller assertion";
        await Assert.That(() => JsonSerializer.Deserialize<GetModerationClassificationMaterialInventoryQuery>(
            document.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    [Arguments("eventGenerationId")]
    [Arguments("taxonomyId")]
    [Arguments("profileId")]
    [Arguments("mode")]
    [Arguments("mandate")]
    public async Task MissingInventoryIntentCannotBind(string member)
    {
        var document = JsonSerializer.SerializeToNode(Query(), Options)!.AsObject();
        document.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<GetModerationClassificationMaterialInventoryQuery>(
            document.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    public async Task QualifiedInventoryTransportIsAuthenticatedNamedBoundedAndUncached()
    {
        var controller = typeof(ModerationClassificationMaterialInventoryController);
        var method = controller.GetMethod(nameof(ModerationClassificationMaterialInventoryController.Read))!;
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Template).IsEqualTo("inventory");
        await Assert.That(method.GetCustomAttribute<HttpPostAttribute>()?.Name).IsEqualTo(RouteNames.ReadModerationClassificationMaterialInventory);
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        await Assert.That(controller.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(controller.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        var limit = ((IRequestSizeLimitMetadata)controller.GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize;
        var query = Query();
        query = query with
        {
            Mandate = query.Mandate with
            {
                OwnershipGrounds = new string('\u4e00', 2048), ReviewGrounds = new string('\u4e00', 2048)
            }
        };
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(query, Options).LongLength <= limit).IsTrue();
    }

    private static GetModerationClassificationMaterialInventoryQuery Query()
    {
        var owner = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        return new()
        {
            EventGenerationId = Guid.CreateVersion7(), TaxonomyId = Guid.CreateVersion7(), ProfileId = Guid.CreateVersion7(),
            Mode = ModerationNativeMode.Embedded,
            Mandate = new(owner, owner, Guid.NewGuid(), 1, reviewer, reviewer,
                "Current event assignment.", "Independent material review.")
        };
    }
}
