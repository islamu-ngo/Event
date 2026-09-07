using System.Collections.Immutable;
using System.Net;
using System.Security.Claims;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.Services;

public sealed class InProcessEventApiDispatcher
{
    public static readonly Uri InternalBaseAddress = new("http://event-api.internal/");
    private static readonly object RequestMarker = new();

    private RequestDelegate? _pipeline;
    private RequestDelegate? _endpointSelector;

    public void Bind(RequestDelegate pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (Interlocked.CompareExchange(ref _pipeline, pipeline, null) is not null)
        {
            throw new InvalidOperationException("The in-process Event API pipeline is already bound.");
        }
    }

    public Task DispatchAsync(HttpContext context)
    {
        var pipeline = Volatile.Read(ref _pipeline)
            ?? throw new InvalidOperationException("The in-process Event API pipeline is not bound.");
        return pipeline(context);
    }

    public void BindEndpointSelector(RequestDelegate endpointSelector)
    {
        ArgumentNullException.ThrowIfNull(endpointSelector);
        if (Interlocked.CompareExchange(ref _endpointSelector, endpointSelector, null) is not null)
        {
            throw new InvalidOperationException("The in-process Event API endpoint selector is already bound.");
        }
    }

    public Task SelectEndpointAsync(HttpContext context)
    {
        var endpointSelector = Volatile.Read(ref _endpointSelector)
            ?? throw new InvalidOperationException("The in-process Event API endpoint selector is not bound.");
        return endpointSelector(context);
    }

    public void MarkRequest(HttpContext context) => context.Items[RequestMarker] = true;

    public bool IsMarkedRequest(HttpContext context) => context.Items.ContainsKey(RequestMarker);
}

