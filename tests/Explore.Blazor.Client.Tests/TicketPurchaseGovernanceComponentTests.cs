using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Registration;
using Explore.Blazor.Client.Contracts.Services;
using NSubstitute;

namespace Explore.Blazor.Client.Tests;

public sealed class TicketPurchaseGovernanceComponentTests :
    IDisposable
{
    private readonly BlazorTestContext _context = new();
    private readonly ITicketPurchaseGovernanceService _service;

    public TicketPurchaseGovernanceComponentTests()
    {
        _service = _context.AddMockService<
            ITicketPurchaseGovernanceService>();
        _service.ReserveAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<int>(),
                Arg.Any<Guid?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new TicketPurchaseGovernanceSubmission(
                true,
                false,
                "order"));
    }

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task MissingHalActionOmitsPurchaseControl()
    {
        var cut = Render(hasAction: false);

        await Assert.That(cut.FindAll("button")).IsEmpty();
        await Assert.That(cut.FindAll(
                "[data-testid='ticket-purchase-governance']"))
            .IsEmpty();
    }

    [Test]
    public async Task HalActionRendersSemanticScopeChoiceAndNamedAction()
    {
        var cut = Render(hasAction: true);

        await Assert.That(cut.FindAll(
                "section[aria-labelledby='ticket-purchase-governance-title']"))
            .HasSingleItem();
        await Assert.That(cut.FindAll("fieldset"))
            .HasSingleItem();
        await Assert.That(cut.FindAll("legend"))
            .HasSingleItem();
        await Assert.That(cut.FindAll(
                "button[type='button']"))
            .HasSingleItem();
    }

    [Test]
    public async Task NameOnlyChoicePublishesHonestOrderScopeAlert()
    {
        var cut = Render(
            hasAction: true,
            authenticated: false);
        var choices = cut.FindAll(
            "input[name='purchase-access-mode']");

        await Assert.That(choices.Count).IsEqualTo(2);
        await choices.Single(element =>
                element.GetAttribute("value") == "3")
            .ChangeAsync(new ChangeEventArgs
            {
                Value = "3",
            });

        await Assert.That(cut.FindAll(
                "[role='alert'][data-enforcement-scope='order']"))
            .HasSingleItem();
    }

    [Test]
    public async Task SubmissionUsesBffServiceAndAnnouncesOutcome()
    {
        var cut = Render(
            hasAction: true,
            authenticated: false);
        var buttons = cut.FindAll("button[type='button']");
        await Assert.That(buttons).HasSingleItem();

        await buttons[0].ClickAsync(
            new MouseEventArgs());

        await _service.Received(1).ReserveAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            3,
            null,
            "opaque-capability",
            false,
            Arg.Any<CancellationToken>());
        await Assert.That(cut.FindAll(
                "[role='status'][aria-live='polite']"))
            .HasSingleItem();
    }

    [Test]
    public async Task SubmissionDisablesActionUntilExactServiceCompletion()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<
            TicketPurchaseGovernanceSubmission>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _service.ReserveAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<int>(),
                Arg.Any<Guid?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                entered.TrySetResult();
                return await completion.Task;
            });
        var cut = Render(
            hasAction: true,
            authenticated: false);
        var button = cut.Find("button[type='button']");

        Task click = cut.InvokeAsync(() => button.Click());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(
                cut.Find("button[type='button']")
                    .HasAttribute("disabled"))
            .IsTrue();
        completion.SetResult(
            new TicketPurchaseGovernanceSubmission(
                true,
                false,
                "order"));
        await click;
    }

    private IRenderedComponent<
        TicketPurchaseGovernancePanel> Render(
        bool hasAction,
            bool authenticated = true)
    {
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        IDictionary<string, HalLink> links = hasAction
            ? new Dictionary<string, HalLink>
            {
                ["reserve-purchase-authority"] =
                    new()
                    {
                        Href =
                            $"/api/events/{eventId:D}/registration-orders/{orderId:D}/purchase-authority",
                        Method = "POST",
                    },
            }
            : new Dictionary<string, HalLink>();
        return _context.Render<
            TicketPurchaseGovernancePanel>(
            parameters => parameters
                .Add(component => component.EventId, eventId)
                .Add(component => component.OrderId, orderId)
                .Add(component => component.Links, links)
                .Add(
                    component => component.IsAuthenticated,
                    authenticated)
                .Add(
                    component => component.GuestCapability,
                    authenticated
                        ? null
                        : "opaque-capability"));
    }
}
