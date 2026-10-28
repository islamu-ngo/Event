using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.Moderation.Notices;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Hateoas;

/// <summary>
/// The actual HTTP output seam. HAL assembly and JSON serialization finish privately before
/// the final native qualification. No MVC formatter can start later with an unqualified DTO.
/// </summary>
public sealed class ModerationNoticeResult(
    ModerationNoticeReadResult pending,
    IResourceAssembler<ModerationNoticeDto, ModerationNoticeDto> assembler,
    IOptions<JsonOptions> json, LinkGenerator routes, bool detail, int limit, object? routeValues,
    Func<ModerationNoticeReadOutcome, ActionResult> failure) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        await using var projection = pending.Projection;
        if (pending.Outcome != ModerationNoticeReadOutcome.Allowed || projection is null)
        {
            await failure(pending.Outcome).ExecuteResultAsync(context);
            return;
        }

        var outcome = await projection.DiscloseAsync(
            async (page, _) =>
            {
                if (detail)
                {
                    var resource = await assembler.ToResource(page.Items.Single(), context.HttpContext);
                    return JsonSerializer.SerializeToUtf8Bytes(resource, json.Value.JsonSerializerOptions);
                }
                var collection = await assembler.ToCollectionResource(page.Items,
                    RouteNames.GetModerationNotices, routeValues, context.HttpContext);
                if (!page.Items.IsEmpty && routes.GetPathByName(context.HttpContext, RouteNames.GetModerationNotices,
                    new { afterId = page.Items[^1].Id, limit }) is { } next)
                    collection.Links[LinkRelations.Next] = HalLink.Create(next);
                return JsonSerializer.SerializeToUtf8Bytes(collection, json.Value.JsonSerializerOptions);
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
