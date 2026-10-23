using Explore.Domain;
using System.Text.Json.Serialization;

namespace Explore.Application.Contracts.Persistence;

public sealed record IdempotencyRequestDescriptor(
    Guid TenantId, string Key, string? UserId, string RequestMethod, string RequestTarget,
    string? RequestContentType, string RequestBodyHash, string PrincipalFingerprint)
{
    public static IdempotencyRequestDescriptor FromRecord(IdempotencyRecord record) =>
        new(record.TenantId, record.Key, record.UserId, record.RequestMethod, record.RequestTarget,
            record.RequestContentType, record.RequestBodyHash, record.PrincipalFingerprint);
}

public enum IdempotencyLookupOutcome { Absent, RequestMismatch, Existing }

public sealed record IdempotencyLookup(IdempotencyLookupOutcome Outcome, IdempotencyRecord? Record);

/// <summary>
/// Opaque original acquisition authority. Disposal never deletes or reopens a receipt.
/// A returned entity or its identifier cannot reconstruct completion authority.
/// </summary>
public interface IIdempotencyClaimOwner : IDisposable;

public sealed record IdempotencyClaim(
    IdempotencyLookup Lookup, [property: JsonIgnore] IIdempotencyClaimOwner? Owner)
{
    public bool IsOwner => Owner is not null;
    public IdempotencyRecord? Record => Lookup.Record;
}

public interface IIdempotencyRepository
{
    Task<IdempotencyLookup> LookupAsync(
        IdempotencyRequestDescriptor request, DateTime utcNow, CancellationToken cancellationToken = default);
    Task<IdempotencyClaim> TryClaimAsync(IdempotencyRecord record, CancellationToken cancellationToken = default);
    Task<bool> CompleteAsync(
        IIdempotencyClaimOwner owner,
        int statusCode,
        string? responseBody,
        string? contentType,
        CancellationToken cancellationToken = default);
    Task<int> CountExpiredAsync(DateTime expiresBeforeUtc, int batchSize, CancellationToken cancellationToken = default);
    /// <summary>
    /// Removes expired replay payloads in a bounded batch, retaining request identities
    /// as response-unavailable fences. Returns the number of receipts scrubbed, not rows deleted.
    /// </summary>
    Task<int> ScrubExpiredAsync(DateTime expiresBeforeUtc, int batchSize, CancellationToken cancellationToken = default);
}
