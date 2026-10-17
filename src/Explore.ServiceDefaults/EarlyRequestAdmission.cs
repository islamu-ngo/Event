using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Explore.ServiceDefaults;

/// <summary>
/// Process-local, zero-queue admission before identity/provider work. Peer shards
/// are bounded conservative budgets, not identity or tenant authority.
/// </summary>
public sealed class EarlyRequestAdmission(TimeProvider timeProvider, bool enabled)
{
    private const int TotalConcurrency = 32;
    private const int InternalConcurrency = 8;
    private const int PeerPermits = 600;
    private const int NetworkPermits = 6000;
    private const int InternalPermits = 1200;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly object ActiveLease = new();
    private readonly object _sync = new();
    private readonly Bucket[] _peers = new Bucket[4096];
    private readonly Bucket[] _networks = new Bucket[1024];
    private Bucket _internal;
    private int _inFlight;
    private int _internalInFlight;

    public bool IsEnabled { get; } = enabled;

    /// <summary>Called only by the native server dispatcher; no header creates this feature.</summary>
    public static void MarkInternalRequest(HttpContext context) =>
        context.Features.Set(InternalRequest.Instance);

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.RequestAborted.ThrowIfCancellationRequested();
        if (!IsEnabled || context.Items.TryGetValue(ActiveLease, out var active) && ReferenceEquals(active, this))
        {
            await next(context);
            return;
        }

        bool internalRequest = ReferenceEquals(context.Features.Get<InternalRequest>(), InternalRequest.Instance);
        var (peer, network) = Partition(context.Connection.RemoteIpAddress);
        bool admitted;
        lock (_sync)
        {
            long now = timeProvider.GetTimestamp();
            admitted = _inFlight < TotalConcurrency;
            if (internalRequest)
            {
                ResetWindow(ref _internal, now);
                admitted &= _internalInFlight < InternalConcurrency && _internal.Count < InternalPermits;
                if (admitted)
                {
                    _internal.Count++;
                    _internalInFlight++;
                }
            }
            else
            {
                ResetWindow(ref _peers[peer], now);
                ResetWindow(ref _networks[network], now);
                admitted &= _peers[peer].Count < PeerPermits && _networks[network].Count < NetworkPermits;
                if (admitted)
                {
                    _peers[peer].Count++;
                    _networks[network].Count++;
                }
            }
            if (admitted) _inFlight++;
        }

        if (!admitted)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "1";
            context.Response.Headers.CacheControl = "no-store";
            return;
        }

        context.Items[ActiveLease] = this;
        try
        {
            await next(context);
        }
        finally
        {
            context.Items.Remove(ActiveLease);
            lock (_sync)
            {
                _inFlight--;
                if (internalRequest) _internalInFlight--;
            }
        }
    }

    private void ResetWindow(ref Bucket bucket, long now)
    {
        if (!bucket.Initialized || timeProvider.GetElapsedTime(bucket.Started, now) >= Window)
        {
            bucket.Started = now;
            bucket.Count = 0;
            bucket.Initialized = true;
        }
    }

    private static (int Peer, int Network) Partition(IPAddress? address)
    {
        if (address is null) return (0, 0);
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out int length);
        var peer = new HashCode();
        var network = new HashCode();
        peer.Add(length);
        network.Add(length);
        int networkLength = length == 4 ? 3 : 8;
        for (int i = 0; i < length; i++)
        {
            peer.Add(bytes[i]);
            if (i < networkLength) network.Add(bytes[i]);
        }
        return ((int)((uint)peer.ToHashCode() % 4096), (int)((uint)network.ToHashCode() % 1024));
    }

    private struct Bucket
    {
        public bool Initialized;
        public long Started;
        public int Count;
    }

    private sealed class InternalRequest
    {
        private InternalRequest() { }
        public static InternalRequest Instance { get; } = new();
    }
}

public static class EarlyRequestAdmissionExtensions
{
    public static IServiceCollection AddEarlyRequestAdmission(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        bool disabled = configuration.GetValue<bool>("EarlyAdmission:DisableInTesting");
        if (disabled && !environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("Early admission may be disabled only in Testing.");
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton(provider => new EarlyRequestAdmission(
            provider.GetRequiredService<TimeProvider>(), !disabled));
        return services;
    }

    public static IApplicationBuilder UseEarlyRequestAdmission(this IApplicationBuilder app) =>
        app.Use(next => context => context.RequestServices.GetRequiredService<EarlyRequestAdmission>()
            .InvokeAsync(context, next));
}
