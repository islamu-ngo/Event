using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Configuration;

public sealed class OspreyProviderOptionsValidator : IValidateOptions<OspreyProviderOptions>
{
    public ValidateOptionsResult Validate(string? name, OspreyProviderOptions options)
    {
        List<string> failures = [];

        var transport = NormalizeTransport(options.Transport);

        if (transport is null)
        {
            failures.Add("Reporting:Osprey:Transport must be 'HttpJson' or 'Grpc'.");
        }

        if (!string.IsNullOrWhiteSpace(options.EndpointUrl)
            || options.Enabled && string.Equals(transport, OspreyProviderOptions.TransportHttpJson, StringComparison.OrdinalIgnoreCase))
        {
            ValidateEndpointSafety(
                options.EndpointUrl,
                "Reporting:Osprey:EndpointUrl",
                options.AllowLocalProviderEndpoints,
                failures);
        }

        if (!string.IsNullOrWhiteSpace(options.GrpcEndpointUrl)
            || options.Enabled && string.Equals(transport, OspreyProviderOptions.TransportGrpc, StringComparison.OrdinalIgnoreCase))
        {
            ValidateEndpointSafety(
                options.GrpcEndpointUrl,
                "Reporting:Osprey:GrpcEndpointUrl",
                options.AllowLocalProviderEndpoints,
                failures);
        }

        if (options.Enabled
            && string.Equals(transport, OspreyProviderOptions.TransportHttpJson, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.EndpointUrl))
        {
            failures.Add("Reporting:Osprey:EndpointUrl is required when Reporting:Osprey:Enabled is true and Transport is HttpJson.");
        }

        if (options.Enabled
            && string.Equals(transport, OspreyProviderOptions.TransportGrpc, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.GrpcEndpointUrl))
        {
            failures.Add("Reporting:Osprey:GrpcEndpointUrl is required when Reporting:Osprey:Enabled is true and Transport is Grpc.");
        }

        if (string.IsNullOrWhiteSpace(options.EvaluatePath) || !options.EvaluatePath.Trim().StartsWith('/'))
        {
            failures.Add("Reporting:Osprey:EvaluatePath must be an absolute path starting with '/'.");
        }

        if (string.IsNullOrWhiteSpace(options.EventType))
        {
            failures.Add("Reporting:Osprey:EventType is required.");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            failures.Add("Reporting:Osprey:TimeoutSeconds must be between 1 and 300.");
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey) && !IsValidHeaderName(options.ApiKeyHeaderName))
        {
            failures.Add("Reporting:Osprey:ApiKeyHeaderName must be a valid HTTP header name.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static string? NormalizeTransport(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OspreyProviderOptions.TransportHttpJson;
        }

        var normalized = value.Trim();
        return string.Equals(normalized, OspreyProviderOptions.TransportHttpJson, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, OspreyProviderOptions.TransportGrpc, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : null;
    }

    private static void ValidateEndpointSafety(
        string endpointUrl,
        string configKey,
        bool allowLocalProviderEndpoints,
        List<string> failures)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint)
            || !IsHttpEndpoint(endpoint))
        {
            failures.Add($"{configKey} must be an absolute HTTP or HTTPS URL.");
            return;
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo))
        {
            failures.Add($"{configKey} must not contain embedded credentials.");
        }

        if (!string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            failures.Add($"{configKey} must not contain query strings or fragments.");
        }

        if (!allowLocalProviderEndpoints && IsLocalOrPrivateEndpoint(endpoint))
        {
            failures.Add($"{configKey} must not target local, loopback, link-local, or private network hosts unless Reporting:Osprey:AllowLocalProviderEndpoints is true.");
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
