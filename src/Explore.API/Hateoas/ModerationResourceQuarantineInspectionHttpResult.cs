using Explore.Application.Features.Moderation.Inspection;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Hateoas;

/// <summary>
/// The fresh safety-review output seam. Material is an attachment; an external destination is
/// inert UTF-8 text, never an HTTP redirect or a server fetch. No deferred formatter owns bytes.
/// </summary>
public sealed class ModerationResourceQuarantineInspectionHttpResult(
    ModerationResourceQuarantineInspectionResult pending) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        try
        {
            var disclosed = await pending.DiscloseAsync(async (bytes, destination, token) =>
            {
                if (!response.HasStarted)
                {
                    response.StatusCode = StatusCodes.Status200OK;
                    response.ContentType = destination ? "text/plain; charset=utf-8" : "application/octet-stream";
                    response.Headers.ContentDisposition = destination
                        ? "attachment; filename=\"destination.txt\""
                        : "attachment; filename=\"material.bin\"";
                }
                await response.Body.WriteAsync(bytes, token);
            }, context.HttpContext.RequestAborted);
            if (!disclosed)
                await new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Material unavailable",
                    Detail = "The requested material is unavailable for inspection."
                })
                { StatusCode = StatusCodes.Status404NotFound }.ExecuteResultAsync(context);
        }
        catch
        {
            // A partial response cannot be remapped to a successful file or retried.
            context.HttpContext.Abort();
            throw;
        }
    }
}
