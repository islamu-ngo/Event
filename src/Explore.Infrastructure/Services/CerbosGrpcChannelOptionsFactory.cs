using System.Net;
using System.Net.Sockets;
using Grpc.Net.Client;

namespace Explore.Infrastructure.Services;

internal static class CerbosGrpcChannelOptionsFactory
{
    public static GrpcChannelOptions Create() => new()
    {
        HttpHandler = CreateIpv4Handler(),
        DisposeHttpClient = true
    };

    private static SocketsHttpHandler CreateIpv4Handler() => new()
    {
        EnableMultipleHttp2Connections = true,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = static async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(
                context.DnsEndPoint.Host,
                AddressFamily.InterNetwork,
                cancellationToken).ConfigureAwait(false);

            if (addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            var socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(
                    addresses,
                    context.DnsEndPoint.Port,
                    cancellationToken).ConfigureAwait(false);

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
}
