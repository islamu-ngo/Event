using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Hateoas;

public sealed class ModerationPreFindingNoticeHttpResult(
    ModerationPreFindingNoticeResult pending,
    Func<ModerationPreFindingView, HalResource<ModerationPreFindingView>> assemble,
    IOptions<JsonOptions> json, Func<ModerationQualificationWriteOutcome, ActionResult> failure) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "private, no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        var outcome = await pending.DiscloseAsync((view, _) =>
        {
            var resource = assemble(view);
            return Task.FromResult<ReadOnlyMemory<byte>>(
                JsonSerializer.SerializeToUtf8Bytes(resource, json.Value.JsonSerializerOptions));
        }, async (bytes, token) =>
        {
            response.ContentType = HateoasConstants.HalJsonMediaType;
            response.ContentLength = bytes.Length;
            response.StatusCode = StatusCodes.Status200OK;
            await response.Body.WriteAsync(bytes, token);
        }, context.HttpContext.RequestAborted);
        if (outcome != ModerationQualificationWriteOutcome.Known)
            await failure(outcome).ExecuteResultAsync(context);
    }
}
