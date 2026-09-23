using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Models;

/// <summary>Transfers a protected destination only after the final single-use authority gate.</summary>
public sealed class EventResourceRedirectResult(
    EventResourceAuthorityResult pending,
    IQueryHandler<GetEventResourceAccessHeadersQuery, EventResourceHeaderResult> completion,
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
            if (completed.Outcome != EventResourceAuthorityOutcome.Allowed
                || preparation is not EventResourcePreparedDestination prepared)
            {
                await failure(completed.Outcome).ExecuteResultAsync(context);
                return;
            }
            var response = context.HttpContext.Response;
            response.Headers.CacheControl = "no-store";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers.Location = prepared.TakeDestination();
            response.StatusCode = StatusCodes.Status302Found;
        }
    }
}
