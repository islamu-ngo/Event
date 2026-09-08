using System.Net;
using System.Net.Sockets;

namespace Explore.Infrastructure.Tests.Fixtures;

internal sealed class SvixAcceptThenDropProxy : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly HttpClient _client;
    private readonly Uri _upstream;
    private readonly Task _processing;
    private readonly TaskCompletionSource _forwarded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SvixAcceptThenDropProxy(HttpListener listener, Uri upstream)
    {
        _listener = listener;
        _upstream = upstream;
        _client = new HttpClient();
        _processing = ProcessOneAsync();
    }

    public string ServerUrl => _listener.Prefixes.Single().TrimEnd('/');

    public Task Forwarded => _forwarded.Task.WaitAsync(TimeSpan.FromSeconds(15));

    public static Task<SvixAcceptThenDropProxy> StartAsync(Uri upstream)
    {
        using var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        HttpListener? listener = new();
        try
        {
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var proxy = new SvixAcceptThenDropProxy(listener, upstream);
            listener = null;
            return Task.FromResult(proxy);
        }
        finally
        {
            listener?.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Close();
        try
        {
            await _processing;
        }
        catch (Exception) when (_forwarded.Task.IsCompleted)
        {
        }

        _client.Dispose();
    }

    private async Task ProcessOneAsync()
    {
        var context = await _listener.GetContextAsync();
        try
        {
            using var upstreamRequest = new HttpRequestMessage(
                new HttpMethod(context.Request.HttpMethod),
                new Uri(_upstream, context.Request.RawUrl));
            var body = await ReadBodyAsync(context.Request);
            if (body.Length > 0)
            {
                upstreamRequest.Content = new ByteArrayContent(body);
            }

            foreach (var headerName in context.Request.Headers.AllKeys.OfType<string>())
            {
                var values = context.Request.Headers.GetValues(headerName);
                if (string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase) || values is null)
                {
                    continue;
                }

                if (!upstreamRequest.Headers.TryAddWithoutValidation(headerName, values))
                {
                    upstreamRequest.Content?.Headers.TryAddWithoutValidation(headerName, values);
                }
            }

            using var upstreamResponse = await _client.SendAsync(upstreamRequest);
            _ = await upstreamResponse.Content.ReadAsByteArrayAsync();
            upstreamResponse.EnsureSuccessStatusCode();
            _forwarded.TrySetResult();
            context.Response.Abort();
        }
        catch (Exception exception)
        {
            _forwarded.TrySetException(exception);
            throw;
        }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
        {
            return [];
        }

        using var buffer = new MemoryStream();
        await request.InputStream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
