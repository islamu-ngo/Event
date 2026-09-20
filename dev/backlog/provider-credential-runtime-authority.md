# Provider Credential Runtime Authority And Auth-Value Cleanup

- **Status:** Deferred; approved direction, not implemented by this brief.
- **Owner:** implementing agent.
- **Delivery:** PR 2; D1 `PC-101`, `PC-102`, `PC-103`, `PC-104`, plus auth-only D6 `DP-401` / `DP-402`.
- **Provenance:** `PC-CTO-r2`, graduated by `KG-101`; original identifiers are design-packet references, not completed tasks.
- **Decision:** [ADR-031](../../docs/internal/adr/ADR-031-provider-credential-lifetime-boundaries.md).
- **Review:** [I-VSD report](../../islamic-value-sensitive-design/workstreams/i-vsd-provider-credential-onboarding.md), current / plan-aligned to the reviewed plan, not an implementation certificate.

## Problem And Repository Evidence

At behavior commit `1ea011a5353ec12a75340bec222c28526e9da9c8`:

- [AuthProviderConfigurationService](../../src/Explore.Application/Services/AuthProviderConfigurationService.cs) serializes Keycloak and Google client secrets into `SystemSetting.Value` and reads them through `ReadConfigurationWithSecretsAsync`. JSON serialization is not encryption.
- [DynamicAuthSchemeManager](../../src/Explore.Blazor/Services/DynamicAuthSchemeManager.cs) consumes the internal secret-bearing API contract, updates OIDC schemes and retains an in-memory Keycloak secret. Deleting rows without replacing cold-start and refresh consumers is unsafe.
- [SecretResolver](../../src/Explore.Secrets/Services/SecretResolver.cs) selects stored bindings before registry defaults and dispatches the binding to its selected source. Changing catalogue defaults alone cannot retire stored coordinates or cached values.
- [BFF Infisical provider](../../src/Explore.Blazor/Configuration/InfisicalConfigurationProvider.cs) fetches recursively with reference expansion before mapping. Filtering afterwards does not prove non-acquisition.

HTTP no-store/replay exclusion shipped in H. It did not remove these stores, consumers, caches or historical replay rows.

## Prerequisites And Safe Sequence

1. Retain PR 1 HTTP policies and server-derived setup/instance authority. Resolve exact existing setting keys, binding coordinates and consumer inventory before writing deletion selectors.
2. Provision selected-authority runtime values and exact BFF roots `/runtime/bff/oidc/keycloak` and `/runtime/bff/oidc/google`. A Ready, explicitly writable Infisical binding permits a capability-specific write-only adapter. Environment and explicitly selected Development/Testing User Secrets require operator injection and restart/refresh; never fall back to another source.
3. Deploy no-database-write code, value-free reads/ownership DTOs and the replacement `DynamicAuthSchemeManager` cold-start/refresh consumer. Remove the internal secret-bearing GET together with its generated/BFF consumers, not earlier.
4. Fence/drain old writers and readers, then activate exact transactional, idempotent auth-row/binding cleanup. No external HTTP call belongs inside the cleanup transaction. If rolling convergence cannot be proved, require an operator-approved maintenance cutover.
5. Restart/drain all replicas, verify runtime readiness, then rotate/revoke retired values and permissions. Document backup exposure; never restore deleted credentials during rollback.

## Scope And Exclusions

Owning paths inherited from D1:

- `src/Explore.Domain/Constants/InfrastructureSecretSettingKeys.cs`, `src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs`, `src/Explore.Domain/Settings/Documents/SettingsDocumentTaxonomy.cs`.
- New `src/Explore.Application/Contracts/Secrets/IAuthenticationRuntimeSecretWriter.cs` and `src/Explore.Secrets/Services/AuthenticationRuntimeSecretWriter.cs`; registration in `src/Explore.Secrets/Extensions/SecretResolutionServiceCollectionExtensions.cs`.
- `src/Explore.Application/DTOs/Onboarding/AuthProviderConfigurationDto.cs`, its validator, `src/Explore.Application/DTOs/Secrets/SecretOwnershipDto.cs`, and `AuthProviderConfigurationService`.
- `BootstrapKeycloakRealmCommandHandler`, `RotateKeycloakClientSecretCommandHandler`, `UpdateAuthProviderConfigurationCommandHandler` under `src/Explore.Application/Features/InstanceOnboarding/Handlers/Commands/`.
- New `src/Explore.Persistence/Services/ProviderCredentialDataMigration.cs`, `src/Event.MigrationService/Worker.cs`, and focused Domain/Application/Persistence/Secrets/API tests.
- Auth-only D6 BFF acquisition/mapping, BFF service and tests, API internal contract, generated OpenAPI/tag clients and inventory must travel with their changed source contracts.

