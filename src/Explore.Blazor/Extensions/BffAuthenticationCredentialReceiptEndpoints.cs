using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.InstanceAdmin;
using Explore.Blazor.Configuration;
using Explore.Blazor.Services.Preferences;

namespace Explore.Blazor.Extensions;

public static class BffAuthenticationCredentialReceiptEndpoints
{
    public static IEndpointRouteBuilder MapBffAuthenticationCredentialReceiptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/bff/auth/credential-acquisition-receipts", ReadAsync)
            .RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> ReadAsync(
        HttpContext context, IInstanceAdminClient controlPlaneClient,
        AuthenticationCredentialAcquisitionReceipts receipts, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
        HalResourceOfInstanceOverviewDto overview;
        try
        {
            overview = await controlPlaneClient.GetInstanceAdminOverviewAsync(cancellationToken: cancellationToken);
        }
        catch (ApiException exception)
        {
            return BffForwardingResults.Problem(exception,
                "Current instance administration capabilities could not be read.",
                "Credential acquisition inspection unavailable");
        }
        if (overview._links is null
            || !overview._links.TryGetValue(InstanceAdminLinkRelations.AuthenticationCredentialCutovers, out var link)
            || string.IsNullOrWhiteSpace(link.Href)
            || !string.Equals(link.Method, "GET", StringComparison.OrdinalIgnoreCase))
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Credential acquisition inspection unavailable",
                detail: "Current instance administration capabilities do not permit this inspection.");

        var snapshot = receipts.Snapshot();
        if (snapshot.Count == 0 || snapshot.Any(receipt =>
            receipt.AuthorityProfileRevision is null
            || receipt.AuthorityProfileRevision != overview.AuthenticationCredentialProfileRevision
            || !string.Equals(receipt.Authority.ToString(), overview.AuthenticationCredentialAuthority,
                StringComparison.Ordinal)))
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Credential acquisition profile mismatch",
                detail: "This process has no acquisition matching the independently selected API deployment profile.");

        return Results.Json(snapshot);
    }
}
