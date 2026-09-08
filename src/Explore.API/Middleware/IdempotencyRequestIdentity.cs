// ABOUTME: Computes stable request identity metadata for Idempotency-Key replay validation.
// ABOUTME: Canonicalizes JSON request bodies so equivalent formatting does not change fingerprints.

using System.Buffers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.API.Authentication;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Microsoft.AspNetCore.Routing;
using Microsoft.IO;
using Microsoft.Net.Http.Headers;

namespace Explore.API.Middleware;

internal sealed record IdempotencyRequestIdentity(
    string Method,
    string RequestTarget,
    string? ContentType,
    string BodyHash,
    string PrincipalFingerprint,
    string? UserId);

internal static class IdempotencyRequestIdentityFactory
{
    private static readonly string[] CapabilityHeaders =
    [
        "X-Registration-Order-Capability",
        "X-Registration-Attempt-Capability"
    ];

    public static async Task<IdempotencyRequestIdentity> CreateAsync(
        HttpContext context,
        RecyclableMemoryStreamManager streamManager,
        CancellationToken cancellationToken,
        bool preserveAmbiguousMemberOrder = false)
    {
        var method = context.Request.Method.ToUpperInvariant();
        var requestTarget = ResolveRequestTarget(context);
        var contentType = NormalizeContentType(context.Request.ContentType);
        ClaimsIdentity? identity = ResolveAuthenticatedIdentity(context.User);
        var userId = context.User.GetPlatformUserId()?.ToString("D");
        var principalFingerprint = ComputeSha256Hex(
            $"{ResolvePrincipalScope(context.User, identity, userId)}|capabilities:{CapabilityScope(context.Request)}");
        var bodyHash = await ComputeBodyHashAsync(context.Request, streamManager, cancellationToken,
            preserveAmbiguousMemberOrder || context.GetEndpoint()?.Metadata
                .GetMetadata<RequireAnonymousRegistrationChallengeAttribute>() is not null);

        return new IdempotencyRequestIdentity(
            method,
            requestTarget,
            contentType,
            bodyHash,
            principalFingerprint,
            userId);
    }

    internal static async Task<IdempotencyRequestIdentity> CreateIntendedGuestStartAsync(
        HttpContext context,
        Guid eventId,
        RecyclableMemoryStreamManager streamManager,
        CancellationToken cancellationToken)
    {
        var endpoints = context.RequestServices.GetRequiredService<EndpointDataSource>();
        RouteEndpoint start = endpoints.Endpoints.OfType<RouteEndpoint>().Single(endpoint =>
            endpoint.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName == RouteNames.StartGuestRegistrationOrder);
        var values = new RouteValueDictionary(start.RoutePattern.Defaults) { ["eventId"] = eventId.ToString("D") };
        string path = context.RequestServices.GetRequiredService<LinkGenerator>().GetPathByName(
            RouteNames.StartGuestRegistrationOrder,
            new { eventId },
            pathBase: context.Request.PathBase)
            ?? throw new InvalidOperationException("The guest start route is unavailable.");
        IdempotencyRequestIdentity identity = await CreateAsync(context, streamManager, cancellationToken,
            preserveAmbiguousMemberOrder: true);
        return identity with
        {
            Method = HttpMethods.Post,
            RequestTarget = ResolveRequestTarget(start.RoutePattern.RawText, path, values, context.Request.QueryString)
        };
    }

    internal static string ComputeGuestStartDigest(
        IdempotencyRequestIdentity identity, Guid tenantId, Guid eventId, string key) =>
        ComputeSha256Hex(JsonSerializer.Serialize(new
        {
            TenantId = tenantId,
            EventId = eventId,
            Key = key,
            Identity = identity
        })).ToUpperInvariant();

    private static async Task<string> ComputeBodyHashAsync(
        HttpRequest request,
        RecyclableMemoryStreamManager streamManager,
        CancellationToken cancellationToken,
        bool preserveAmbiguousMemberOrder)
    {
        request.EnableBuffering();

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        using var bodyStream = streamManager.GetStream("idempotency-request-body");
        await request.Body.CopyToAsync(bodyStream, cancellationToken);

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        if (bodyStream.Length == 0)
        {
            return ComputeSha256Hex(ReadOnlySpan<byte>.Empty);
        }

        bodyStream.Position = 0;
        if (IsJsonContentType(request.ContentType)
            && TryCanonicalizeJson(bodyStream, streamManager, preserveAmbiguousMemberOrder, out var canonicalJson))
        {
            using (canonicalJson)
            {
                return ComputeSha256Hex(canonicalJson.ToArray());
            }
        }

        bodyStream.Position = 0;
        return bodyStream.TryGetBuffer(out var buffer)
            ? ComputeSha256Hex(buffer.AsSpan(0, (int)bodyStream.Length))
            : ComputeSha256Hex(bodyStream.ToArray());
    }

    private static bool TryCanonicalizeJson(
        Stream jsonStream,
        RecyclableMemoryStreamManager streamManager,
        bool preserveAmbiguousMemberOrder,
        out MemoryStream canonicalJson)
    {
        canonicalJson = streamManager.GetStream("idempotency-canonical-json");

        try
        {
            using var document = JsonDocument.Parse(jsonStream);
            using var writer = new Utf8JsonWriter(canonicalJson);
            WriteCanonicalJson(document.RootElement, writer, preserveAmbiguousMemberOrder);
            writer.Flush();
            canonicalJson.Position = 0;
            return true;
        }
        catch (JsonException)
        {
            canonicalJson.Dispose();
            canonicalJson = new MemoryStream(0);
            return false;
        }
    }

