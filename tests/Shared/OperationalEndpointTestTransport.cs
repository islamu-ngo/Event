using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Testing.Hosting;

/// <summary>Models the private socket in TestServer without forging HTTP forwarding headers.</summary>
internal static class OperationalEndpointTestTransport
{
    public static HttpClient CreateClient(TestServer server, Uri? baseAddress = null)
    {
        var configuration = server.Services.GetRequiredService<IConfiguration>();
        configuration["OperationalEndpoints:Address"] = "127.0.0.1";
        configuration["OperationalEndpoints:Port"] = "9091";
        return new HttpClient(server.CreateHandler(context =>
        {
            context.Connection.LocalIpAddress = IPAddress.Loopback;
            context.Connection.LocalPort = 9091;
        }))
        {
            BaseAddress = baseAddress ?? new Uri("https://localhost")
        };
    }
}
