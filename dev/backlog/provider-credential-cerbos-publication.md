# Cerbos One-Time Administration And Operator-Owned Publication

- **Status:** Deferred; no runtime administration removed by this brief.
- **Owner:** implementing agent.
- **Delivery:** PR 5; D4 `CB-301`, `CB-302`, `CB-303` and D5 `CB-304`, `CB-305`, `CB-306`, together as one release-safety boundary.
- **Provenance:** `PC-CTO-r2`, graduated by `KG-101`.
- **Decision:** [ADR-031](../../docs/internal/adr/ADR-031-provider-credential-lifetime-boundaries.md).
- **Review:** [I-VSD report](../../islamic-value-sensitive-design/workstreams/i-vsd-provider-credential-onboarding.md); all findings remain open.

## Problem And Repository Evidence

At H (`1ea011a5353ec12a75340bec222c28526e9da9c8`):

- [CerbosPolicyPackageService](../../src/Explore.Infrastructure/Services/CerbosPolicyPackageService.cs) accepts one-time publication credentials but also resolves configured targets/fallback; status performs provider inspection. The reviewed D4 packet identifies credential cache/config and exception-output risks in this service and [CerbosConfigResolver](../../src/Explore.Infrastructure/Services/CerbosConfigResolver.cs).
- [PolicySyncService](../../src/Explore.Infrastructure/Services/PolicySyncService.cs) passes role changes to the same static package publisher. `BuildManifestAsync` enumerates policy/schema files, not role rows; republishing does not repair custom-role semantics.
- [CerbosPolicyBootSyncRunner](../../src/Explore.API/BackgroundServices/CerbosPolicyBootSyncRunner.cs) and deployment reconciliation are current runtime publication owners in the reviewed packet.
- [Policy workflow](../../.github/workflows/cerbos-policy-check.yml) has production publication after policy validation. [Coolify deploy](../../.github/workflows/deploy-coolify.yml) depends on change detection/build, not that separate workflow's publication. A cross-workflow `needs` name cannot establish ordering.

H only excludes HTTP cache/replay. Static-package/custom-role parity is an [independent backlog issue](cerbos-custom-role-parity.md), not an effect this packet claims to fix.

## Prerequisites And Mandatory Release Sequence

Retain PR 1 HTTP/current-authority policies and [PR 2](provider-credential-runtime-authority.md)'s external-authority contract. Do not delete live Cerbos admin bindings during earlier auth cleanup.

1. Establish explicit CI/GitOps and manual publication ownership, package validation and receipt semantics before removing runtime publishers. Deployment must consume successful required publication tied to the same revision, package content hash and target environment. Use a reusable publication workflow invoked by deploy or a validated receipt; do not rely on unrelated workflow success or PDP reachability alone.
2. Preserve interactive setup/admin one-time sync and manual ZIP/`cerbosctl` recovery. Distinguish runtime gRPC health, a declared historical publication receipt and explicit current live-store verification.
3. In the same safe release boundary, require complete transient pairs for application-mediated publication/live status, remove standing fallback/config/cache/admin bindings, startup publishers and role-change publishing. Preserve tenant-qualified runtime endpoint selection and existing principal/cache updates.
4. Drain cached credentials/old replicas, verify runtime authorization and publication readiness, then revoke retired runtime admin permissions. D4 and D5 MUST NOT deploy independently in an unsafe order.

## Scope And Exclusions

D4 inventory:

- `src/Explore.Application/DTOs/Onboarding/AuthorizationPolicyPackageSyncRequestDto.cs`, `AuthorizationProviderConfigurationDto.cs`, sync validator, `src/Explore.Application/Features/InstanceOnboarding/Handlers/Commands/SyncAuthorizationPolicyPackageCommandHandler.cs`, `src/Explore.Application/Models/CerbosConfiguration.cs`.
- `src/Explore.Infrastructure/Services/AuthorizationProviderConfigurationService.cs`, `CerbosConfigResolver.cs`, `CerbosAdminApiSettings.cs`, `CerbosPolicyPackageService.cs`.
- Onboarding/settings controllers, Blazor components/services, generated OpenAPI/tag clients and contract inventory, focused Application/Infrastructure/API/Blazor tests.

D5 inventory:

- Role command handlers; delete `src/Explore.Application/Contracts/Infrastructure/IPolicySyncService.cs` and `src/Explore.Infrastructure/Services/PolicySyncService.cs` after callers converge.
- `src/Explore.Infrastructure/InfrastructureServicesRegistration.cs`, `src/Explore.API/Hosting/ApiHostServiceCollectionExtensions.cs`; remove boot-sync options/runner/worker under `src/Explore.API/BackgroundServices/`.
- Deployment reconciliation in `AuthorizationProviderConfigurationService`, the two workflows above, affected role/API/architecture/diagnostic tests.

No custom-role semantic redesign, new encrypted store, background credential retry, browser-to-provider admin, broad tenant target, destructive store reset or new provider dependency. Keep native CQS, server-owned instance authority, tenant isolation and HAL; never queue credentials. New domain-event side effects, if necessary, use the existing transactional outbox with value-free payloads, not provider HTTP inside DB transactions.

