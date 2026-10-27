using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.Moderation.Notices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Hateoas;

/// <summary>HAL and JSON stay private until final native requalification, including late and purged status.</summary>
public sealed class ModerationNoticeStatusHttpResult(
    ModerationNoticeStatusResult pending,
    IResourceAssembler<ModerationNoticeStatusDto, ModerationNoticeStatusDto> assembler,
    IOptions<JsonOptions> json, Func<ModerationNoticeReadOutcome, ActionResult> failure) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "private, no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        await using var projection = pending.Projection;
        if (pending.Outcome != ModerationNoticeReadOutcome.Allowed || projection is null)
        {
            await failure(pending.Outcome).ExecuteResultAsync(context);
            return;
        }
        var outcome = await projection.DiscloseAsync(
            async (dto, _) =>
            {
                var resource = await assembler.ToResource(dto, context.HttpContext);
                return JsonSerializer.SerializeToUtf8Bytes(resource, json.Value.JsonSerializerOptions);
            },
            async (bytes, token) =>
            {
                response.ContentType = HateoasConstants.HalJsonMediaType;
                response.ContentLength = bytes.Length;
                response.StatusCode = StatusCodes.Status200OK;
                await response.Body.WriteAsync(bytes, token);
            }, context.HttpContext.RequestAborted);
        if (outcome != ModerationNoticeReadOutcome.Allowed)
            await failure(outcome).ExecuteResultAsync(context);
    }
}
