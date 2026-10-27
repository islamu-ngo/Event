using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Hateoas;

public sealed class ModerationContentResponseStatementHttpResult(
    ModerationContentResponseStatementResult pending,
    Func<ModerationQualificationWriteOutcome, ActionResult> failure) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "private, no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.XContentTypeOptions = "nosniff";
        var outcome = await pending.DiscloseAsync(async (bytes, token) =>
        {
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength = bytes.Length;
            response.StatusCode = StatusCodes.Status200OK;
            await response.Body.WriteAsync(bytes, token);
        }, context.HttpContext.RequestAborted);
        if (outcome != ModerationQualificationWriteOutcome.Known)
            await failure(outcome).ExecuteResultAsync(context);
    }
}
