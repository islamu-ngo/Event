using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Blazor.Models;
using Explore.Blazor.Services.Auth;
using Microsoft.AspNetCore.Http.Features;

namespace Explore.Blazor.Extensions;

public static class BffLocalVisitorEnrollmentEndpoints
{
    public const string Prefix = "/bff/auth/local/enrollment";
    private const int BodyLimit = 16384;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IApplicationBuilder UseLocalVisitorEnrollmentPrivacyHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "no-store, private";
                    context.Response.Headers["Referrer-Policy"] = "no-referrer";
                    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                    return Task.CompletedTask;
                });
                if (context.Request.ContentLength > BodyLimit)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
                    limit.MaxRequestBodySize = BodyLimit;
            }
            await next(context);
        });

    public static WebApplication MapLocalVisitorEnrollmentEndpoints(this WebApplication app)
    {
        app.MapPost(Prefix + "/start", StartAsync).ValidateAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.LocalAuthenticationPolicy).AllowAnonymous().ExcludeFromDescription();
        app.MapPost(Prefix + "/register", RegisterAsync).ValidateAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.LocalAuthenticationPolicy).AllowAnonymous().ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> StartAsync(
        HttpContext context, LocalVisitorEnrollmentFlowStore store, IHttpClientFactory factory, CancellationToken ct)
    {
        var target = store.CaptureTarget(context);
        if (target is null) return await RejectAsync(context, store, ct);
        if (!context.Request.HasJsonContentType()) return Failure(415, "enrollment_invalid");
        try
        {
            var body = await ReadBoundedAsync<LocalVisitorEnrollmentStartRequest>(context.Request.Body, ct);
            if (body is null || store.CaptureTarget(context) != target) return await RejectAsync(context, store, ct);
            var returnPath = body.ReturnUrl ?? target.PathBase + "/";
            if (!LocalVisitorEnrollmentFlowStore.IsSafeLocalPath(returnPath))
                return await RejectAsync(context, store, ct);

            var existingCookie = context.Request.Cookies.ContainsKey(LocalVisitorEnrollmentFlowStore.CookieName);
            var flow = await store.ReadAsync(context, ct);
            if (store.CaptureTarget(context) != target || existingCookie && flow is null)
                return await RejectAsync(context, store, ct);
            var admission = await BffLocalCredentialEndpoints.GetFreshAdmissionAsync(context, ct);
            if (store.CaptureTarget(context) != target) return await RejectAsync(context, store, ct);
            if (admission != BffLocalCredentialEndpoints.LocalCredentialAdmission.Allowed)
                return await AdmissionFailureAsync(context, store, admission, ct);
            if (flow is null)
                flow = await store.CreateAsync(context, target, returnPath, ct);
            if (!store.Matches(context, flow, existingCookie))
                return await RejectAsync(context, store, ct);
            if (flow.EnrollmentIntent is not null)
                return Results.Ok(new LocalVisitorEnrollmentStatus("ready"));

            using var request = CreateApiRequest("/api/auth/local/enrollment/reserve", flow, new ReserveRequest(flow.OperationId), context);
            using var client = factory.CreateClient(BffLocalCredentialEndpoints.HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!store.Matches(context, flow, existingCookie)) return await RejectAsync(context, store, ct);
            if (response.StatusCode != HttpStatusCode.OK)
                return await ApiFailureAsync(context, store, response.StatusCode, ct);

            using var reservationStream = await response.Content.ReadAsStreamAsync(ct);
            if (!store.Matches(context, flow, existingCookie)) return await RejectAsync(context, store, ct);
            var reservation = await ReadApiResponseAsync<Reservation>(reservationStream, ct);
            if (!store.Matches(context, flow, existingCookie)) return await RejectAsync(context, store, ct);
            if (reservation is null || reservation.OperationId != flow.OperationId
                || string.IsNullOrWhiteSpace(reservation.EnrollmentIntent)
                || reservation.EnrollmentIntent.Length > 8192
                || reservation.ExpiresAt <= context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow())
                return Failure(503, "enrollment_unavailable");
            // The API may shorten a flow, but can never renew its original BFF deadline.
            flow = flow with
            {
                EnrollmentIntent = reservation.EnrollmentIntent,
                ExpiresAt = reservation.ExpiresAt < flow.ExpiresAt ? reservation.ExpiresAt : flow.ExpiresAt
            };
            await store.SaveAsync(flow, ct);
            if (!store.Matches(context, flow, existingCookie)) return await RejectAsync(context, store, ct);
            return Results.Ok(new LocalVisitorEnrollmentStatus("ready"));
        }
        catch (EnrollmentBodyTooLargeException) { return Results.StatusCode(413); }
        catch (JsonException) { return Failure(400, "enrollment_invalid"); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            if (store.CaptureTarget(context) != target) return await RejectAsync(context, store, CancellationToken.None);
            return Failure(503, "enrollment_unavailable");
        }
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context, LocalVisitorEnrollmentFlowStore store, IHttpClientFactory factory, CancellationToken ct)
    {
        var target = store.CaptureTarget(context);
        if (target is null) return await RejectAsync(context, store, ct);
        if (!context.Request.HasJsonContentType()) return Failure(415, "enrollment_invalid");
        try
        {
            var flow = await store.ReadAsync(context, ct);
            if (flow is null || flow.EnrollmentIntent is null) return await RejectAsync(context, store, ct);
            var body = await ReadBoundedAsync<LocalVisitorEnrollmentRegisterRequest>(context.Request.Body, ct);
            if (!store.Matches(context, flow)) return await RejectAsync(context, store, ct);
            if (body is null || string.IsNullOrWhiteSpace(body.Username) || body.Username.Length > 256
                || string.IsNullOrEmpty(body.Password) || body.Email?.Length > 320)
                return Failure(400, "enrollment_invalid");
            var admission = await BffLocalCredentialEndpoints.GetFreshAdmissionAsync(context, ct);
            if (!store.Matches(context, flow)) return await RejectAsync(context, store, ct);
            if (admission != BffLocalCredentialEndpoints.LocalCredentialAdmission.Allowed)
                return await AdmissionFailureAsync(context, store, admission, ct);

            using var request = CreateApiRequest("/api/auth/local/enrollment/register", flow,
                new RegisterRequest(flow.OperationId, body.Username, body.Password, body.Email, flow.EnrollmentIntent), context);
            using var client = factory.CreateClient(BffLocalCredentialEndpoints.HttpClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!store.Matches(context, flow)) return await RejectAsync(context, store, ct);
            if (response.StatusCode != HttpStatusCode.OK)
                return await ApiFailureAsync(context, store, response.StatusCode, ct);
            using var receiptStream = await response.Content.ReadAsStreamAsync(ct);
            if (!store.Matches(context, flow)) return await RejectAsync(context, store, ct);
            var receipt = await ReadApiResponseAsync<RegistrationReceipt>(receiptStream, ct);
            if (!store.Matches(context, flow)) return await RejectAsync(context, store, ct);
            if (receipt is null || !receipt.Success || receipt.Id != flow.OperationId)
                return Failure(503, "enrollment_unavailable");
            // An operation receipt is not a Local authentication response.
            admission = await BffLocalCredentialEndpoints.GetFreshAdmissionAsync(context, ct);
            if (!store.Matches(context, flow)) return await RejectAsync(context, store, ct);
            await store.CompleteAsync(context, flow, ct);
            if (!store.Matches(context, flow)
                || admission != BffLocalCredentialEndpoints.LocalCredentialAdmission.Allowed)
                return Failure(409, "enrollment_closed");
            var loginPath = flow.Target.PathBase + "/login?returnUrl=" + Uri.EscapeDataString(flow.ReturnPath);
            return Results.Ok(new LocalVisitorEnrollmentStatus("account_created", loginPath));
        }
        catch (EnrollmentBodyTooLargeException) { return Results.StatusCode(413); }
        catch (JsonException) { return Failure(400, "enrollment_invalid"); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            if (store.CaptureTarget(context) != target) return await RejectAsync(context, store, CancellationToken.None);
            return Failure(503, "enrollment_unavailable");
        }
    }

    private static HttpRequestMessage CreateApiRequest(string path, LocalVisitorEnrollmentFlow flow, object body, HttpContext context)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Local-Enrollment-Proof", flow.Proof);
        if (flow.Target.TenantSlug is not null) request.Headers.Add("X-Tenant-Slug", flow.Target.TenantSlug);
        if (context.Connection.RemoteIpAddress is { } peer)
        {
            request.Headers.Add("X-Forwarded-Host", flow.Target.Host);
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("X-Forwarded-For", peer.ToString());
        }
        return request;
    }

    private static async Task<IResult> RejectAsync(HttpContext context, LocalVisitorEnrollmentFlowStore store, CancellationToken ct)
    {
        await store.ClearAsync(context, ct);
        return Failure(403, "enrollment_invalid");
    }

    private static Task<IResult> AdmissionFailureAsync(HttpContext context, LocalVisitorEnrollmentFlowStore store,
        BffLocalCredentialEndpoints.LocalCredentialAdmission admission, CancellationToken ct) =>
        admission == BffLocalCredentialEndpoints.LocalCredentialAdmission.Unavailable
            ? Task.FromResult(Failure(503, "enrollment_unavailable"))
            : ApiFailureAsync(context, store, HttpStatusCode.Conflict, ct);

    private static async Task<IResult> ApiFailureAsync(HttpContext context, LocalVisitorEnrollmentFlowStore store,
        HttpStatusCode status, CancellationToken ct)
    {
        if (status is HttpStatusCode.Forbidden or HttpStatusCode.Conflict or HttpStatusCode.Unauthorized)
            await store.ClearAsync(context, ct);
        return status switch
        {
            HttpStatusCode.BadRequest => Failure(400, "enrollment_invalid"),
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => Failure(403, "enrollment_invalid"),
            HttpStatusCode.Conflict => Failure(409, "enrollment_closed"),
            HttpStatusCode.TooManyRequests => Failure(429, "rate_limited"),
            _ => Failure(503, "enrollment_unavailable")
        };
    }

    private static IResult Failure(int status, string code) => Results.Problem(
        statusCode: status, title: "Local enrollment could not be completed",
        extensions: new Dictionary<string, object?> { ["code"] = code });

    private static async Task<T?> ReadBoundedAsync<T>(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[BodyLimit + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        if (count > BodyLimit) throw new EnrollmentBodyTooLargeException();
        return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, count), JsonOptions);
    }

    private static async Task<T?> ReadApiResponseAsync<T>(Stream stream, CancellationToken ct)
    {
        try { return await ReadBoundedAsync<T>(stream, ct); }
        catch (Exception exception) when (exception is JsonException or EnrollmentBodyTooLargeException)
        {
            return default;
        }
    }

    private sealed class EnrollmentBodyTooLargeException : Exception { }
    private sealed record RegistrationReceipt(Guid Id, bool Success)
    {
        public override string ToString() => nameof(RegistrationReceipt);
    }
    private sealed record ReserveRequest(Guid OperationId)
    {
        public override string ToString() => nameof(ReserveRequest);
    }
    private sealed record RegisterRequest(Guid OperationId, string Username, string Password, string? Email, string EnrollmentIntent)
    {
        public override string ToString() => nameof(RegisterRequest);
    }
    private sealed record Reservation(Guid OperationId, string EnrollmentIntent, DateTimeOffset ExpiresAt)
    {
        public override string ToString() => nameof(Reservation);
    }
}
