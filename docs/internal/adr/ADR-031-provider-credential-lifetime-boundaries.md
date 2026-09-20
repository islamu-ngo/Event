# ADR-031: Provider Credential Lifetime Boundaries

> **Status:** Accepted design direction; custody migration deferred
> **Date:** 2026-09-20
> **Owner:** implementing agent
> **Scope:** Keycloak/Cerbos provider authority and runtime-secret custody
> **Reviewed revision:** PC-CTO-r2

## Context

Provider name is not a safe credential-lifetime boundary. The reviewed repository mixes human administration, runtime authentication, provider infrastructure and policy publication under application-readable configuration/bindings. Authentication client secrets are JSON strings in `SystemSetting.Value`; the BFF consumes a secret-bearing internal API read. Recursive Infisical reads acquire values before post-fetch mapping can reject them. Stored binding coordinates and caches can outlive a registry-default change.

Source anchors are [AuthProviderConfigurationService](../../../src/Explore.Application/Services/AuthProviderConfigurationService.cs), [DynamicAuthSchemeManager](../../../src/Explore.Blazor/Services/DynamicAuthSchemeManager.cs), [SecretResolver](../../../src/Explore.Secrets/Services/SecretResolver.cs), the [shared Infisical provider](../../../src/Explore.Secrets/Configuration/InfisicalConfigurationProvider.cs) and [BFF provider](../../../src/Explore.Blazor/Configuration/InfisicalConfigurationProvider.cs). [Keycloak lifecycle email](../../../src/Explore.Infrastructure/Services/Keycloak/KeycloakAccountAuthorityLifecycleEmailService.cs) uses password-grant administration. [Cerbos publication](../../../src/Explore.Infrastructure/Services/CerbosPolicyPackageService.cs) and [role sync](../../../src/Explore.Infrastructure/Services/PolicySyncService.cs) retain runtime publication paths. These are source/design evidence, not claims of a production incident or measured security outcome.

## Decision: Three Credential Lifetime Classes

| Class | Permitted purpose and custody | Prohibited custody |
| --- | --- | --- |
| Request-scoped administration | Explicitly authorized, bounded synchronous Keycloak/Cerbos operation; complete fresh pair, transient server-process observation, discard on every terminal path | DB, secret registry/bindings, runtime configuration, browser storage, replay, jobs/outbox, logs/traces/support artifacts |
| Deployment provisioning | One-shot provider initialization, provider infrastructure and operator/CI policy publication; purpose-specific external authority and identities | API/BFF runtime identities or broadly recursive provider folders |
| Runtime service identity | Necessary recurring capability with selected external authority, exact consumer binding and least privilege; confidential BFF OIDC or dedicated API lifecycle client | Human-admin reuse, BFF/API identity reuse, application DB values, fallback sources |

The accepted target is no durable application retention of provider-administrator credentials. It is not yet implemented end to end. No physical erasure guarantee is made for managed memory, kernels, dumps, backups or storage hardware.

Runtime secrets belong only to the selected approved authority. Ready writable Infisical bindings support a capability-specific write-only authentication writer. Environment and explicitly selected shared User Secrets in Development/Testing remain operator-written and require restart/refresh; User Secrets elsewhere and cross-authority fallback are rejected. Databases hold non-secret settings and reference metadata, never raw or reversibly encrypted secret values.

Exact provider roots are `/runtime/bff/oidc/keycloak`, `/runtime/bff/oidc/google`, `/runtime/api/authentication/keycloak`, `/runtime/api/authorization/cerbos`, `/provisioning/keycloak`, `/provisioning/cerbos`, `/infrastructure/keycloak/database` and `/infrastructure/cerbos`. Admit paths before acquisition; no unsafe recursive/reference-expanded fetch. Combined hosts receive the exact API+BFF runtime union, not provisioning authority. Preserve unrelated approved service roots.

Keycloak human-admin operations remain explicit and transient. Recurring lifecycle email instead uses a dedicated API-only OAuth client-credentials identity with real-provider minimum-role evidence and deployment overprivilege validation. Token success is not proof of least privilege.

Cerbos runtime decisions do not require Admin API credentials in the accepted target. Explicit one-time setup/admin publication, CI/GitOps and manual package upload remain supported. Runtime health, historical publication receipt and credentialed live-store equality are separate claims. Removing static role-change publication does not fix the independent custom-role parity gap.

## Implementation Status And Delivery Boundaries

Behavior commit **`1ea011a5353ec12a75340bec222c28526e9da9c8` (H)** shipped the existing endpoint metadata for seven credential POSTs and cache policy for the internal auth-configuration GET:

