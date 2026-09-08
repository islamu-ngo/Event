<!-- ABOUTME: Domain journal for authentication, authorization, multi-tenancy, and security trust boundaries. -->
<!-- ABOUTME: Captures durable findings on Cerbos policies, Keycloak, BFF tokens, and tenant isolation. -->

# Auth & Multi-Tenancy Knowledge Ledger

> **Scope**: Authentication, Cerbos policies, Keycloak, BFF token forwarding, tenant resolution, and security boundaries.

---

## 1. Architectural Decisions

- **Single User ID Authority**: Derive user identity exclusively via `Explore.Application.Authentication.PlatformIdentityPrincipalExtensions` (`sub` $\rightarrow$ `nameidentifier` $\rightarrow$ `sid` $\rightarrow$ `internal_user_id`). Never parse raw claims or re-derive identity ad-hoc in controllers.
- **Central Tenant Isolation**: Enforce tenant isolation centrally via EF Core global query filters in `ExploreDbContext`. Do not bypass query filters without documented architectural review.
- **BFF Anti-Spoofing**: Trust tenant and user headers only from the verified BFF gateway; strip incoming client `X-Tenant-Slug` on public edge.
- **Write-Only Secrets in Dashboards**: Tenant provider secrets must be accepted only at write boundaries. Read DTOs, HAL responses, and metrics must remain redacted and expose only boolean configuration status or aggregate counts.

---

## 2. Technical Insights & Patterns

- **Consent Audit FKs Must Match Aggregate Roots**: When an operation creates a parent and child rows atomically (e.g. `EventRegistrationIntent` parent + multiple child `EventRegistration` rows), audit foreign keys (like `EventContactShareConsent`) must target the parent aggregate root (`SourceEventRegistrationIntentId`), not an arbitrary child row.
- **Cerbos Authorization Attributes Use Organization ID, Not Actor ID**: Cerbos `org_admin` derived roles check `resource.attr.organizationId` against the user's organization membership. Because `Actor.Id` and `Organization.Id` are distinct GUIDs, controllers/handlers must resolve `recipientActorId → Actor.OrganizationId` server-side before evaluating authorization.
- **`ISecureRequest.ResourceAttributes` Resolution Precedes Handler Execution**: `AuthorizationBehavior<TRequest, TResponse>` pulls `ResourceAttributes` synchronously before MediatR handler invocation. Any contextual lookup required for authorization must be resolved before `_mediator.Send`.
- **Event-Child Fallback Authorization Must Validate Resource Tenant**: Optimized batch fallback authorization must resolve event context from resource attributes and verify `resourceTenantId == profile.TenantId` before allowing access, failing closed on tenant mismatch.
- **Event-Scoped Operational Roles**: `EventRoleAssignment` serves as the persisted event-instance grant using canonical effective predicate `Status == Active && StartsAtUtc <= now && (ExpiresAtUtc IS NULL OR ExpiresAtUtc > now)`.

---

## 3. Failed Approaches & Lessons

- **Mocking `IUserContext` in Controller Tests**: Mocking `IUserContext` through the container prevents testing the real claim chain. Controller tests must set real claims on `ControllerContext.HttpContext.User`.
- **Client-Side Role Checks for UI Affordances**: Checking user roles/claims in Blazor components to gate actions is forbidden. Gating must rely solely on the presence of HAL `_links` returned by the server.

[2026-09-06 Europe/Brussels] — Expiry must be checked after awaited authority work

**Context**: Adversarial verification of database-backed ATProto login exercised committed consumption and durable machine-assertion admission across independent API/BFF clocks.

**Symptom / Observation**: A handoff consumed before its deadline could arrive at expiry and still issue a cookie while browser proof remained live. Separately, a replay INSERT admitted just before expiry could wait until cleanup removed its original claim, then commit and dispatch the identical assertion again. Real HTTP/PostgreSQL regressions observed both failures.

**Root Cause**: A pre-await expiry check establishes eligibility only at that checkpoint. Exact returned-candidate equality does not establish freshness after HTTP delivery; successful database commit does not establish assertion freshness after persistence. A ten-second replay-retention margin covers the supported pairwise host-clock difference, but cannot stop an INSERT already admitted before the cleanup boundary.

**Resolution**: The shared BFF transport rechecks expiry after response equality. The replay repository rechecks its unchanged acceptance expiry after `SaveChangesAsync`, returning false for a late commit while retaining its claim. Neither path retries or compensates a committed operation. With the documented test environment/secret authority, `dotnet run --project tests/Event.API.IntegrationTests --configuration Release -- --treenode-filter "/*/*/*AtprotoRelationalLoginFlowTests/*"` passed seven cases; the equivalent `AtprotoTransientAuthenticationTests` filter passed 58, including the prior-used and first-use delayed INSERT cases. Full phase acceptance remains separate from these focused proofs.

