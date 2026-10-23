using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

public static partial class PostgresIdempotencyReceiptContract
{
    internal static async Task<IdempotencyLookup?> TryReadAsync(
        ExploreDbContext context, Func<PostgresNativeQueueEnrollment> enrollment,
        IdempotencyRequestDescriptor request, DateTime utcNow, CancellationToken token) =>
        context.Database.IsNpgsql()
            ? await ReadAsync(context, enrollment(), request, utcNow, token)
            : null;

    internal static async Task<NativeClaim?> TryClaimAsync(
        ExploreDbContext context, Func<PostgresNativeQueueEnrollment> enrollment,
        IdempotencyRecord proposal, ReadOnlyMemory<byte> nonce, CancellationToken token) =>
        context.Database.IsNpgsql()
            ? await ClaimAsync(context, enrollment(), proposal, nonce,
                context.Database.CurrentTransaction is not null, token)
            : null;

    internal static async Task<bool?> TryCompleteAsync(
        ExploreDbContext context, Func<PostgresNativeQueueEnrollment> enrollment,
        Guid tenant, Guid recordId, ReadOnlyMemory<byte> nonce, int status, string? body, string? contentType,
        DateTime utcNow, CancellationToken token) =>
        context.Database.IsNpgsql()
            ? await CompleteAsync(context, enrollment(), tenant, recordId, nonce, status, body, contentType, utcNow, token)
            : null;

    internal static async Task<int?> TryCountExpiredAsync(
        ExploreDbContext context, Func<PostgresNativeQueueEnrollment> enrollment,
        Guid tenant, DateTime cutoff, int maximumRows, CancellationToken token) =>
        context.Database.IsNpgsql()
            ? await CountExpiredAsync(context, enrollment(), tenant, cutoff, maximumRows, token)
            : null;

    internal static async Task<int?> TryScrubExpiredAsync(
        ExploreDbContext context, Func<PostgresNativeQueueEnrollment> enrollment,
        Guid tenant, DateTime cutoff, int maximumRows, CancellationToken token) =>
        context.Database.IsNpgsql()
            ? await ScrubExpiredAsync(context, enrollment(), tenant, cutoff, maximumRows, token)
            : null;
}
