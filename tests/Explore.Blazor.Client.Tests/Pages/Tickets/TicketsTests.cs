using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using TicketsPage = Explore.Blazor.Client.Pages.Tickets.Tickets;

namespace Explore.Blazor.Client.Tests.Pages.Tickets;

public sealed class TicketsTests : IDisposable
{
    private readonly BlazorTestContext _context = new();
    private readonly IAdmissionTicketService _service;

    public TicketsTests()
    {
        _service = _context.AddMockService<IAdmissionTicketService>();
    }

    [Test]
    public async Task TicketWithoutHalLinksRendersNoNavigationActions()
    {
        HalResourceOfAdmissionTicketDto ticket = Ticket();
        _service.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { ticket });

        IRenderedComponent<TicketsPage> cut = _context.RenderMudComponent<TicketsPage>();

        await Assert.That(cut.FindAll("[data-testid='open-admission-ticket']").Count)
            .IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-testid='open-registration-order']").Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task TicketNavigationRendersFromSelfAndOrderRelations()
    {
        HalResourceOfAdmissionTicketDto ticket = Ticket();
        ticket._links = new Dictionary<string, HalLink>
        {
            ["self"] = new()
            {
                Href = $"/api/tickets/{ticket.TicketId:D}",
                Method = HttpMethod.Get.Method
            },
            ["registration-order"] = new()
            {
                Href =
                    $"/api/events/{ticket.EventId:D}/registration-orders/{ticket.RegistrationOrderId:D}",
                Method = HttpMethod.Get.Method
            }
        };
        _service.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { ticket });

        IRenderedComponent<TicketsPage> cut = _context.RenderMudComponent<TicketsPage>();

        await Assert.That(cut.FindAll("[data-testid='open-admission-ticket']").Count)
            .IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='open-registration-order']").Count)
            .IsEqualTo(1);
    }

    public void Dispose() => _context.Dispose();

    private static HalResourceOfAdmissionTicketDto Ticket()
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
            StatusCode = "ACTIVE",
            DisplayReference = "TKT-1234"
        };
    }
}
