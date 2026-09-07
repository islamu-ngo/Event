using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class EventPromotionServiceTests
{
    [Test]
    public async Task GetPromotionsAsync_MapsTypedHalItemsAndExactRelations()
    {
        var api = Substitute.For<IEventPromotionsClient>();
        var service = new EventPromotionService(api);
        var eventId = Guid.CreateVersion7();
        var catalogVersionId = Guid.CreateVersion7();
        var definitionId = Guid.CreateVersion7();
        using var source = new CancellationTokenSource();
        api.GetEventPromotionsAsync(eventId, catalogVersionId, cancellationToken: source.Token).Returns(new HalCollectionResourceOfPromotionManagementDto
        {
            _links = new Dictionary<string, HalLink>
            {
                ["create-promotion"] = new() { Href = "/api/events/promotions", Method = "POST" }
            },
            _embedded = new HalCollectionEmbeddedOfPromotionManagementDto
            {
                Items =
                [
                    new HalResourceOfPromotionManagementDto
                    {
                        EventId = eventId,
                        TicketCatalogVersionId = catalogVersionId,
                        DefinitionId = definitionId,
                        DisplayLabel = "Launch",
                        StatusName = "Published",
                        DiscountKind = "fixed",
                        CurrencyCode = "USD",
                        FixedDiscountMinor = 100,
                        StartsAtUtc = DateTimeOffset.Parse("2026-08-15T00:00:00Z"),
                        EndsAtUtc = DateTimeOffset.Parse("2026-08-22T00:00:00Z"),
                        IncludesAllTickets = true,
                        PromotionCodeDisplayLabel = "SAVE-••24",
                        _links = new Dictionary<string, HalLink>
                        {
                            ["rotate-promotion-code"] = new() { Href = "/api/events/promotions/code:rotate", Method = "POST" }
                        }
                    }
                ]
            }
        });

        var result = await service.GetPromotionsAsync(eventId, catalogVersionId, source.Token);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.HasLink("create-promotion")).IsTrue();
        await Assert.That(result.Items.Single().DefinitionId).IsEqualTo(definitionId);
        await Assert.That(result.Items.Single().HasLink("rotate-promotion-code")).IsTrue();
        await api.Received(1).GetEventPromotionsAsync(eventId, catalogVersionId, cancellationToken: source.Token);
    }

    [Test]
    public async Task LifecycleMutations_ForwardGeneratedRequestsAndCancellation()
    {
        var api = Substitute.For<IEventPromotionsClient>();
        var service = new EventPromotionService(api);
        var eventId = Guid.CreateVersion7();
        var definitionId = Guid.CreateVersion7();
        using var source = new CancellationTokenSource();
        var create = new CreatePromotionDraftRequest
        {
            TicketCatalogVersionId = Guid.CreateVersion7(),
            DisplayLabel = "Launch",
            Code = "CODE",
            DiscountKind = "fixed",
            FixedDiscountMinor = 100,
            StartsAtUtc = TestTime.UtcNow,
            EndsAtUtc = TestTime.UtcNow.AddDays(1),
            EligibleTicketTypeIds = []
        };
        var revise = new RevisePromotionRequest
        {
            DisplayLabel = "Revised launch",
            DiscountKind = "fixed",
            FixedDiscountMinor = 200,
            StartsAtUtc = TestTime.UtcNow,
            EndsAtUtc = TestTime.UtcNow.AddDays(2),
            EligibleTicketTypeIds = []
        };
        var code = new PromotionCodeRequest { Code = "CODE" };
        var revoke = new RevokePromotionRequest();
        var issued = new PromotionCodeIssuedCommandResponseDto { Success = true };
        var managed = new PromotionManagementCommandResponseDto { Success = true };
        api.CreateEventPromotionDraftAsync(eventId, Arg.Is<string>(value => IsUuid7(value)), create, cancellationToken: source.Token).Returns(issued);
        api.ReviseEventPromotionAsync(eventId, definitionId, Arg.Is<string>(value => IsUuid7(value)), revise, cancellationToken: source.Token).Returns(managed);
        api.PublishEventPromotionAsync(eventId, definitionId, Arg.Is<string>(value => IsUuid7(value)), code, cancellationToken: source.Token).Returns(managed);
        api.RevokeEventPromotionAsync(eventId, definitionId, Arg.Is<string>(value => IsUuid7(value)), revoke, cancellationToken: source.Token).Returns(managed);
        api.RotateEventPromotionCodeAsync(eventId, definitionId, Arg.Is<string>(value => IsUuid7(value)), code, cancellationToken: source.Token).Returns(issued);

        await Assert.That(await service.CreateDraftAsync(eventId, create, source.Token)).IsSameReferenceAs(issued);
        await Assert.That(await service.ReviseAsync(eventId, definitionId, revise, source.Token)).IsSameReferenceAs(managed);
        await Assert.That(await service.PublishAsync(eventId, definitionId, code, source.Token)).IsSameReferenceAs(managed);
        await Assert.That(await service.RevokeAsync(eventId, definitionId, revoke, source.Token)).IsSameReferenceAs(managed);
        await Assert.That(await service.RotateCodeAsync(eventId, definitionId, code, source.Token)).IsSameReferenceAs(issued);
        await api.Received(1).CreateEventPromotionDraftAsync(eventId, Arg.Is<string>(value => IsUuid7(value)), create, cancellationToken: source.Token);
        await api.Received(1).ReviseEventPromotionAsync(eventId, definitionId, Arg.Is<string>(value => IsUuid7(value)), revise, cancellationToken: source.Token);
        await api.Received(1).PublishEventPromotionAsync(eventId, definitionId, Arg.Is<string>(value => IsUuid7(value)), code, cancellationToken: source.Token);
        await api.Received(1).RevokeEventPromotionAsync(eventId, definitionId, Arg.Is<string>(value => IsUuid7(value)), revoke, cancellationToken: source.Token);
        await api.Received(1).RotateEventPromotionCodeAsync(eventId, definitionId, Arg.Is<string>(value => IsUuid7(value)), code, cancellationToken: source.Token);
    }

    private static bool IsUuid7(string? value) =>
        Guid.TryParse(value, out Guid idempotencyKey) && idempotencyKey.Version == 7;
}
