using System.Net;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Exceptions;
using Explore.Blazor.Client.Services.Admissions;
using Explore.Blazor.Client.Services.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AdmissionTicketServiceTests
{
    [Test]
    public async Task GetCurrentReturnsTypedHalResources()
    {
        IAdmissionTicketClient api = Substitute.For<IAdmissionTicketClient>();
        HalResourceOfAdmissionTicketDto ticket = Ticket();
        api.GetCurrentAdmissionTicketsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfAdmissionTicketDto
            {
                _embedded = new HalCollectionEmbeddedOfAdmissionTicketDto
                {
                    Items = [ticket]
                }
            });
        var service = Create(api);

        IReadOnlyList<HalResourceOfAdmissionTicketDto> result =
            await service.GetCurrentAsync();

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].TicketId).IsEqualTo(ticket.TicketId);
        await Assert.That(result[0]._links).ContainsKey("self");
    }

    [Test]
    public async Task ReissueQrWithoutExactHalPostLinkNeverCallsApi()
    {
        IAdmissionTicketClient api = Substitute.For<IAdmissionTicketClient>();
        HalResourceOfAdmissionTicketDto ticket = Ticket();
        ticket._links!["qr-code"] = new HalLink
        {
            Href = $"/api/tickets/{ticket.TicketId:D}/qr",
            Method = HttpMethod.Get.Method
        };
        var service = Create(api);

        AdmissionTicketQrDeliveryDto? result = await service.ReissueQrAsync(ticket);

        await Assert.That(result).IsNull();
        await api.DidNotReceiveWithAnyArgs()
            .ReissueCurrentAdmissionTicketQrAsync(default, default, default, default);
    }

    [Test]
    public async Task ReissueQrForwardsExactHalPostAction()
    {
        IAdmissionTicketClient api = Substitute.For<IAdmissionTicketClient>();
        HalResourceOfAdmissionTicketDto ticket = Ticket();
        ticket._links!["qr-code"] = new HalLink
        {
            Href = $"/api/tickets/{ticket.TicketId:D}/qr",
            Method = HttpMethod.Post.Method
        };
        var expected = new AdmissionTicketQrDeliveryDto
        {
            TicketId = ticket.TicketId,
            QrRepresentation = "<svg/>",
            PrintModel = "print",
            DeliverySurface = "qr"
        };
        api.ReissueCurrentAdmissionTicketQrAsync(
                ticket.TicketId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = Create(api);

        AdmissionTicketQrDeliveryDto? result = await service.ReissueQrAsync(ticket);

        await Assert.That(result).IsSameReferenceAs(expected);
        await api.Received(1).ReissueCurrentAdmissionTicketQrAsync(
            ticket.TicketId,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConsumeRecoveryUsesBffAndMapsNotFoundWithoutApiClientCall()
    {
        IAdmissionTicketClient api = Substitute.For<IAdmissionTicketClient>();
        IAdmissionTicketRecoveryClient recoveryClient =
            Substitute.For<IAdmissionTicketRecoveryClient>();
        IAdmissionRecoveryBffClient bff =
            Substitute.For<IAdmissionRecoveryBffClient>();
        bff.ConsumeAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(ApiResult<AdmissionTicketRecoveryDeliveryDto>.Failure(
                new ApiProblemException(HttpStatusCode.NotFound, "Not found")));
        var service = Create(api, bff, recoveryClient);

        AdmissionRecoveryUiResult result =
            await service.ConsumeRecoveryAsync("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        await Assert.That(result.Outcome).IsEqualTo(AdmissionRecoveryUiOutcome.Invalid);
        await bff.Received(1).ConsumeAsync(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            Arg.Any<CancellationToken>());
        await recoveryClient.DidNotReceiveWithAnyArgs()
            .ConsumeAdmissionTicketRecoveryAsync(default, default, default, default);
    }

    private static AdmissionTicketService Create(
        IAdmissionTicketClient api,
        IAdmissionRecoveryBffClient? bff = null,
        IAdmissionTicketRecoveryClient? recoveryClient = null)
    {
        return new AdmissionTicketService(
            api,
            recoveryClient ?? Substitute.For<IAdmissionTicketRecoveryClient>(),
            bff ?? Substitute.For<IAdmissionRecoveryBffClient>(),
            NullLogger<AdmissionTicketService>.Instance);
    }

    private static HalResourceOfAdmissionTicketDto Ticket()
    {
        Guid ticketId = Guid.CreateVersion7();
        return new HalResourceOfAdmissionTicketDto
        {
            Id = ticketId,
            TicketId = ticketId,
            EventId = Guid.CreateVersion7(),
            RegistrationOrderId = Guid.CreateVersion7(),
            HolderDisplayName = "Ticket holder",
            TicketTypeName = "General admission",
            Entitlements =
            [
                new AdmissionTicketEntitlementDto
                {
                    ScopeCode = "EVENT",
                    EventTitle = "Community gathering",
                    IncludedQuantity = 1
                }
            ],
            IssuedAtUtc = TestTime.UtcNow,
            StatusCode = "ACTIVE",
            DisplayReference = "TKT-1234",
            _links = new Dictionary<string, HalLink>
            {
                ["self"] = new()
                {
                    Href = $"/api/tickets/{ticketId:D}",
                    Method = HttpMethod.Get.Method
                }
            }
        };
    }
}
