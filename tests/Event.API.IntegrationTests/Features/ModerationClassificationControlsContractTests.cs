using System.Text.Json;
using Explore.API.Controllers;
using Explore.API.Serialization;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationClassificationControlsContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter() }
    };

    [Test]
    [Arguments("isApproved")]
    [Arguments("isComplete")]
    [Arguments("publicDisclosureAllowed")]
    [Arguments("decisionId")]
    [Arguments("$type")]
    public async Task CallerAssertionsCannotEnterIndependentAdoption(string member)
    {
        var document = JsonSerializer.SerializeToNode(Adoption(), Options)!.AsObject();
        document[member] = "caller assertion";
        await Assert.That(() => JsonSerializer.Deserialize<AdoptModerationClassificationEventTargetCommand>(
            document.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    [Arguments("selectionId")]
    [Arguments("expectedSelectionRevision")]
    [Arguments("bindingOrdinal")]
    [Arguments("eventGenerationId")]
    [Arguments("expectedEventGenerationRevision")]
    [Arguments("validUntil")]
    public async Task MissingNativePartitionCoordinatesCannotBind(string member)
    {
        var document = JsonSerializer.SerializeToNode(Adoption(), Options)!.AsObject();
        document.Remove(member);
        await Assert.That(() => JsonSerializer.Deserialize<AdoptModerationClassificationEventTargetCommand>(
            document.ToJsonString(), Options)).Throws<JsonException>();
    }

    [Test]
    public async Task RevocationUsesRouteIdentityBeforeNativeAcquisition()
    {
        var adoption = Adoption();
        var controller = new ModerationClassificationControlsController(null!, null!,
            new RevokeModerationClassificationEventTargetCommandHandler(null!))
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        var action = await controller.Revoke(Guid.Empty, new RevokeModerationClassificationEventTargetCommand
        {
            OperationId = adoption.OperationId,
            Target = adoption.Target,
            ExpectedQualificationRevision = 6,
            AdoptionId = adoption.AdoptionId,
            ExpectedAdoptionRevision = 1,
            Grounds = "End the independently adopted material control."
        }, CancellationToken.None);
        var failure = (ObjectResult)action.Result!;
        await Assert.That(failure.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(failure.Value is ProblemDetails).IsTrue();
    }

    private static AdoptModerationClassificationEventTargetCommand Adoption()
    {
        var tenant = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
        return new()
        {
            OperationId = Guid.CreateVersion7(),
            Target = AuthorityScope.Event(tenant, Guid.NewGuid()),
            ExpectedQualificationRevision = 5,
            AdoptionId = Guid.CreateVersion7(),
            SelectionId = Guid.CreateVersion7(),
            ExpectedSelectionRevision = 1,
            BindingOrdinal = 0,
            EventGenerationId = Guid.CreateVersion7(),
            ExpectedEventGenerationRevision = 1,
            ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
        };
    }
}
