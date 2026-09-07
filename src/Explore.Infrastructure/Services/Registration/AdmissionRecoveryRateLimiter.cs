using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionRecoveryRateLimiter : IAdmissionRecoveryRateLimiter, IDisposable
{
    private readonly Slot[] slots;
    private readonly int permitCount;
    private readonly long windowTicks;
    private readonly byte[] partitionKey = RandomNumberGenerator.GetBytes(32);

    public AdmissionRecoveryRateLimiter(IOptions<AdmissionRecoveryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        AdmissionRecoveryOptions value = options.Value;
        if (value.RateLimitBucketCount is < 64 or > 65_536 ||
            value.RateLimitPermitCount is < 1 or > 100 ||
            value.RateLimitWindowSeconds is < 60 or > 86_400)
        {
            throw new InvalidOperationException("Admission recovery rate policy is invalid.");
        }

        slots = Enumerable.Range(0, value.RateLimitBucketCount)
            .Select(_ => new Slot())
            .ToArray();
        permitCount = value.RateLimitPermitCount;
        windowTicks = TimeSpan.FromSeconds(value.RateLimitWindowSeconds).Ticks;
    }

    public AdmissionRecoveryRateLimitDecision TryAcquire(
        Guid tenantId,
        string normalizedIdentity,
        DateTimeOffset occurredAtUtc)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(normalizedIdentity) ||
            !string.Equals(
                normalizedIdentity,
                normalizedIdentity.Trim().ToUpperInvariant(),
                StringComparison.Ordinal) ||
            occurredAtUtc == default)
        {
            throw new ArgumentException("Normalized recovery rate lineage is required.");
        }

        Slot slot = slots[Partition(tenantId, normalizedIdentity)];
        long nowTicks = occurredAtUtc.UtcTicks;
        lock (slot.Sync)
        {
            if (slot.WindowStartedAtUtcTicks == 0 ||
                nowTicks - slot.WindowStartedAtUtcTicks >= windowTicks)
            {
                slot.WindowStartedAtUtcTicks = nowTicks;
                slot.Acquisitions = 0;
            }

            if (slot.Acquisitions >= permitCount)
            {
                long remainingTicks = Math.Max(
                    TimeSpan.TicksPerSecond,
                    windowTicks - (nowTicks - slot.WindowStartedAtUtcTicks));
                return new AdmissionRecoveryRateLimitDecision(
                    false,
                    (int)Math.Ceiling(remainingTicks / (double)TimeSpan.TicksPerSecond));
            }

            slot.Acquisitions++;
            return new AdmissionRecoveryRateLimitDecision(true);
        }
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(partitionKey);

    private int Partition(Guid tenantId, string normalizedIdentity)
    {
        Span<byte> tenantBytes = stackalloc byte[16];
        tenantId.TryWriteBytes(tenantBytes);
        byte[] identityBytes = Encoding.UTF8.GetBytes(normalizedIdentity);
        byte[] input = GC.AllocateUninitializedArray<byte>(tenantBytes.Length + identityBytes.Length);
        tenantBytes.CopyTo(input);
        identityBytes.CopyTo(input, tenantBytes.Length);
        Span<byte> digest = stackalloc byte[32];
        HMACSHA256.HashData(partitionKey, input, digest);
        CryptographicOperations.ZeroMemory(input);
        CryptographicOperations.ZeroMemory(identityBytes);
        uint partition = BinaryPrimitives.ReadUInt32LittleEndian(digest);
        CryptographicOperations.ZeroMemory(digest);
        return (int)(partition % (uint)slots.Length);
    }

    private sealed class Slot
    {
        internal Lock Sync { get; } = new();
        internal long WindowStartedAtUtcTicks { get; set; }
        internal int Acquisitions { get; set; }
    }
}
