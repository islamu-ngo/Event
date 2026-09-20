# Request-Scoped Keycloak Administration

- **Status:** Deferred; no custody or UI implementation in this brief.
- **Owner:** implementing agent.
- **Delivery:** PR 3; D2 `KC-201`, `KC-202`, `KC-203`; graduated by `KG-101` from `PC-CTO-r2`.
- **Decision:** [ADR-031](../../docs/internal/adr/ADR-031-provider-credential-lifetime-boundaries.md).
- **Review:** [I-VSD report](../../islamic-value-sensitive-design/workstreams/i-vsd-provider-credential-onboarding.md); all findings remain open.

## Problem And Repository Evidence

The reviewed packet identifies existing one-time credential support in bootstrap, doctor, sync preview/apply and rotation, but inconsistent optional-mode flags and retained usernames. The owning [KeycloakBootstrapService](../../src/Explore.Infrastructure/Services/Keycloak/KeycloakBootstrapService.cs), [authentication settings controller](../../src/Explore.API/Controllers/InstanceAuthenticationSettingsController.cs) and [onboarding form](../../src/Explore.Blazor.Client/Pages/Onboarding/AuthProviderConfiguration.razor) remain the cutover surface. This is inherited PC-CTO-r2 custody evidence, not new exploit verification.

H (`1ea011a5353ec12a75340bec222c28526e9da9c8`) adds HTTP no-store/replay suppression only. It does not sanitize provider exceptions, forbid credential-forwarding redirects, clear browser fields or replace the existing DTO/ownership model.

## Prerequisites And Scope

Complete [runtime-authority PR 2](provider-credential-runtime-authority.md) first: the external writer and BFF consumer contract must support activation without database secret custody. Preserve server-derived setup/instance authority, tenant boundaries, rate limits and HAL affordances. Provider credentials never authenticate a platform caller.

D2 owning inventory:

- Under `src/Explore.Application/DTOs/Onboarding/`: `KeycloakBootstrapRequestDto.cs`, `KeycloakRealmDoctorRequestDto.cs`, `KeycloakRealmSyncPreviewRequestDto.cs`, `KeycloakRealmSyncApplyRequestDto.cs`, `KeycloakClientSecretRotationRequestDto.cs`, `KeycloakClientSecretRotationResultDto.cs`, and bootstrap/rotation validators under `Validators/`.
- Keycloak requests/native handlers under `src/Explore.Application/Features/InstanceOnboarding/`; `src/Explore.Infrastructure/Services/Keycloak/KeycloakBootstrapService.cs`.
- `src/Explore.API/Controllers/InstanceOnboardingController.cs` and `InstanceAuthenticationSettingsController.cs`.
- `src/Explore.Blazor.Client/Pages/Onboarding/AuthProviderConfiguration.razor`, `src/Explore.Blazor.Client/Pages/Admin/Instance/Components/InstanceAuthProviderSection.razor`, `src/Explore.Blazor.Client/Services/InstanceOnboardingService.cs`.
- Source-owned regeneration of `schemas/openapi_islamu-event.json`, `docs/internal/API_CONTRACT_INVENTORY.md`, `src/Explore.Blazor.Client/Clients/EventApiTagClients.g.cs`; focused Application/Infrastructure/API/Blazor tests.

Require complete one-time administrator pairs for bounded Admin REST work, remove obsolete temporary-mode/ownership fields cleanly, and clear both username and password on all terminal outcomes. Keep anonymous discovery separate from administrative operations. Use safe endpoint admission before credential transport; do not forward credentials on redirects. Preserve additive provider reconciliation and a synchronous request lifetime.

## Exclusions

No standing fallback, browser-to-provider administration, DB or encrypted credential store, browser storage, job/retry queue, credential-bearing outbox or generic secret facade. No lifecycle service-client delivery ([PR 4](provider-credential-keycloak-lifecycle.md)), Cerbos cutover or deployment-root migration. No destructive realm reset/reimport or compatibility aliases. Generated contracts stay with the originating source change; no handwritten mirrors. No claim of physical managed-memory erasure.

