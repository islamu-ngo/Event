// ABOUTME: Marks guest allocation endpoints requiring bound proof before idempotency claim or replay.
// ABOUTME: The middleware authenticates this metadata before any cached guest capability is disclosed.

namespace Explore.API.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireAnonymousRegistrationChallengeAttribute : Attribute;
