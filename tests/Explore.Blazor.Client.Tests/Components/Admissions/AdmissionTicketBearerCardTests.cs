using Explore.Blazor.Client.Components.Admissions;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Tests.Components.Admissions;

public sealed class AdmissionTicketBearerCardTests : IDisposable
{
    private readonly BlazorTestContext _context = new();

    [Test]
    public async Task BearerRendersSensitiveManualAlternativeAndEncodedQrImage()
    {
        IRenderedComponent<AdmissionTicketBearerCard> cut = Render(showPrint: false);

        await Assert.That(cut.Find("img").GetAttribute("alt"))
            .Contains("Admission ticket QR code");
        await Assert.That(cut.Find("img").GetAttribute("src"))
            .StartsWith("data:image/svg+xml;base64,");
        await Assert.That(cut.Find("code").GetAttribute("dir")).IsEqualTo("ltr");
        _ = cut.Find("[data-testid='sensitive-bearer-warning']");
        _ = cut.Find("[data-testid='sensitive-manual-code']");
        await Assert.That(cut.FindAll("[data-testid='print-admission-ticket']").Count)
            .IsEqualTo(0);
    }

    [Test]
    public async Task PrintActionRendersOnlyWhenDeliveryEnablesIt()
    {
        IRenderedComponent<AdmissionTicketBearerCard> cut = Render(showPrint: true);

        await Assert.That(cut.FindAll("[data-testid='print-admission-ticket']").Count)
            .IsEqualTo(1);
    }

    public void Dispose() => _context.Dispose();

    private IRenderedComponent<AdmissionTicketBearerCard> Render(bool showPrint) =>
        _context.RenderMudComponent<AdmissionTicketBearerCard>(parameters => parameters
            .Add(component => component.DisplayReference, "TKT-1234")
            .Add(component => component.StatusCode, "ACTIVE")
            .Add(component => component.ManualCode, "sensitive-manual-code")
            .Add(component => component.ManualCodeClassificationCode, "SENSITIVE_BEARER")
            .Add(component => component.QrRepresentation, "<svg/>")
            .Add(component => component.HolderDisplayName, "Ticket holder")
            .Add(component => component.TicketTypeName, "General admission")
            .Add(component => component.Entitlements, new[]
            {
                new AdmissionTicketEntitlementDto
                {
                    ScopeCode = "EVENT",
                    EventTitle = "Community gathering",
                    IncludedQuantity = 1
                }
            })
            .Add(component => component.ShowPrintAction, showPrint));
}
