using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Blazor.Authentication;
using Explore.Blazor.Services.Auth;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.Extensions;

public static class AtprotoOAuthEndpointExtensions
{
    private const int MaximumDocumentBytes = 32 * 1024;
    private const string PublicationCacheControl = "public, max-age=300, must-revalidate";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static WebApplication MapAtprotoOAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/oauth/client-metadata.json", PublishClientMetadata).AllowAnonymous();
        app.MapGet("/oauth/jwks.json", PublishJwks).AllowAnonymous();
        return app;
    }

    private static IResult PublishClientMetadata(
        HttpContext context,
        IOptions<AtprotoAuthenticationOptions> configuredOptions,
        AtprotoClientKeyProvider keyProvider)
    {
        if (!TryGetCanonicalBaseUri(context.Request, configuredOptions.Value, keyProvider, out var baseUri))
        {
            return Results.NotFound();
        }

        var options = configuredOptions.Value;
        if (!TryGetCanonicalCallbackPath(options.CallbackPath, out var callbackPath))
        {
            return Results.NotFound();
        }

        var metadata = new AtprotoClientMetadata(
            new Uri(baseUri, "/oauth/client-metadata.json").AbsoluteUri,
            OptionalText(options.ClientName),
            OptionalAbsoluteHttpsUri(options.ClientUri),
            OptionalAbsoluteHttpsUri(options.LogoUri),
            OptionalAbsoluteHttpsUri(options.TermsOfServiceUri),
            OptionalAbsoluteHttpsUri(options.PolicyUri),
            [new Uri(baseUri, callbackPath).AbsoluteUri],
            "atproto transition:generic",
            ["authorization_code", "refresh_token"],
            ["code"],
            "web",
            true,
            "private_key_jwt",
            "ES256",
            new Uri(baseUri, "/oauth/jwks.json").AbsoluteUri);

        return JsonDocumentResult(context.Response, metadata);
    }

    private static IResult PublishJwks(
        HttpContext context,
        IOptions<AtprotoAuthenticationOptions> configuredOptions,
        AtprotoClientKeyProvider keyProvider)
    {
        if (!TryGetCanonicalBaseUri(context.Request, configuredOptions.Value, keyProvider, out _))
        {
            return Results.NotFound();
        }

        return JsonDocumentResult(context.Response, new AtprotoJsonWebKeySet(keyProvider.GetPublicKeys()));
    }

    private static IResult JsonDocumentResult<T>(HttpResponse response, T document)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (payload.Length > MaximumDocumentBytes)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        response.Headers.CacheControl = PublicationCacheControl;
        response.Headers.XContentTypeOptions = "nosniff";
        return Results.Bytes(payload, "application/json", enableRangeProcessing: false);
    }

    private static bool TryGetCanonicalBaseUri(
        HttpRequest request,
        AtprotoAuthenticationOptions options,
        AtprotoClientKeyProvider keyProvider,
        out Uri baseUri)
    {
        baseUri = null!;
        var configuredPublicUrl = options.PublicUrl;
        if (!keyProvider.IsReady
            || string.IsNullOrWhiteSpace(configuredPublicUrl)
            || !string.Equals(configuredPublicUrl, configuredPublicUrl.Trim(), StringComparison.Ordinal)
            || !Uri.TryCreate(configuredPublicUrl, UriKind.Absolute, out var configuredUri)
            || configuredUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(configuredUri.UserInfo)
            || !string.IsNullOrEmpty(configuredUri.Query)
            || !string.IsNullOrEmpty(configuredUri.Fragment)
            || configuredUri.AbsolutePath != "/"
            || configuredUri.Host.EndsWith('.')
            || configuredUri.Host.Any(character => character > 0x7F)
            || !string.Equals(configuredUri.Host, configuredUri.IdnHost, StringComparison.Ordinal)
            || Uri.CheckHostName(configuredUri.Host) != UriHostNameType.Dns)
        {
            return false;
        }

        int? configuredPort = configuredUri.IsDefaultPort ? null : configuredUri.Port;
        if (!string.Equals(request.Scheme, configuredUri.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(request.Host.Host, configuredUri.IdnHost, StringComparison.OrdinalIgnoreCase)
            || request.Host.Port != configuredPort)
        {
            return false;
        }

        baseUri = new UriBuilder(Uri.UriSchemeHttps, configuredUri.IdnHost, configuredPort ?? -1, "/").Uri;
        return true;
    }

    private static bool TryGetCanonicalCallbackPath(string? configuredPath, out string callbackPath)
    {
        callbackPath = "/signin-atproto";
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return true;
        }

        if (configuredPath.Length is < 2 or > 256
            || configuredPath[0] != '/'
            || configuredPath[1] == '/'
            || configuredPath.Contains("//", StringComparison.Ordinal)
            || configuredPath.Any(character => !IsCanonicalCallbackPathCharacter(character))
            || configuredPath.Split('/', StringSplitOptions.None).Any(segment => segment is "." or ".."))
        {
            return false;
        }

        callbackPath = configuredPath;
        return true;
    }

    private static bool IsCanonicalCallbackPathCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '/' or '-' or '_' or '.' or '~';

    private static string? OptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? OptionalAbsoluteHttpsUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;
    }

    private sealed record AtprotoClientMetadata(
        string ClientId,
        string? ClientName,
        string? ClientUri,
        string? LogoUri,
        string? TosUri,
        string? PolicyUri,
        IReadOnlyList<string> RedirectUris,
        string Scope,
        IReadOnlyList<string> GrantTypes,
        IReadOnlyList<string> ResponseTypes,
        string ApplicationType,
        bool DpopBoundAccessTokens,
        string TokenEndpointAuthMethod,
        string TokenEndpointAuthSigningAlg,
        string JwksUri);

    private sealed record AtprotoJsonWebKeySet(IReadOnlyList<AtprotoPublicJsonWebKey> Keys);
}
