using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Event.Web.BffHosting.Security;

public static class BffDevelopmentHostPolicy
{
    public static bool IsDevelopmentTrustedBaseAddress(string baseAddress, IWebHostEnvironment environment)
    {
        if (!string.Equals(
                environment.EnvironmentName,
                Environments.Development,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Uri.TryCreate(baseAddress, UriKind.Absolute, out var destinationUri)
            && IsDevelopmentTrustedHost(destinationUri.Host);
    }

    public static bool IsDevelopmentTrustedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("100.64.0.2", StringComparison.OrdinalIgnoreCase)
            || IsTailscaleAddress(host))
        {
            return true;
        }

        var additionalHosts = Environment.GetEnvironmentVariable("BFF_DEV_TRUSTED_HOSTS");
        if (string.IsNullOrWhiteSpace(additionalHosts))
        {
            return false;
        }

        return additionalHosts
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTailscaleAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
    }
}
