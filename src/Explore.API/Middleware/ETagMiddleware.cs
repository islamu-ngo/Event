using System.Buffers;
using System.Security.Cryptography;
using Microsoft.IO;

namespace Explore.API.Middleware;

/// <summary>
/// Handles ETag generation and conditional request processing for GET endpoints.
/// Computes a weak ETag from the response body hash and returns 304 Not Modified
/// when the client sends a matching If-None-Match header.
/// Uses <see cref="RecyclableMemoryStreamManager"/> to eliminate per-request MemoryStream allocations.
/// Skips ETag computation for responses larger than <see cref="MaxETagBodySize"/>.
/// </summary>
public sealed class ETagMiddleware
{
    /// <summary>
    /// Maximum response body size (256 KB) for which ETags are computed.
    /// Larger responses skip ETag computation — they should rely on output cache alone.
    /// </summary>
    private const int MaxETagBodySize = 256 * 1024;

    private readonly RequestDelegate _next;
    private readonly RecyclableMemoryStreamManager _streamManager;

    public ETagMiddleware(RequestDelegate next, RecyclableMemoryStreamManager streamManager)
    {
        _next = next;
        _streamManager = streamManager;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var bufferStream = _streamManager.GetStream("etag-middleware");
        context.Response.Body = bufferStream;

        try
        {
            await _next(context);
        }
        catch
        {
            context.Response.Body = originalBodyStream;
            throw;
        }

        // Only process successful JSON responses
        if (context.Response.StatusCode is not (>= 200 and < 300))
        {
            bufferStream.Position = 0;
            await bufferStream.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;
            return;
        }

        var contentType = context.Response.ContentType;
        if (contentType is null ||
            (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) &&
             !contentType.Contains("application/hal+json", StringComparison.OrdinalIgnoreCase)))
        {
            bufferStream.Position = 0;
            await bufferStream.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;
            return;
        }

        var length = (int)bufferStream.Length;

        // Skip ETag computation for large responses — rely on output cache alone
        if (length > MaxETagBodySize)
        {
            bufferStream.Position = 0;
            await bufferStream.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;
            return;
        }

        // Compute SHA256 hash without ToArray() — rent from ArrayPool to avoid heap allocation
        bufferStream.Position = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            _ = bufferStream.Read(buffer, 0, length);
            var hash = SHA256.HashData(buffer.AsSpan(0, length));
            var etag = $"W/\"{Convert.ToBase64String(hash.AsSpan(0, 8))}\"";

            context.Response.Headers.ETag = etag;

            var ifNoneMatch = context.Request.Headers.IfNoneMatch.FirstOrDefault();
            if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag)
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.Body = originalBodyStream;
                context.Response.ContentLength = 0;
                return;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        bufferStream.Position = 0;
        await bufferStream.CopyToAsync(originalBodyStream);
        context.Response.Body = originalBodyStream;
    }
}

public static class ETagMiddlewareExtensions
{
    public static IApplicationBuilder UseETag(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ETagMiddleware>();
    }
}
