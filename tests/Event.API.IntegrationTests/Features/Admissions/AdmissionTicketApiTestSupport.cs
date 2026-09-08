using System.Reflection;
using System.Text.Json;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class AdmissionTicketApiRedContractTests
{
    private const string RecoveryCapabilityHeader = "X-Admission-Ticket-Recovery-Capability";
    private const string RecoveryRequestRateLimitPolicy = "public_transactional";
    private const string RecoveryConsumeRateLimitPolicy = "admission_ticket_recovery";
    private const string QrRelation = "qr-code";
    private const string PrintRelation = "print";

    private static readonly ApiRouteContract AccountList = new(
        "api/tickets", HttpMethods.Get, "GetCurrentAdmissionTickets");
    private static readonly ApiRouteContract AccountDetail = new(
        "api/tickets/{ticketId:guid}", HttpMethods.Get, "GetCurrentAdmissionTicket");
    private static readonly ApiRouteContract AccountQr = new(
        "api/tickets/{ticketId:guid}/qr", HttpMethods.Post, "ReissueCurrentAdmissionTicketQr");
    private static readonly ApiRouteContract AccountPrint = new(
        "api/tickets/{ticketId:guid}/print", HttpMethods.Post, "ReissueCurrentAdmissionTicketPrint");
    private static readonly ApiRouteContract RecoveryRequest = new(
        "api/tickets/recovery", HttpMethods.Post, "RequestAdmissionTicketRecovery");
    private static readonly ApiRouteContract RecoveryConsume = new(
        "api/tickets/recovery/consume", HttpMethods.Post, "ConsumeAdmissionTicketRecovery");

    private static ApiRouteContract[] AllRoutes() =>
    [
        AccountList, AccountDetail, AccountQr, AccountPrint, RecoveryRequest, RecoveryConsume
    ];

    private static async Task RequireRoute(ApiRouteContract expected)
    {
        ActionContract? action = FindAction(expected);
        await Assert.That(action).IsNotNull()
            .Because($"Phase 20 requires {expected.HttpMethod} {expected.Template}");
        await Assert.That(action!.RouteName).IsEqualTo(expected.RouteName);
    }

    private static ActionContract? FindAction(ApiRouteContract expected) => ApiActions()
        .SingleOrDefault(candidate =>
            candidate.Template == expected.Template && candidate.HttpMethod == expected.HttpMethod);

    private static IEnumerable<ActionContract> ApiActions()
    {
        foreach (Type controller in typeof(Program).Assembly.GetTypes().Where(type =>
                     !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type)))
        {
            string prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            foreach (MethodInfo method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (HttpMethodAttribute route in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    string template = CombineRoute(prefix, route.Template);
                    foreach (string httpMethod in route.HttpMethods)
                        yield return new ActionContract(method, template, httpMethod, route.Name);
                }
            }
        }
    }

    private static string CombineRoute(string prefix, string? suffix) =>
        string.Join('/', new[] { prefix, suffix }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim('/')));

    private static int[] ProducedStatuses(ActionContract action) => action.Method
        .GetCustomAttributes<ProducesResponseTypeAttribute>()
        .Select(attribute => attribute.StatusCode)
        .ToArray();

    private static bool IsCapabilityParameter(ParameterInfo parameter) =>
        parameter.Name?.Contains("capability", StringComparison.OrdinalIgnoreCase) == true
        || parameter.GetCustomAttribute<FromHeaderAttribute>()?.Name?.Contains(
            "capability", StringComparison.OrdinalIgnoreCase) == true;

    private static async Task<HttpResponseMessage> SendRecoveryRequest(
        HttpClient client,
        string identity,
        string idempotencyKey,
        string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tickets/recovery")
        {
            Content = JsonContent.Create(new { email = identity })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (forwardedFor is not null)
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendRecoveryConsume(
        HttpClient client,
        string capability)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tickets/recovery/consume");
        request.Headers.Add(RecoveryCapabilityHeader, capability);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendAccountGet(HttpClient client, string path)
        => await SendAccount(client, HttpMethod.Get, path);

    private static async Task<HttpResponseMessage> SendAccountPost(HttpClient client, string path)
        => await SendAccount(client, HttpMethod.Post, path);

    private static async Task<HttpResponseMessage> SendAccount(
        HttpClient client,
        HttpMethod method,
        string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        return await client.SendAsync(request);
    }

    private static async Task<string> ResponseShape(HttpResponseMessage response) =>
        $"{(int)response.StatusCode}|{response.Content.Headers.ContentType?.MediaType}|" +
        CanonicalJson(await response.Content.ReadAsStringAsync());

    private static async Task<string> ProblemFingerprint(HttpResponseMessage response) =>
        CanonicalJson(await response.Content.ReadAsStringAsync());

    private static string CanonicalJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        using JsonDocument document = JsonDocument.Parse(body);
        return CanonicalElement(document.RootElement);
    }

    private static string CanonicalElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(',', element.EnumerateObject()
            .Where(property => property.Name is not (
                "traceId" or "timestamp" or "correlationId" or "instance"))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => property.Name + ":" + CanonicalElement(property.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(',', element.EnumerateArray().Select(CanonicalElement)) + "]",
        _ => element.GetRawText()
    };

    private sealed record ApiRouteContract(string Template, string HttpMethod, string RouteName);
    private sealed record ActionContract(
        MethodInfo Method,
        string Template,
        string HttpMethod,
        string? RouteName);
}