## Red Invariants And Acceptance

- `SCN-CRED-001`: active setup authority, safe coordinates and ready external runtime ownership yield bounded additive bootstrap and value-free output, with no retained administrator material.
- `SCN-CRED-003`: invalid credentials, timeout, cancellation, provider rejection, local activation failure and navigation/disposal clear both UI fields. Every new attempt requires fresh entry; no delayed/background retry retains credentials.
- `SCN-CRED-004` / `SCN-KC-001`: later doctor, preview, repair and rotation require a fresh complete pair. Absent/partial pairs permit only public discovery or bounded blocked results, never deployment/database fallback.
- `SCN-OBS-001`: generated username/password/token canaries inside provider errors and responses never reach logs, traces, metrics, ProblemDetails, support artifacts or client diagnostics. Prove redirect rejection/no credential forwarding through the real provider transport seam.
- `SCN-OBS-002`: preserve H's private/no-store, no historical replay and current authorization on retries; do not mistake provider idempotence for generic HTTP replay.
- `SCN-OBS-003`: deliberately let external rotation succeed and fail local activation. Return a bounded partial-completion code and safe reconciliation action, without a rollback claim or provider payload.
- `SCN-OPS-001` through `SCN-OPS-003`: this packet owns the interactive `KC-203` contribution and truthful external/manual guidance; do not claim CI/bundled isolation is completed here. Keep localized, accessible forms and HAL-only action gating.

Use real route authorization and stateful boundary fixtures; subscribe to exact completion/barrier signals before cancellation/concurrency and await with bounded timeouts. Never pin prose or pass by timing luck.

## I-VSD Mapping And Revision Gate

`IVSD-F001` / `IVSD-M001` maps `SCN-CRED-001` through `SCN-CRED-004` to D2 `KC-201` (Cerbos portions remain PR 5). `IVSD-F005` / `IVSD-M005` maps remaining `SCN-OBS-001` through `SCN-OBS-003` to D2 `KC-202`. `IVSD-F006` / `IVSD-M006` maps `SCN-OPS-001` through `SCN-OPS-003` to D2 `KC-203`, shared with the later operator/deployment owners. These IDs retain their original meaning; the lifecycle minimum-role fixture belongs to D3, not this UI packet.

Before implementation create a separately reviewed active triad and exact atomic commit packets. Bind current I-VSD/CTO review to that revision, retaining ADR-031's PC-CTO-r2 hashes as provenance. Changes to authority, credential lifetime, transport, failure semantics, operator burden or scenario/task mapping require revalidation. Prior program approval does not waive this gate.

## Dual Documentation And Recovery

Update [AUTHENTICATION](../../docs/internal/AUTHENTICATION.md), [SECURITY-MODEL](../../docs/internal/SECURITY-MODEL.md), [API](../../docs/internal/API.md), [API_CHANGELOG](../../docs/internal/API_CHANGELOG.md), generated contract inventory and public [authentication](../../docs/public/documentation/readme/security-and-identity/authentication.md) in the same delivery. Explain complete one-time pairs, clearing/re-entry, temporary-account revocation, partial rotation and safe reconciliation. Keep runtime OIDC usable; recover by fresh authorized reconciliation and external runtime-value repair, never restoration of retained human credentials. Record breaking API changes in the owning release inputs, not as already shipped by H.

## Verification Contract

Run failing paired-credential/authority/output/redirect/partial-completion invariants first. Use owning Application/Infrastructure/API/Blazor class slices; one Release build and at most one selected project per phase (D2 selects `Explore.Infrastructure.Tests`). At PR exit use the existing real Keycloak integration lane, UI terminal-state tests, deterministic generated-contract checks, architecture gates and anonymized security/operations MAD. Evidence is value-free; unrelated baseline failures are quarantined. This brief claims no runtime test execution or completed mitigation.
