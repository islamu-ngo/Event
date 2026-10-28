using Explore.Application.Features.Moderation.Narratives;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Hateoas;

/// <summary>Writes only inside the custody owner's final native transaction, without a deferred MVC payload formatter.</summary>
public sealed class ModerationCaseNarrativeResult(ModerationCaseNarrativeReadResult pending) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        var disclosed = await pending.DiscloseAsync(async (bytes, token) =>
        {
            response.ContentType = "application/octet-stream";
            response.Headers.ContentDisposition = "attachment; filename=\"allegation.bin\"";
            response.ContentLength = bytes.Length;
            response.StatusCode = StatusCodes.Status200OK;
            await response.Body.WriteAsync(bytes, token);
        }, context.HttpContext.RequestAborted);
        if (!disclosed)
            await new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Allegation unavailable",
                Detail = "The requested allegation is unavailable."
            })
            { StatusCode = StatusCodes.Status404NotFound }.ExecuteResultAsync(context);
    }
}
