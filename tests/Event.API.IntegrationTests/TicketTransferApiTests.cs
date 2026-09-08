using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests;

[ClassDataSource<ContractApiFixture>(
    Shared = SharedType.PerAssembly)]
public sealed class TicketTransferApiTests(
    ContractApiFixture fixture)
{
    private const string CapabilityHeader =
        "X-Ticket-Transfer-Capability";
    private const string ControllerTypeName =
        "Explore.API.Controllers.TicketTransferController";
    private const string DtoTypeName =
        "Explore.Application.DTOs.Admissions.TicketTransferDto";
    private const string LinkPolicyTypeName =
        "Explore.API.Hateoas.Policies.TicketTransferLinkPolicy";

    [Test]
    public async Task TransferControllerPublishesExactLifecycleRoutes()
    {
        Type? controller = ApiType(ControllerTypeName);

        await Assert.That(controller).IsNotNull();
        await Assert.That(
                controller!.GetCustomAttribute<ApiControllerAttribute>())
            .IsNotNull();
        RouteAttribute? route =
            controller.GetCustomAttribute<RouteAttribute>();
        await Assert.That(route?.Template)
            .IsEqualTo(
                "api/events/{eventId:guid}/admission-tickets/{admissionTicketId:guid}/transfers");

        await AssertMethodRouteAsync(
            controller,
            "Get",
            "{transferId:guid}",
            HttpVerb.Get);
        await AssertMethodRouteAsync(
            controller,
            "Offer",
            "",
            HttpVerb.Post);
        await AssertMethodRouteAsync(
            controller,
            "Accept",
            "{transferId:guid}/accept",
            HttpVerb.Post);
        await AssertMethodRouteAsync(
            controller,
            "Cancel",
            "{transferId:guid}",
            HttpVerb.Delete);
        await AssertMethodRouteAsync(
            controller,
            "Correct",
            "{transferId:guid}/correction",
            HttpVerb.Post);
        await AssertMethodRouteAsync(
            controller,
            "Reissue",
            "{transferId:guid}/reissue",
            HttpVerb.Post);
    }

    [Test]
    public async Task TransferWritesRequireAuthorizationAndRateLimits()
    {
        Type? controller = ApiType(ControllerTypeName);

        await Assert.That(controller).IsNotNull();
        MethodInfo[] writes =
        [
            RequireMethod(controller!, "Offer"),
            RequireMethod(controller, "Accept"),
            RequireMethod(controller, "Cancel"),
            RequireMethod(controller, "Correct"),
            RequireMethod(controller, "Reissue"),
        ];
        foreach (MethodInfo write in writes)
        {
            await Assert.That(
                    write.GetCustomAttribute<
                        AuthorizeAttribute>())
                .IsNotNull();
            await Assert.That(
                    write.GetCustomAttribute<
                        EnableRateLimitingAttribute>())
                .IsNotNull();
            EndpointClassificationAttribute? classification =
                write.GetCustomAttribute<
                    EndpointClassificationAttribute>();
            await Assert.That(classification).IsNotNull();
            await Assert.That(
                    classification!.Class)
                .IsEqualTo(
                    EndpointClass.Authenticated);
        }

        MethodInfo read = RequireMethod(
            controller!,
            "Get");
        await Assert.That(
                read.GetCustomAttribute<
                    AllowAnonymousAttribute>())
            .IsNotNull();
    }

    [Test]
    public async Task TransferContractContainsOnlyBoundedNonPiiState()
    {
        Type? dto = ApplicationType(DtoTypeName);

        await Assert.That(dto).IsNotNull();
        string[] expected =
        [
            "Id",
            "AdmissionTicketId",
            "StatusCode",
            "SupportCode",
            "TransferHop",
            "ExpiresAt",
            "CredentialGeneration",
        ];
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
            .IsEquivalentTo(expected);
        string[] affordanceProperties =
        [
            "CanOffer",
            "CanAccept",
            "CanCancel",
            "CanCorrect",
            "CanReissue",
        ];
        foreach (string affordance in affordanceProperties)
        {
            PropertyInfo? property = dto.GetProperty(
                affordance);
            await Assert.That(property).IsNotNull();
            await Assert.That(
                    property!.GetCustomAttribute<
                        System.Text.Json.Serialization
                            .JsonIgnoreAttribute>())
                .IsNotNull();
        }

        string[] forbiddenFragments =
        [
            "tenant",
            "order",
            "line",
            "participant",
            "subject",
            "user",
            "actor",
            "email",
            "phone",
            "name",
            "address",
            "answer",
            "consent",
            "approval",
            "payment",
            "refund",
            "merchant",
            "currency",
            "amount",
            "digest",
            "token",
            "capability",
        ];
        await Assert.That(publicProperties.Any(property =>
                forbiddenFragments.Any(fragment =>
                    property.Contains(
                        fragment,
                        StringComparison.OrdinalIgnoreCase))))
            .IsFalse();
    }

    [Test]
    public async Task HalRelationsExposeOnlyServerAuthorizedTransferActions()
    {
        Type? policy = ApiType(LinkPolicyTypeName);
        Type? dto = ApplicationType(DtoTypeName);

        await Assert.That(policy).IsNotNull();
        await Assert.That(dto).IsNotNull();
        await Assert.That(policy!.GetInterfaces().Any(
                contract =>
                    contract.IsGenericType
                    && contract.GetGenericTypeDefinition()
                        .Name.StartsWith(
                            "ILinkPolicy",
                            StringComparison.Ordinal)
                    && contract.GenericTypeArguments[0] ==
                    dto))
            .IsTrue();

        string[] relations =
        [
            "OfferTicketTransfer",
            "AcceptTicketTransfer",
            "CancelTicketTransfer",
            "CorrectTicketTransfer",
            "ReissueTransferredTicket",
        ];
        foreach (string relation in relations)
        {
            FieldInfo? field = typeof(LinkRelations)
                .GetField(
                    relation,
                    BindingFlags.Public
                    | BindingFlags.Static);
            await Assert.That(field).IsNotNull();
            await Assert.That(field!.GetValue(null))
                .IsTypeOf<string>();
        }
    }

    [Test]
    public async Task OpenApiPublishesTransferOperationsAndCapabilityHeader()
    {
        HttpClient client = fixture.Client;
        using HttpResponseMessage response =
            await client.GetAsync(
                "/openapi/islamu-event.json");
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        JsonElement paths =
            document.RootElement.GetProperty("paths");
        string root =
            "/api/events/{eventId}/admission-tickets/{admissionTicketId}/transfers";
        string item = root + "/{transferId}";

        await Assert.That(paths.TryGetProperty(
                root,
                out JsonElement rootPath))
            .IsTrue();
        await Assert.That(
                rootPath.TryGetProperty("post", out _))
            .IsTrue();
        await Assert.That(paths.TryGetProperty(
                item,
                out JsonElement itemPath))
            .IsTrue();
        await Assert.That(
                itemPath.TryGetProperty("get", out _))
            .IsTrue();
        await Assert.That(
                itemPath.TryGetProperty("delete", out _))
            .IsTrue();
        foreach (string suffix in new[]
                 {
                     "/accept",
                     "/correction",
                     "/reissue",
                 })
        {
            await Assert.That(paths.TryGetProperty(
                    item + suffix,
                    out JsonElement actionPath))
                .IsTrue();
            await Assert.That(
                    actionPath.TryGetProperty("post", out _))
                .IsTrue();
        }

        string openApi =
            document.RootElement.GetRawText();
        await Assert.That(openApi)
            .Contains(CapabilityHeader);
        await Assert.That(openApi)
            .DoesNotContain(
                "capability={",
                StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task InvalidCapabilityIsGenericPrivateProblemDetails()
    {
        HttpClient client = fixture.Client;
        string sentinel =
            "SENTINEL_TRANSFER_CAPABILITY_DO_NOT_ECHO";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/events/{Guid.CreateVersion7()}/admission-tickets/{Guid.CreateVersion7()}/transfers/{Guid.CreateVersion7()}");
        request.Headers.TryAddWithoutValidation(
            CapabilityHeader,
            sentinel);
        using HttpResponseMessage response =
            await client.SendAsync(request);
        string body =
            await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(
                response.Content.Headers.ContentType?
                    .MediaType)
            .IsEqualTo(
                "application/problem+json");
        await Assert.That(body)
            .DoesNotContain(sentinel);
        await Assert.That(body)
            .DoesNotContain(
                "capability",
                StringComparison.OrdinalIgnoreCase);
        await AssertPrivateSecurityHeadersAsync(response);
    }

    [Test]
    public async Task UnauthenticatedTransferWritesFailClosedWithoutEcho()
    {
        HttpClient client = fixture.Client;
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid transferId = Guid.CreateVersion7();
        string root =
            $"/api/events/{eventId}/admission-tickets/{ticketId}/transfers";
        string sentinel =
            "SENTINEL_TRANSFER_WRITE_DO_NOT_ECHO";
        (HttpMethod Method, string Path)[] writes =
        [
            (HttpMethod.Post, root),
            (
                HttpMethod.Post,
                $"{root}/{transferId}/accept"),
            (
                HttpMethod.Delete,
                $"{root}/{transferId}"),
            (
                HttpMethod.Post,
                $"{root}/{transferId}/correction"),
            (
                HttpMethod.Post,
                $"{root}/{transferId}/reissue"),
        ];
        foreach ((HttpMethod method, string path) in writes)
        {
            using var request = new HttpRequestMessage(
                method,
                path);
            request.Headers.TryAddWithoutValidation(
                CapabilityHeader,
                sentinel);
            request.Content = new StringContent(
                "{}",
                Encoding.UTF8,
                "application/json");
            using HttpResponseMessage response =
                await client.SendAsync(request);
            string body =
                await response.Content.ReadAsStringAsync();
            await Assert.That(response.StatusCode)
                .IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(body)
                .DoesNotContain(sentinel);
            await AssertPrivateSecurityHeadersAsync(response);
        }
    }

    private static Type? ApiType(string name) =>
        typeof(ParticipantReadinessController)
            .Assembly
            .GetType(name);

    private static Type? ApplicationType(string name) =>
        typeof(LinkRelations)
            .Assembly
            .GetType(name);

    private static MethodInfo RequireMethod(
        Type type,
        string name) =>
        type.GetMethod(
            name,
            BindingFlags.Public
            | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"Expected public method '{name}'.");

    private static async Task AssertMethodRouteAsync(
        Type controller,
        string methodName,
        string template,
        HttpVerb verb)
    {
        MethodInfo method = RequireMethod(
            controller,
            methodName);
        HttpMethodAttribute? route = verb switch
        {
            HttpVerb.Get =>
                method.GetCustomAttribute<HttpGetAttribute>(),
            HttpVerb.Post =>
                method.GetCustomAttribute<HttpPostAttribute>(),
            HttpVerb.Delete =>
                method.GetCustomAttribute<HttpDeleteAttribute>(),
            _ => null,
        };
        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template)
            .IsEqualTo(template);
    }

    private static async Task
        AssertPrivateSecurityHeadersAsync(
            HttpResponseMessage response)
    {
        await Assert.That(
                response.Headers.CacheControl?.Private)
            .IsTrue();
        await Assert.That(
                response.Headers.CacheControl?.NoStore)
            .IsTrue();
        await Assert.That(response.Headers.TryGetValues(
                "Referrer-Policy",
                out IEnumerable<string>? values))
            .IsTrue();
        await Assert.That(values)
            .Contains("no-referrer");
    }

    private enum HttpVerb
    {
        Get,
        Post,
        Delete,
    }
}
