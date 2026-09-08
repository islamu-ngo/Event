// ABOUTME: Adds bounded effective-IP, subnet, and process concurrency limits to anonymous challenge/start intake.
// ABOUTME: Uses process-keyed fixed bucket partitions with no raw network identifiers or durable seat counters.

using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Extensions;

public static partial class RateLimitingExtensions
{
    public const string AnonymousRegistrationPolicy = "anonymous_registration";
    private const int AnonymousNetworkBuckets = 4096;

    private static void AddAnonymousRegistrationRateLimiting(IServiceCollection services)
    {
        // Resolve final native host configuration, including composed-host overrides, when options initialize.
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            IConfiguration section = configuration.GetSection("RateLimiting:AnonymousRegistration");
            int ipLimit = Bounded("IpPermitLimit", 10, 1, 1000);
            int subnetLimit = Bounded("SubnetPermitLimit", 40, 1, 10000);
            int windowSeconds = Bounded("WindowSeconds", 60, 1, 3600);
            int concurrencyLimit = Bounded("ConcurrencyLimit", 8, 1, 128);
            int queueLimit = Bounded("QueueLimit", 0, 0, 64);
            byte[] partitionKey = RandomNumberGenerator.GetBytes(32);

            options.AddPolicy(AnonymousRegistrationPolicy, context =>
                Window($"ip:{NetworkBucket(context, subnet: false)}", ipLimit));
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                options.GlobalLimiter!,
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsAnonymousIntake(context)
                    ? Window($"subnet:{NetworkBucket(context, subnet: true)}", subnetLimit)
                    : RateLimitPartition.GetNoLimiter("not-anonymous-intake")),
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsAnonymousIntake(context)
                    ? RateLimitPartition.GetConcurrencyLimiter(AnonymousRegistrationPolicy, _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = concurrencyLimit,
                        QueueLimit = queueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    })
                    : RateLimitPartition.GetNoLimiter("not-anonymous-intake")));

            int Bounded(string name, int fallback, int minimum, int maximum)
            {
                int value = section.GetValue(name, fallback);
                return value >= minimum && value <= maximum
                    ? value : throw new InvalidOperationException($"RateLimiting:AnonymousRegistration:{name} is outside its supported bounds.");
            }

            RateLimitPartition<string> Window(string key, int limit) =>
                RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                });

            int NetworkBucket(HttpContext context, bool subnet)
            {
                IPAddress? address = ResolveClientIp(context);
                if (address?.IsIPv4MappedToIPv6 == true)
                    address = address.MapToIPv4();
                byte[] bytes = address?.GetAddressBytes() ?? [];
                if (subnet && bytes.Length > 0)
                {
                    // IPv4 /24 and IPv6 /56; unknown clients share one conservative bucket.
                    Array.Clear(bytes, bytes.Length == 4 ? 3 : 7, bytes.Length == 4 ? 1 : 9);
                }
                byte[] hash = HMACSHA256.HashData(partitionKey, bytes);
                return (int)(BinaryPrimitives.ReadUInt32BigEndian(hash) % AnonymousNetworkBuckets);
            }
        });
    }

    private static bool IsAnonymousIntake(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == AnonymousRegistrationPolicy;
}
