using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell.Workspaces;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioOrdersTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventService _eventService;
    private readonly IStudioContextService _studioContextService;
    private readonly IRegistrationOrderService _registrationOrderService;
    private readonly IRegistrationPaymentService _registrationPaymentService;
    private readonly UiShellState _shellState;

    public StudioOrdersTests()
    {
        _eventService = _ctx.AddMockService<IEventService>();
        _studioContextService = _ctx.AddMockService<IStudioContextService>();
        _registrationOrderService = _ctx.AddMockService<IRegistrationOrderService>();
        _registrationPaymentService = _ctx.AddMockService<IRegistrationPaymentService>();
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _shellState = _ctx.Services.GetRequiredService<UiShellState>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task ActorOrders_OmitsEventsWithoutOrderHalRelationWithoutRequestingTheirOrders()
    {
        var actorId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        _shellState.ReconcileActiveActors([new ManagedActorDto { ActorId = actorId, DisplayName = "Community" }], actorId);
        _studioContextService.GetContextAsync(actorId, Arg.Any<CancellationToken>()).Returns(new HalResourceOfStudioContextDto
        {
            _links = new Dictionary<string, HalLink>
            {
                ["view-registration-orders"] = new() { Href = "/api/studio/context", Method = "GET" }
            }
        });
        _eventService.GetManagedEventsByActorAsync(actorId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateResult([CreateEvent(eventId)])));
        _studioContextService.GetEventOrdersAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HalResourceOfRegistrationOrderDto>>([]));

        var cut = _ctx.RenderMudComponent<StudioOrders>();

        cut.WaitForElement("[data-testid='studio-orders-no-events']");
        await Assert.That(cut.FindAll("[data-testid='studio-order-list']")).IsEmpty();
        await _studioContextService.DidNotReceive().GetEventOrdersAsync(eventId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ActorOrders_RendersOnlyEventsWithTheirOwnOrderHalRelation()
    {
        var actorId = Guid.CreateVersion7();
        var linkedEventId = Guid.CreateVersion7();
        var unlinkedEventId = Guid.CreateVersion7();
        _shellState.ReconcileActiveActors([new ManagedActorDto { ActorId = actorId, DisplayName = "Community" }], actorId);
        _studioContextService.GetContextAsync(actorId, Arg.Any<CancellationToken>()).Returns(new HalResourceOfStudioContextDto
        {
            _links = new Dictionary<string, HalLink>
            {
                ["view-registration-orders"] = new() { Href = "/api/studio/context", Method = "GET" }
            }
        });
        _eventService.GetManagedEventsByActorAsync(actorId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateResult([CreateEvent(linkedEventId, hasOrderLink: true), CreateEvent(unlinkedEventId)])));
        _studioContextService.GetEventOrdersAsync(linkedEventId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HalResourceOfRegistrationOrderDto>>([]));

        var cut = _ctx.RenderMudComponent<StudioOrders>();

        cut.WaitForElement("[data-testid='studio-orders-empty']");
        await Assert.That(cut.FindAll(".studio-orders__event")).Count().IsEqualTo(1);
        await _studioContextService.Received(1).GetEventOrdersAsync(linkedEventId, Arg.Any<CancellationToken>());
        await _studioContextService.DidNotReceive().GetEventOrdersAsync(unlinkedEventId, Arg.Any<CancellationToken>());
        await _registrationPaymentService.DidNotReceive().GetRefundCampaignsAsync(linkedEventId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PersonalOrders_UsesMyEventsWhenNoActorIsActiveAndOrdersRelationIsVisible()
    {
        var eventId = Guid.CreateVersion7();
        _studioContextService.GetContextAsync(null, Arg.Any<CancellationToken>()).Returns(new HalResourceOfStudioContextDto
        {
            _links = new Dictionary<string, HalLink>
            {
                ["view-registration-orders"] = new() { Href = "/api/studio/context", Method = "GET" }
            }
        });
        _eventService.GetMyEventsPagedAsync(1, 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateResult([CreateEvent(eventId, hasOrderLink: true)])));
        _studioContextService.GetEventOrdersAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HalResourceOfRegistrationOrderDto>>([]));

        var cut = _ctx.RenderMudComponent<StudioOrders>();

        cut.WaitForElement("[data-testid='studio-orders-empty']");
        await Assert.That(cut.FindAll("[data-testid='studio-orders-unavailable']")).IsEmpty();
        await _eventService.Received(1).GetMyEventsPagedAsync(1, 100, Arg.Any<CancellationToken>());
        await _eventService.DidNotReceive().GetManagedEventsByActorAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _studioContextService.Received(1).GetEventOrdersAsync(eventId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ActorOrders_RendersUnavailableInsteadOfEmptyWhenManagedEventReadFails()
    {
        var actorId = Guid.CreateVersion7();
        _shellState.ReconcileActiveActors([new ManagedActorDto { ActorId = actorId, DisplayName = "Community" }], actorId);
        _studioContextService.GetContextAsync(actorId, Arg.Any<CancellationToken>()).Returns(new HalResourceOfStudioContextDto
        {
            _links = new Dictionary<string, HalLink>
            {
                ["view-registration-orders"] = new() { Href = "/api/studio/context", Method = "GET" }
            }
        });
        _eventService.GetManagedEventsByActorAsync(actorId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PaginatedResult<EventListDto>>(new InvalidOperationException("unavailable")));

        var cut = _ctx.RenderMudComponent<StudioOrders>();

        cut.WaitForElement("[data-testid='studio-orders-unavailable']");
        await Assert.That(cut.FindAll("[data-testid='studio-orders-no-events']")).IsEmpty();
        await Assert.That(cut.Markup).Contains("Registration orders are currently unavailable.");
    }

    [Test]
    public async Task EventOrderList_RendersUnavailableInsteadOfEmptyWhenTheOrderReadFails()
    {
        var eventId = Guid.CreateVersion7();
        _studioContextService.GetEventOrdersAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<HalResourceOfRegistrationOrderDto>>(new InvalidOperationException("unavailable")));

        var cut = _ctx.RenderMudComponent<StudioOrderList>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='studio-orders-unavailable']");
        await Assert.That(cut.FindAll("[data-testid='studio-orders-empty']")).IsEmpty();
        await Assert.That(cut.Markup).Contains("Registration orders are currently unavailable.");
    }

    [Test]
    public async Task EventOrderList_EventNavigationCancelsAndRejectsLatePriorEventOrders()
    {
        var firstEventId = Guid.CreateVersion7();
        var secondEventId = Guid.CreateVersion7();
        var staleOrder = CreateOrder();
        staleOrder.StatusName = "Stale prior event";
        var currentOrder = CreateOrder();
        currentOrder.StatusName = "Current event";
        var pending = new TaskCompletionSource<IReadOnlyList<HalResourceOfRegistrationOrderDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        _studioContextService.GetEventOrdersAsync(firstEventId, Arg.Any<CancellationToken>()).Returns(call =>
        {
            firstToken = call.ArgAt<CancellationToken>(1);
            return pending.Task;
        });
        _studioContextService.GetEventOrdersAsync(secondEventId, Arg.Any<CancellationToken>()).Returns([currentOrder]);

        var cut = _ctx.RenderMudComponent<StudioOrderList>(parameters => parameters.Add(component => component.EventId, firstEventId));
        cut.Render(parameters => parameters.Add(component => component.EventId, secondEventId));
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Current event", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Current event orders have not rendered.");
            }
        });
        pending.SetResult([staleOrder]);
        await Task.Yield();

        await Assert.That(firstToken.IsCancellationRequested).IsTrue();
        await Assert.That(cut.Markup).Contains("Current event");
        await Assert.That(cut.Markup).DoesNotContain("Stale prior event");
    }

    [Test]
    public async Task EventOrderList_LoadsBoundedPaymentOnlyFromExactStudioRelation()
    {
        var eventId = Guid.CreateVersion7();
        var linkedOrder = CreateOrder("studio-payment-status");
        var unlinkedOrder = CreateOrder();
        _studioContextService.GetEventOrdersAsync(eventId, Arg.Any<CancellationToken>()).Returns([linkedOrder, unlinkedOrder]);
        _registrationPaymentService.GetStudioPaymentAsync(eventId, linkedOrder.Id!.Value, linkedOrder, Arg.Any<CancellationToken>()).Returns(
            new HalResourceOfRegistrationPaymentDto
            {
                StatusCode = "NeedsReconciliation",
                StatusName = "Needs reconciliation",
                LastUpdatedAt = TestTime.UtcNow,
                FailureCode = "PAYMENT_RETRY_NOT_AVAILABLE"
            });

        var cut = _ctx.RenderMudComponent<StudioOrderList>(parameters => parameters.Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='studio-payment-status']");
        await Assert.That(cut.Markup).Contains("Needs reconciliation");
        await Assert.That(cut.Markup).Contains("Retry is not available");
        await Assert.That(cut.Markup).DoesNotContain("Provider");
        await _registrationPaymentService.Received(1).GetStudioPaymentAsync(
            eventId, linkedOrder.Id.Value, linkedOrder, Arg.Any<CancellationToken>());
        await _registrationPaymentService.DidNotReceive().GetStudioPaymentAsync(
            eventId, unlinkedOrder.Id!.Value, unlinkedOrder, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StudioPaymentStatus_EventOrderChangeCancelsAndIgnoresLateResponse()
    {
        var firstEventId = Guid.CreateVersion7();
        var secondEventId = Guid.CreateVersion7();
        var firstOrder = CreateOrder("studio-payment-status");
        var secondOrder = CreateOrder("studio-payment-status");
        var pending = new TaskCompletionSource<HalResourceOfRegistrationPaymentDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        _registrationPaymentService.GetStudioPaymentAsync(firstEventId, firstOrder.Id!.Value, firstOrder, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                firstToken = call.ArgAt<CancellationToken>(3);
                return pending.Task;
            });
        _registrationPaymentService.GetStudioPaymentAsync(secondEventId, secondOrder.Id!.Value, secondOrder, Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfRegistrationPaymentDto { StatusCode = "Succeeded", StatusName = "Current payment" });

        var cut = _ctx.RenderMudComponent<StudioPaymentStatus>(parameters => parameters
            .Add(component => component.EventId, firstEventId)
            .Add(component => component.Order, firstOrder));
        cut.Render(parameters => parameters
            .Add(component => component.EventId, secondEventId)
            .Add(component => component.Order, secondOrder));
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Current payment", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Current payment has not rendered.");
            }
        });
        pending.SetResult(new HalResourceOfRegistrationPaymentDto { StatusCode = "Failed", StatusName = "Stale payment" });
        await Task.Yield();

        await Assert.That(firstToken.IsCancellationRequested).IsTrue();
        await Assert.That(cut.Markup).Contains("Current payment");
        await Assert.That(cut.Markup).DoesNotContain("Stale payment");
    }

    [Test]
    public async Task StudioPaymentStatus_UsesContractReconciliationAlertAndUniqueHeadingIds()
    {
        var eventId = Guid.CreateVersion7();
        var first = CreateOrder("studio-payment-status");
        var second = CreateOrder("studio-payment-status");
        _studioContextService.GetEventOrdersAsync(eventId, Arg.Any<CancellationToken>()).Returns([first, second]);
        _registrationPaymentService.GetStudioPaymentAsync(eventId, Arg.Any<Guid>(), Arg.Any<HalResourceOfRegistrationOrderDto>(), Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfRegistrationPaymentDto { StatusCode = "NeedsReconciliation", StatusName = "Needs reconciliation" });

        var cut = _ctx.RenderMudComponent<StudioOrderList>(parameters => parameters.Add(component => component.EventId, eventId));

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-testid='studio-payment-status']").Count != 2)
            {
                throw new InvalidOperationException("Both Studio payment projections have not rendered.");
            }
        });
        string[] labelledBy = cut.FindAll("[data-testid='studio-payment-status']")
            .Select(element => element.GetAttribute("aria-labelledby")!)
            .ToArray();
        await Assert.That(labelledBy.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(2);
        await Assert.That(cut.FindAll("[data-testid='studio-payment-status'][role='alert']").Count).IsEqualTo(2);
    }

    [Test]
    public async Task StudioPaymentStatusRendersRefundAndDisputeTruthAndGatesMutationByPaymentHal()
    {
        Guid eventId = Guid.CreateVersion7();
        HalResourceOfRegistrationOrderDto order = CreateOrder("studio-payment-status");
        var payment = new HalResourceOfRegistrationPaymentDto
        {
            StatusCode = "Succeeded",
            StatusName = "Succeeded",
            CurrencyCode = "EUR",
            CapturedAmountMinor = 1_000,
            Refunds = [new RegistrationRefundDto
            {
                StatusCode = "Pending",
                StatusName = "Pending",
                AmountMinor = 400,
                CurrencyCode = "EUR"
            }],
            Disputes = [new RegistrationPaymentDisputeDto { StageCode = "Formal", StatusCode = "Open" }],
            _links = new Dictionary<string, HalLink>
            {
                ["create-refund"] = new() { Href = "/api/refund", Method = "POST" },
                ["retry-refund"] = new()
                {
                    Href = $"/api/events/{eventId:D}/registration-orders/{order.Id:D}/payment/studio/refunds/{Guid.CreateVersion7():D}/retry",
                    Method = "POST"
                }
            }
        };
        _registrationPaymentService.GetStudioPaymentAsync(eventId, order.Id!.Value, order, Arg.Any<CancellationToken>())
            .Returns(payment);

        var cut = _ctx.RenderMudComponent<StudioPaymentStatus>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.Order, order));

        cut.WaitForElement("[data-testid='studio-refund-status']");
        await Assert.That(cut.FindAll("[data-testid='studio-dispute-status']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='studio-create-refund']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='studio-retry-refund']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task RefundCampaignRecoveryRendersOnlyFromCampaignHal()
    {
        Guid eventId = Guid.CreateVersion7();
        var campaign = new HalResourceOfRefundCampaignDto
        {
            Id = Guid.CreateVersion7(),
            KindCode = "EventCancellation",
            StatusCode = "RequiresOperator",
            OperatorCaseCount = 1,
            _links = new Dictionary<string, HalLink>
            {
                ["resume-refund-campaign"] = new() { Href = "/api/refund-campaign/resume", Method = "POST" }
            }
        };
        _registrationPaymentService.GetRefundCampaignsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfRefundCampaignDto
            {
                _embedded = new HalCollectionEmbeddedOfRefundCampaignDto { Items = [campaign] }
            });

        var cut = _ctx.RenderMudComponent<StudioRefundCampaigns>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='studio-refund-campaign']");
        await Assert.That(cut.FindAll("[data-testid='resume-refund-campaign']").Count).IsEqualTo(1);

        campaign._links!.Clear();
        cut.Render();
        await Assert.That(cut.FindAll("[data-testid='resume-refund-campaign']")).IsEmpty();
    }

    [Test]
    public async Task ActorOrders_LoadsCampaignsOnlyWhenEventAdvertisesCampaignRelation()
    {
        Guid actorId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        _shellState.ReconcileActiveActors([new ManagedActorDto { ActorId = actorId, DisplayName = "Community" }], actorId);
        _studioContextService.GetContextAsync(actorId, Arg.Any<CancellationToken>()).Returns(new HalResourceOfStudioContextDto
        {
            _links = new Dictionary<string, HalLink>
            {
                ["view-registration-orders"] = new() { Href = "/api/studio/context", Method = "GET" }
            }
        });
        _eventService.GetManagedEventsByActorAsync(actorId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateResult([CreateEvent(eventId, hasOrderLink: true, hasRefundCampaignLink: true)])));
        _studioContextService.GetEventOrdersAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HalResourceOfRegistrationOrderDto>>([]));
        _registrationPaymentService.GetRefundCampaignsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfRefundCampaignDto
            {
                _embedded = new HalCollectionEmbeddedOfRefundCampaignDto { Items = [] }
            });

        _ctx.RenderMudComponent<StudioOrders>().WaitForElement("[data-testid='studio-orders-empty']");

        await _registrationPaymentService.Received(1).GetRefundCampaignsAsync(eventId, Arg.Any<CancellationToken>());
    }

    private static HalResourceOfRegistrationOrderDto CreateOrder(params string[] relations) => new()
    {
        Id = Guid.CreateVersion7(),
        StatusCode = "AWAITING_PAYMENT",
        StatusName = "Awaiting payment",
        CurrencyCode = "EUR",
        _links = relations.ToDictionary(
            relation => relation,
            relation => new HalLink { Href = $"/api/orders/payment/{relation}", Method = "GET" })
    };

    private static EventListDto CreateEvent(
        Guid eventId,
        bool hasOrderLink = false,
        bool hasRefundCampaignLink = false)
    {
        var item = new EventListDto { Id = eventId, Title = "Community event" };
        var links = new Dictionary<string, object>();
        if (hasOrderLink)
        {
            links["view-registration-orders"] = new { href = $"/api/events/{eventId}/registration-orders" };
        }
        if (hasRefundCampaignLink)
        {
            links["refund-campaigns"] = new { href = $"/api/events/{eventId}/refund-campaigns" };
        }
        item.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            links);
        return item;
    }

    private static PaginatedResult<EventListDto> CreateResult(IReadOnlyList<EventListDto> events) => new()
    {
        Items = events.ToList(),
        PageNumber = 1,
        PageSize = 100,
        TotalCount = events.Count
    };
}
