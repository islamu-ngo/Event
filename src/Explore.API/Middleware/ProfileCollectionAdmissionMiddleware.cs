using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;
using Explore.API.Attributes;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Middleware;

/// <summary>Holds native admission across MVC consumption and commit, before returning buffered output.</summary>
public sealed class ProfileCollectionAdmissionMiddleware(RequestDelegate next)
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> NominationOptions = new();

    public async Task InvokeAsync(HttpContext context, IModerationProfileCollectionAdmissionOwner owner,
        IOptions<JsonOptions> json)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ProfileCollectionAdmissionAttribute>() is null)
        { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.User.Identity?.IsAuthenticated != true)
        { await RejectAsync(context, 401); return; }
        var header = context.Request.Headers[ProfileCollectionAdmissionAttribute.HeaderName];
        if (header.Count != 1 || header[0] is not { } value || Encoding.UTF8.GetByteCount(value) > 8192)
        { await RejectAsync(context, 400); return; }
        Nomination? nomination;
        try
        {
            var options = NominationOptions.GetValue(json.Value.JsonSerializerOptions, static source => new(source)
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 });
            nomination = JsonSerializer.Deserialize<Nomination>(value, options);
        }
        catch (JsonException) { await RejectAsync(context, 400); return; }
        if (nomination?.Assessment?.Original is null)
        { await RejectAsync(context, 400); return; }

        var originalCancellation = context.RequestAborted;
        using var buffer = new ProfileCollectionResponseBuffer(context);
        bool accepted;
        try
        {
            accepted = await owner.ExecuteAsync(nomination.Assessment, nomination.ConsentId, async (admission, token) =>
            {
                if (!Guid.TryParse(context.Request.RouteValues["id"]?.ToString(), out var subject)
                    || subject != admission.SubjectId)
                    throw new AuthorizationException("Only the admitted subject may update this profile.");
                context.RequestAborted = token;
                await next(context);
                token.ThrowIfCancellationRequested();
                await buffer.SealAsync(token);
                if (context.Response.StatusCode >= 400) throw new RejectedResponse();
            }, originalCancellation);
        }
        catch (RejectedResponse) { accepted = true; }
        finally
        {
            context.RequestAborted = originalCancellation;
        }
        if (!accepted)
        { buffer.Dispose(); await RejectAsync(context, 503); return; }
        originalCancellation.ThrowIfCancellationRequested();
        await buffer.DeliverAsync(originalCancellation);
    }

    private static Task RejectAsync(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        context.Response.ContentLength = null;
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = status == 401 ? "Authentication required" : status == 400
                ? "A valid profile collection nomination is required" : "Profile collection is unavailable"
        }, cancellationToken: context.RequestAborted);
    }

    private sealed record Nomination(ModerationProfileSubjectConsentAssessment Assessment, Guid? ConsentId)
    {
        public override string ToString() => nameof(Nomination);
    }
    private sealed class RejectedResponse : Exception;
}
