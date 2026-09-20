# Consumer-Isolated Provider Credential Deployment

- **Status:** Deferred; exact-root isolation has not shipped in H or this brief.
- **Owner:** implementing agent.
- **Delivery:** PR 6; remaining D6 `DP-401`, `DP-402`, `DP-403`. Auth-specific D6 work belongs to [PR 2](provider-credential-runtime-authority.md), not a second implementation here.
- **Provenance:** `PC-CTO-r2`, graduated by `KG-101`.
- **Decision:** [ADR-031](../../docs/internal/adr/ADR-031-provider-credential-lifetime-boundaries.md).
- **Review:** [I-VSD report](../../islamic-value-sensitive-design/workstreams/i-vsd-provider-credential-onboarding.md).

## Problem And Repository Evidence

At H (`1ea011a5353ec12a75340bec222c28526e9da9c8`), both [API/shared Infisical configuration](../../src/Explore.Secrets/Configuration/InfisicalConfigurationProvider.cs) and [BFF Infisical configuration](../../src/Explore.Blazor/Configuration/InfisicalConfigurationProvider.cs) request `recursive=true` and `expandSecretReferences=true`. Path validation occurs after the response is acquired; BFF database rejection cannot prove credentials never entered the process.

The reviewed packet inventories [Compose](../../docker-compose.yml), [AppHost](../../src/Explore.AppHost/AppHost.cs), [Keycloak init](../../docker/keycloak/keycloak-init.sh), and Setup Core catalogue/metadata as the remaining consumer/provisioner projections. Mixed runtime/admin/verifier/database folders are a pre-existing custody problem, not introduced or fixed by H's HTTP metadata.

## Prerequisites And Ownership

Complete PRs 2-5 consumer and publication contracts first. Reuse the BFF-specific work from PR 2; do not resurrect its old paths. [Cerbos PR 5](provider-credential-cerbos-publication.md) establishes publication readiness before removing runtime administration.

Resolve shared-file ownership before editing. The original root checkout has unrelated changes in the public Infisical guide, `src/Explore.Secrets/Configuration/InfisicalConfigurationProvider.cs`, `tests/Event.API.IntegrationTests/Features/ConfigurationAuthorityRegressionTests.cs`, and `tests/Explore.Secrets.UnitTests/Configuration/InfisicalConfigurationProviderTests.cs`. Those hunks were not copied into this worktree. A later executor must inspect current ownership, not assume the old dirty state persists or absorb it blindly.

## Scope And Consumer Contract

Admit exact roots before secret acquisition; disable unsafe recursion/reference expansion and reject missing provenance or an unexpected returned path. Selected authority is absolute, not a precedence/fallback chain.

| Consumer | Provider roots allowed by this design |
| --- | --- |
| BFF | `/runtime/bff/oidc/keycloak`, `/runtime/bff/oidc/google` |
| API | `/runtime/api/authentication/keycloak`, `/runtime/api/authorization/cerbos` |
| Combined host | Exact union of API and BFF runtime roots, never provisioning/infrastructure |
| One-shot provisioner/publisher | Purpose-specific `/provisioning/keycloak` or `/provisioning/cerbos` |
| Provider infrastructure | `/infrastructure/keycloak/database`, `/infrastructure/cerbos`, limited to owning provider resources |

Preserve unrelated approved runtime service roots and their consumers; this table does not authorize deleting unrelated services. Provisioner/publisher identities receive only necessary credentials; runtime identities must be unable to acquire them, not merely decline to map them.

D6 owning inventory:

- Both Infisical providers above and focused unit/integration tests, host folder projections and selected-authority composition.
- `.env.example`, `docker-compose.yml`, `docker/keycloak/keycloak-init.sh`, `src/Explore.AppHost/AppHost.cs`.
- `src/Event.Setup.Core/Environment/CanonicalEnvironmentCatalogue.cs`, `CanonicalEnvironmentMetadata.cs`; Setup Core/Diagnostic/Standalone/Architecture tests.
- Public Infisical/environment/secrets/Compose/Coolify/troubleshooting docs; internal secrets/configuration/self-hosting/security/operations/troubleshooting contracts and delivered release evidence.

ADR/journal/backlog graduation originally listed in D6 is already the documentation purpose of KG-101/Packet K, not proof of deployed root isolation.

## Exclusions

No broad `/keycloak` or `/cerbos` fallback, backwards aliases, post-fetch-only admission, new secret provider, hard-coded AppHost/test credentials or .env onboarding admin values. No DB reset, secret restoration, unrelated root migration or custom-role semantics change. Secrets originate only in selected Infisical, explicit documented environment injection or selected shared User Secrets in Development/Testing; User Secrets elsewhere are rejected. Provider credentials never enter runtime jobs/outbox or diagnostics.

