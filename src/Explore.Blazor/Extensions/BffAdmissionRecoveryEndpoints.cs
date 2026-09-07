using Explore.Blazor.Client.Clients;
using Microsoft.Net.Http.Headers;

namespace Explore.Blazor.Extensions;

public static class BffAdmissionRecoveryEndpoints
{
    private const string ConsumeRoute = "/bff/admission-recovery/consume";

    public static WebApplication MapAdmissionRecoveryEndpoints(this WebApplication app)
    {
        app.MapPost(ConsumeRoute, HandleConsumeAsync)
            .ValidateAntiforgery()
            .ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> HandleConsumeAsync(
        AdmissionRecoveryBffRequest request,
        HttpContext context,
        IAdmissionTicketRecoveryClient apiClient,
        ILogger<RecoveryLogCategory> logger,
        CancellationToken cancellationToken)
    {
        SetSensitiveResponseHeaders(context.Response.Headers);
        if (context.Request.Query.Count != 0 ||
            string.IsNullOrWhiteSpace(request.Capability) ||
            request.Capability.Length > 256)
        {
            return Results.NotFound();
        }

        try
        {
            AdmissionTicketRecoveryDeliveryDto delivery =
                await apiClient.ConsumeAdmissionTicketRecoveryAsync(
                    request.Capability,
                    cancellationToken: cancellationToken);
            return Results.Ok(delivery);
        }
        catch (ApiException exception)
            when (exception.StatusCode is StatusCodes.Status401Unauthorized
                or StatusCodes.Status403Forbidden
                or StatusCodes.Status404NotFound)
        {
            return Results.NotFound();
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }
        catch (ApiException exception)
        {
            logger.LogError(
                "Admission recovery BFF handoff failed with downstream status {StatusCode}",
                exception.StatusCode);
            return Results.Problem(
                title: "Admission recovery unavailable",
                detail: "Admission recovery is temporarily unavailable.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static void SetSensitiveResponseHeaders(IHeaderDictionary headers)
    {
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers[HeaderNames.Pragma] = "no-cache";
        headers[HeaderNames.Expires] = "0";
        headers["Referrer-Policy"] = "no-referrer";
    }

    private sealed class RecoveryLogCategory;
}

public sealed record AdmissionRecoveryBffRequest(string Capability)
{
    public override string ToString() => "AdmissionRecoveryBffRequest(<redacted>)";
}
