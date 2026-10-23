using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.ServiceDefaults;

public static class OperationalEndpointAccess
{
    /// <summary>
    /// Operational responses are served only on an explicitly configured private local listener.
    /// Forwarded headers and the request Host never select this listener.
    /// </summary>
    public static TBuilder RequirePrivateOperationalListener<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpoint =>
        {
            var next = endpoint.RequestDelegate
                ?? throw new InvalidOperationException("operational_endpoint_delegate_missing");
            endpoint.RequestDelegate = async context =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
                var port = configuration.GetValue<int>("OperationalEndpoints:Port");
                var address = configuration["OperationalEndpoints:Address"];
                if (port is < 1 or > 65535
                    || !IPAddress.TryParse(address, out var expected)
                    || !IsPrivate(expected)
                    || context.Connection.LocalPort != port
                    || context.Connection.LocalIpAddress is not { } local
                    || !local.Equals(expected))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                await next(context);
            };
        });
        return builder;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168
            : bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc;
    }
}
