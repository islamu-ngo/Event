using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Explore.API.Middleware;

/// <summary>
/// Processes the <c>Idempotency-Key</c> header on write operations (POST, PUT, PATCH, DELETE).
/// When a key is provided, the middleware checks for an existing cached response and replays it.
/// When no cached response exists, the response is captured and persisted for future replay.
/// GET and HEAD requests are always passed through without processing.
/// The header is opt-in unless endpoint metadata requires it for a specific action.
/// </summary>
public sealed class IdempotencyMiddleware
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string ReplayHeader = "X-Idempotency-Replay";
    private const string ProtectedReplayPrefix = "dp:v1:";
    private const int MaxKeyLength = 128;
    private const int MaxStoredResponseBodyBytes = 1024 * 1024;
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromHours(24);

    private static readonly HashSet<string> WriteMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    private readonly RequestDelegate _next;
    private readonly RecyclableMemoryStreamManager _streamManager;
    private readonly ILogger<IdempotencyMiddleware> _logger;
    private readonly IDataProtector _replayProtector;
    public IdempotencyMiddleware(
        RequestDelegate next,
        RecyclableMemoryStreamManager streamManager,
        ILogger<IdempotencyMiddleware> logger,
        IDataProtectionProvider dataProtectionProvider)
    {
        _next = next;
        _streamManager = streamManager;
        _logger = logger;
        _replayProtector = dataProtectionProvider.CreateProtector(
            "Explore.API.IdempotencyReplay",
            "v1");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip non-write methods entirely
        if (!WriteMethods.Contains(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (context.GetEndpoint()?.Metadata
                .GetMetadata<SuppressIdempotencyResponseStorageAttribute>() is not null)
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName
                == RouteNames.CreateAnonymousRegistrationChallenge)
            {
                // MVC must leave the original issuance bytes available for intended-start normalization.
                context.Request.EnableBuffering();
            }
            await _next(context);
            return;
        }

        var requiresIdempotencyKey = context.GetEndpoint()?.Metadata
            .GetMetadata<RequireIdempotencyKeyAttribute>() is not null;
        var hasIdempotencyKey = context.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var keyValues)
            && !string.IsNullOrEmpty(keyValues.FirstOrDefault());

        if (!hasIdempotencyKey)
        {
            if (requiresIdempotencyKey)
            {
                await WriteBadRequestAsync(context, "Idempotency-Key is required.");
                return;
            }

            await _next(context);
            return;
        }

        var key = keyValues.FirstOrDefault()!;

        if (requiresIdempotencyKey && string.IsNullOrWhiteSpace(key))
        {
            await WriteBadRequestAsync(context, "Idempotency-Key is required.");
            return;
        }

        // AI message sends persist run-level idempotency inside the Application handler.
        // Let that domain-specific record own replay/conflict semantics instead of
        // caching the HTTP response with a different request fingerprint.
        if (IsApplicationManagedAiMessageSend(context.Request)
            || IsShortLivedWebhookPortalAccess(context.Request))
        {
            await _next(context);
            return;
        }

        // Validate key: max length, no whitespace
        if (key.Length > MaxKeyLength || key.AsSpan().ContainsAny(" \t\r\n"))
        {
            await WriteBadRequestAsync(
                context,
                "Idempotency-Key must be at most 128 characters and contain no whitespace.");
            return;
        }

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        var repository = context.RequestServices.GetRequiredService<IIdempotencyRepository>();
        var replayProtection = context.GetEndpoint()?.Metadata
            .GetMetadata<ProtectIdempotencyReplayAttribute>();
        if (replayProtection is not null)
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "private, no-store";
                return Task.CompletedTask;
            });
        }
        var revalidateReplay = context.GetEndpoint()?.Metadata
            .GetMetadata<RevalidateIdempotencyReplayAttribute>() is not null;
        var tenantId = tenantContext.TenantId;
        var requestIdentity = await IdempotencyRequestIdentityFactory.CreateAsync(
            context,
            _streamManager,
            context.RequestAborted);

        AnonymousRegistrationChallengeAuthority? challenge = null;
        GuestRegistrationOrderStartDto? committedGuest = null;
        bool requiresChallenge = context.GetEndpoint()?.Metadata
            .GetMetadata<RequireAnonymousRegistrationChallengeAttribute>() is not null;
        if (requiresChallenge)
        {
            challenge = await AnonymousRegistrationChallengeBoundary.AuthenticateAsync(context, requestIdentity, key);
            if (challenge is null)
            {
                return;
            }

            committedGuest = await AnonymousRegistrationChallengeBoundary.RecoverAsync(context, challenge);
            if (committedGuest is null && !challenge.IsFresh(
                    context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()))
            {
                await AnonymousRegistrationChallengeBoundary.RejectAsync(context);
                return;
            }
        }

        var now = requiresChallenge
            ? context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime
            : DateTime.UtcNow;
        var record = new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            Key = key,
            TenantId = tenantId,
            UserId = requestIdentity.UserId,
            RequestMethod = requestIdentity.Method,
            RequestTarget = requestIdentity.RequestTarget,
            RequestContentType = requestIdentity.ContentType,
            RequestBodyHash = requestIdentity.BodyHash,
            PrincipalFingerprint = requestIdentity.PrincipalFingerprint,
            StatusCode = IdempotencyRecord.InProgressStatusCode,
            CreatedAt = now,
            ExpiresAt = challenge is not null && challenge.RecoverUntil.UtcDateTime < now.Add(DefaultExpiration)
                ? challenge.RecoverUntil.UtcDateTime : now.Add(DefaultExpiration)
        };

        IdempotencyClaim claim;
        try
        {
            claim = await repository.TryClaimAsync(record, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogError("Unable to claim idempotency request state.");
            await WritePersistenceFailureAsync(context);
            return;
        }

        using var claimOwner = claim.Owner;
        if (claim.Lookup.Outcome == IdempotencyLookupOutcome.RequestMismatch)
        {
            await WriteKeyReuseConflictAsync(context);
            return;
        }
        record = claim.Record ?? throw new InvalidOperationException("A receipt acquisition must retain its result.");
        if (!claim.IsOwner)
        {
            if (!MatchesRequestIdentity(record, requestIdentity))
            {
                await WriteKeyReuseConflictAsync(context);
                return;
            }

            if (record.StatusCode is IdempotencyRecord.InProgressStatusCode
                or IdempotencyRecord.ResponseUnavailableStatusCode
                || record.ExpiresAt <= (requiresChallenge
                    ? context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime
                    : DateTime.UtcNow))
            {
                // A committed exact order is independent of HTTP response-store completion.
                // This read-only branch never acquires or releases the still-live claim.
                if (challenge is not null && committedGuest is not null)
                {
                    if (await RejectExpiredRecoveryAsync(context, challenge))
                    {
                        return;
                    }

                    await AnonymousRegistrationChallengeBoundary.WriteRecoveryAsync(context, challenge.EventId, committedGuest);
                    return;
                }

                if (record.StatusCode != IdempotencyRecord.InProgressStatusCode)
                {
                    await WriteResponseUnavailableAsync(context);
                }
                else
                {
                    await WriteInProgressConflictAsync(context);
                }
                return;
            }

            if (challenge is not null && record.StatusCode is >= 200 and < 300 && committedGuest is null)
            {
                await WriteKeyReuseConflictAsync(context);
                return;
            }

            if (revalidateReplay)
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = record.StatusCode;
            context.Response.Headers[ReplayHeader] = "true";

            if (!string.IsNullOrEmpty(record.ContentType))
            {
                context.Response.ContentType = record.ContentType;
            }

            string? replayBody = record.ResponseBody;
            if (replayProtection is not null)
            {
                if (!TryUnprotectReplay(record, replayBody, out ProtectedReplayEnvelope? replay))
                {
                    await WritePersistenceFailureAsync(context);
                    return;
                }

                if (challenge is not null && record.StatusCode is >= 200 and < 300
                    && (!replay.Headers.TryGetValue("X-Registration-Order-Capability", out string? capability)
                        || !string.Equals(capability, challenge.GuestCapabilityToken, StringComparison.Ordinal)))
                {
                    context.Response.Clear();
                    await WriteKeyReuseConflictAsync(context);
                    return;
                }

                replayBody = replay.Body;
                foreach (string headerName in replayProtection.ResponseHeaders)
                {
                    if (replay.Headers.TryGetValue(headerName, out string? headerValue))
                    {
                        context.Response.Headers[headerName] = headerValue;
                    }
                }
            }
            else if (replayBody?.StartsWith(ProtectedReplayPrefix, StringComparison.Ordinal) == true)
            {
                await WritePersistenceFailureAsync(context);
                return;
            }

            if (await RejectExpiredRecoveryAsync(context, challenge))
            {
                return;
            }

            if (record.ExpiresAt <= (requiresChallenge
                ? context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime
                : DateTime.UtcNow))
            {
                context.Response.Clear();
                await WriteResponseUnavailableAsync(context);
                return;
            }

            if (!string.IsNullOrEmpty(replayBody)
                && context.Response.StatusCode is not (StatusCodes.Status204NoContent or StatusCodes.Status304NotModified))
            {
                await context.Response.WriteAsync(replayBody, context.RequestAborted);
            }

            return;
        }

        var owner = claimOwner ?? throw new InvalidOperationException("The original receipt acquisition is required.");
        var originalBodyStream = context.Response.Body;
        var produces = context.GetEndpoint()?.Metadata.GetMetadata<ProducesAttribute>();
        bool declaredIneligible = produces?.ContentTypes.Contains("text/event-stream", StringComparer.OrdinalIgnoreCase) is true
            || produces is { ContentTypes.Count: > 0 } && !produces.ContentTypes.Any(IsReplayContentType);
        using var capture = new BoundedResponseCaptureStream(
            originalBodyStream,
            MaxStoredResponseBodyBytes,
            () => !declaredIneligible && !context.Response.HasStarted
                && context.Response.ContentLength is not > MaxStoredResponseBodyBytes
                && ShouldPersistResponse(context.Response, 0),
            () =>
            {
                if (challenge is not null
                    && context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow() >= challenge.RecoverUntil)
                {
                    if (!context.Response.HasStarted)
                    {
                        context.Response.Clear();
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        context.Response.Headers.CacheControl = "private, no-store";
                    }
                    return true;
                }
                return context.Response.StatusCode is StatusCodes.Status204NoContent or StatusCodes.Status304NotModified;
            },
            // Recovery capabilities must remain private until receipt completion succeeds.
            deferFlush: challenge is not null);
        // Declared streams must keep their native body/flush/start semantics.
        if (!declaredIneligible)
        {
            context.Response.Body = capture;
        }

        try
        {
            await _next(context);
            context.Response.Body = originalBodyStream;
            if (!declaredIneligible && capture.IsCaptured
                && !context.Response.HasStarted
                && ShouldPersistResponse(context.Response, capture.Length)
                && context.Response.ContentLength is not > MaxStoredResponseBodyBytes)
            {
                string? responseBody = capture.Length == 0
                    || context.Response.StatusCode is StatusCodes.Status204NoContent or StatusCodes.Status304NotModified
                    ? null : Encoding.UTF8.GetString(capture.CapturedBody.Span);
                string? persistedResponseBody = replayProtection is null
                    ? responseBody
                    : ProtectReplay(record, responseBody, context.Response, replayProtection);
                bool completed;
                try
                {
                    completed = await repository.CompleteAsync(
                        owner,
                        context.Response.StatusCode,
                        persistedResponseBody,
                        context.Response.ContentType,
                        context.RequestAborted);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError("Unable to complete idempotency request state.");
                    await RetainUnavailableAsync(repository, owner);
                    context.Response.Body = originalBodyStream;
                    await WritePersistenceFailureAsync(context);
                    return;
                }
                if (!completed)
                {
                    await RetainUnavailableAsync(repository, owner);
                    context.Response.Body = originalBodyStream;
                    await WritePersistenceFailureAsync(context);
                    return;
                }
            }
            else
            {
                await RetainUnavailableAsync(repository, owner);
            }

            if (!context.Response.HasStarted && await RejectExpiredRecoveryAsync(context, challenge))
            {
                return;
            }

            await capture.ForwardPrefixAsync(context.RequestAborted);
        }
        catch
        {
            // Even if this update fails, the original status-zero claim is a durable
            // fence. A disconnected client cannot turn transport failure into rollback.
            await RetainUnavailableAsync(repository, owner);
            throw;
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private static async Task<bool> RejectExpiredRecoveryAsync(
        HttpContext context, AnonymousRegistrationChallengeAuthority? challenge)
    {
        // Reads, unprotection and response persistence must not outlive the original disclosure authority.
        if (challenge is null || context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow() < challenge.RecoverUntil)
        {
            return false;
        }

        context.Response.Clear();
        await AnonymousRegistrationChallengeBoundary.RejectAsync(context);
        return true;
    }

    private string ProtectReplay(
        IdempotencyRecord record,
        string? responseBody,
        HttpResponse response,
        ProtectIdempotencyReplayAttribute metadata)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string headerName in metadata.ResponseHeaders)
        {
            if (response.Headers.TryGetValue(headerName, out var headerValue))
            {
                headers[headerName] = headerValue.ToString();
            }
        }

        var envelope = new ProtectedReplayEnvelope(1, responseBody, headers);
        return ProtectedReplayPrefix + _replayProtector.CreateProtector(record.Id.ToString("D"))
            .Protect(JsonSerializer.Serialize(envelope));
    }

    private bool TryUnprotectReplay(IdempotencyRecord record, string? storedBody, out ProtectedReplayEnvelope replay)
    {
        replay = null!;
        if (storedBody?.StartsWith(ProtectedReplayPrefix, StringComparison.Ordinal) != true)
        {
            return false;
        }

        try
        {
            replay = JsonSerializer.Deserialize<ProtectedReplayEnvelope>(
                _replayProtector.CreateProtector(record.Id.ToString("D"))
                    .Unprotect(storedBody[ProtectedReplayPrefix.Length..]))!;
            return replay is { Version: 1, Headers: not null };
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record ProtectedReplayEnvelope(
        int Version,
        string? Body,
        Dictionary<string, string> Headers);

    private static bool MatchesRequestIdentity(
        IdempotencyRecord record,
        IdempotencyRequestIdentity requestIdentity)
    {
        return string.Equals(record.RequestMethod, requestIdentity.Method, StringComparison.Ordinal)
               && string.Equals(record.RequestTarget, requestIdentity.RequestTarget, StringComparison.Ordinal)
               && string.Equals(record.RequestContentType, requestIdentity.ContentType, StringComparison.Ordinal)
               && string.Equals(record.RequestBodyHash, requestIdentity.BodyHash, StringComparison.Ordinal)
               && string.Equals(record.PrincipalFingerprint, requestIdentity.PrincipalFingerprint, StringComparison.Ordinal);
    }

    private static bool IsApplicationManagedAiMessageSend(HttpRequest request)
    {
        return HttpMethods.IsPost(request.Method)
            && request.Path.StartsWithSegments("/api/ai/assistant/conversations", StringComparison.OrdinalIgnoreCase)
            && request.Path.Value?.EndsWith("/messages", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsShortLivedWebhookPortalAccess(HttpRequest request)
    {
        return HttpMethods.IsPost(request.Method)
            && request.Path.Equals("/api/webhooks/svix/app-portal", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteKeyReuseConflictAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            Title = "Conflict",
            Status = StatusCodes.Status409Conflict,
            Detail = "Idempotency-Key has already been used with a different request.",
            Instance = context.Request.Path
        };
        problemDetails.Extensions["code"] = "idempotency_key_reuse";

        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problemDetails
        });
    }

    private static async Task WriteInProgressConflictAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            Title = "Conflict",
            Status = StatusCodes.Status409Conflict,
            Detail = "An identical request with this Idempotency-Key is still in progress.",
            Instance = context.Request.Path
        };
        problemDetails.Extensions["code"] = "idempotency_request_in_progress";

        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problemDetails
        });
    }

    private static async Task WritePersistenceFailureAsync(HttpContext context)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        var problemDetails = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.4",
            Title = "Service Unavailable",
            Status = StatusCodes.Status503ServiceUnavailable,
            Detail = "The request could not be processed safely.",
            Instance = context.Request.Path
        };
        problemDetails.Extensions["code"] = "idempotency_unavailable";

        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problemDetails
        });
    }

    private async Task RetainUnavailableAsync(IIdempotencyRepository repository, IIdempotencyClaimOwner owner)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await repository.CompleteAsync(owner,
                IdempotencyRecord.ResponseUnavailableStatusCode, null, null, deadline.Token);
        }
        catch (Exception)
        {
            _logger.LogError("Unable to mark idempotency response unavailable; the original claim remains fenced.");
        }
    }

    private static async Task WriteResponseUnavailableAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        context.Response.Headers.CacheControl = "private, no-store";
        await context.RequestServices.GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Operation outcome requires reconciliation",
                    Detail = "This operation may have completed, but its response is unavailable. Reconcile current state before submitting another operation.",
                    Extensions = { ["code"] = "idempotency_response_unavailable" }
                }
            });
    }

    private static async Task WriteBadRequestAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                Title = "Bad Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = detail,
                Instance = context.Request.Path
            }
        });
    }

    private static bool ShouldPersistResponse(HttpResponse response, long responseBodyLength)
    {
        if (response.StatusCode < StatusCodes.Status200OK || response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return false;
        }

        if (response.StatusCode is StatusCodes.Status400BadRequest or StatusCodes.Status415UnsupportedMediaType)
        {
            return false;
        }

        if (responseBodyLength > MaxStoredResponseBodyBytes)
        {
            return false;
        }

        return IsReplayContentType(response.ContentType);
    }

    private static bool IsReplayContentType(string? contentType)
    {
        string? mediaType = contentType?.Split(';', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(mediaType)
            || mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/problem+json", StringComparison.OrdinalIgnoreCase);
    }
}
