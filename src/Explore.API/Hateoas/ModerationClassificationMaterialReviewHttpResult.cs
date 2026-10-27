using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Hateoas;

/// <summary>Private HAL preparation precedes original reviewer/proposal revalidation, retaining native fences through actual body delivery.</summary>
public sealed class ModerationClassificationMaterialReviewHttpResult(
    ModerationClassificationMaterialReviewReadResult pending,
    IResourceAssembler<ModerationClassificationMaterialReviewView, ModerationClassificationMaterialReviewView> assembler,
    IOptions<JsonOptions> json, Func<ModerationCurrentPolicyReadOutcome, ActionResult> failure) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        await using var projection = pending.Projection;
        if (pending.Outcome != ModerationCurrentPolicyReadOutcome.Allowed || projection is null)
        {
            await failure(pending.Outcome).ExecuteResultAsync(context);
            return;
        }
        var outcome = await projection.DiscloseAsync(
            async (view, _) => JsonSerializer.SerializeToUtf8Bytes(
                await assembler.ToResource(view, context.HttpContext), json.Value.JsonSerializerOptions),
            async (bytes, token) =>
            {
                response.ContentType = HateoasConstants.HalJsonMediaType;
                response.ContentLength = bytes.Length;
                response.StatusCode = StatusCodes.Status200OK;
                await response.Body.WriteAsync(bytes, token);
            }, context.HttpContext.RequestAborted);
        if (outcome != ModerationCurrentPolicyReadOutcome.Allowed)
            await failure(outcome).ExecuteResultAsync(context);
    }
}