Do not delete live Cerbos admin bindings: [PR 5](provider-credential-cerbos-publication.md) owns that consumer cutover. Remaining API/provisioning isolation belongs to [PR 6](provider-credential-deployment-isolation.md). No encrypted DB store, generic secret CRUD, compatibility alias, dual read/write, raw-credential outbox/job, provider reset or hand-edited generated artifact is authorized. Application owns native CQS contracts and references Domain only; Secrets/Persistence implement ports; BFF uses generated transport contracts, not backend layers.

## Red Invariants And Acceptance

- `SCN-STORE-001`: Ready Infisical writes only to the selected exact coordinate; no secret readback via DTO/API and no DB value. Missing, unauthorized, unavailable or invalid writer outcomes block activation without fallback.
- `SCN-STORE-002`: non-writable authority returns bounded operator action; injection and restart/refresh restore readiness without DB custody.
- `SCN-STORE-003`: cleanup preserves unrelated rows/tenants, removes only verified exact keys/coordinates transactionally, reports counts only, is repeatable and fails closed on remaining prohibited state.
- `SCN-KC-002`: BFF cold restart and rotation/refresh work using its own selected external authority, never the API secret endpoint or provider-admin/database credential.
- Auth-only `SCN-PATH-001` / `SCN-PATH-002`: acquisition rejects wrong-consumer/provisioning roots and reference expansion before fetching; test an allowed logical key with a prohibited stored coordinate.
- `SCN-OBS-001`, `SCN-OBS-002`, `SCN-OBS-003`: runtime-generated canaries never escape into DB, replay, logs, traces, DTOs, ProblemDetails or evidence; retained HTTP policies remain effective; provider mutation followed by local activation failure returns truthful partial completion, never a false rollback.
- Preserve the `SCN-CRED-001` through `SCN-CRED-004` custody obligations assigned to D1 `PC-101`; this packet does not complete the later one-time forms. Preserve authority-specific operator handoff under `SCN-OPS-001` through `SCN-OPS-003` without claiming the full deployment cutover.
- Before implementation, make a deterministically barrier-controlled old writer race cleanup and a stale reader race refresh. Assert no resurrection and no use of retired cached authority. No sleeps, polling or internal mock-call assertions.

## I-VSD Mapping And Revision Gate

Preserve `IVSD-F001` / `IVSD-M001` -> `SCN-CRED-001` through `SCN-CRED-004`, D1 `PC-101`; `IVSD-F002` / `IVSD-M002` -> `SCN-PATH-001` / `SCN-PATH-002`, auth-only D6 `DP-401` / `DP-402`; `IVSD-F003` / `IVSD-M003` -> `SCN-STORE-001` through `SCN-STORE-003`, D1 `PC-101` through `PC-104`; and `IVSD-F005` / `IVSD-M005` -> remaining `SCN-OBS-001` through `SCN-OBS-003`, D1 `PC-101`. `IVSD-F006` / `IVSD-M006` retains PR 2's auth-specific operator handoff; later PRs own the remaining cutover. All findings remain open.

Before implementation, create a separate active triad with exact path-limited atomic commit packets and current revision-bound I-VSD/CTO review. Use ADR-031's exact PC-CTO-r2 binding as provenance, not approval of a new revision. Re-review any changed authority, cleanup selector, lifetime, consumer boundary, recovery or scenario mapping. The original umbrella commit packets are withdrawn.

## Dual Documentation And Recovery

Update internal [SECRETS](../../docs/internal/SECRETS.md), [CONFIGURATION](../../docs/internal/CONFIGURATION.md), [BACKUP_RESTORE_UPGRADE](../../docs/internal/BACKUP_RESTORE_UPGRADE.md), authentication/security and changed API contracts alongside public [secrets](../../docs/public/documentation/readme/configuration-and-operations/secrets.md), [Infisical](../../docs/public/documentation/readme/configuration-and-operations/infisical.md), [authentication](../../docs/public/documentation/readme/security-and-identity/authentication.md) and [backup/upgrade](../../docs/public/documentation/readme/configuration-and-operations/backup-restore-upgrade.md). Document write-only versus operator-written authority, cold restart, exact cleanup, backup retention and revocation. Governed breaking release metadata must describe only this delivered scope; H's fragment is not evidence of this migration.

## Verification Contract

Author failing invariants before implementation. Use exact in-memory Domain/Application/Secrets class slices; HTTP and BFF semantics stay in their owning projects. Per intermediate phase run one Release build and at most one selected project on one canonical provider (D1 selects `Event.Application.UnitTests`). At custody PR exit run the supported real-database matrix for exact cleanup, preservation, idempotency and no-resurrection, plus BFF cold-start/rotation/partial-completion integration, architecture, deterministic OpenAPI/client generation and anonymized security/operations MAD. No skipped provider counts as passed. Capture value-free evidence; quarantine unrelated baseline failures. No such runtime verification is claimed by this documentation graduation.