public sealed class InProcessEventApiHttpMessageHandler(
    InProcessEventApiDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    IHttpContextAccessor contextAccessor,
    IOptions<KestrelServerOptions>? kestrelOptions = null) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(() => SendIsolatedAsync(request, cancellationToken), CancellationToken.None);
        }
    }

    private async Task<HttpResponseMessage> SendIsolatedAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uri = request.RequestUri
            ?? throw new InvalidOperationException("An Event API request URI is required.");
        var state = new InProcessRequestState(cancellationToken);
        var requestSizeFeature = new InProcessMaxRequestBodySizeFeature(
            kestrelOptions?.Value.Limits.MaxRequestBodySize);
        var requestBody = new SizeLimitedReadStream(
            state.RequestPipe.Reader.AsStream(),
            requestSizeFeature);
        var responseFeature = new InProcessResponseFeature(state.ResponsePipe, state.Cancel);
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        context.Features.Set<IHttpResponseBodyFeature>(responseFeature);
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(requestSizeFeature);
        state.Scope = scopeFactory.CreateAsyncScope();
        context.RequestServices = state.Scope.Value.ServiceProvider;
        context.RequestAborted = state.Token;
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        context.Request.Method = request.Method.Method;
        context.Request.Scheme = uri.Scheme;
        context.Request.Host = new HostString(uri.Authority);
        context.Request.Path = uri.AbsolutePath;
        context.Request.QueryString = new QueryString(uri.Query);
        context.Request.Body = requestBody;
        CopyRequestHeaders(request, context.Request.Headers);
        dispatcher.MarkRequest(context);

        state.RequestPump = PumpRequestAsync(request.Content, state.RequestPipe.Writer, state.Token);
        _ = RunDispatchAsync(context, responseFeature, state);
        await responseFeature.Started.ConfigureAwait(false);

        var response = new HttpResponseMessage((HttpStatusCode)responseFeature.StatusCode)
        {
            ReasonPhrase = responseFeature.ReasonPhrase,
            RequestMessage = request,
            Content = new PipeHttpContent(state.ResponsePipe.Reader.AsStream(), state.Cancel)
        };
        foreach (var header in responseFeature.Headers)
        {
            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        return response;
    }

    private async Task RunDispatchAsync(
        HttpContext context,
        InProcessResponseFeature responseFeature,
        InProcessRequestState state)
    {
        var ambientContext = contextAccessor.HttpContext;
        contextAccessor.HttpContext = context;
        try
        {
            await dispatcher.SelectEndpointAsync(context).ConfigureAwait(false);
            await dispatcher.DispatchAsync(context).ConfigureAwait(false);
            await responseFeature.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await responseFeature.AbortAsync(exception).ConfigureAwait(false);
        }
        finally
        {
            await responseFeature.InvokeCompletedAsync().ConfigureAwait(false);
            responseFeature.Dispose();
            state.Cancel();
            await ObserveRequestPumpAsync(state.RequestPump).ConfigureAwait(false);
            await context.Request.Body.DisposeAsync().ConfigureAwait(false);
            if (state.Scope is { } scope)
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }

            contextAccessor.HttpContext = ambientContext;
            state.Dispose();
        }
    }

    private static async Task PumpRequestAsync(
        HttpContent? content,
        PipeWriter destination,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            if (content is not null)
            {
                await content.CopyToAsync(destination.AsStream(), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            await destination.CompleteAsync(failure).ConfigureAwait(false);
        }
    }

    private static async Task ObserveRequestPumpAsync(Task requestPump)
    {
        try
        {
            await requestPump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void CopyRequestHeaders(HttpRequestMessage request, IHeaderDictionary destination)
    {
        foreach (var header in request.Headers)
        {
            if (!header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                && !header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                destination[header.Key] = header.Value.ToArray();
            }
        }

        if (request.Content is null)
        {
            return;
        }

        foreach (var header in request.Content.Headers)
        {
            destination[header.Key] = header.Value.ToArray();
        }
    }
}

internal sealed class InProcessRequestState : IDisposable
{
    private readonly CancellationTokenSource _cancellation;

    public InProcessRequestState(CancellationToken cancellationToken)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public Pipe RequestPipe { get; } = CreatePipe();
    public Pipe ResponsePipe { get; } = CreatePipe();
    public CancellationToken Token => _cancellation.Token;
    public AsyncServiceScope? Scope { get; set; }
    public Task RequestPump { get; set; } = Task.CompletedTask;
    public void Cancel()
    {
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose() => _cancellation.Dispose();

    private static Pipe CreatePipe() => new(new PipeOptions(
        pauseWriterThreshold: 64 * 1024,
        resumeWriterThreshold: 32 * 1024,
        useSynchronizationContext: false));
}

internal sealed class InProcessMaxRequestBodySizeFeature(long? maximumBodySize)
    : IHttpMaxRequestBodySizeFeature
{
    private long? _maximumBodySize = maximumBodySize;

    public bool IsReadOnly { get; private set; }

    public long? MaxRequestBodySize
    {
        get => _maximumBodySize;
        set
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException("The maximum request body size cannot be changed after reading starts.");
            }

            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _maximumBodySize = value;
        }
    }

    public void MakeReadOnly() => IsReadOnly = true;
}

internal sealed class SizeLimitedReadStream(
    Stream inner,
    InProcessMaxRequestBodySizeFeature sizeFeature) : Stream
{
    private long _bytesRead;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        sizeFeature.MakeReadOnly();
        var read = inner.Read(buffer, offset, count);
        Count(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        sizeFeature.MakeReadOnly();
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Count(read);
        return read;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void Count(int read)
    {
        _bytesRead += read;
        if (sizeFeature.MaxRequestBodySize is { } maximumBodySize && _bytesRead > maximumBodySize)
        {
            throw new Microsoft.AspNetCore.Http.BadHttpRequestException(
                "Request body too large.",
                StatusCodes.Status413PayloadTooLarge);
        }
    }
}

internal sealed class InProcessResponseFeature : IHttpResponseFeature, IHttpResponseBodyFeature, IDisposable
{
    private readonly Pipe _pipe;
    private readonly Action _abort;
    private ImmutableArray<(Func<object, Task> Callback, object State)> _starting = [];
    private ImmutableArray<(Func<object, Task> Callback, object State)> _completed = [];
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stream _stream;
    private readonly object _startLock = new();
    private PipeWriter? _writer;
    private int _completionStarted;
    private bool _startingCallbacks;
    private int _statusCode = StatusCodes.Status200OK;
    private string? _reasonPhrase;

    public InProcessResponseFeature(Pipe pipe, Action abort)
    {
        _pipe = pipe;
        _abort = abort;
        _stream = new StartingStream(pipe.Writer.AsStream(leaveOpen: true), StartAsync);
    }

    public Task Started => _started.Task;

    public int StatusCode
    {
        get => _statusCode;
        set
        {
            ThrowIfStarted();
            _statusCode = value;
        }
    }

    public string? ReasonPhrase
    {
        get => _reasonPhrase;
        set
        {
            ThrowIfStarted();
            _reasonPhrase = value;
        }
    }

    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

    public Stream Body
    {
        get => _stream;
        set => throw new NotSupportedException("The in-process response body stream cannot be replaced.");
    }

    public bool HasStarted => _started.Task.IsCompletedSuccessfully;

    Stream IHttpResponseBodyFeature.Stream => _stream;

    public PipeWriter Writer => _writer ??= PipeWriter.Create(
        _stream,
        new StreamPipeWriterOptions(leaveOpen: true));

    public void OnStarting(Func<object, Task> callback, object state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_startLock)
        {
            if (HasStarted || _startingCallbacks)
            {
                throw new InvalidOperationException("OnStarting cannot be set because the response has already started.");
            }

            _starting = _starting.Add((callback, state));
        }
    }

    public void OnCompleted(Func<object, Task> callback, object state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_startLock)
        {
            _completed = _completed.Add((callback, state));
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_startLock)
        {
            if (_started.Task.IsCompleted)
            {
                return _started.Task;
            }

            if (_startingCallbacks)
            {
                return cancellationToken.CanBeCanceled
                    ? _started.Task.WaitAsync(cancellationToken)
                    : _started.Task;
            }

            _startingCallbacks = true;
            return InvokeStartingAsync();
        }
    }

    private async Task InvokeStartingAsync()
    {
        try
        {
            for (var index = _starting.Length - 1; index >= 0; index--)
            {
                var callback = _starting[index];
                await callback.Callback(callback.State).ConfigureAwait(false);
            }

            if (Headers is HeaderDictionary headers)
            {
                headers.IsReadOnly = true;
            }

            _started.TrySetResult();
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
            throw;
        }
    }

    public async Task CompleteAsync()
    {
        if (Interlocked.Exchange(ref _completionStarted, 1) != 0)
        {
            return;
        }

        await StartAsync().ConfigureAwait(false);
        if (_writer is not null)
        {
            await _writer.CompleteAsync().ConfigureAwait(false);
        }

        await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
    }

    public void DisableBuffering()
    {
    }

    public async Task SendFileAsync(
        string path,
        long offset,
        long? count,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        await using var file = File.OpenRead(path);
        file.Seek(offset, SeekOrigin.Begin);
        if (count is null)
        {
            await file.CopyToAsync(_stream, cancellationToken).ConfigureAwait(false);
            return;
        }

        var remaining = count.Value;
        var buffer = new byte[Math.Min(64 * 1024, remaining)];
        while (remaining > 0)
        {
            var read = await file.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await _stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    public async Task AbortAsync(Exception exception)
    {
        _abort();
        _started.TrySetException(exception);
        await _pipe.Writer.CompleteAsync(exception).ConfigureAwait(false);
    }

    public async Task InvokeCompletedAsync()
    {
        ImmutableArray<(Func<object, Task> Callback, object State)> completed;
        lock (_startLock)
        {
            completed = _completed;
        }

        for (var index = completed.Length - 1; index >= 0; index--)
        {
            try
            {
                var callback = completed[index];
                await callback.Callback(callback.State).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    public void Dispose() => _stream.Dispose();

    private void ThrowIfStarted()
    {
        if (HasStarted)
        {
            throw new InvalidOperationException("The response has already started.");
        }
    }
}

internal sealed class StartingStream(Stream inner, Func<CancellationToken, Task> start) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => FlushAsync().GetAwaiter().GetResult();

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await start(cancellationToken).ConfigureAwait(false);
        await inner.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await start(cancellationToken).ConfigureAwait(false);
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

internal sealed class PipeHttpContent(Stream stream, Action abort) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) =>
        stream.CopyToAsync(target);

    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(stream);

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            abort();
            stream.Dispose();
        }

        base.Dispose(disposing);
    }
}