    private static void WriteCanonicalJson(
        JsonElement element, Utf8JsonWriter writer, bool preserveAmbiguousMemberOrder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                // MVC binds case-insensitively and the last matching member wins. Sorting aliases
                // would erase a business-significant order change. Other endpoint semantics stay unchanged.
                IEnumerable<JsonProperty> properties = element.EnumerateObject();
                if (!preserveAmbiguousMemberOrder || !HasAmbiguousMembers(element))
                {
                    properties = properties.OrderBy(property => property.Name, StringComparer.Ordinal);
                }
                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(property.Value, writer, preserveAmbiguousMemberOrder);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(item, writer, preserveAmbiguousMemberOrder);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                element.WriteTo(writer);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool HasAmbiguousMembers(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                return true;
            }
        }
        return false;
    }

    private static string ResolveRequestTarget(HttpContext context)
    {
        var endpointPattern = context.GetEndpoint() is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText
            : null;
        return ResolveRequestTarget(endpointPattern,
            context.Request.PathBase.Add(context.Request.Path).ToUriComponent(),
            context.Request.RouteValues, context.Request.QueryString);
    }

    private static string ResolveRequestTarget(
        string? endpointPattern, string path, RouteValueDictionary values, QueryString query)
    {
        string actualPath = path.ToLowerInvariant();
        string routeValues = string.Join(
            '&',
            values
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{Uri.EscapeDataString(pair.Key.ToLowerInvariant())}={Uri.EscapeDataString(Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture)?.ToLowerInvariant() ?? string.Empty)}"));
        string target = $"{endpointPattern?.ToLowerInvariant() ?? actualPath}|path:{actualPath}|route:{routeValues}";
        return query.HasValue ? $"{target}{query.Value}" : target;
    }

    private static string CapabilityScope(HttpRequest request)
    {
        var canonical = new StringBuilder();
        foreach (string headerName in CapabilityHeaders)
        {
            string[] values = request.Headers[headerName]
                .Select(value => value ?? string.Empty)
                .Order(StringComparer.Ordinal)
                .ToArray();
            canonical.Append(headerName).Append(':').Append(values.Length).Append(':');
            foreach (string value in values)
            {
                canonical.Append(value.Length).Append(':').Append(value).Append('|');
            }
        }

        return ComputeSha256Hex(canonical.ToString());
    }

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        return MediaTypeHeaderValue.TryParse(contentType, out var parsed)
            ? parsed.ToString().ToLowerInvariant()
            : contentType.Trim().ToLowerInvariant();
    }

    private static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)
            || !MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            return false;
        }

        var mediaType = parsed.MediaType.Value ?? string.Empty;
        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
               || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static ClaimsIdentity? ResolveAuthenticatedIdentity(ClaimsPrincipal principal)
    {
        ClaimsIdentity[] identities = principal.Identities
            .Where(candidate => candidate.IsAuthenticated)
            .ToArray();
        return identities is [{ AuthenticationType: { Length: > 0 } } identity]
            ? identity
            : null;
    }

    private static string ResolvePrincipalScope(
        ClaimsPrincipal principal,
        ClaimsIdentity? identity,
        string? userId)
    {
        if (identity is null)
        {
            return principal.Identities.Any(candidate => candidate.IsAuthenticated)
                ? "ambiguous-authenticated-principal"
                : "anonymous";
        }

        string scheme = identity.AuthenticationType!;
        string? purposeIdentity = scheme switch
        {
            ApiAuthenticationSchemeNames.AdmissionScanner => ResolveGuidClaim(
                identity,
                AdmissionScannerAuthenticationDefaults.CapabilityIdClaim),
            ApiAuthenticationSchemeNames.ManagedControlPlane => ResolveGuidClaim(
                identity,
                ManagedControlPlaneAuthenticationDefaults.ManagedInstanceIdClaim),
            ApiAuthenticationSchemeNames.ApiKey => identity.FindFirst(
                ApiAuthenticationClaimTypes.ApiKeyId)?.Value,
            ApiAuthenticationSchemeNames.AtprotoBootstrap or ApiAuthenticationSchemeNames.AtprotoSession =>
                identity.FindFirst(AtprotoJwtOptions.DidClaim)?.Value,
            ApiAuthenticationSchemeNames.PrivacyErasureReceipt => identity.FindFirst(
                PrivacyErasureReceiptAuthenticationHandler.IntentIdClaim)?.Value,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(purposeIdentity))
        {
            return $"purpose:{scheme}:{purposeIdentity}";
        }

        if (userId is not null)
        {
            return $"platform:{scheme}:{userId}";
        }

        string? providerSubject = principal.GetProviderSubject();
        return string.IsNullOrWhiteSpace(providerSubject)
            ? $"authenticated:{scheme}"
            : $"provider:{scheme}:{providerSubject}";
    }

    private static string? ResolveGuidClaim(ClaimsIdentity identity, string claimType)
    {
        string? value = identity.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out Guid id) && id != Guid.Empty
            ? id.ToString("N")
            : null;
    }

    private static string ComputeSha256Hex(string value)
        => ComputeSha256Hex(Encoding.UTF8.GetBytes(value));

    private static string ComputeSha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string ComputeSha256Hex(ReadOnlySequence<byte> sequence)
        => sequence.IsSingleSegment
            ? ComputeSha256Hex(sequence.FirstSpan)
            : ComputeSha256Hex(sequence.ToArray());
}
