ABOUTME: The single authority for turning a ClaimsPrincipal into a platform user id.
ABOUTME: Prevents the divergent per-caller extraction helpers this repository previously accumulated.

# User ID Extraction

## Rule (Required)

**Do not write an extraction helper.** Call the one that exists:

```csharp
using Explore.Application.Authentication;

Guid? userId = principal.GetPlatformUserId();          // null when unauthenticated
Guid required = principal.GetRequiredPlatformUserId(); // throws UnauthorizedAccessException
```

In a controller, `ExploreControllerBase` already projects these as `CurrentUserId` and `RequiredUserId`.
In Application code that cannot see a principal, take `IUserContext` — it delegates to the same extensions.

## The Chain

`PlatformIdentityPrincipalExtensions.GetPlatformUserId` tries, in order, accepting only GUID-parseable values:

1. `sub`
2. `ClaimTypes.NameIdentifier`
3. `sid`
4. `internal_user_id`

The provider claims come **before** `internal_user_id` deliberately: for platform-managed accounts the provider
subject *is* the local user id, and preferring it keeps one identifier authoritative. This ordering is pinned by
`Explore.Infrastructure.Tests/Identity/UserContextTests.cs` — treat it as a contract, not a default.

## When The Subject Is Not A Platform User Id

ATProto DIDs and Google subjects are not GUIDs, so the chain above returns `null` unless `internal_user_id` is
present. Resolve the linked local account instead of guessing:

```csharp
// Inject IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?> identityQuery.
Guid? userId = await identityQuery.ResolveCurrentUserIdAsync(User, cancellationToken);
```

A reconstructed provider account takes precedence over GUID/internal-user claims. The exact provider-account
binding is authoritative, with no email fallback. Only when no provider identity is available does resolution
use the existing platform-ID chain. A `null` result is an authentication outcome to map, never a reason to
fall back to a different identity source. The native query is deliberately unannotated identity resolution;
its protected DI wrapper does not invent a PDP capability.

## Provider Bootstrap

First-login and account-sync flows need the external account, not the local id:

```csharp
ProviderIdentity? identity = User.GetProviderIdentity();  // null => treat as unauthenticated
```

It returns the subject, provider (`keycloak` / `google` / `atproto`), provider id (the DID for ATProto),
email, and verified flag as one value, so a partially-populated identity cannot be assembled by accident.

## Purpose-Bound Schemes Are Separate

API-key, setup-secret, managed-control-plane, ATProto session, and privacy-erasure receipt principals validate
their own claims at the authentication boundary. They are protocol validation, not ambient user identity.
Do **not** route them through the chain above — merging them widens trust.

## Why This Is A Rule

Three subtly different chains once coexisted here: `UserContext` used four claims, a principal extension used
two, and a base controller preferred `internal_user_id` first. They disagreed about who the caller was.
Compiled controller dependency checks reject request-container service
location, while principal-extension and controller behavior tests exercise the
single canonical claim chain. Purpose-bound authentication schemes remain
separate by protocol behavior rather than a source-file allowlist.

**Related**: `auth-patterns` skill; `docs/internal/CODEBASE_INSIGHTS.md` §15.
