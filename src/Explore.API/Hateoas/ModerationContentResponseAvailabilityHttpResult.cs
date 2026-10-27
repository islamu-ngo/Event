using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Hateoas;

public sealed class ModerationContentResponseAvailabilityHttpResult(
    ModerationContentResponseAvailabilityResult pending,
    IResourceAssembler<ModerationContentResponseAvailability, ModerationContentResponseAvailability> assembler,
    IOptions<JsonOptions> json, Func<ModerationQualificationWriteOutcome, ActionResult> failure) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "private, no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        var outcome = await pending.DiscloseAsync(async (availability, _) =>
        {
            var resource = await assembler.ToResource(availability, context.HttpContext);
            return JsonSerializer.SerializeToUtf8Bytes(resource, json.Value.JsonSerializerOptions);
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
