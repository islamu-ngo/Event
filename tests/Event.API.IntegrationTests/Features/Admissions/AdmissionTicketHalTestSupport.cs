using System.Text.Json;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class AdmissionTicketApiRedContractTests
{
    private static string[] Relations(string halBody)
    {
        using JsonDocument document = JsonDocument.Parse(halBody);
        return document.RootElement.TryGetProperty("_links", out JsonElement links)
            ? links.EnumerateObject().Select(property => property.Name).ToArray()
            : [];
    }

    private static bool HasHalLinksMember(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("_links", out _);
    }

    private static string LinkHref(string halBody, string relation) =>
        LinkProperty(halBody, relation, "href");

    private static string LinkMethod(string halBody, string relation) =>
        LinkProperty(halBody, relation, "method");

    private static string LinkProperty(string halBody, string relation, string property)
    {
        using JsonDocument document = JsonDocument.Parse(halBody);
        return document.RootElement.GetProperty("_links").GetProperty(relation)
            .GetProperty(property).GetString()!;
    }

    private static string JsonString(string body, string property)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty(property).GetString()!;
    }

    private static Guid JsonGuid(string body, string property) =>
        Guid.Parse(JsonString(body, property));

    private static async Task AssertPrivateNoReferrer(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single())
            .IsEqualTo("no-referrer");
    }

    private static string AccountPath(Guid ticketId) => $"/api/tickets/{ticketId:D}";

    private static string AccountSurfacePath(Guid ticketId, string surface) => surface switch
    {
        "account-detail" => AccountPath(ticketId),
        "account-qr" => AccountPath(ticketId) + "/qr",
        "account-print" => AccountPath(ticketId) + "/print",
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
    };

    private static ApiRouteContract SurfaceRoute(string surface) => surface switch
    {
        "account-detail" => AccountDetail,
        "account-qr" => AccountQr,
        "account-print" => AccountPrint,
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
    };
}
