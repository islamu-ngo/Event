using System.Net;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Filters;
using Explore.API.Hateoas.Policies;
using Explore.Application.DTOs.Waitlist;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests;

[ClassDataSource<ContractApiFixture>(
    Shared = SharedType.PerAssembly)]
public sealed class FairReturnWaitlistApiTests(
    ContractApiFixture fixture)
{
    private const string CapabilityHeader =
        "X-Registration-Order-Capability";
    private const string ControllerTypeName =
        "Explore.API.Controllers." +
        "FairReturnWaitlistController";
    private const string DtoTypeName =
        "Explore.Application.DTOs.Waitlist." +
        "FairReturnWaitlistDto";
    private const string LinkPolicyTypeName =
        "Explore.API.Hateoas.Policies." +
        "FairReturnWaitlistLinkPolicy";
    private const string OpenApiPath =
        "/api/events/{eventId}/registration-orders/" +
        "{registrationOrderId}/lines/" +
        "{registrationOrderLineId}/waitlist";

    [Test]
    public async Task ControllerPublishesExactLineScopedLifecycleRoutes()
    {
        Type? controller = ApiType(
            ControllerTypeName);

        await Assert.That(controller).IsNotNull();
        await Assert.That(controller!
                .GetCustomAttribute<
                    ApiControllerAttribute>())
            .IsNotNull();
        RouteAttribute? route =
            controller.GetCustomAttribute<
                RouteAttribute>();
        await Assert.That(route?.Template)
            .IsEqualTo(
                "api/events/{eventId:guid}/" +
                "registration-orders/" +
                "{registrationOrderId:guid}/lines/" +
                "{registrationOrderLineId:guid}/" +
                "waitlist");
        await AssertMethodRouteAsync(
            controller,
            "Get",
            "",
            HttpVerb.Get);
        await AssertMethodRouteAsync(
            controller,
            "Join",
            "",
            HttpVerb.Post);
        await AssertMethodRouteAsync(
            controller,
            "Leave",
            "",
            HttpVerb.Delete);
        await AssertMethodRouteAsync(
            controller,
            "AcceptOffer",
            "offers/{offerId:guid}/accept",
            HttpVerb.Post);
        await AssertMethodRouteAsync(
            controller,
            "WithdrawSupply",
            "supply/{supplyId:guid}",
            HttpVerb.Delete);
    }

    [Test]
    public async Task ReadIsAnonymousPrivateAndWritesFailClosed()
    {
        Type? controller = ApiType(
            ControllerTypeName);

        await Assert.That(controller).IsNotNull();
        MethodInfo read = RequireMethod(
            controller!,
            "Get");
        await Assert.That(read.GetCustomAttribute<
                AllowAnonymousAttribute>())
            .IsNotNull();
        await Assert.That(read.GetCustomAttribute<
                PrivateNoStoreAttribute>())
            .IsNotNull();

        foreach (string methodName in new[]
                 {
                     "Join",
                     "Leave",
                     "AcceptOffer",
                     "WithdrawSupply",
                 })
        {
            MethodInfo write = RequireMethod(
                controller,
                methodName);
            await Assert.That(write.GetCustomAttribute<
                    AuthorizeAttribute>())
                .IsNotNull();
            await Assert.That(write.GetCustomAttribute<
                    EnableRateLimitingAttribute>())
                .IsNotNull();
            await Assert.That(write.GetCustomAttribute<
                    RequireIdempotencyKeyAttribute>())
                .IsNotNull();
            await Assert.That(write.GetCustomAttribute<
                    ProtectIdempotencyReplayAttribute>())
                .IsNotNull();
            await Assert.That(write.GetCustomAttribute<
                    PrivateNoStoreAttribute>())
                .IsNotNull();
            EndpointClassificationAttribute?
                classification = write
                    .GetCustomAttribute<
                        EndpointClassificationAttribute>();
            await Assert.That(classification)
                .IsNotNull();
            await Assert.That(
                    classification!.Class)
                .IsEqualTo(
                    EndpointClass
                        .Authenticated);
        }
    }

    [Test]
    public async Task ResourceContainsOnlyBoundedNonPiiState()
    {
        Type? dto = ApplicationType(DtoTypeName);

        await Assert.That(dto).IsNotNull();
        string[] publicProperties = dto!
            .GetProperties(
                BindingFlags.Public
                | BindingFlags.Instance)
            .Where(property =>
                property.GetCustomAttribute<
                    System.Text.Json.Serialization
                        .JsonIgnoreAttribute>() is null)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(publicProperties)
            .IsEquivalentTo([
                "Id",
                "OfferExpiresAt",
                "Position",
                "ReasonCode",
                "StatusCode",
            ]);

        FieldInfo? zero = dto.GetField(
            "PositionUnavailable",
            BindingFlags.Public
            | BindingFlags.Static);
        FieldInfo? ceiling = dto.GetField(
            "MaximumPublishedPosition",
            BindingFlags.Public
            | BindingFlags.Static);
        await Assert.That(
                zero?.GetRawConstantValue())
            .IsEqualTo(0);
        await Assert.That(
                (int?)ceiling
                    ?.GetRawConstantValue())
            .IsEqualTo(999);
    }

    [Test]
    public async Task ContractCannotSellOrAcceptPaidPriority()
    {
        Type? dto = ApplicationType(DtoTypeName);
        Type? joinRequest = ApiType(
            "Explore.API.Models." +
            "JoinFairReturnWaitlistRequest");

        await Assert.That(dto).IsNotNull();
        await Assert.That(joinRequest).IsNotNull();
        string[] forbidden =
        [
            "priority",
            "paid",
            "amount",
            "price",
            "payment",
            "currency",
            "bid",
            "boost",
            "tenant",
            "participant",
            "user",
            "email",
            "phone",
            "name",
            "address",
            "answer",
            "consent",
        ];
        string[] exposed = dto!.GetProperties()
            .Concat(joinRequest!.GetProperties())
            .Select(property => property.Name)
            .ToArray();
        await Assert.That(exposed.Any(property =>
                forbidden.Any(fragment =>
                    property.Contains(
                        fragment,
                        StringComparison.OrdinalIgnoreCase))))
            .IsFalse();
    }

    [Test]
    public async Task HalRelationsAreServerOwnedAndCoverStopControlledActions()
    {
        Type? policy = ApiType(
            LinkPolicyTypeName);
        Type? dto = ApplicationType(
            DtoTypeName);

        await Assert.That(policy).IsNotNull();
        await Assert.That(dto).IsNotNull();
        await Assert.That(policy!.GetInterfaces()
                .Any(contract =>
                    contract.IsGenericType
                    && contract
                        .GetGenericTypeDefinition()
                        .Name.StartsWith(
                            "ILinkPolicy",
                            StringComparison.Ordinal)
                    && contract
                        .GenericTypeArguments[0] ==
                    dto))
            .IsTrue();
        foreach (string flag in new[]
                 {
                     "CanJoin",
                     "CanLeave",
                     "CanAcceptOffer",
                     "CanWithdrawSupply",
                     "AllocationOpen",
                     "WithdrawalOpen",
                 })
        {
            PropertyInfo? property =
                dto!.GetProperty(flag);
            await Assert.That(property).IsNotNull();
            await Assert.That(property!
                    .GetCustomAttribute<
                        System.Text.Json.Serialization
                            .JsonIgnoreAttribute>())
                .IsNotNull();
        }
        foreach (string relation in new[]
                 {
                     "JoinFairReturnWaitlist",
                     "LeaveFairReturnWaitlist",
                     "AcceptFairReturnOffer",
                     "WithdrawFairReturnSupply",
                 })
        {
            await Assert.That(
                    typeof(LinkRelations).GetField(
                        relation,
                        BindingFlags.Public
                        | BindingFlags.Static))
                .IsNotNull();
        }

        var policyInstance =
            new FairReturnWaitlistLinkPolicy();
        var stopped = new FairReturnWaitlistDto
        {
            Id = Guid.CreateVersion7(),
            StatusCode = "AVAILABLE",
            Position =
                FairReturnWaitlistDto
                    .PositionUnavailable,
            ReasonCode = "WAITLIST_AVAILABLE",
            CanJoin = true,
            CanWithdrawSupply = true,
            AllocationOpen = false,
            WithdrawalOpen = false,
            EventId = Guid.CreateVersion7(),
            RegistrationOrderId =
                Guid.CreateVersion7(),
            RegistrationOrderLineId =
                Guid.CreateVersion7(),
            SupplyId = Guid.CreateVersion7(),
        };
        string[] stoppedRelations = policyInstance
            .GetLinks(stopped, null)
            .Select(link => link.Rel)
            .ToArray();
        await Assert.That(stoppedRelations)
            .DoesNotContain(
                LinkRelations
                    .JoinFairReturnWaitlist);
        await Assert.That(stoppedRelations)
            .DoesNotContain(
                LinkRelations
                    .WithdrawFairReturnSupply);

        string[] openRelations = policyInstance
            .GetLinks(
                stopped with
                {
                    AllocationOpen = true,
                    WithdrawalOpen = true,
                },
                null)
            .Select(link => link.Rel)
            .ToArray();
        await Assert.That(openRelations)
            .Contains(
                LinkRelations
                    .JoinFairReturnWaitlist);
        await Assert.That(openRelations)
            .Contains(
                LinkRelations
                    .WithdrawFairReturnSupply);
    }

    [Test]
    public async Task OpenApiPublishesPrivateResourceAndAuthorityHeaders()
    {
        using HttpResponseMessage response =
            await fixture.Client.GetAsync(
                "/openapi/islamu-event.json");
        using JsonDocument document =
            JsonDocument.Parse(
                await response.Content
                    .ReadAsStreamAsync());
        JsonElement paths =
            document.RootElement.GetProperty(
                "paths");

        await Assert.That(paths.TryGetProperty(
                OpenApiPath,
                out JsonElement path))
            .IsTrue();
        foreach (string operationName
                 in new[] { "get", "post", "delete" })
        {
            await Assert.That(path.TryGetProperty(
                    operationName,
                    out JsonElement operation))
                .IsTrue();
            await AssertHeaderAsync(
                operation,
                CapabilityHeader,
                required: false);
            if (operationName != "get")
            {
                await AssertHeaderAsync(
                    operation,
                    "Idempotency-Key",
                    required: true);
            }
        }
        string schema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("FairReturnWaitlistDto")
            .GetRawText();
        await Assert.That(schema.Contains(
                "priority",
                StringComparison.OrdinalIgnoreCase))
            .IsFalse();
        await Assert.That(schema.Contains(
                "email",
                StringComparison.OrdinalIgnoreCase))
            .IsFalse();
    }

    [Test]
    public async Task UnknownIdentityAndSellerConflictRemainGenericAndPrivate()
    {
        string path =
            $"/api/events/{Guid.CreateVersion7()}" +
            $"/registration-orders/" +
            $"{Guid.CreateVersion7()}/lines/" +
            $"{Guid.CreateVersion7()}/waitlist";
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                path);
        request.Headers.Add(
            CapabilityHeader,
            Guid.CreateVersion7().ToString("N"));

        using HttpResponseMessage response =
            await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Headers
                .CacheControl?.NoStore)
            .IsTrue();
        string body =
            await response.Content.ReadAsStringAsync();
        await Assert.That(body.Contains(
                "seller",
                StringComparison.OrdinalIgnoreCase))
            .IsFalse();
        await Assert.That(body.Contains(
                "participant",
                StringComparison.OrdinalIgnoreCase))
            .IsFalse();
        await Assert.That(body.Contains(
                "payment",
                StringComparison.OrdinalIgnoreCase))
            .IsFalse();
    }

    private static Type? ApiType(
        string fullName) =>
        typeof(EndpointClassificationAttribute)
            .Assembly.GetType(fullName);

    private static Type? ApplicationType(
        string fullName) =>
        typeof(LinkRelations).Assembly.GetType(
            fullName);

    private static MethodInfo RequireMethod(
        Type controller,
        string methodName) =>
        controller.GetMethod(methodName)
        ?? throw new InvalidOperationException(
            $"Method '{methodName}' is missing.");

    private static async Task
        AssertMethodRouteAsync(
            Type controller,
            string methodName,
            string template,
            HttpVerb verb)
    {
        MethodInfo method = RequireMethod(
            controller,
            methodName);
        HttpMethodAttribute? route = method
            .GetCustomAttributes<
                HttpMethodAttribute>()
            .SingleOrDefault();
        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template ?? "")
            .IsEqualTo(template);
        await Assert.That(route.HttpMethods)
            .Contains(verb.ToString()
                .ToUpperInvariant());
        await Assert.That(route.Name)
            .IsNotNull();
    }

    private static async Task AssertHeaderAsync(
        JsonElement operation,
        string name,
        bool required)
    {
        JsonElement parameter = operation
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(value =>
                value.GetProperty("name")
                    .GetString() == name
                && value.GetProperty("in")
                    .GetString() == "header");
        bool actualRequired =
            parameter.TryGetProperty(
                "required",
                out JsonElement requiredElement)
            && requiredElement.GetBoolean();
        await Assert.That(actualRequired)
            .IsEqualTo(required);
    }

    private enum HttpVerb
    {
        Get,
        Post,
        Delete,
    }
}
