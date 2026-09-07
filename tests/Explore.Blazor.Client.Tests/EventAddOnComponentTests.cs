using AngleSharp.Dom;
using Bunit;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Registration;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Explore.Blazor.Client.Tests;

public sealed class EventAddOnComponentTests
{
    [Test]
    public async Task BuyerSurfaceRendersUncheckedUnavailableAndBadInputStates()
    {
        using var context = new BlazorTestContext();
        IEventAddOnService service = Substitute.For<IEventAddOnService>();
        context.Services.AddSingleton(service);
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        Guid availableId = Guid.CreateVersion7();
        HalResourceOfEventAddOnCatalogDto catalog = Catalog(
            eventId,
            [
                Item(availableId, "Lunch", true, 2),
                Item(Guid.CreateVersion7(), "Parking", false, 0),
            ]);
        HalResourceOfRegistrationOrderAddOnSummaryDto order = Order(orderId);
        service.GetCatalogAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfEventAddOnCatalogDto?>(catalog));
        service.GetOrderAsync(
                eventId,
                orderId,
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfRegistrationOrderAddOnSummaryDto?>(order));

        IRenderedComponent<EventAddOnSelector> rendered =
            context.Render<EventAddOnSelector>(parameters => parameters
                .Add(component => component.EventId, eventId)
                .Add(component => component.RegistrationOrderId, orderId));
        rendered.WaitForState(() => rendered.FindAll("input[type=checkbox]").Count == 2);

        IElement[] checkboxes = rendered.FindAll("input[type=checkbox]").ToArray();
        await Assert.That(checkboxes.All(input => !input.HasAttribute("checked"))).IsTrue();
        await Assert.That(checkboxes[1].HasAttribute("disabled")).IsTrue();
        await Assert.That(rendered.FindAll("button[type=submit]")).IsEmpty();

        checkboxes[0].Change(true);
        IElement quantity = rendered.Find($"#event-add-on-{availableId:N}-quantity");
        await Assert.That(quantity.GetAttribute("value")).IsEqualTo("1");
        await Assert.That(rendered.FindAll("button[type=submit]").Count).IsEqualTo(1);

        quantity.Change("not-a-number");
        await Assert.That(rendered.FindAll("button[type=submit]")).IsEmpty();
        await Assert.That(
                rendered.Find($"#event-add-on-{availableId:N}-quantity")
                    .GetAttribute("value"))
            .IsEqualTo("0");
    }

    [Test]
    public async Task OrganizerSurfaceRendersOnlyServerAdvertisedLifecycleActions()
    {
        using var context = new BlazorTestContext();
        IEventAddOnService service = Substitute.For<IEventAddOnService>();
        context.Services.AddSingleton(service);
        Guid eventId = Guid.CreateVersion7();
        HalResourceOfEventAddOnCatalogDto resource = Catalog(eventId, []);
        resource._links = new Dictionary<string, HalLink>
        {
            ["create-event-add-on-catalog-draft"] =
                new() { Href = $"/api/events/{eventId}/add-ons/management" },
        };
        service.GetManagementAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HalResourceOfEventAddOnCatalogDto?>(resource));

        IRenderedComponent<EventAddOnCatalogEditor> rendered =
            context.Render<EventAddOnCatalogEditor>(parameters =>
                parameters.Add(component => component.EventId, eventId));
        rendered.WaitForState(() =>
            rendered.Markup.Contains("Create add-on catalog", StringComparison.Ordinal));

        await Assert.That(rendered.Markup).Contains("Create add-on catalog");
        await Assert.That(rendered.Markup).DoesNotContain("Add catalog item");
        await Assert.That(rendered.Markup).DoesNotContain("Publish catalog");
        await Assert.That(rendered.Markup).DoesNotContain("Retire catalog");
        await Assert.That(rendered.Find("main").GetAttribute("aria-labelledby"))
            .IsEqualTo("add-on-editor-title");
        await Assert.That(rendered.Find("[role=status]").GetAttribute("aria-live"))
            .IsEqualTo("polite");
    }

    private static HalResourceOfEventAddOnCatalogDto Catalog(
        Guid eventId,
        ICollection<EventAddOnCatalogItemDto> items) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            CurrencyCode = "EUR",
            VersionNumber = 1,
            Items = items,
        };

    private static EventAddOnCatalogItemDto Item(
        Guid id,
        string name,
        bool available,
        int maximumSelectableQuantity) =>
        new()
        {
            Id = id,
            Name = name,
            UnitPriceMinor = 500,
            CurrencyCode = "EUR",
            MaximumSelectableQuantity = maximumSelectableQuantity,
            IsAvailable = available,
            FulfillmentDisclosure = "Collect at the service desk.",
            RefundDisclosure = "Refund before fulfillment.",
        };

    private static HalResourceOfRegistrationOrderAddOnSummaryDto Order(Guid orderId) =>
        new()
        {
            CurrencyCode = "EUR",
            AddOnTotalMinor = 0,
            Lines = [],
            _links = new Dictionary<string, HalLink>
            {
                ["reserve-event-add-ons"] =
                    new() { Href = $"/api/registration-orders/{orderId}/add-ons" },
            },
        };

}
