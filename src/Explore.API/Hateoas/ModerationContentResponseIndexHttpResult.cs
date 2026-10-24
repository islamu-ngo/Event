using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Hateoas;

public sealed class ModerationContentResponseIndexHttpResult(
    ModerationContentResponseIndexResult pending,
    IResourceAssembler<ModerationContentResponseSummary, ModerationContentResponseSummary> assembler,
    IOptions<JsonOptions> json, LinkGenerator routes, Guid caseId, Guid? afterId, int limit,
    Func<ModerationQualificationWriteOutcome, ActionResult> failure) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "private, no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        var outcome = await pending.DiscloseAsync(async (items, _) =>
        {
            var collection = await assembler.ToCollectionResource(items, RouteNames.GetModerationContentResponses,
                new { caseId, afterId, limit }, context.HttpContext);
            if (items.Count == limit && routes.GetPathByName(context.HttpContext, RouteNames.GetModerationContentResponses,
                new { caseId, afterId = items[^1].Id, limit }) is { } next)
                collection.Links[LinkRelations.Next] = HalLink.Create(next);
            return JsonSerializer.SerializeToUtf8Bytes(collection, json.Value.JsonSerializerOptions);
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