## Red Invariants And Acceptance

- `SCN-PATH-001`: exercise actual API, BFF and combined-host acquisition with approved fixture authorities. Wrong-consumer, provisioning, infrastructure, missing-provenance and reference-expanded values are never requested/acquired/materialized. Generate credential canaries at runtime; assert the acquisition boundary and resulting configuration, not only mapper output or source text.
- `SCN-PATH-002`: only the provisioner/publisher/provider resources receive their purpose credentials. Prove runtime machine-identity denial and environment projection separation, including allowed logical keys pointing at stale prohibited binding coordinates.
- `SCN-OPS-001`: interactive setup retains authority-specific write-only/operator-injection guidance and explicit one-time admin/manual options.
- `SCN-OPS-002`: headless/GitOps runtime uses pre-provisioned values; deployment tooling owns administration with no fallback into app identities.
- `SCN-OPS-003`: Compose and Aspire one-shot init/publish resources receive only their credentials, readiness order is enforced and API/BFF start without admin values. Setup Core catalogue, metadata and emitted topology agree.
- Preserve unrelated service roots and test failures for absent/unauthorized/invalid selected authority. Prove cold restart and multi-replica drain/refresh before revoking old values; use subscribed barriers/events with bounded timeout, not sleeps or polling.

## I-VSD Mapping And Revision Gate

Preserve `IVSD-F002` / `IVSD-M002` -> `SCN-PATH-001` / `SCN-PATH-002`, D6 `DP-401` through `DP-403` (auth subset delivered separately in PR 2). Preserve `IVSD-F006` / `IVSD-M006` -> `SCN-OPS-001` through `SCN-OPS-003`, D6 `DP-402` / `DP-403`. All findings remain open; documentation parity is not acquisition or usability proof.

Before implementation create a separate active triad with exact atomic paths/commands, resolve overlapping ownership and bind current I-VSD/CTO review to the revised packet. ADR-031 records original PC-CTO-r2 provenance. Changed roots, consumer identity, credential lifetime, recovery, operator obligations or mappings require revalidation. Do not execute the withdrawn umbrella phase commits.

## Dual Documentation And Recovery

Update internal [SECRETS](../../docs/internal/SECRETS.md), [CONFIGURATION](../../docs/internal/CONFIGURATION.md), [SELF_HOSTING](../../docs/internal/SELF_HOSTING.md), [SECURITY-MODEL](../../docs/internal/SECURITY-MODEL.md), [OPERATIONS](../../docs/internal/OPERATIONS.md), [TROUBLESHOOTING](../../docs/internal/TROUBLESHOOTING.md) alongside public [Infisical](../../docs/public/documentation/readme/configuration-and-operations/infisical.md), [environment variables](../../docs/public/documentation/readme/configuration-and-operations/environment-variables.md), [secrets](../../docs/public/documentation/readme/configuration-and-operations/secrets.md), [Compose](../../docs/public/documentation/readme/self-hosting/docker-compose.md), [Coolify/Cerbos](../../docs/public/documentation/readme/self-hosting/coolify-cerbos-traefik.md) and [troubleshooting](../../docs/public/documentation/readme/configuration-and-operations/troubleshooting-and-health.md). Show all three lifetime classes, exact consumer identities and Infisical/Environment/User Secrets differences without duplicating raw configuration.

Forward-fix configuration and provision fresh runtime values. Restore prior runtime-only mappings/credentials only if still safe; never reintroduce broad recursive roots or provisioner authority. Restart/drain consumers, verify readiness, revoke old credentials/permissions, and disclose backups/snapshots rather than claiming physical erasure. Release inputs must describe only the actual breaking deployment cutover.

## Verification Contract

Red tests precede composition changes; use owning Secrets/host class slices. Per intermediate phase run one Release build and at most one selected project/canonical provider (D6 selects `Explore.Secrets.UnitTests`). At PR exit run owning executable host/Setup Core/Diagnostic/Standalone/Architecture checks, selected-authority and consumer-isolation integration, applicable release/intent checks and anonymized security/operations MAD. Reuse earlier custody/provider evidence only with explicit revision applicability; run any newly affected supported-provider gates rather than calling skipped providers passed. Capture value-free evidence, quarantine unrelated failures, and do not manufacture source/prose-pinning tests. This graduation executes none of these future runtime gates.
