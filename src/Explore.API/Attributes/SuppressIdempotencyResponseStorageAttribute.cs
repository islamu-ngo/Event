namespace Explore.API.Attributes;

/// <summary>
/// Bypasses generic idempotency response capture and replay for an action whose response must not be stored.
/// </summary>
/// <remarks>
/// On write requests the middleware passes directly to the endpoint before header validation,
/// claim creation or cached-response lookup, even when <see cref="RequireIdempotencyKeyAttribute"/>
/// is also present. The endpoint and application operation must enforce any required operation key
/// and own duplicate/conflict recovery. API-key issuance uses digest-only receipts for metadata
/// recovery so the original plaintext credential is never stored or replayed by this middleware.
/// This attribute does not itself prevent browser or intermediary caching.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SuppressIdempotencyResponseStorageAttribute : Attribute;