## Red Invariants And Acceptance

- `SCN-CRED-002` / `SCN-CRED-003` / `SCN-CB-001`: a complete one-time pair publishes synchronously to the authorized, safely admitted target; absent/partial/rejected credentials fail closed without reading fallback. All terminal outcomes clear username/password and require fresh entry.
- `SCN-CRED-004`: later administration cannot reuse onboarding credentials. `SCN-OBS-001` / `SCN-OBS-002` / `SCN-OBS-003`: canaries in errors, redirects and partial provider completion never escape; HTTP replay remains excluded; local failure after external mutation gives truthful reconciliation guidance, not rollback or synchronized-state claims.
- `SCN-CB-002`: startup checks runtime PDP reachability and declared publication readiness without Admin API calls or credentials. A historical receipt is not proof of current store equality after external changes.
- `SCN-CB-003`: committed role create/update/delete updates existing principal facts/cache paths with no publication and no false retry-next-sync message. This does not prove custom-role parity.
- `SCN-OPS-001` / `SCN-OPS-002`: one-time UI, manual package and GitOps paths remain usable; production cannot outrun required policy publication. Non-production/manual paths have explicit owners/status and no runtime fallback.
- Fail publication while deployment is waiting; reject failed, absent, stale, wrong-revision/hash/environment receipts. Hold and release exact events/barriers with bounded timeouts, not sleeps or polling. Keep always-present workflow/job identities stable and environment approval/credential isolation intact.
- Attempt wrong-tenant Admin API targeting and verify no credential send/provider mutation. Tenant runtime endpoint resolution remains tenant-qualified after tenant admin-binding removal.

## I-VSD Mapping And Revision Gate

Preserve these exact shared assignments:

| Finding / mitigation | Original scenarios and tasks |
| --- | --- |
| `IVSD-F001` / `IVSD-M001` | `SCN-CRED-001` through `SCN-CRED-004`; D4 `CB-301` owns Cerbos portions, Keycloak portions stay PR 3 |
| `IVSD-F004` / `IVSD-M004` | `SCN-CB-002`, `SCN-CB-003`; D4 `CB-302`, D5 `CB-305`; `SCN-KC-003` stays PR 4 |
| `IVSD-F005` / `IVSD-M005` | Remaining `SCN-OBS-001` through `SCN-OBS-003`; D4 `CB-302` |
| `IVSD-F006` / `IVSD-M006` | `SCN-OPS-001` through `SCN-OPS-003`; D4 `CB-303`, D5 `CB-305`, shared with PR 6 bundled isolation |

Before implementation, create a separately reviewed active triad with exact atomic commits that preserve this single deployability boundary. Bind current I-VSD/CTO review to that revision; ADR-031 retains PC-CTO-r2 provenance. Publication ownership, readiness semantics, tenant targeting, lifetime, recovery or mapping changes require revalidation. Approval of the direction is not provider/runtime evidence.

## Dual Documentation And Recovery

Update internal [AUTHORIZATION](../../docs/internal/AUTHORIZATION.md), [SECRETS](../../docs/internal/SECRETS.md), [OPERATIONS](../../docs/internal/OPERATIONS.md), [TROUBLESHOOTING](../../docs/internal/TROUBLESHOOTING.md), [CI_CD_GOVERNANCE](../../docs/internal/CI_CD_GOVERNANCE.md), [RELEASE_CHECKLIST](../../docs/internal/RELEASE_CHECKLIST.md) and public [authorization](../../docs/public/documentation/readme/security-and-identity/authorization.md), [Coolify/Cerbos](../../docs/public/documentation/readme/self-hosting/coolify-cerbos-traefik.md), [troubleshooting](../../docs/public/documentation/readme/configuration-and-operations/troubleshooting-and-health.md) and [backup/upgrade](../../docs/public/documentation/readme/configuration-and-operations/backup-restore-upgrade.md) guides in the same delivery. Regenerate changed contracts and record delivered breaking security/operator scope in release inputs.

On failed publication halt deployment, preserve existing PDP authorization, and republish a previously validated package through operator/CI authority. Offer safe re-entry/manual reconciliation; never restore runtime admin credentials or background fallback. Receipts contain only non-secret package ID/hash/time/publisher evidence. Revoke retired authority and disclose backup/cache exposure without physical-erasure claims.

## Verification Contract

Write Red provider-transport, tenant, receipt/deploy-race and startup/role invariants first. Use owning in-memory class slices. Intermediate phases get one Release build and at most one selected project/canonical provider (D4 `Explore.Infrastructure.Tests`; D5 `Event.API.IntegrationTests`). At this PR's exit run existing explicit Cerbos integration, failed/mismatched receipt release tests, deterministic generated-contract checks, architecture and anonymized security/operations MAD. Evidence must be value-free; quarantine unrelated failures. No tests or publication are claimed by this brief.