- `PrivateNoStore` preserves `Cache-Control: private, no-store`, `Pragma: no-cache` and `Referrer-Policy: no-referrer` where a response exists, including early denial.
- `SuppressIdempotencyResponseStorage` bypasses generic replay before identity/repository access. Retries re-enter current authority and provider outcome; old replay success cannot stand in for current permission.
- Routes, DTOs, HAL, provider reconciliation, DB values, secret acquisition, BFF/UI and deployment behavior remain unchanged. Historical replay rows are neither read nor purged by H. H does not prove log/browser erasure or provider-side deduplication.

Packet K records this decision, unchanged review evidence and follow-up contracts only. No custody migration is delivered by this ADR. The [HTTP boundary tests](../../../tests/Event.API.IntegrationTests/Features/ProviderCredentialHttpBoundaryTests.cs) are H's executable specification; the active execution ledger records prior Red/Green results, not new test runs by this documentation packet.

| Deferred delivery | Durable owner packet | Original IDs |
| --- | --- | --- |
| PR 2 external auth authority, BFF and cleanup | [Runtime authority](../../../dev/backlog/provider-credential-runtime-authority.md) | D1 `PC-101` through `PC-104`; auth-only D6 `DP-401` / `DP-402` |
| PR 3 complete one-time Keycloak contracts/UI | [Keycloak administration](../../../dev/backlog/provider-credential-keycloak-admin.md) | D2 `KC-201` through `KC-203` |
| PR 4 dedicated lifecycle identity | [Keycloak lifecycle](../../../dev/backlog/provider-credential-keycloak-lifecycle.md) | D3 `KC-204` through `KC-206` |
| PR 5 coupled one-time Cerbos/publication cutover | [Cerbos publication](../../../dev/backlog/provider-credential-cerbos-publication.md) | D4 `CB-301` through `CB-303`; D5 `CB-304` through `CB-306` |
| PR 6 remaining deployment isolation | [Deployment isolation](../../../dev/backlog/provider-credential-deployment-isolation.md) | remaining D6 `DP-401` through `DP-403` |
| Outside credential program | [Custom-role parity](../../../dev/backlog/cerbos-custom-role-parity.md) | original `KG-601` |

Each implementing agent owns a fresh active triad, exact atomic commit contracts, current revision-bound I-VSD/CTO review and proportional runtime verification before implementing its packet. Program approval does not waive these gates. Original umbrella phase commits are withdrawn.

## Mandatory Sequencing And Clean Breaking Posture

1. **Writer and BFF before cleanup:** provision external values; deploy no-DB-write code and replace `DynamicAuthSchemeManager` cold-start/refresh plus the internal secret-bearing contract/generated consumers together. Fence/drain old writers/readers before enabling exact transactional auth cleanup. Test no resurrection with deterministic writer barriers. Do not delete still-live Cerbos admin bindings in PR 2.
2. **Operational expand/contract, not dual custody:** provision first, converge new consumers, contract storage afterwards. No aliases, dual reads/writes, deprecated DTOs or old-folder fallback. If safe rolling convergence is unproved, require an operator-approved maintenance cutover. Generated EF artifacts are never hand-edited; no schema migration is assumed for exact data cleanup.
3. **Publication before runtime-admin removal:** establish same-revision/hash/environment Cerbos publication readiness before removing startup/role publishers, config/cache and fallback. D4/D5 share one release boundary. Separate workflows require an actual reusable dependency or validated receipt; a cross-workflow `needs` reference is not a gate.
4. PR 3 depends on PR 2; PR 4 also depends on PR 2 but not its UI follow-up. Remaining PR 6 isolation follows PRs 2-5. Consumer removal owns matching binding eradication; a vendor-wide early deletion is forbidden.

Preserve native Application CQS, Domain dependency direction, manually instantiated validators, server-owned setup/admin authority, tenant-qualified runtime targets and HAL affordances. Provider HTTP calls stay outside DB transactions. Credentials never enter outbox/jobs; genuine domain-event side effects retain the existing transactional outbox with value-free payloads.

## Rejected Alternatives

