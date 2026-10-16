using System.Net;
using System.Net.Sockets;

namespace Explore.Infrastructure.Http;

/// <summary>Connects to vetted literal addresses without a second hostname resolution.</summary>
public sealed class VettedAddressConnector(
    Func<OutboundEndpointPolicy> policy,
    Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve =
        resolve ?? Dns.GetHostAddressesAsync;

    public SocketsHttpHandler CreateHandler(TimeSpan connectTimeout) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectTimeout = connectTimeout,
        // A changing profile must apply to the next request, not an old pooled socket.
        PooledConnectionLifetime = TimeSpan.Zero,
        ConnectCallback = ConnectAsync
    };

    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        Uri endpoint = context.InitialRequestMessage.RequestUri
            ?? throw new OutboundEndpointDeniedException("invalid_url");
        if (!string.Equals(endpoint.IdnHost.Trim('[', ']'), context.DnsEndPoint.Host.Trim('[', ']'),
                StringComparison.OrdinalIgnoreCase) || endpoint.Port != context.DnsEndPoint.Port)
            throw new OutboundEndpointDeniedException("origin_mismatch");
        var currentPolicy = policy();
        currentPolicy.RequireEndpoint(endpoint);
        string host = context.DnsEndPoint.Host.Trim('[', ']');
        IPAddress[] addresses = IPAddress.TryParse(host, out IPAddress? literal)
            ? [literal] : await _resolve(host, cancellationToken);
        currentPolicy.RequireAllowed(endpoint, addresses);
        SocketException? failure = null;
        // Preserve IPv4-first operator connectivity without bypassing full answer-set validation.
        foreach (IPAddress address in addresses.OrderBy(address =>
                     address.IsIPv4MappedToIPv6 || address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IPAddress normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            var socket = new Socket(normalized.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(normalized, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                failure = exception;
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw new HttpRequestException("Outbound connection failed.", failure);
    }
}
