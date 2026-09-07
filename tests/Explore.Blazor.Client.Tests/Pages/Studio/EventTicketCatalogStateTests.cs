using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Pages.Studio;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class EventTicketCatalogStateTests
{
    [Test]
    public async Task TryParse_DirectTypedWrapper_PreservesCommercialReadinessAndExactLinks()
    {
        var eventId = Guid.CreateVersion7();
        var catalogId = Guid.CreateVersion7();
        var ticketTypeId = Guid.CreateVersion7();
        var poolId = Guid.CreateVersion7();
        var selfLink = new HalLink { Href = $"/api/events/{eventId}/ticketing", Method = "GET", Title = "Catalog" };
        var createTypeLink = new HalLink { Href = $"/api/events/{eventId}/ticketing/ticket-types", Method = "POST" };
        var itemEditLink = new HalLink { Href = $"/api/events/{eventId}/ticketing/ticket-types/{ticketTypeId}", Method = "PUT", Title = "Edit ticket" };
        var poolEditLink = new HalLink { Href = $"/api/events/{eventId}/ticketing/capacity-pools/{poolId}", Method = "PUT" };
        var resource = new HalResourceOfEventTicketCatalogManagementDto
        {
            EventId = eventId,
            CatalogId = catalogId,
            VersionNumber = 3,
            CurrencyCode = "EUR",
            StatusId = 2,
            StatusCode = "READY",
            StatusName = "Ready",
            MerchantDisclosureText = "Sold by Example Organizer",
            RefundPolicyDisclosureText = "Refunds close 24 hours before start.",
            SupportContactDisclosureText = "Email support@example.test",
            PublicationPreflight = new PaidEventPublicationPreflightDto
            {
                IsPaidCatalog = true,
                IsReady = false,
                Blockers = [new Blockers2 { Code = "merchant_not_ready", Explanation = "Connect payments first." }]
            },
            _links = new Dictionary<string, HalLink>(StringComparer.Ordinal)
            {
                ["self"] = selfLink,
                ["create-type"] = createTypeLink
            },
            _embedded = Embedded(eventId, ticketTypeId, poolId, itemEditLink, poolEditLink)
        };

        var parsed = EventTicketCatalogState.TryParse(resource, out var state);

        await Assert.That(parsed).IsTrue();
        await Assert.That(state).IsNotNull();
        await Assert.That(state!.EventId).IsEqualTo(eventId);
        await Assert.That(state.CatalogId).IsEqualTo(catalogId);
        await Assert.That(state.VersionNumber).IsEqualTo(3);
        await Assert.That(state.CurrencyCode).IsEqualTo("EUR");
        await Assert.That(state.MerchantDisclosureText).IsEqualTo("Sold by Example Organizer");
        await Assert.That(state.RefundPolicyDisclosureText).IsEqualTo("Refunds close 24 hours before start.");
        await Assert.That(state.SupportContactDisclosureText).IsEqualTo("Email support@example.test");
        await Assert.That(state.PublicationPreflight).IsNotNull();
        await Assert.That(state.PublicationPreflight!.IsPaidCatalog).IsTrue();
        await Assert.That(state.PublicationPreflight.IsReady).IsFalse();
        await Assert.That(state.PublicationPreflight.Blockers.Single().Code).IsEqualTo("merchant_not_ready");
        await Assert.That(state.PublicationPreflight.Blockers.Single().Explanation).IsEqualTo("Connect payments first.");
        await Assert.That(state.Links["self"]).IsSameReferenceAs(selfLink);
        await Assert.That(state.Links["create-type"]).IsSameReferenceAs(createTypeLink);
        await Assert.That(state.TicketTypes.Single().Links["edit"].Href).IsEqualTo(itemEditLink.Href);
        await Assert.That(state.TicketTypes.Single().Links["edit"].Title).IsEqualTo("Edit ticket");
        await Assert.That(state.TicketTypes.Single().HasLink("delete")).IsFalse();
        await Assert.That(state.CapacityPools.Single().Links["edit"].Href).IsEqualTo(poolEditLink.Href);
    }

    [Test]
    public async Task TryParse_DirectTypedWrapperWithJsonElementEmbedded_Parses()
    {
        var eventId = Guid.CreateVersion7();
        var ticketTypeId = Guid.CreateVersion7();
        var poolId = Guid.CreateVersion7();
        var resource = new HalResourceOfEventTicketCatalogManagementDto
        {
            EventId = eventId,
            CurrencyCode = "EUR",
            _links = new Dictionary<string, HalLink>(StringComparer.Ordinal)
            {
                ["self"] = new() { Href = $"/api/events/{eventId}/ticketing", Method = "GET" }
            },
            _embedded = JsonSerializer.SerializeToElement(Embedded(eventId, ticketTypeId, poolId))
        };

        var parsed = EventTicketCatalogState.TryParse(resource, out var state);

        await Assert.That(parsed).IsTrue();
        await Assert.That(state!.TicketTypes.Single().Id).IsEqualTo(ticketTypeId);
        await Assert.That(state.CapacityPools.Single().Id).IsEqualTo(poolId);
    }

    [Test]
    public async Task TryParse_ExtensionDataCatalog_PreservesDataAndExactRelations()
    {
        var eventId = Guid.CreateVersion7();
        var ticketTypeId = Guid.CreateVersion7();
        var poolId = Guid.CreateVersion7();
        var resource = Resource($$"""
            {
              "eventId": "{{eventId}}",
              "catalogId": "{{Guid.CreateVersion7()}}",
              "versionNumber": 2,
              "currencyCode": "EUR",
              "statusId": 1,
              "statusCode": "DRAFT",
              "statusName": "Draft",
              "_links": {
                "self": { "href": "/api/events/{{eventId}}/ticketing", "method": "GET" },
                "create-type": { "href": "/api/events/{{eventId}}/ticketing/ticket-types", "method": "POST" },
                "publish": { "href": "/api/events/{{eventId}}/ticketing/publish", "method": "POST" }
              },
              "_embedded": {
                "ticket-types": [{
                  "id": "{{ticketTypeId}}",
                  "name": "General admission",
                  "ticketPricingModeId": 1,
                  "fixedPriceMinor": 1200,
                  "minimumPriceMinor": null,
                  "suggestedPriceMinor": null,
                  "participantDataCollectionModeId": 2,
                  "capacityPoolId": "{{poolId}}",
                  "minimumAge": 16,
                  "maximumAge": 90,
                  "requiresGuardian": false,
                  "requiresApproval": true,
                  "perOrderLimit": 4,
                  "perAccountLimit": 6,
                  "perVerifiedContactLimit": 2,
                  "perBookingPartyLimit": 8,
                  "entitlements": [{
                    "entitlementScopeTypeId": 1,
                    "eventDayId": null,
                    "eventSessionId": null,
                    "includedQuantity": 1,
                    "entitlementSelectionRuleId": 1
                  }],
                  "_links": {
                    "edit": { "href": "/api/events/{{eventId}}/ticketing/ticket-types/{{ticketTypeId}}", "method": "PUT" },
                    "delete": { "href": "/api/events/{{eventId}}/ticketing/ticket-types/{{Guid.CreateVersion7()}}", "method": "DELETE" }
                  }
                }],
                "capacity-pools": [{
                  "id": "{{poolId}}",
                  "name": "Main hall",
                  "maximumQuantity": 200,
                  "holdDurationSeconds": 900,
                  "capacityHoldPolicyId": 2,
                  "capacityHoldPolicyCode": "TIMED_HOLD_ON_SELECTION",
                  "capacityHoldPolicyName": "Timed hold on selection",
                  "capacityOversellPolicyId": 1,
                  "isActive": true,
                  "_links": {
                    "edit": { "href": "/api/events/{{eventId}}/ticketing/capacity-pools/{{poolId}}", "method": "PUT" }
                  }
                }]
              }
            }
            """);

        var parsed = EventTicketCatalogState.TryParse(resource, out var state);

        await Assert.That(parsed).IsTrue();
        await Assert.That(state).IsNotNull();
        await Assert.That(state!.EventId).IsEqualTo(eventId);
        await Assert.That(state.HasLink("create-type")).IsTrue();
        await Assert.That(state.HasLink("create-pool")).IsFalse();
        await Assert.That(state.TicketTypes.Single().HasLink("edit")).IsTrue();
        await Assert.That(state.TicketTypes.Single().HasLink("delete")).IsFalse();
        await Assert.That(state.TicketTypes.Single().ToRequest().Entitlements!.Single().IncludedQuantity).IsEqualTo(1);
        await Assert.That(state.CapacityPools.Single().HasLink("edit")).IsTrue();
        await Assert.That(state.CapacityPools.Single().CapacityHoldPolicyCode).IsEqualTo("TIMED_HOLD_ON_SELECTION");
    }

    [Test]
    [Arguments("{\"eventId\":\"00000000-0000-0000-0000-000000000000\",\"_links\":{},\"_embedded\":{\"ticket-types\":[],\"capacity-pools\":[]}}")]
    [Arguments("{\"eventId\":\"01938b55-f390-7e0f-a3d7-62972ff97bd0\",\"_embedded\":{\"ticket-types\":[],\"capacity-pools\":[]}}")]
    [Arguments("{\"eventId\":\"01938b55-f390-7e0f-a3d7-62972ff97bd0\",\"_links\":{\"self\":{\"href\":\"/api\"}}}")]
    public async Task TryParse_MissingRequiredDataOrLinks_FailsClosed(string json)
    {
        var parsed = EventTicketCatalogState.TryParse(Resource(json), out var state);

        await Assert.That(parsed).IsFalse();
        await Assert.That(state).IsNull();
    }

    [Test]
    public async Task TryParse_DirectTypedWrapperWithMalformedRequiredEmbeddedData_FailsClosed()
    {
        var eventId = Guid.CreateVersion7();
        var resource = new HalResourceOfEventTicketCatalogManagementDto
        {
            EventId = eventId,
            CurrencyCode = "EUR",
            _links = new Dictionary<string, HalLink>(StringComparer.Ordinal)
            {
                ["self"] = new() { Href = $"/api/events/{eventId}/ticketing", Method = "GET" }
            },
            _embedded = new Dictionary<string, object?>
            {
                ["ticket-types"] = Array.Empty<object>()
            }
        };

        var parsed = EventTicketCatalogState.TryParse(resource, out var state);

        await Assert.That(parsed).IsFalse();
        await Assert.That(state).IsNull();
    }

    [Test]
    public async Task TryParse_ReadOnlyEmbeddedItemWithoutLinks_RemainsVisibleWithoutActions()
    {
        var eventId = Guid.CreateVersion7();
        var ticketTypeId = Guid.CreateVersion7();
        var resource = Resource($$"""
            {
              "eventId": "{{eventId}}",
              "currencyCode": "EUR",
              "_links": { "self": { "href": "/api/events/{{eventId}}/ticketing", "method": "GET" } },
              "_embedded": {
                "ticket-types": [{
                  "id": "{{ticketTypeId}}",
                  "name": "Published ticket",
                  "ticketPricingModeId": 2,
                  "participantDataCollectionModeId": 1,
                  "requiresGuardian": false,
                  "requiresApproval": false,
                  "entitlements": [{ "entitlementScopeTypeId": 1, "includedQuantity": 1, "entitlementSelectionRuleId": 1 }]
                }],
                "capacity-pools": []
              }
            }
            """);

        var parsed = EventTicketCatalogState.TryParse(resource, out var state);

        await Assert.That(parsed).IsTrue();
        await Assert.That(state!.TicketTypes.Single().HasLink("edit")).IsFalse();
        await Assert.That(state.TicketTypes.Single().HasLink("delete")).IsFalse();
    }

    [Test]
    public async Task TryParse_ItemMutationHrefDoesNotEndWithItemId_OmitsAction()
    {
        var eventId = Guid.CreateVersion7();
        var ticketTypeId = Guid.CreateVersion7();
        var resource = Resource($$"""
            {
              "eventId": "{{eventId}}",
              "currencyCode": "EUR",
              "_links": { "self": { "href": "/api/events/{{eventId}}/ticketing", "method": "GET" } },
              "_embedded": {
                "ticket-types": [{
                  "id": "{{ticketTypeId}}",
                  "name": "General admission",
                  "ticketPricingModeId": 1,
                  "participantDataCollectionModeId": 1,
                  "requiresGuardian": false,
                  "requiresApproval": false,
                  "entitlements": [{ "entitlementScopeTypeId": 1, "includedQuantity": 1, "entitlementSelectionRuleId": 1 }],
                  "_links": { "edit": { "href": "/api/items/{{ticketTypeId}}/edit", "method": "PUT" } }
                }],
                "capacity-pools": []
              }
            }
            """);

        var parsed = EventTicketCatalogState.TryParse(resource, out var state);

        await Assert.That(parsed).IsTrue();
        await Assert.That(state!.TicketTypes.Single().HasLink("edit")).IsFalse();
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

    private static Dictionary<string, object?> Embedded(
        Guid eventId,
        Guid ticketTypeId,
        Guid poolId,
        HalLink? ticketEditLink = null,
        HalLink? poolEditLink = null) => new(StringComparer.Ordinal)
        {
            ["ticket-types"] = new object[]
            {
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = ticketTypeId,
                    ["name"] = "General admission",
                    ["ticketPricingModeId"] = 1,
                    ["ticketPricingModeCode"] = "FIXED",
                    ["ticketPricingModeName"] = "Fixed price",
                    ["fixedPriceMinor"] = 1200,
                    ["participantDataCollectionModeId"] = 2,
                    ["capacityPoolId"] = poolId,
                    ["requiresGuardian"] = false,
                    ["requiresApproval"] = true,
                    ["entitlements"] = new object[]
                    {
                        new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["entitlementScopeTypeId"] = 1,
                            ["includedQuantity"] = 1,
                            ["entitlementSelectionRuleId"] = 1
                        }
                    },
                    ["_links"] = new Dictionary<string, HalLink>(StringComparer.Ordinal)
                    {
                        ["edit"] = ticketEditLink ?? new HalLink { Href = $"/api/events/{eventId}/ticketing/ticket-types/{ticketTypeId}", Method = "PUT" },
                        ["delete"] = new() { Href = $"/api/events/{eventId}/ticketing/ticket-types/{Guid.CreateVersion7()}", Method = "DELETE" }
                    }
                }
            },
            ["capacity-pools"] = new object[]
            {
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["id"] = poolId,
                    ["name"] = "Main hall",
                    ["maximumQuantity"] = 200,
                    ["holdDurationSeconds"] = 900,
                    ["capacityHoldPolicyId"] = 2,
                    ["capacityHoldPolicyCode"] = "TIMED_HOLD_ON_SELECTION",
                    ["capacityHoldPolicyName"] = "Timed hold on selection",
                    ["capacityOversellPolicyId"] = 1,
                    ["isActive"] = true,
                    ["_links"] = new Dictionary<string, HalLink>(StringComparer.Ordinal)
                    {
                        ["edit"] = poolEditLink ?? new HalLink { Href = $"/api/events/{eventId}/ticketing/capacity-pools/{poolId}", Method = "PUT" }
                    }
                }
            }
        };
}
