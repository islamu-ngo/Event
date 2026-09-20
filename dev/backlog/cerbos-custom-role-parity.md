# Cerbos Static-Package And Custom-Role Authorization Parity

- **Status:** Open investigation; existing gap, not fixed by credential work.
- **Owner:** implementing agent.
- **Provenance:** Original `KG-601`, graduated through provider-credential-onboarding `KG-101` at `PC-CTO-r2`.
- **Boundary:** Independent of the credential program; no credential scenario or `IVSD-*` finding is closed by this brief.

## Problem And Repository Evidence

At `1ea011a5353ec12a75340bec222c28526e9da9c8`:

- [PolicySyncService.SyncRolePoliciesAsync](../../src/Explore.Infrastructure/Services/PolicySyncService.cs) uses `roleId` only in diagnostic operation context, then calls the same package publisher. Its summary returns zero role/permission counts.
- [CerbosPolicyPackageService.BuildManifestAsync](../../src/Explore.Infrastructure/Services/CerbosPolicyPackageService.cs) enumerates bundled policy/schema files and hashes their artifacts; it does not build policies from application role rows.
- [CerbosPrincipalBuilder](../../src/Explore.Infrastructure/Services/CerbosPrincipalBuilder.cs) projects runtime role/permission facts, including event authority permission codes. [Derived roles](../../cerbos/policies/derived_roles.yaml) and resource policies consume their own declared attributes/roles; static publication alone does not establish agreement with every custom-role permission.

These observations establish why republishing the same files cannot be evidence of custom-role parity. They do not establish the full set of mismatched actions or a verified exploit. That matrix remains to be measured against actual Local and Cerbos authorization behavior.

## Prerequisites And Scope

Create a separately approved security workstream. Inventory supported custom-role create/update/delete, effective tenant/event authority, permission-to-action mapping, runtime principal construction, cache invalidation and Local/Cerbos decision paths. Bind findings to exact policy/application revisions and preserve tenant/server authority and HAL-only UI affordances.

[Credential publication PR 5](provider-credential-cerbos-publication.md) may remove no-op role publication independently. Its removal is neither a parity fix nor a prerequisite to discover this gap. Coordinate shared files if both tasks execute concurrently; do not broaden that PR to absorb this issue.

## Exclusions

No standing Admin API credential restoration, dynamic publisher added merely to preserve old behavior, credential-lifetime redesign, blanket custom-role expansion, invented policy semantics or cross-tenant permission shortcuts. No source-text assertion that a publish call disappeared may substitute for authorization outcomes. This brief grants no runtime implementation approval and carries no new I-VSD mitigation assignment.

## Red Invariants And Acceptance Criteria

- Establish a reviewable action/resource/permission matrix for advertised custom-role behavior, recording intentional provider differences explicitly rather than assuming parity.
- Write failing real decision tests for each evidenced mismatch before changing semantics. Cover custom-role grant, permission removal, role deletion and refreshed principal/cache behavior.
- Use equivalent server-derived principals/resources across Local and actual Cerbos policy evaluation. Verify expected allows and denies, wrong tenant, stale/revoked grants and insufficient authority; a mock that simply returns the expected decision is not evidence.
- Preserve tenant/event boundaries and ensure policy changes cannot expand an unrelated role or action. Coordinate cache-refresh/concurrency events deterministically, with bounded timeout and no sleeps/polling.
- Either implement approved parity or explicitly remove/limit unsupported affordances and document the supported contract. Do not report parity merely because static republishing is removed.
- Keep policy fixtures, principal mappings and operator-visible behavior consistent; capture value-free evidence with exact application/policy versions.

## Dual Documentation And Review Gate

The eventual behavior PR updates [AUTHORIZATION](../../docs/internal/AUTHORIZATION.md), applicable API/role contracts and public [authorization](../../docs/public/documentation/readme/security-and-identity/authorization.md) together, with truthful supported-role semantics and recovery after grant revocation. Include governed release impact for actual authorization changes.

Before implementation establish a new active triad, exact atomic commit contracts, threat-boundary intake and revision-bound technical/security review. Route any new provider-responsibility findings through a separate current I-VSD assessment; the [credential report](../../islamic-value-sensitive-design/workstreams/i-vsd-provider-credential-onboarding.md) explicitly excludes this semantic gap. Do not reuse its open findings as approval of a new authorization policy.

## Verification

Use in-memory rule/principal Red/Green slices and the real Cerbos policy test lane for decision parity. Per phase run one Release build and at most one selected owning project/canonical provider; at exit run affected application/infrastructure/API authorization integration, architecture and anonymized security/operations MAD. Test runtime outcomes, not prose, source calls or timing. Quarantine unrelated baseline failures. This backlog graduation runs no runtime test and claims no parity implementation.
