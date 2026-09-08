// ABOUTME: Authenticates bounded anonymous proof transport before idempotency state can disclose authority.
// ABOUTME: Recovers only an exact committed allocation without claiming or invoking a competing starter.

using System.Text.Json;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Commands;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Middleware;

internal static class AnonymousRegistrationChallengeBoundary
{
    public const string ChallengeHeader = "X-Registration-Challenge";
    public const string ProofHeader = "X-Registration-Proof";
    private static readonly object AuthorityKey = new();
    private static readonly object IntendedRequestKey = new();

    public static AnonymousRegistrationChallengeAuthority? GetAuthority(HttpContext context) =>
        context.Items.TryGetValue(AuthorityKey, out object? authority)
            ? authority as AnonymousRegistrationChallengeAuthority : null;

    public static async Task<AnonymousRegistrationChallengeAuthority?> AuthenticateAsync(
        HttpContext context, IdempotencyRequestIdentity identity, string key)
    {
        if (!Guid.TryParse(Convert.ToString(context.Request.RouteValues["eventId"], System.Globalization.CultureInfo.InvariantCulture), out Guid eventId)
            || !TryReadHeader(context, ChallengeHeader, 4096, out string? challenge)
            || !TryReadHeader(context, ProofHeader, 16, out string? proof)
            || context.Request.Headers["Idempotency-Key"].Count != 1
            || key.Any(character => character is < '!' or > '~'))
        {
            await RejectAsync(context);
            return null;
        }

        StartRegistrationOrderRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<StartRegistrationOrderRequest>(context.Request.Body,
                context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions,
                context.RequestAborted);
        }
        catch (JsonException)
        {
            await RejectAsync(context);
            return null;
        }
        finally
        {
            context.Request.Body.Position = 0;
        }
        if (body is null || body.Lines is null)
        {
            await RejectAsync(context);
            return null;
        }
        var intended = new StartGuestRegistrationOrderCommand(eventId, body.TicketCatalogVersionId,
            body.BookingPartyType, body.Lines, body.PlatformContributionBasisPoints);
        Guid tenantId = context.RequestServices.GetRequiredService<ITenantContext>().TenantId;
        string digest = IdempotencyRequestIdentityFactory.ComputeGuestStartDigest(identity, tenantId, eventId, key);
        var authority = await context.RequestServices.GetRequiredService<IMediator>().Send(
            new ConsumeAnonymousRegistrationChallengeCommand(eventId, digest, key, challenge, proof, intended),
            context.RequestAborted);
        if (authority is null)
        {
            await RejectAsync(context);
            return null;
        }

        context.Items[AuthorityKey] = authority;
        context.Items[IntendedRequestKey] = intended with { ChallengeAuthority = authority };
        return authority;
    }

    public static async Task<GuestRegistrationOrderStartDto?> RecoverAsync(
        HttpContext context, AnonymousRegistrationChallengeAuthority authority)
    {
        var result = await context.RequestServices.GetRequiredService<IRegistrationOrderStarter>()
            .TryRecoverCommittedGuestAsync((StartGuestRegistrationOrderCommand)context.Items[IntendedRequestKey]!, context.RequestAborted);
        return result?.IsSuccess == true
            ? GuestRegistrationOrderStartDto.Success(result.Id, result.Message, authority.GuestCapabilityToken)
            : null;
    }

    public static async Task WriteRecoveryAsync(HttpContext context, Guid eventId, GuestRegistrationOrderStartDto result)
    {
        context.Response.StatusCode = StatusCodes.Status201Created;
        context.Response.Headers["X-Registration-Order-Capability"] = result.GuestCapabilityToken;
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Idempotency-Replay"] = "true";
        context.Response.Headers.Location = context.RequestServices.GetRequiredService<LinkGenerator>()
            .GetUriByName(context, RouteNames.GetGuestRegistrationOrder, new { eventId, orderId = result.Id });
        await context.Response.WriteAsJsonAsync(result,
            context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions,
            context.RequestAborted);
    }

    public static async Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.Headers.CacheControl = "private, no-store";
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid registration challenge",
                Detail = "A valid bound registration challenge and proof are required.",
                Extensions = { ["code"] = "anonymous_registration_challenge_invalid" }
            }
        });
    }

    private static bool TryReadHeader(HttpContext context, string name, int maximumLength, out string? value)
    {
        var values = context.Request.Headers[name];
        value = values.Count == 1 ? values[0] : null;
        return value is { Length: > 0 } && value.Length <= maximumLength;
    }
}
