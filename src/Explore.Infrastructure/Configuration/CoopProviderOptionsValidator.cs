using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Configuration;

public sealed class CoopProviderOptionsValidator : IValidateOptions<CoopProviderOptions>
{
    public ValidateOptionsResult Validate(string? name, CoopProviderOptions options)
    {
        List<string> failures = [];

        if (!string.IsNullOrWhiteSpace(options.EndpointUrl) || options.Enabled)
        {
            ValidateEndpointSafety(options, failures);
        }

        if (options.Enabled && string.IsNullOrWhiteSpace(options.EndpointUrl))
        {
            failures.Add("Reporting:Coop:EndpointUrl is required when Reporting:Coop:Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(options.MirrorPath) || !options.MirrorPath.Trim().StartsWith('/'))
        {
            failures.Add("Reporting:Coop:MirrorPath must be an absolute path starting with '/'.");
        }

        if (string.IsNullOrWhiteSpace(options.ItemType))
        {
            failures.Add("Reporting:Coop:ItemType is required.");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            failures.Add("Reporting:Coop:TimeoutSeconds must be between 1 and 300.");
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey) && !IsValidHeaderName(options.ApiKeyHeaderName))
        {
            failures.Add("Reporting:Coop:ApiKeyHeaderName must be a valid HTTP header name.");
        }

        if (!IsValidHeaderName(options.WebhookSignatureHeaderName))
        {
            failures.Add("Reporting:Coop:WebhookSignatureHeaderName must be a valid HTTP header name.");
        }

        if (!IsValidHeaderName(options.WebhookTimestampHeaderName))
        {
            failures.Add("Reporting:Coop:WebhookTimestampHeaderName must be a valid HTTP header name.");
        }

        if (options.WebhookToleranceSeconds is < 30 or > 86_400)
        {
            failures.Add("Reporting:Coop:WebhookToleranceSeconds must be between 30 and 86400.");
        }

        if (options.WebhookMaxBodyBytes is < 1_024 or > 1_048_576)
        {
            failures.Add("Reporting:Coop:WebhookMaxBodyBytes must be between 1024 and 1048576.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateEndpointSafety(CoopProviderOptions options, List<string> failures)
    {
        if (!Uri.TryCreate(options.EndpointUrl, UriKind.Absolute, out var endpoint)
            || !IsHttpEndpoint(endpoint))
        {
            failures.Add("Reporting:Coop:EndpointUrl must be an absolute HTTP or HTTPS URL.");
            return;
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo))
        {
            failures.Add("Reporting:Coop:EndpointUrl must not contain embedded credentials.");
        }

        if (!string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            failures.Add("Reporting:Coop:EndpointUrl must not contain query strings or fragments.");
        }

        if (!options.AllowLocalProviderEndpoints && IsLocalOrPrivateEndpoint(endpoint))
        {
            failures.Add("Reporting:Coop:EndpointUrl must not target local, loopback, link-local, or private network hosts unless Reporting:Coop:AllowLocalProviderEndpoints is true.");
        }
    }

    private static bool IsHttpEndpoint(Uri endpoint) =>
        string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidHeaderName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var character in value.Trim())
        {
            var allowed = char.IsAsciiLetterOrDigit(character)
                || character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLocalOrPrivateEndpoint(Uri endpoint)
    {
        if (endpoint.IsLoopback || string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(endpoint.Host, out var address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork =>
                bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168,
            AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal || address.IsIPv6SiteLocal,
            _ => false
        };
    }
}
