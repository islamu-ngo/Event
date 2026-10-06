namespace Explore.API.Attributes;

/// <summary>
/// Marks a controller or action whose write requests require an Idempotency-Key header.
/// </summary>
/// <remarks>
/// The generic idempotency middleware rejects missing, empty or whitespace-only required keys
/// and validates admitted keys before ordinary response capture and replay. Non-write requests
/// are unaffected. When <see cref="SuppressIdempotencyResponseStorageAttribute"/> is present,
/// the middleware bypasses this validation too: the endpoint and application operation must
/// enforce header cardinality and operation-key grammar themselves. This marker alone neither
/// establishes issuance authority nor makes credential replay safe.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    Inherited = true,
    AllowMultiple = false)]
public sealed class RequireIdempotencyKeyAttribute : Attribute;
