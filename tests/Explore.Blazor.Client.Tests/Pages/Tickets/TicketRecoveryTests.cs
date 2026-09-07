using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Admissions;
using Explore.Blazor.Client.Pages.Tickets;

namespace Explore.Blazor.Client.Tests.Pages.Tickets;

public sealed class TicketRecoveryTests : IDisposable
{
    private const string Capability = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly BlazorTestContext _context = new();
    private readonly IAdmissionTicketService _service;
    private readonly IAdmissionRecoveryFragmentInterop _fragment;
    private readonly IAccessibilityFocusService _focus;

    public TicketRecoveryTests()
    {
        _service = _context.AddMockService<IAdmissionTicketService>();
        _fragment = _context.AddMockService<IAdmissionRecoveryFragmentInterop>();
        _ = _context.AddMockService<IAdmissionTicketPrintInterop>();
        _focus = _context.Services.GetRequiredService<IAccessibilityFocusService>();
    }

    [Test]
    public async Task MissingFragmentRendersUniformEmailRequest()
    {
        _fragment.TakeCapabilityAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>(null));

        IRenderedComponent<TicketRecovery> cut =
            _context.RenderMudComponent<TicketRecovery>();

        _ = cut.Find("[data-testid='ticket-recovery-email']");
        await Assert.That(cut.FindAll("[data-testid='admission-ticket-bearer']").Count)
            .IsEqualTo(0);
        await _service.DidNotReceiveWithAnyArgs()
            .ConsumeRecoveryAsync(default!, default);
    }

    [Test]
    public async Task FragmentCapabilityIsConsumedWithoutEnteringMarkup()
    {
        var delivery = new AdmissionTicketRecoveryDeliveryDto
        {
            Id = Guid.CreateVersion7(),
            TicketId = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            StatusCode = "ACTIVE",
            DisplayReference = "TKT-1234",
            ManualCode = "sensitive-manual-code",
            ManualCodeClassificationCode = "SENSITIVE_BEARER",
            QrRepresentation = "<svg/>",
            PrintModel = "print"
        };
        _fragment.TakeCapabilityAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<string?>(Capability));
        _service.ConsumeRecoveryAsync(Capability, Arg.Any<CancellationToken>())
            .Returns(new AdmissionRecoveryUiResult(
                AdmissionRecoveryUiOutcome.Consumed,
                delivery));

        IRenderedComponent<TicketRecovery> cut =
            _context.RenderMudComponent<TicketRecovery>();

        _ = cut.Find("[data-testid='admission-ticket-bearer']");
        _ = cut.Find("[data-testid='sensitive-manual-code']");
        await Assert.That(cut.Markup).DoesNotContain(Capability);
        await _service.Received(1).ConsumeRecoveryAsync(
            Capability,
            Arg.Any<CancellationToken>());
        await _focus.Received(1).FocusByIdAsync(
            "admission-ticket-bearer-heading",
            false);
    }

    public void Dispose() => _context.Dispose();
}
