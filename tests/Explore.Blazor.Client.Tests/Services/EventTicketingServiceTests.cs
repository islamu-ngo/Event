using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class EventTicketingServiceTests
{
    [Test]
    public async Task GetCatalogAsync_DelegatesAndReturnsParsedPresentationState()
    {
        var eventId = Guid.CreateVersion7();
        using var cancellation = new CancellationTokenSource();
        var resource = new HalResourceOfEventTicketCatalogManagementDto
        {
            EventId = eventId,
            CurrencyCode = "EUR",
            MerchantDisclosureText = "Sold by Example Organizer",
            _links = new Dictionary<string, HalLink>(StringComparer.Ordinal)
            {
                ["self"] = new() { Href = $"/api/events/{eventId}/ticketing", Method = "GET" }
            },
            _embedded = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ticket-types"] = Array.Empty<object>(),
                ["capacity-pools"] = Array.Empty<object>()
            }
        };
        var apiClient = Substitute.For<IEventTicketingClient>();
        apiClient.GetEventTicketCatalogManagementAsync(eventId, null, null, cancellation.Token)
            .Returns(resource);
        var service = new EventTicketingService(apiClient);

        EventTicketCatalogState? result = await service.GetCatalogAsync(eventId, cancellation.Token);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.EventId).IsEqualTo(eventId);
        await Assert.That(result.CurrencyCode).IsEqualTo("EUR");
        await Assert.That(result.MerchantDisclosureText).IsEqualTo("Sold by Example Organizer");
        await apiClient.Received(1).GetEventTicketCatalogManagementAsync(eventId, null, null, cancellation.Token);
    }

    [Test]
    public async Task GetCatalogAsync_WhenGeneratedWrapperIsMalformed_FailsClosed()
    {
        var eventId = Guid.CreateVersion7();
        var apiClient = Substitute.For<IEventTicketingClient>();
        apiClient.GetEventTicketCatalogManagementAsync(eventId, null, null, Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfEventTicketCatalogManagementDto());
        var service = new EventTicketingService(apiClient);

        EventTicketCatalogState? result = await service.GetCatalogAsync(eventId);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PaidCommerceWrappers_DelegateGeneratedDtosAndExactItemIds()
    {
        var eventId = Guid.CreateVersion7();
        using var cancellation = new CancellationTokenSource();
        var preflight = new HalResourceOfPaidEventPublicationPreflightDto();
        var paymentConnection = new HalResourceOfEventOrganizerPaymentConnectionManagementDto();
        var commercialDisclosures = new UpdateEventTicketCatalogCommercialDisclosuresCommand
        {
            MerchantDisclosureText = "Merchant disclosure",
            RefundPolicyDisclosureText = "Refund policy",
            SupportContactDisclosureText = "Support contact"
        };
        var onboarding = new BaseCommandResponseOfOrganizerPaymentOnboardingLinkResult
        {
            Success = true,
            Id = new OrganizerPaymentOnboardingLinkResult
            {
                OnboardingUrl = new Uri("https://example.test/onboarding"),
                ReusedExistingConnection = false
            }
        };
        var response = new BaseCommandResponseOfGuid { Success = true };
        var apiClient = Substitute.For<IEventTicketingClient>();
        apiClient.GetPaidEventPublicationPreflightAsync(eventId, null, null, cancellation.Token).Returns(preflight);
        apiClient.UpdateEventTicketCatalogCommercialDisclosuresAsync(eventId, commercialDisclosures, null, null, cancellation.Token).Returns(response);
        apiClient.GetEventOrganizerPaymentConnectionAsync(eventId, null, null, cancellation.Token).Returns(paymentConnection);
        apiClient.StartEventOrganizerPaymentOnboardingAsync(eventId, null, null, cancellation.Token).Returns(onboarding);
        apiClient.ReturnEventOrganizerPaymentOnboardingAsync(eventId, null, null, cancellation.Token).Returns(Task.CompletedTask);
        apiClient.RefreshEventOrganizerPaymentOnboardingAsync(eventId, null, null, cancellation.Token).Returns(Task.CompletedTask);
        var service = new EventTicketingService(apiClient);

        await Assert.That(await service.GetPaidPublicationPreflightAsync(eventId, cancellation.Token)).IsSameReferenceAs(preflight);
        await Assert.That(await service.UpdateCommercialDisclosuresAsync(eventId, commercialDisclosures, cancellation.Token)).IsSameReferenceAs(response);
        await Assert.That(await service.GetPaymentConnectionAsync(eventId, cancellation.Token)).IsSameReferenceAs(paymentConnection);
        await Assert.That(await service.StartPaymentOnboardingAsync(eventId, cancellation.Token)).IsSameReferenceAs(onboarding);

        await service.ReturnPaymentOnboardingAsync(eventId, cancellation.Token);
        await service.RefreshPaymentOnboardingAsync(eventId, cancellation.Token);

        await apiClient.Received(1).GetPaidEventPublicationPreflightAsync(eventId, null, null, cancellation.Token);
        await apiClient.Received(1).UpdateEventTicketCatalogCommercialDisclosuresAsync(eventId, commercialDisclosures, null, null, cancellation.Token);
        await apiClient.Received(1).GetEventOrganizerPaymentConnectionAsync(eventId, null, null, cancellation.Token);
        await apiClient.Received(1).StartEventOrganizerPaymentOnboardingAsync(eventId, null, null, cancellation.Token);
        await apiClient.Received(1).ReturnEventOrganizerPaymentOnboardingAsync(eventId, null, null, cancellation.Token);
        await apiClient.Received(1).RefreshEventOrganizerPaymentOnboardingAsync(eventId, null, null, cancellation.Token);
    }

    [Test]
    public async Task Mutations_DelegateGeneratedDtosAndExactItemIds()
    {
        var eventId = Guid.CreateVersion7();
        var ticketTypeId = Guid.CreateVersion7();
        var capacityPoolId = Guid.CreateVersion7();
        var ticketType = new ManageEventTicketTypeDto { Name = "General admission" };
        var capacityPool = new ManageEventCapacityPoolDto { Name = "Main hall" };
        var draft = new CreateEventTicketCatalogDraftCommand { CurrencyCode = "EUR" };
        var response = new BaseCommandResponseOfGuid { Success = true };
        var apiClient = Substitute.For<IEventTicketingClient>();
        apiClient.CreateEventTicketCatalogDraftAsync(eventId, draft, null, null, Arg.Any<CancellationToken>()).Returns(response);
        apiClient.CloneEventTicketCatalogDraftAsync(eventId, null, null, Arg.Any<CancellationToken>()).Returns(response);
        apiClient.CreateEventTicketTypeAsync(eventId, ticketType, null, null, Arg.Any<CancellationToken>()).Returns(response);
        apiClient.UpdateEventTicketTypeAsync(eventId, ticketTypeId, ticketType, null, null, Arg.Any<CancellationToken>()).Returns(response);
        apiClient.DeleteEventTicketTypeAsync(eventId, ticketTypeId, null, null, Arg.Any<CancellationToken>()).Returns(response);
        apiClient.CreateEventCapacityPoolAsync(eventId, capacityPool, null, null, Arg.Any<CancellationToken>()).Returns(response);
        apiClient.UpdateEventCapacityPoolAsync(eventId, capacityPoolId, capacityPool, null, null, Arg.Any<CancellationToken>()).Returns(response);
        apiClient.DeleteEventCapacityPoolAsync(eventId, capacityPoolId, null, null, Arg.Any<CancellationToken>()).Returns(response);
        apiClient.PublishEventTicketCatalogAsync(eventId, null, null, Arg.Any<CancellationToken>()).Returns(response);
        var service = new EventTicketingService(apiClient);

        await service.CreateDraftAsync(eventId, draft);
        await service.CloneDraftAsync(eventId);
        await service.CreateTicketTypeAsync(eventId, ticketType);
        await service.UpdateTicketTypeAsync(eventId, ticketTypeId, ticketType);
        await service.DeleteTicketTypeAsync(eventId, ticketTypeId);
        await service.CreateCapacityPoolAsync(eventId, capacityPool);
        await service.UpdateCapacityPoolAsync(eventId, capacityPoolId, capacityPool);
        await service.DeleteCapacityPoolAsync(eventId, capacityPoolId);
        await service.PublishAsync(eventId);

        await apiClient.Received(1).CreateEventTicketCatalogDraftAsync(eventId, draft, null, null, Arg.Any<CancellationToken>());
        await apiClient.Received(1).UpdateEventTicketTypeAsync(eventId, ticketTypeId, ticketType, null, null, Arg.Any<CancellationToken>());
        await apiClient.Received(1).DeleteEventTicketTypeAsync(eventId, ticketTypeId, null, null, Arg.Any<CancellationToken>());
        await apiClient.Received(1).UpdateEventCapacityPoolAsync(eventId, capacityPoolId, capacityPool, null, null, Arg.Any<CancellationToken>());
        await apiClient.Received(1).DeleteEventCapacityPoolAsync(eventId, capacityPoolId, null, null, Arg.Any<CancellationToken>());
        await apiClient.Received(1).PublishEventTicketCatalogAsync(eventId, null, null, Arg.Any<CancellationToken>());
    }

    private static HalResourceOfEventTicketCatalogManagementDto Resource(string json)
    {
        using var document = JsonDocument.Parse(json);
        var resource = new HalResourceOfEventTicketCatalogManagementDto();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            resource.AdditionalProperties[property.Name] = property.Value.Clone();
        }

        return resource;
    }
}