**Why This Matters for Future Work**: Test expiry at the completion boundary, not just before request admission. To prove committed deletion rather than expiry-filtered absence, keep the database reader's clock before the credential deadline while advancing only the receiving BFF clock. For replay claims, coordinate a real preINSERT barrier and independent cleanup context; a longer retention period alone does not prove in-flight admission safety.

**References**:

- `src/Explore.Blazor/Services/Auth/ApiBackedAtprotoTransientStore.cs:73`
- `src/Explore.Persistence/Repositories/AtprotoTransientAssertionReplayRepository.cs:35`
- `tests/Event.API.IntegrationTests/Authentication/AtprotoRelationalLoginFlowTests.cs:155`
- `tests/Event.API.IntegrationTests/Authentication/AtprotoTransientAuthenticationTests.cs:284`
- `docs/internal/adr/ADR-014-atproto-session-trust-bridge.md`

**Promotion Consideration**:

- [x] Stays in journal as regression-design evidence; the architectural contract is recorded in ADR-014.

---

[2026-09-06 Europe/Brussels] — Security rejection tests must prove boundary reachability

**Context**: Usable-store readiness became a prerequisite for ATProto login. Existing hostile discovery tests used BFF fixtures without the private relational API.

**Symptom / Observation**: Some tests failed with the new readiness response, but other rejection tests could remain green because an earlier dependency/discovery failure also rejected the request. Temporarily forcing a missing PDS made all three unsafe authorization-endpoint cases fail their metadata-reachability assertion.

**Root Cause**: An expected rejection and absence of persisted login state prove that a request was denied, not which security boundary denied it. The fixture could stop before consuming the hostile metadata under test.

**Resolution**: Move the eleven affected cases to the existing real Production BFF/private API/PostgreSQL fixture. Keep external-provider response controls only. Assert that the expected DID-document or authorization-metadata request occurred, followed by the exact rejection, no pushed authorization request and no login state. The eleven-case `AtprotoRelationalPreflightTests` class passed after restoring the deliberate missing-PDS fault: `dotnet run --project tests/Event.API.IntegrationTests --configuration Release -- --treenode-filter "/*/*/*AtprotoRelationalPreflightTests/*"`.

**Why This Matters for Future Work**: When adding an earlier admission gate, audit downstream negative tests for false greens. Observe the real external boundary and resulting state; do not mock readiness success or replace meaningful tests with source-text assertions. A deliberately earlier failure should break the test's reachability assertion.

**References**:

- `tests/Event.API.IntegrationTests/Authentication/AtprotoRelationalPreflightTests.cs:75`
- `tests/Event.API.IntegrationTests/Authentication/AtprotoRelationalLoginFixture.cs`
- `src/Explore.Blazor/Services/Auth/BffProviderReadinessService.cs`

**Promotion Consideration**:

- [x] Stays in journal as reusable security-test design evidence.

---

[2026-09-06 Europe/Brussels] — Health-probe stampedes can consume login admission

**Context**: ATProto readiness gained an authenticated synthetic create/read/consume probe through the same private admission boundary used by login-state requests.

**Symptom / Observation**: A held cold-cache probe followed by concurrent readiness callers produced 66 outbound requests in the Red test. Health checks could exhaust the shared per-minute admission budget and interfere with ordinary login.

**Root Cause**: Caching completed results does not coalesce concurrent misses. Every caller could see an empty cache and independently perform the expensive, rate-limited probe.

**Resolution**: Use the existing singleton readiness service's semaphore with a second cache check after acquisition. Cache completed outcomes for ten seconds; bound waiting and transport together, and preserve individual waiter cancellation. The four-case `AtprotoOperationalReadinessTests` class passed after the correction using `dotnet run --project tests/Event.API.IntegrationTests --configuration Release -- --treenode-filter "/*/*/*AtprotoOperationalReadinessTests/*"`. Its concurrency case observes one real outbound request with 64 followers, cancels a separate waiter and then successfully creates ordinary OAuth state with production rate limiting enabled.

**Why This Matters for Future Work**: Health traffic can compete with the capability it measures. Prove that concurrent cache misses preserve ordinary admission, not just that a cached health result is correct. Coalescing is per BFF instance, not a fleet-wide quota guarantee; replica probe traffic still consumes the configured instance limiter.

**References**:

- `src/Explore.Blazor/Services/Auth/BffProviderReadinessService.cs:175`
- `tests/Event.API.IntegrationTests/Authentication/AtprotoOperationalReadinessTests.cs:22`
- `docs/internal/OPERATIONS.md`

**Promotion Consideration**:

- [x] Stays in journal as concurrency-regression evidence; readiness behavior belongs in the operator contract.

---
