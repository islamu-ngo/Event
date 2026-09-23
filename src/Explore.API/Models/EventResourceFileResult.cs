using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Models;

/// <summary>Completes the native lease at result execution, after asynchronous action/result preparation.</summary>
public sealed class EventResourceFileResult(
    EventResourceAuthorityResult pending,
    IQueryHandler<CompleteEventResourceContentQuery, EventResourceHeaderResult> completion,
    Func<EventResourceAuthorityOutcome, IActionResult> failure) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        await using (pending)
        {
            if (pending.Lease is not { } lease)
            {
                await failure(pending.Outcome).ExecuteResultAsync(context);
                return;
            }
            var completed = await completion.QueryAsync(new(lease), context.HttpContext.RequestAborted);
            await using var preparation = completed.Preparation;
            if (completed.Outcome != EventResourceAuthorityOutcome.Allowed ||
                preparation is not EventResourcePreparedContent prepared)
            {
                await failure(completed.Outcome).ExecuteResultAsync(context);
                return;
            }
            var content = prepared.TakeContent();
            await using var stream = content.Content;
            context.HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.HttpContext.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
            await new FileStreamResult(stream, content.ContentType)
            {
                FileDownloadName = content.SafeDisplayName,
                EnableRangeProcessing = false
            }.ExecuteResultAsync(context);
        }
    }
}
