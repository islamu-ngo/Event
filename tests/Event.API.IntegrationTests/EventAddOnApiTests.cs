using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Filters;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class EventAddOnApiTests(ContractApiFixture fixture)
{
    private const string CapabilityHeader = "X-Registration-Order-Capability";
    private const string CatalogController =
        "Explore.API.Controllers.EventAddOnCatalogController";
    private const string ManagementController =
        "Explore.API.Controllers.EventAddOnManagementController";
    private const string OrderController =
        "Explore.API.Controllers.RegistrationOrderAddOnController";
    private const string CatalogDto =
        "Explore.Application.DTOs.EventAddOns.EventAddOnCatalogDto";
    private const string CatalogItemDto =
        "Explore.Application.DTOs.EventAddOns.EventAddOnCatalogItemDto";
    private const string OrderSummaryDto =
        "Explore.Application.DTOs.EventAddOns.RegistrationOrderAddOnSummaryDto";
    private const string OrderLineDto =
        "Explore.Application.DTOs.EventAddOns.RegistrationOrderAddOnLineDto";
    private const string CatalogPath = "/api/events/{eventId}/add-ons";
    private const string ManagementPath = "/api/events/{eventId}/add-ons/management";
    private const string OrderPath =
        "/api/events/{eventId}/registration-orders/{registrationOrderId}/add-ons";

    [Test]
    public async Task ControllersPublishPublicCatalogOrganizerManagementAndOrderLifecycleRoutes()
    {
        Type? catalog = ApiType(CatalogController);
        Type? management = ApiType(ManagementController);
        Type? order = ApiType(OrderController);

        await Assert.That(catalog).IsNotNull();
        await Assert.That(management).IsNotNull();
        await Assert.That(order).IsNotNull();
        if (catalog is null || management is null || order is null)
        {
            return;
        }

        await Assert.That(catalog.GetCustomAttribute<RouteAttribute>()?.Template)
            .IsEqualTo("api/events/{eventId:guid}/add-ons");
        await Assert.That(management.GetCustomAttribute<RouteAttribute>()?.Template)
            .IsEqualTo("api/events/{eventId:guid}/add-ons/management");
        await Assert.That(order.GetCustomAttribute<RouteAttribute>()?.Template)
            .IsEqualTo(
                "api/events/{eventId:guid}/registration-orders/" +
                "{registrationOrderId:guid}/add-ons");

        await AssertMethodRouteAsync(catalog, "Get", "", HttpVerb.Get);
        await AssertMethodRouteAsync(management, "Get", "", HttpVerb.Get);
        await AssertMethodRouteAsync(management, "CreateDraft", "draft", HttpVerb.Post);
        await AssertMethodRouteAsync(management, "AddItem", "items", HttpVerb.Post);
        await AssertMethodRouteAsync(management, "Publish", "publish", HttpVerb.Post);
        await AssertMethodRouteAsync(management, "Retire", "retire", HttpVerb.Post);
        await AssertMethodRouteAsync(order, "Get", "", HttpVerb.Get);
        await AssertMethodRouteAsync(order, "Reserve", "", HttpVerb.Post);
        await AssertMethodRouteAsync(
            order,
            "Fulfill",
            "{registrationOrderAddOnLineId:guid}/fulfillment",
            HttpVerb.Post);
        await AssertMethodRouteAsync(
            order,
            "Refund",
            "{registrationOrderAddOnLineId:guid}/refunds",
            HttpVerb.Post);
    }

    [Test]
    public async Task ReadsAreExplicitPrivateAndEveryWriteIsAuthenticatedIdempotentAndRateLimited()
    {
        foreach ((string controllerName, string methodName) in new[]
                 {
                     (CatalogController, "Get"),
                     (ManagementController, "Get"),
                     (OrderController, "Get"),
                 })
        {
            Type? controller = ApiType(controllerName);
            await Assert.That(controller).IsNotNull();
            if (controller is null)
            {
                continue;
            }

            MethodInfo read = RequireMethod(controller, methodName);
            await Assert.That(read.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
            await Assert.That(read.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
        }

        foreach ((string controllerName, string methodName) in new[]
                 {
                     (ManagementController, "CreateDraft"),
                     (ManagementController, "AddItem"),
                     (ManagementController, "Publish"),
                     (ManagementController, "Retire"),
                     (OrderController, "Reserve"),
                     (OrderController, "Fulfill"),
                     (OrderController, "Refund"),
                 })
        {
            Type? controller = ApiType(controllerName);
            await Assert.That(controller).IsNotNull();
            if (controller is null)
            {
                continue;
            }

            MethodInfo write = RequireMethod(controller, methodName);
            await Assert.That(write.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
            await Assert.That(write.GetCustomAttribute<EnableRateLimitingAttribute>()).IsNotNull();
            await Assert.That(write.GetCustomAttribute<RequireIdempotencyKeyAttribute>()).IsNotNull();
            await Assert.That(write.GetCustomAttribute<ProtectIdempotencyReplayAttribute>()).IsNotNull();
            await Assert.That(write.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
            await Assert.That(write.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
                .IsEqualTo(EndpointClass.Authenticated);
        }
    }

    [Test]
    public async Task PublicContractsDiscloseExactOptionalMoneyFulfillmentAndRefundFacts()
    {
        await AssertExactJsonPropertiesAsync(
            CatalogDto,
            "CurrencyCode",
            "Id",
            "Items",
            "VersionNumber");
        await AssertExactJsonPropertiesAsync(
            CatalogItemDto,
            "CurrencyCode",
            "Description",
            "FulfillmentDisclosure",
            "Id",
            "IsAvailable",
            "MaximumSelectableQuantity",
            "Name",
            "RefundDisclosure",
            "UnitPriceMinor");
        await AssertExactJsonPropertiesAsync(
            OrderSummaryDto,
            "AddOnTotalMinor",
            "CurrencyCode",
            "GrandTotalMinor",
            "Lines",
            "RegistrationOrderId");
        await AssertExactJsonPropertiesAsync(
            OrderLineDto,
            "CatalogItemId",
            "CurrencyCode",
            "FulfillmentDisclosure",
            "FulfillmentStatusCode",
            "Id",
            "LineTotalMinor",
            "MaximumRefundableQuantity",
            "Name",
            "Quantity",
            "RefundDisclosure",
            "RefundAllocatedMinor",
            "RefundAllocatedQuantity",
            "RefundStatusCode",
            "UnitPriceMinor");

        foreach (string dtoName in new[] { CatalogDto, CatalogItemDto, OrderSummaryDto, OrderLineDto })
        {
            Type? dto = ApplicationType(dtoName);
            await Assert.That(dto).IsNotNull();
            if (dto is null)
            {
                continue;
            }

            await Assert.That(dto.GetProperties().Any(property =>
                    ForbiddenPublicFragments.Any(fragment =>
                        property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
                .IsFalse();
        }
    }

    [Test]
    public async Task RequestBodiesCarryOnlyOrganizerOrBuyerIntentNeverAuthorityOrComputedMoney()
    {
        await AssertExactJsonPropertiesAsync(
            "Explore.API.Models.EventAddOnSelectionRequest",
            "CatalogItemId",
            "Quantity");
        await AssertExactJsonPropertiesAsync(
            "Explore.API.Models.ReserveEventAddOnsRequest",
            "CatalogId",
            "Selections");
        await AssertExactJsonPropertiesAsync(
            "Explore.API.Models.RefundEventAddOnRequest",
            "Quantity");
        await AssertExactJsonPropertiesAsync(
            "Explore.API.Models.ManageEventAddOnCatalogItemRequest",
            "Description",
            "FulfillmentDisclosure",
            "InventoryCapacity",
            "Name",
            "RefundDisclosure",
            "UnitPriceMinor");

        foreach (string requestName in new[]
                 {
                     "Explore.API.Models.EventAddOnSelectionRequest",
                     "Explore.API.Models.ReserveEventAddOnsRequest",
                     "Explore.API.Models.RefundEventAddOnRequest",
                     "Explore.API.Models.ManageEventAddOnCatalogItemRequest",
                 })
        {
            Type? request = ApiType(requestName);
            await Assert.That(request).IsNotNull();
            if (request is null)
            {
                continue;
            }

            await Assert.That(request.GetProperties().Any(property =>
                    ForbiddenRequestFragments.Any(fragment =>
                        property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
                .IsFalse();
        }
    }

    [Test]
    public async Task HalPoliciesOwnManagementReservationFulfillmentAndRefundAffordances()
    {
        Type? catalogPolicy = ApiType(
            "Explore.API.Hateoas.Policies.EventAddOnCatalogLinkPolicy");
        Type? orderPolicy = ApiType(
            "Explore.API.Hateoas.Policies.RegistrationOrderAddOnLinkPolicy");
        await Assert.That(catalogPolicy).IsNotNull();
        await Assert.That(orderPolicy).IsNotNull();

        foreach (string relation in new[]
                 {
                     "ManageEventAddOns",
                     "CreateEventAddOnCatalogDraft",
                     "AddEventAddOnCatalogItem",
                     "PublishEventAddOnCatalog",
                     "RetireEventAddOnCatalog",
                     "ReserveEventAddOns",
                     "FulfillEventAddOn",
                     "RefundEventAddOn",
                 })
        {
            await Assert.That(typeof(LinkRelations).GetField(
                    relation,
                    BindingFlags.Public | BindingFlags.Static))
                .IsNotNull();
        }

        foreach ((string dtoName, string[] flags) in new[]
                 {
                     (CatalogDto, new[] { "CanManage", "CanCreateDraft", "CanAddItem", "CanPublish", "CanRetire" }),
                     (OrderSummaryDto, new[] { "CanReserve" }),
                     (OrderLineDto, new[] { "CanFulfill", "CanRefund" }),
                 })
        {
            Type? dto = ApplicationType(dtoName);
            await Assert.That(dto).IsNotNull();
            if (dto is null)
            {
                continue;
            }

            foreach (string flag in flags)
            {
                PropertyInfo? property = dto.GetProperty(flag);
                await Assert.That(property).IsNotNull();
                await Assert.That(property?.GetCustomAttribute<JsonIgnoreAttribute>()).IsNotNull();
            }
        }
    }

    [Test]
    public async Task OpenApiPublishesAllAddOnPathsAndRequiredAuthorityHeaders()
    {
        using HttpResponseMessage response = await fixture.Client.GetAsync("/openapi/islamu-event.json");
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync());
        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach ((string path, string[] verbs) in new[]
                 {
                     (CatalogPath, new[] { "get" }),
                     (ManagementPath, new[] { "get" }),
                     ($"{ManagementPath}/draft", new[] { "post" }),
                     ($"{ManagementPath}/items", new[] { "post" }),
                     ($"{ManagementPath}/publish", new[] { "post" }),
                     ($"{ManagementPath}/retire", new[] { "post" }),
                     (OrderPath, new[] { "get", "post" }),
                     ($"{OrderPath}/{{registrationOrderAddOnLineId}}/fulfillment", new[] { "post" }),
                     ($"{OrderPath}/{{registrationOrderAddOnLineId}}/refunds", new[] { "post" }),
                 })
        {
            await Assert.That(paths.TryGetProperty(path, out JsonElement pathContract)).IsTrue();
            foreach (string verb in verbs)
            {
                await Assert.That(pathContract.TryGetProperty(verb, out JsonElement operation)).IsTrue();
                if (path.StartsWith(OrderPath, StringComparison.Ordinal))
                {
                    await AssertHeaderAsync(
                        operation,
                        CapabilityHeader,
                        required: false);
                }

                if (verb == "post")
                {
                    await AssertHeaderAsync(operation, "Idempotency-Key", required: true);
                }
            }
        }
    }

    [Test]
    public async Task UnknownOrderAndCapabilityReturnGenericNoStoreWithoutSentinelLeakage()
    {
        string sentinel = "ADDON-SENTINEL-CAPABILITY";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/events/{Guid.CreateVersion7()}/registration-orders/" +
            $"{Guid.CreateVersion7()}/add-ons");
        request.Headers.Add(CapabilityHeader, sentinel);

        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain(sentinel);
        await Assert.That(body.Contains("tenant", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(body.Contains("admission", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(body.Contains("inventory", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    private static Type? ApiType(string fullName) =>
        typeof(EndpointClassificationAttribute).Assembly.GetType(fullName);

    private static Type? ApplicationType(string fullName) =>
        typeof(LinkRelations).Assembly.GetType(fullName);

    private static MethodInfo RequireMethod(Type controller, string methodName) =>
        controller.GetMethod(methodName) ??
        throw new InvalidOperationException($"Method '{methodName}' is missing.");

    private static async Task AssertMethodRouteAsync(
        Type controller,
        string methodName,
        string template,
        HttpVerb verb)
    {
        HttpMethodAttribute? route = RequireMethod(controller, methodName)
            .GetCustomAttributes<HttpMethodAttribute>()
            .SingleOrDefault();
        await Assert.That(route).IsNotNull();
        await Assert.That(route?.Template ?? string.Empty).IsEqualTo(template);
        await Assert.That(route?.HttpMethods).Contains(verb.ToString().ToUpperInvariant());
        await Assert.That(route?.Name).IsNotNull();
    }

    private static async Task AssertExactJsonPropertiesAsync(
        string typeName,
        params string[] expected)
    {
        Type? type = typeName.StartsWith("Explore.API.", StringComparison.Ordinal)
            ? ApiType(typeName)
            : ApplicationType(typeName);
        await Assert.That(type).IsNotNull();
        if (type is null)
        {
            return;
        }

        string[] actual = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(actual).IsEquivalentTo(expected.Order(StringComparer.Ordinal).ToArray());
    }

    private static async Task AssertHeaderAsync(
        JsonElement operation,
        string name,
        bool required)
    {
        JsonElement parameter = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(value =>
                value.GetProperty("name").GetString() == name &&
                value.GetProperty("in").GetString() == "header");
        bool actualRequired =
            parameter.TryGetProperty("required", out JsonElement requiredElement) &&
            requiredElement.GetBoolean();
        await Assert.That(actualRequired).IsEqualTo(required);
    }

    private static readonly string[] ForbiddenPublicFragments =
    [
        "Tenant",
        "User",
        "Participant",
        "Admission",
        "Credential",
        "CheckIn",
        "InventoryCapacity",
    ];

    private static readonly string[] ForbiddenRequestFragments =
    [
        "Tenant",
        "EventId",
        "RegistrationOrderId",
        "User",
        "Participant",
        "Currency",
        "LineTotal",
        "GrandTotal",
        "Admission",
        "Credential",
    ];

    private enum HttpVerb
    {
        Get,
        Post,
    }
}
