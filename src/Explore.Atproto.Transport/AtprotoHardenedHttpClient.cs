using System.Net;
using System.Net.Sockets;

namespace Explore.Atproto.Transport;

public sealed class AtprotoBoundedResponseHandler(int maximumResponseBytes, HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = await AtprotoHttpContent.ReadBoundedAsync(
                response.Content,
                maximumResponseBytes,
                cancellationToken).ConfigureAwait(false);
            AtprotoHttpContent.ReplaceResponseContent(response, payload);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}

public static class AtprotoHttpContent
{
    public static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new AtprotoOAuthSecurityException("response_too_large");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new AtprotoOAuthSecurityException("response_too_large");
            }

            destination.Write(buffer, 0, read);
        }
    }

    public static void ReplaceResponseContent(HttpResponseMessage response, byte[] payload)
    {
        var previous = response.Content;
        var replacement = new ByteArrayContent(payload);
        CopyHeaders(previous, replacement, skipRepresentationHeaders: false);
        response.Content = replacement;
        previous.Dispose();
    }

    public static void ReplaceRequestForm(HttpRequestMessage request, IReadOnlyDictionary<string, string> form)
    {
        var previous = request.Content ?? throw new AtprotoOAuthSecurityException("form_content_required");
        var replacement = new FormUrlEncodedContent(form);
        CopyHeaders(previous, replacement, skipRepresentationHeaders: true);
        request.Content = replacement;
        previous.Dispose();
    }

    private static void CopyHeaders(HttpContent source, HttpContent destination, bool skipRepresentationHeaders)
    {
        foreach (var header in source.Headers)
        {
            if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                || skipRepresentationHeaders && header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            destination.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}

public static class AtprotoHardenedHttpClient
{
    public static HttpMessageHandler CreatePrimaryHandler(AtprotoOutboundPolicy policy, TimeSpan connectTimeout) =>
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = connectTimeout,
            ConnectCallback = (context, cancellationToken) => ConnectPinnedAsync(
                context.DnsEndPoint.Host,
                context.DnsEndPoint.Port,
                policy,
                static (host, token) => new(Dns.GetHostAddressesAsync(host, token)),
                ConnectSocketAsync,
                cancellationToken)
        };

    internal static async ValueTask<Stream> ConnectPinnedAsync(
        string host,
        int port,
        AtprotoOutboundPolicy policy,
        Func<string, CancellationToken, ValueTask<IPAddress[]>> resolveAddresses,
        Func<IPAddress, int, CancellationToken, ValueTask<Stream>> connectAddress,
        CancellationToken cancellationToken)
    {
        var addresses = await resolveAddresses(host, cancellationToken).ConfigureAwait(false);
        policy.ValidateResolvedAddresses(host, addresses);
        SocketException? lastError = null;
        foreach (var address in addresses)
        {
            try
            {
                return await connectAddress(address, port, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SocketException exception)
            {
                lastError = exception;
            }
        }

        throw new HttpRequestException("ATProto outbound connection failed.", lastError);
    }

    private static async ValueTask<Stream> ConnectSocketAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
