using System.Security.Cryptography;
using Explore.API.Filters;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Middleware;

/// <summary>
/// Computes weak validators only for completely captured, successful JSON responses.
/// Retention is capped before allocation; streaming and uncaptured output has no validator.
/// </summary>
public sealed class ETagMiddleware(RequestDelegate next)
{
    private const int MaxETagBodySize = 256 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var produces = context.GetEndpoint()?.Metadata.GetMetadata<ProducesAttribute>();
        if (context.GetEndpoint()?.Metadata.GetMetadata<PrivateNoStoreAttribute>() is not null
            || produces?.ContentTypes.Contains("text/event-stream", StringComparer.OrdinalIgnoreCase) is true
            || produces is { ContentTypes.Count: > 0 } && !produces.ContentTypes.Any(IsJson)
            || !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await next(context);
            return;
        }

        Stream original = context.Response.Body;
        using var capture = new BoundedResponseCaptureStream(
            original,
            MaxETagBodySize,
            () => !context.Response.HasStarted
                && context.Response.StatusCode is >= 200 and < 300
                && IsJson(context.Response.ContentType)
                && context.Response.ContentLength is not > MaxETagBodySize,
            () => HttpMethods.IsHead(context.Request.Method)
                || context.Response.StatusCode is StatusCodes.Status204NoContent or StatusCodes.Status304NotModified);
        context.Response.Body = capture;
        try
        {
            await next(context);
            if (capture.IsCaptured && !context.Response.HasStarted
                && context.Response.StatusCode is >= 200 and < 300
                && context.Response.StatusCode != StatusCodes.Status204NoContent
                && context.Response.ContentLength is not > MaxETagBodySize
                && IsJson(context.Response.ContentType))
            {
                var hash = SHA256.HashData(capture.CapturedBody.Span);
                var etag = $"W/\"{Convert.ToBase64String(hash.AsSpan(0, 8))}\"";
                context.Response.Headers.ETag = etag;
                if (context.Request.Headers.IfNoneMatch.FirstOrDefault() == etag)
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.ContentLength = null;
                    return;
                }

                if (HttpMethods.IsHead(context.Request.Method))
                {
                    context.Response.ContentLength ??= capture.Length;
                }
            }

            await capture.ForwardPrefixAsync(context.RequestAborted);
        }
        finally
        {
            context.Response.Body = original;
        }
    }

    private static bool IsJson(string? contentType)
    {
        string? mediaType = contentType?.Split(';', 2)[0].Trim();
        return mediaType?.Equals("application/json", StringComparison.OrdinalIgnoreCase) is true
            || mediaType?.Equals("application/hal+json", StringComparison.OrdinalIgnoreCase) is true;
    }
}

public static class ETagMiddlewareExtensions
{
    public static IApplicationBuilder UseETag(this IApplicationBuilder app) =>
        app.UseMiddleware<ETagMiddleware>();
}