- One provider folder or "everything in Infisical": encrypted storage does not justify broad application read authority or excessive lifetime.
- Application DB encryption, file store or API secret cache as the authority: introduces another secret store and backup/recovery burden rather than removing custody.
- Post-fetch filtering: a rejected value has already crossed the acquisition boundary.
- Browser-direct administration: bypasses the BFF/server authority boundary and exposes provider details/transport concerns to clients.
- Human password-grant lifecycle automation or BFF-client reuse: conflates independent purposes and blast radii.
- Standing Cerbos Basic Auth plus best-effort startup/role publishing: runtime decisions gain unnecessary control-plane authority; static republish does not consume role rows.
- Big-bang cleanup or removal before replacement consumers/publication: breaks cold restart or permits app/policy mismatch.
- Compatibility aliases, dual stores and credential-bearing retry jobs: preserve obsolete custody and violate request-scoped operation.
- Removing all automation: burdens operators unnecessarily; narrow runtime service identities and operator CI/manual tools remain valid.

## Recovery, Revocation And Consequences

Custody rollback is forward-fix, not restoration of retired credentials. Re-provision runtime values in the selected authority, restart/drain replicas, verify readiness, then revoke old provider credentials and machine permissions. Backups/snapshots may retain historical values; disclose that exposure and rotate/revoke rather than claiming physical erasure. A restored backup does not grant authority to use retired secrets.

External provider mutation can precede local failure. Return a bounded partial-completion code and safe additive reconciliation action; never claim provider rollback or queue the original credential. Temporary administrator accounts/tokens need operator revocation guidance. Lifecycle recovery may disable delegation or restore a valid service-client value, never human password-grant configuration. Cerbos failure halts deployment or uses operator-owned publication of a previously validated package; it never reinstates runtime admin fallback.

Environment-only operators may need to pause activation and inject values/restart. Public operator guides and internal technical contracts must change together in each delivery. No new secret provider/dependency, destructive realm reimport, generic secret CRUD or custom-role redesign is authorized here.

## I-VSD Traceability And Evidence Limits

The unchanged [provider-credential I-VSD report](../../../islamic-value-sensitive-design/workstreams/i-vsd-provider-credential-onboarding.md) is **current / plan-aligned for PC-CTO-r2**, a planning snapshot, not current execution telemetry. Its historical statements that tests/briefs were pending remain intentionally unchanged. Execution completion belongs to Git and the ignored ledger; all six findings remain open.

Exact provenance:

- Reviewed plan SHA-256: `d65973b1c6299240a06429fdafa6fc440a7bbeb5148f972e65ff179f1255f9ab`.
- Reviewed tasks SHA-256: `a06d6fde4ab01bab1e86a687932f1243f10cd6c29bf9a374dc3cf61962fa7ae2`. Later execution checkbox updates are not a design revision.
- Report SHA-256: `f9465aa04f07558b4cd0c555eb9a76c345b6c8c97e8ce130388bf49eb596e437`.

| Finding / mitigation | Preserved mapping and remaining delivery |
| --- | --- |
| `IVSD-F001` / `IVSD-M001` | `SCN-CRED-001` through `SCN-CRED-004`; D1 `PC-101`, D2 `KC-201`, D4 `CB-301`; PRs 2/3/5 |
| `IVSD-F002` / `IVSD-M002` | `SCN-PATH-001` / `SCN-PATH-002`; D6 `DP-401` through `DP-403`; PR 2 auth roots / PR 6 remaining roots |
| `IVSD-F003` / `IVSD-M003` | `SCN-STORE-001` through `SCN-STORE-003`; D1 `PC-101` through `PC-104`; PR 2 |
| `IVSD-F004` / `IVSD-M004` | `SCN-KC-003`, `SCN-CB-002`, `SCN-CB-003`; D3 `KC-205`, D4 `CB-302`, D5 `CB-305`; PRs 4/5 |
| `IVSD-F005` / `IVSD-M005` | H: `SCN-HTTP-001` through `SCN-HTTP-004`, `HTTP-101` through `HTTP-103`; remaining `SCN-OBS-001` through `SCN-OBS-003`, D1 `PC-101`, D2 `KC-202`, D4 `CB-302`; PRs 2/3/5 |
| `IVSD-F006` / `IVSD-M006` | H continuity: `SCN-HTTP-004`, `HTTP-103`; remaining `SCN-OPS-001` through `SCN-OPS-003`, D2 `KC-203`, D4 `CB-303`, D5 `CB-305`, D6 `DP-402` / `DP-403`; PRs 2/3/5/6 |

`KG-101` owns graduation, not closure. Refresh review whenever lifetime, provider authority, database custody, machine paths, lifecycle automation, publication ownership, recovery or mapped acceptance changes. No new stakeholder/usability/provider-role evidence is inferred. This is provider-responsibility design, not a fatwa, religious-legal ruling, product certification or proof of ethical/security outcomes; religious-legal conclusions belong to qualified Sunni scholarly authority.
