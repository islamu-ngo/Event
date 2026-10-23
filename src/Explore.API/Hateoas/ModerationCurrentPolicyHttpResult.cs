using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Hateoas;

/// <summary>
/// HAL and serialization finish privately. The final original native lease check precedes the
/// actual body write, not an MVC formatter that could execute after its fences were released.
/// </summary>
public sealed class ModerationCurrentPolicyHttpResult(
    ModerationCurrentPolicyReadResult pending,
    IResourceAssembler<ModerationCurrentPolicyView, ModerationCurrentPolicyView> assembler,
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
            async (view, _) =>
            {
                var resource = await assembler.ToResource(view, context.HttpContext);
                return JsonSerializer.SerializeToUtf8Bytes(resource, json.Value.JsonSerializerOptions);
            },
            async (bytes, token) =>
            {
                using var abort = token.Register(static state => ((HttpContext)state!).Abort(), context.HttpContext);
                token.ThrowIfCancellationRequested();
                response.ContentType = HateoasConstants.HalJsonMediaType;
                response.ContentLength = bytes.Length;
                response.StatusCode = StatusCodes.Status200OK;
                await response.Body.WriteAsync(bytes, token);
                token.ThrowIfCancellationRequested();
                await response.CompleteAsync();
                token.ThrowIfCancellationRequested();
            }, context.HttpContext.RequestAborted);
        if (outcome != ModerationCurrentPolicyReadOutcome.Allowed)
            await failure(outcome).ExecuteResultAsync(context);
    }
}
