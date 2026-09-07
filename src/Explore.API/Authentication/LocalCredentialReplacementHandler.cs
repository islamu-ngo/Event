// ABOUTME: Defines the isolated bearer scheme for short-lived Local credential replacement challenges.
// ABOUTME: Fails closed with a private bounded problem response and never exposes native token-validation details.

using System.Text.Encodings.Web;
using System.Net.Http.Headers;
using System.Text.Json;
using Explore.API.ExceptionHandling;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Explore.API.Authentication;

public sealed class LocalCredentialReplacementHandler(
    IOptionsMonitor<JwtBearerOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : JwtBearerHandler(options, logger, encoder)
{
    private const int MaximumTokenLength = 8_192;
    private static readonly HashSet<string> HeaderProperties = new(StringComparer.Ordinal) { "alg", "typ" };
    private static readonly HashSet<string> PayloadProperties = new(StringComparer.Ordinal)
    {
        "iss", "aud", "sub", "jti", "iat", "nbf", "exp",
        LocalCredentialChallengeToken.PurposeClaim,
        LocalCredentialChallengeToken.OperationIdClaim,
        LocalCredentialChallengeToken.SecurityStampClaim
    };

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }
        if (Request.Headers.Authorization.Count != 1
            || !AuthenticationHeaderValue.TryParse(Request.Headers.Authorization[0], out AuthenticationHeaderValue? authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter)
            || authorization.Parameter.Length > MaximumTokenLength)
        {
            return InvalidChallenge();
        }

        AuthenticateResult validated = await base.HandleAuthenticateAsync();
        if (!validated.Succeeded || validated.Principal?.TryGetLocalCredentialReplacementAuthority() is null
            || !HasStrictEnvelope(authorization.Parameter))
        {
            return InvalidChallenge();
        }
        return validated;
    }

    private static bool HasStrictEnvelope(string token)
    {
        string[] segments = token.Split('.');
        if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty))
        {
            return false;
        }
        try
        {
            using JsonDocument header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segments[0]));
            using JsonDocument payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segments[1]));
            if (!HasExactProperties(header.RootElement, HeaderProperties)
                || !HasExactProperties(payload.RootElement, PayloadProperties))
            {
                return false;
            }
            foreach (JsonProperty property in header.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
            }
            if (header.RootElement.GetProperty("alg").GetString() != LocalCredentialChallengeToken.RequiredAlgorithm
                || header.RootElement.GetProperty("typ").GetString() != LocalCredentialChallengeToken.TokenType)
            {
                return false;
            }
            foreach (JsonProperty property in payload.RootElement.EnumerateObject())
            {
                if (property.Name is "iat" or "nbf" or "exp")
                {
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt64(out long value) || value < 0)
                    {
                        return false;
                    }
                }
                else if (property.Name == "aud")
                {
                    JsonElement audience = property.Value;
                    if (audience.ValueKind == JsonValueKind.Array)
                    {
                        if (audience.GetArrayLength() != 1)
                        {
                            return false;
                        }
                        audience = audience[0];
                    }
                    if (audience.ValueKind != JsonValueKind.String || audience.GetString() != LocalCredentialChallengeToken.Audience)
                    {
                        return false;
                    }
                }
                else if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }
            }
            long issuedAt = payload.RootElement.GetProperty("iat").GetInt64();
            long notBefore = payload.RootElement.GetProperty("nbf").GetInt64();
            long expiresAt = payload.RootElement.GetProperty("exp").GetInt64();
            return payload.RootElement.GetProperty("iss").GetString() == LocalIdentityOptions.Issuer
                && payload.RootElement.GetProperty(LocalCredentialChallengeToken.PurposeClaim).GetString() == LocalCredentialChallengeToken.Purpose
                && issuedAt == notBefore && expiresAt > issuedAt
                && expiresAt - issuedAt <= (long)LocalCredentialChallengeToken.MaximumLifetime.TotalSeconds;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static bool HasExactProperties(JsonElement element, HashSet<string> required)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!required.Contains(property.Name) || !seen.Add(property.Name))
            {
                return false;
            }
        }
        return seen.Count == required.Count;
    }

    private static AuthenticateResult InvalidChallenge() => AuthenticateResult.Fail("Invalid replacement challenge.");

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var problem = ApiProblemFactory.CreateAuthenticationRequiredProblem(
            httpContext: Context,
            title: "Replacement challenge required",
            detail: "A current replacement challenge is required.");
        await Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: Context.RequestAborted);
    }
}
