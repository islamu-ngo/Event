using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Registration.ProviderLaunch;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Explore.Blazor.Extensions;

public static class BffRegistrationProviderEmbedEndpoints
{
    private const string LaunchRoute = "/bff/registration-provider-embed/launches";
    private const string EmbedRoute = "/bff/registration-provider-embed/launches/{launchId}";
    private const string CacheKeyPrefix = "registration-provider-embed:";
    private const string HtmlContentType = "text/html; charset=utf-8";

    public static WebApplication MapRegistrationProviderEmbedEndpoints(this WebApplication app)
    {
        app.MapPost(LaunchRoute, HandleLaunchAsync)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapGet(EmbedRoute, HandleEmbedHostAsync)
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> HandleLaunchAsync(
        RegistrationProviderBffLaunch request,
        HttpContext ctx,
        IAuthenticatedRegistrationOrderClient authenticatedClient,
        IGuestRegistrationOrderClient guestClient,
        IDistributedCache cache,
        CancellationToken cancellationToken)
    {
        SetNoStoreHeaders(ctx.Response.Headers);
        if (ctx.Request.Query.Count != 0 || request.EventId == Guid.Empty || request.OrderId == Guid.Empty ||
            request.RequirementId == Guid.Empty || request.ChannelId == Guid.Empty || request.BindingId == Guid.Empty ||
            request.FormId == Guid.Empty || request.FormVersionId == Guid.Empty)
        {
            return Results.BadRequest();
        }

        RegistrationProviderLaunchDescriptor descriptor;
        try
        {
            var body = new LaunchRegistrationProviderAttemptRequest
            {
                RequirementId = request.RequirementId,
                ChannelId = request.ChannelId,
                BindingId = request.BindingId,
                FormId = request.FormId,
                FormVersionId = request.FormVersionId
            };
            string idempotencyKey = Guid.CreateVersion7().ToString("D");
            HalResourceOfNativeRegistrationProviderLaunchDescriptorDto resource = string.IsNullOrWhiteSpace(request.GuestCapability)
                ? await authenticatedClient.LaunchAuthenticatedRegistrationProviderAttemptAsync(
                    request.EventId, request.OrderId, idempotencyKey, body, cancellationToken: cancellationToken)
                : await guestClient.LaunchGuestRegistrationProviderAttemptAsync(
                    request.EventId, request.OrderId, idempotencyKey, body, request.GuestCapability, cancellationToken: cancellationToken);
            descriptor = RegistrationProviderLaunchDescriptor.From(resource.AdditionalProperties);
        }
        catch (ApiException exception) when (exception.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound)
        {
            return Results.StatusCode(exception.StatusCode);
        }

        if (!descriptor.Matches(request.RequirementId, request.ChannelId, request.BindingId, request.FormId, request.FormVersionId) ||
            !descriptor.Available ||
            !string.Equals(descriptor.Mode, "embed", StringComparison.Ordinal) ||
            !TryGetApprovedHttpsUri(descriptor.Url, out var embedUri))
        {
            return Results.NotFound();
        }

        string launchId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await cache.SetStringAsync(CacheKeyPrefix + launchId,
            JsonSerializer.Serialize(new CachedLaunch(embedUri.ToString(), descriptor.Title)),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
            cancellationToken);
        return Results.Ok(new RegistrationProviderBffTicket($"{LaunchRoute}/{launchId}"));
    }

    private static async Task<IResult> HandleEmbedHostAsync(
        string launchId,
        HttpContext ctx,
        IDistributedCache cache,
        CancellationToken cancellationToken)
    {
        SetNoStoreHeaders(ctx.Response.Headers);
        if (ctx.Request.Query.Count != 0 || launchId.Length is < 40 or > 50 ||
            await cache.GetStringAsync(CacheKeyPrefix + launchId, cancellationToken) is not { } cachedJson)
        {
            return Results.NotFound();
        }

        CachedLaunch? launch = JsonSerializer.Deserialize<CachedLaunch>(cachedJson);
        if (launch is null || !TryGetApprovedHttpsUri(launch.Url, out var embedUri))
        {
            return Results.NotFound();
        }

        ctx.Response.Headers[HeaderNames.ContentSecurityPolicy] = BuildContentSecurityPolicy(embedUri);
        ctx.Response.Headers[HeaderNames.XFrameOptions] = "SAMEORIGIN";
        ctx.Items[MiddlewareExtensions.PreserveExplicitSecurityHeadersItemKey] = true;
        return Results.Content(BuildHtml(embedUri, launch.Title), HtmlContentType, Encoding.UTF8);
    }

    private static string BuildContentSecurityPolicy(Uri embedUri) =>
        "default-src 'none'; " +
        $"frame-src {embedUri.GetLeftPart(UriPartial.Authority)}; " +
        "frame-ancestors 'self'; " +
        "base-uri 'none'; " +
        "form-action 'none'; " +
        "object-src 'none'; " +
        "script-src 'none'; " +
        "style-src 'none'";

    private static string BuildHtml(Uri embedUri, string title)
    {
        var encodedUrl = HtmlEncoder.Default.Encode(embedUri.ToString());
        var encodedTitle = HtmlEncoder.Default.Encode(string.IsNullOrWhiteSpace(title) ? "Registration form" : title);
        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>" + encodedTitle +
            "</title></head><body><iframe src=\"" + encodedUrl + "\" title=\"" + encodedTitle +
            "\" sandbox=\"allow-forms allow-same-origin allow-scripts\" referrerpolicy=\"no-referrer\"></iframe>" +
            "<p><a href=\"" + encodedUrl + "\" target=\"_blank\" rel=\"noopener noreferrer\">Open " + encodedTitle +
            "</a></p></body></html>";
    }

    private static void SetNoStoreHeaders(IHeaderDictionary headers)
    {
        headers[HeaderNames.CacheControl] = "private, no-store";
        headers[HeaderNames.Pragma] = "no-cache";
        headers[HeaderNames.Expires] = "0";
    }

    private static bool TryGetApprovedHttpsUri(string? url, out Uri uri)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out uri!) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            !string.IsNullOrWhiteSpace(uri.Host) &&
            !IsBlockedLiteralHost(uri.Host);
    }

    private static bool IsBlockedLiteralHost(string host)
    {
        var literal = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
        if (!IPAddress.TryParse(literal, out var address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.IPv6Loopback) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal ||
            address.IsIPv6Multicast ||
            IsUniqueLocalIpv6(address) ||
            IsBlockedIpv4(address);
    }

    private static bool IsBlockedIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 0 ||
            bytes[0] == 10 ||
            bytes[0] == 127 ||
            bytes[0] == 169 && bytes[1] == 254 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
            bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] >= 224;
    }

    private static bool IsUniqueLocalIpv6(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
    }

    private sealed record RegistrationProviderLaunchDescriptor(
        Guid RequirementId,
        Guid ChannelId,
        Guid BindingId,
        Guid FormId,
        Guid FormVersionId,
        string Mode,
        bool Available,
        string? Url,
        string Title)
    {
        public static RegistrationProviderLaunchDescriptor From(IDictionary<string, object> properties) => new(
            GetGuid(properties, "requirementId"),
            GetGuid(properties, "channelId"),
            GetGuid(properties, "bindingId"),
            GetGuid(properties, "formId"),
            GetGuid(properties, "formVersionId"),
            GetString(properties, "mode"),
            GetBool(properties, "available"),
            GetNullableString(properties, "url"),
            GetString(properties, "title"));

        public bool Matches(Guid requirementId, Guid channelId, Guid bindingId, Guid formId, Guid formVersionId) =>
            RequirementId == requirementId &&
            ChannelId == channelId &&
            BindingId == bindingId &&
            FormId == formId &&
            FormVersionId == formVersionId;

        private static Guid GetGuid(IDictionary<string, object> properties, string name) =>
            TryGet<JsonElement>(properties, name, out var element) && element.ValueKind == JsonValueKind.String && element.TryGetGuid(out var guid)
                ? guid
                : TryGet<Guid>(properties, name, out guid) ? guid : Guid.Empty;

        private static string GetString(IDictionary<string, object> properties, string name) =>
            TryGet<JsonElement>(properties, name, out var element) && element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : TryGet<string>(properties, name, out var value) ? value : string.Empty;

        private static string? GetNullableString(IDictionary<string, object> properties, string name)
        {
            var value = GetString(properties, name);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static bool GetBool(IDictionary<string, object> properties, string name) =>
            TryGet<JsonElement>(properties, name, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? element.GetBoolean()
                : TryGet<bool>(properties, name, out var value) && value;

        private static bool TryGet<T>(IDictionary<string, object> properties, string name, out T value)
        {
            if (properties.TryGetValue(name, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default!;
            return false;
        }
    }

    private sealed record CachedLaunch(string Url, string Title);
}
