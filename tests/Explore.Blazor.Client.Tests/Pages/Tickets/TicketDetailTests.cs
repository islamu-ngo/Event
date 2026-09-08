using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Pages.Tickets;

namespace Explore.Blazor.Client.Tests.Pages.Tickets;

public sealed class TicketDetailTests : IDisposable
{
    private readonly BlazorTestContext _context = new();
    private readonly IAdmissionTicketService _service;

    public TicketDetailTests()
    {
        _service = _context.AddMockService<IAdmissionTicketService>();
        _ = _context.AddMockService<IAdmissionTicketPrintInterop>();
    }

    [Test]
    public async Task RevokedTicketRendersInvalidStateWithoutCredentialActions()
    {
        HalResourceOfAdmissionTicketDto ticket = Ticket("REVOKED");
        _service.GetAsync(ticket.TicketId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        IRenderedComponent<TicketDetail> cut = Render(ticket.TicketId);

        _ = cut.Find("[data-testid='non-validating-ticket']");
        await Assert.That(cut.FindAll("[data-testid='reissue-ticket-qr']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-testid='reissue-ticket-print']").Count).IsEqualTo(0);
    }

    [Test]
    public async Task QrActionRendersOnlyFromQrPostRelation()
    {
        HalResourceOfAdmissionTicketDto ticket = Ticket("ACTIVE");
        ticket._links!["qr-code"] = new HalLink
        {
            Href = $"/api/tickets/{ticket.TicketId:D}/qr",
            Method = HttpMethod.Post.Method
        };
        _service.GetAsync(ticket.TicketId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        IRenderedComponent<TicketDetail> cut = Render(ticket.TicketId);

        await Assert.That(cut.FindAll("[data-testid='reissue-ticket-qr']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='reissue-ticket-print']").Count).IsEqualTo(0);
    }

    [Test]
    public async Task LocalActiveStatusNeverCreatesMissingHalAction()
    {
        HalResourceOfAdmissionTicketDto ticket = Ticket("ACTIVE");
        _service.GetAsync(ticket.TicketId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        IRenderedComponent<TicketDetail> cut = Render(ticket.TicketId);

        await Assert.That(cut.FindAll("[data-testid='reissue-ticket-qr']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-testid='reissue-ticket-print']").Count).IsEqualTo(0);
    }

    [Test]
    public async Task WrongMethodHalRelationNeverRendersCredentialAction()
    {
        HalResourceOfAdmissionTicketDto ticket = Ticket("ACTIVE");
        ticket._links!["qr-code"] = new HalLink
        {
            Href = $"/api/tickets/{ticket.TicketId:D}/qr",
            Method = HttpMethod.Get.Method
        };
        _service.GetAsync(ticket.TicketId, Arg.Any<CancellationToken>())
            .Returns(ticket);

        IRenderedComponent<TicketDetail> cut = Render(ticket.TicketId);

        await Assert.That(cut.FindAll("[data-testid='reissue-ticket-qr']").Count).IsEqualTo(0);
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<TicketDetail> Render(Guid ticketId) =>
        _context.RenderMudComponent<TicketDetail>(parameters =>
            parameters.Add(component => component.TicketId, ticketId));

    private static HalResourceOfAdmissionTicketDto Ticket(string status)
    {
        Guid id = Guid.CreateVersion7();
        return new HalResourceOfAdmissionTicketDto
        {
            Id = id,
            TicketId = id,
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
            StatusCode = status,
            DisplayReference = "TKT-1234",
            _links = new Dictionary<string, HalLink>
            {
                ["self"] = new()
                {
                    Href = $"/api/tickets/{id:D}",
                    Method = HttpMethod.Get.Method
                }
            }
        };
    }
}
