using System.Net;
using System.Text.Json;

namespace Explore.Atproto.Transport;

public sealed class AtprotoAuthorizationServerMetadataHandler(
    AtprotoAuthorizationServerRegistry registry,
    AtprotoOutboundPolicy outboundPolicy,
    HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    public const int MaximumMetadataBytes = 64 * 1024;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestUri = request.RequestUri ?? throw new AtprotoOAuthSecurityException("missing_endpoint");
        outboundPolicy.ValidateUri(requestUri);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.Method != HttpMethod.Get
            || !requestUri.AbsolutePath.Equals("/.well-known/oauth-authorization-server", StringComparison.Ordinal))
        {
            return response;
        }

        try
        {
            if (response.StatusCode != HttpStatusCode.OK
                || !string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "application/json",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new AtprotoOAuthSecurityException("invalid_metadata_response");
            }

            var payload = await AtprotoHttpContent.ReadBoundedAsync(
                response.Content,
                MaximumMetadataBytes,
                cancellationToken).ConfigureAwait(false);
            var profile = ValidateMetadata(payload, requestUri, outboundPolicy);
            registry.Register(profile);
            AtprotoHttpContent.ReplaceResponseContent(response, payload);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static AtprotoAuthorizationServerProfile ValidateMetadata(
        byte[] payload,
        Uri metadataEndpoint,
        AtprotoOutboundPolicy outboundPolicy)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
        }
        catch (JsonException exception)
        {
            throw new AtprotoOAuthSecurityException("invalid_metadata_json", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || HasDuplicateProperties(root))
            {
                throw new AtprotoOAuthSecurityException("invalid_metadata_json");
            }

            var issuer = RequiredString(root, "issuer");
            if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri))
            {
                throw new AtprotoOAuthSecurityException("invalid_issuer");
            }

            outboundPolicy.ValidateUri(issuerUri);
            if (!string.IsNullOrEmpty(issuerUri.Query)
                || !IsCanonicalIssuerText(issuer, issuerUri))
            {
                throw new AtprotoOAuthSecurityException("invalid_issuer");
            }

            Uri expectedMetadata;
            try
            {
                expectedMetadata = new Uri(
                    issuerUri.AbsoluteUri.TrimEnd('/') + "/.well-known/oauth-authorization-server",
                    UriKind.Absolute);
            }
            catch (UriFormatException exception)
            {
                throw new AtprotoOAuthSecurityException("invalid_issuer", exception);
            }

            if (!string.Equals(metadataEndpoint.AbsoluteUri, expectedMetadata.AbsoluteUri, StringComparison.Ordinal))
            {
                throw new AtprotoOAuthSecurityException("issuer_mismatch");
            }

            _ = RequiredSafeUri(root, "authorization_endpoint", outboundPolicy);
            var tokenEndpoint = RequiredSafeUri(root, "token_endpoint", outboundPolicy);
            var parEndpoint = RequiredSafeUri(root, "pushed_authorization_request_endpoint", outboundPolicy);
            var revocationEndpoint = OptionalSafeUri(root, "revocation_endpoint", outboundPolicy);

            RequireTrue(root, "require_pushed_authorization_requests");
            RequireStringArrayContains(root, "token_endpoint_auth_methods_supported", "private_key_jwt");
            RequireStringArrayContains(root, "token_endpoint_auth_signing_alg_values_supported", "ES256");
            RequireStringArrayContains(root, "dpop_signing_alg_values_supported", "ES256");
            RequireStringArrayContains(root, "grant_types_supported", "authorization_code");
            RequireStringArrayContains(root, "grant_types_supported", "refresh_token");
            RequireStringArrayContains(root, "response_types_supported", "code");
            RequireStringArrayContains(root, "code_challenge_methods_supported", "S256");
            RequireTrue(root, "authorization_response_iss_parameter_supported");
            RequireTrue(root, "client_id_metadata_document_supported");
            RequireStringArrayContains(root, "scopes_supported", "atproto");
            if (root.TryGetProperty("require_request_uri_registration", out var registration)
                && registration.ValueKind is not JsonValueKind.True)
            {
                throw new AtprotoOAuthSecurityException("request_uri_registration_required");
            }

            return new(issuer, parEndpoint, tokenEndpoint, revocationEndpoint);
        }
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(property => !names.Add(property.Name));
    }

    private static bool IsCanonicalIssuerText(string issuer, Uri issuerUri)
    {
        if (issuerUri.AbsolutePath != "/")
        {
            return string.Equals(issuer, issuerUri.AbsoluteUri, StringComparison.Ordinal);
        }

        var authority = issuerUri.GetLeftPart(UriPartial.Authority);
        return string.Equals(issuer, authority, StringComparison.Ordinal)
            || string.Equals(issuer, authority + "/", StringComparison.Ordinal);
    }

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new AtprotoOAuthSecurityException("missing_metadata_capability");
        }

        return value.GetString()!;
    }

    private static Uri RequiredSafeUri(JsonElement root, string name, AtprotoOutboundPolicy policy)
    {
        if (!Uri.TryCreate(RequiredString(root, name), UriKind.Absolute, out var uri))
        {
            throw new AtprotoOAuthSecurityException("invalid_metadata_endpoint");
        }

        policy.ValidateUri(uri);
        return uri;
    }

    private static Uri? OptionalSafeUri(JsonElement root, string name, AtprotoOutboundPolicy policy)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new AtprotoOAuthSecurityException("invalid_metadata_endpoint");
        }

        return RequiredSafeUri(root, name, policy);
    }

    private static void RequireTrue(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is not JsonValueKind.True)
        {
            throw new AtprotoOAuthSecurityException("missing_metadata_capability");
        }
    }

    private static void RequireStringArrayContains(JsonElement root, string name, string required)
    {
        if (!root.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new AtprotoOAuthSecurityException("missing_metadata_capability");
        }

        var entries = value.EnumerateArray().ToArray();
        if (entries.Length == 0
            || entries.Any(item => item.ValueKind != JsonValueKind.String)
            || !entries.Any(item => item.GetString() == required))
        {
            throw new AtprotoOAuthSecurityException("missing_metadata_capability");
        }
    }
}
