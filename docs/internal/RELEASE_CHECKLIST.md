# Release Checklist

> **Audience:** Contributors | Release operators | AI agents
> **Status:** Implemented at source level; production signer enrollment and live release rehearsal remain gated on Project Steward approval
> **Owner:** Platform/Ops
> **Last Verified:** 2026-11-26
> **Source Anchors:** `docs/internal/RELEASE_POLICY.md`, `docs/internal/RELEASE_RUNBOOK.md`, `docs/internal/BACKUP_RESTORE_UPGRADE.md`, `docs/internal/TESTING.md`, `.github/workflows/release.yml`, `.github/workflows/_container-build.yml`

Use this checklist before tagging or publishing a release. A release is not ready until operators can understand what changed, how to upgrade, how to verify offline and online, and how to recover.

## Complete release-impact metadata

- [ ] Dependency servicing regenerated and verified both Standalone lock profiles and the moderation launcher, browser installer, migration scaffolder, and shared Keycloak tool locks. Clean CI caches receive all profile metadata before license validation.
- [ ] Complete changed-file metadata (including previous names for renames) passed `Release Impact Check` (`validate-release-impact-pr.cs` via in-process `ReleaseImpactPolicy`), with matching category checkboxes and non-empty `Details:`. See [CI/CD Governance](CI_CD_GOVERNANCE.md#complete-pr-file-metadata).

## Release Model And Activation Status

- [ ] Verify the selected SDK (`global.json` `10.0.404`), shared runtime, and all four digest-pinned `.NET 10` Dockerfiles (`src/Explore.API/Dockerfile`, `src/Explore.Blazor/Dockerfile`, `src/Event.Standalone/Dockerfile`, `src/Event.MigrationService/Dockerfile`) match [the servicing baseline](OPERATIONS.md#sdk-and-runtime-servicing) and pass `validate-dockerfile-base-images.cs`.
- [ ] **Production signer & rehearsal gate:** confirm production SSH release signers are enrolled and active in `eng/release/trust/allowed-signers` (which fails closed with `tag_allowed_signers_empty` if no active entry exists) and that the post-merge authorized release-candidate rehearsal (with fault injection between digest-only staging and signing) has completed. Do not claim live OIDC/registry/Cosign operational readiness from source-level checks alone.

## Governed Release Pipeline Pre-Tag Gate

- [ ] Active commit hooks use `.githooks/commit-msg` (`preflight-commit`), and `preflight-range --target develop --head HEAD` passes with zero unmatched commits, valid breaking signals (`!` + `BREAKING CHANGE:`), and full `ReleaseInputPolicy.Validate` passes on any optional high-impact `docs/internal/releases/changes/CHG-*.yaml` fragments (zero embargoed `Public-Disclosure: embargoed` or `Restricted-Details` keys).
- [ ] Only the human-owned per-release inputs `docs/internal/releases/<version>/release.yaml` and `docs/internal/releases/<version>/summary.md` are committed at the release target commit (do not commit generated `release-notes.md`, `release-context.v1.json`, `release-rendering-context.v1.json`, or `contract-diff.v1.json` before tagging; `.github/workflows/release.yml` generates them post-build with real image digests).
- [ ] Local preview of `diff-contracts <base-oid> <head-oid> <output-path>` has been reviewed:
  - Every `breaking` OpenAPI/HAL, configuration, and migration change is reflected in `summary.md` and required high-impact fragments.
  - Every `review_required` entry (unresolved `$ref`, extensions, defaults, asymmetric migration coverage) has received human disposition; automated JSON diffing is not treated as semantic compatibility or provider-parity proof.
- [ ] Verified `git-cliff` `2.14.2` lock (`eng/release/toolchain.lock.json`) and local `verify-tools` pass with zero warnings; full `ISLAMU.ReleaseEngineering.Tests` passes in Release.
- [ ] An authorized release operator signs an annotated SSH tag bound to the exact target commit (`git -c gpg.format=ssh -c user.signingKey=<key> tag -s v<version> <full-commit-oid> -m "Release v<version>"`) and verifies it locally with `verify-release-tag --tag v<version> --target <full-commit-oid>`.

## Source-Bound Artifact Sealing, Signing, And Verification

Confirm the tag-triggered `.github/workflows/release.yml` run completed every stage in order under the protected `release` environment:

- [ ] **Preflight:** `verify-release-tag` passed with exact tag name, annotated tag object ID, authorized SSH principal, and `github.sha` match; pre-build sealed-release guard verified existing published releases before requiring mirror credentials (so already-published reruns stay idempotent no-ops); configured registry variables/secrets (`vars.CONTAINER_REGISTRY`, `vars.CONTAINER_REGISTRY_USER`, optional `vars.CONTAINER_REGISTRY_LOGIN_USER`, `secrets.CONTAINER_REGISTRY_PASSWORD`, or GHCR-only default) validated fail-closed.
- [ ] **Tag exclusivity & ruleset:** `v*` release tags trigger exclusively `.github/workflows/release.yml` (never `deploy-coolify.yml`), and a GitHub tag ruleset restricts updates and deletions on `v*` with reviewed minimal bypass actors.
- [ ] **Digest-only container staging:** `_container-build.yml` (`push-mode: digest-only`) built, Trivy-scanned (`CRITICAL,HIGH` fail-closed), SBOM/SLSA-attested, and staged all four `.NET 10` OCI indexes (`islamu-event-api`, `islamu-event-ui`, `islamu-event-standalone`, `islamu-event-migration-service`) to GHCR (and any configured secondary OCI registry) with zero registry tags assigned.
- [ ] **Prepare & seal:** `release-images.v1.json` captured all 4 digests and all canonical `registry/repository@sha256:<digest>` references (4 in GHCR-only mode; 8 when a secondary registry is configured); offline `prepare --image-manifest` rendered three-persona `release-notes.md` (`Community Admin`, `Self-Hoster / Operator`, `Developer / API Integrator`) and `release-rendering-context.v1.json`; `seal-artifacts` produced `release-artifact-manifest.v1.json` and sorted `SHA256SUMS` without clobbering.
- [ ] **Attest & sign:** `actions/attest` attested all seven sealed files; Cosign signed and verified **every** canonical `registry/repository@sha256:<digest>` reference across every configured registry individually; `cosign sign-blob --yes --bundle SHA256SUMS.sigstore.json` signed and verified `SHA256SUMS`; `gh attestation verify` passed on every attested file and GHCR OCI image.
- [ ] **Verified draft, publish once, and post-publish check:** exact eight assets (`contract-diff.v1.json`, `release-artifact-manifest.v1.json`, `release-context.v1.json`, `release-images.v1.json`, `release-notes.md`, `release-rendering-context.v1.json`, `SHA256SUMS`, `SHA256SUMS.sigstore.json`) uploaded without `--clobber`, passed round-trip byte verification, `sha256sum --check SHA256SUMS`, Cosign verification, pre-publish tag-object OID (`tag-object-id`) and asset-identity snapshot checks, published once by numeric draft ID, and passed immediate post-publish verification.
- [ ] **Mutable Model 2 contributor credit:** optional post-publish `append-contributors` either appended email-free `@handle` acknowledgements solely to the mutable GitHub Release body or degraded cleanly to the identity-free signed notes without altering any asset.
- [ ] **Public changelog sync (post-tag signed sealed archive):** downloaded the published release assets into `docs/internal/releases/<version>/` (or `--sealed-artifacts-root`), authenticated `SHA256SUMS` online with Cosign (`SHA256SUMS.sigstore.json`) and `gh attestation verify`, countersigned `SHA256SUMS` offline with an enrolled release key (`ssh-keygen -Y sign -f <key> -n islamu.event.release-checksums .../SHA256SUMS` → `SHA256SUMS.sig`), ran `sync-public-changelog --publication-base https://github.com/islamu-ngo/Event/blob/` (cryptographically verifying both signed tag-tree inputs and `SHA256SUMS.sig` under `islamu.event.release-checksums` without moving the signed tag), and committed the post-release changelog PR passing `--check`.

## Release Metadata

- [ ] Version/tag (`v<version>`), tag object OID, and target commit OID are recorded.
- [ ] Four multi-arch `.NET 10` OCI index digests and every configured canonical `registry/repository@sha256:<digest>` reference (GHCR plus optional secondary OCI mirror) are recorded in `release-images.v1.json` and `release-artifact-manifest.v1.json`.
- [ ] Supported deployment modes are stated: minimum standalone `Event.Standalone` + SQLite topology, split API + Blazor + MigrationService topology, single-tenant or multi-tenant operation, and optional services.
- [ ] Known incompatible versions and rollback caveats are stated.

## Third-Party Software And Outgoing License

- [ ] Release notes and commercial terms apply the ISLAMU license only to ISLAMU-owned or separately sublicensable material and expressly preserve each third-party material's own license or terms.
- [ ] The release inventory separates linked/runtime dependencies, base images, optional service images, datasets/assets/fonts, and hosted provider/API terms instead of treating them as one license surface.
- [ ] The minimum standalone operational topology is documented as one `Event.Standalone` process/container containing the API and Blazor BFF/UI with SQLite persistence; no optional external service is described as a core requirement.
- [ ] Every optional service is identified as operator-pulled or ISLAMU-conveyed. Referenced deployment manifests do not describe third-party artifacts as ISLAMU-licensed software.
- [ ] Every ISLAMU-conveyed third-party binary or image has an exact version and digest, upstream license evidence, SBOM, required notices/attributions, corresponding-source or source-offer evidence where applicable, modification provenance, and any required commercial entitlement.
- [ ] No floating tag (`latest`, an unqualified major tag, or equivalent mutable alias) is accepted as license, provenance, or offline redistribution evidence.

## Code And Test Gates

- [ ] Release build succeeds sequentially without warnings:
  ```bash
  dotnet build eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ISLAMU.ReleaseEngineering.Tests.csproj --configuration Release -m:1 -nr:false --verbosity quiet
  ```
- [ ] Required per-project tests pass sequentially (`--no-build`); never run bare solution-wide `dotnet test`.
- [ ] Browser prerequisites restore against `eng/tools/InstallAssuranceBrowser/packages.lock.json` without changing shared tool locks; actual named browser cases run for security-relevant candidates.
- [ ] Architecture tests pass:
  ```bash
  dotnet build tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release -m:1 -nr:false --verbosity quiet
  dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --no-build --verbosity quiet
  ```
- [ ] Required GitHub checks match [CI_CD_GOVERNANCE.md](CI_CD_GOVERNANCE.md): fast build/test (including `ISLAMU.ReleaseEngineering.Tests`), workflow security (`actionlint`, `zizmor`, all 4 Dockerfiles, action pins, Dependabot, cache policy, deploy contract), OpenAPI drift, CodeQL (completed analysis, not cancelled), dependency review, and path-relevant security/Cerbos checks.
- [ ] Infrastructure-dependent or advisory/nightly failures are passed or explicitly deferred with owner and reason.

## Migration And Data Contract

- [ ] New EF migrations are named, reviewed, and tied to the feature/release; generated migrations and snapshots were not hand-edited.
- [ ] Clean-database migration and a second idempotent `Event.MigrationService` run pass for PostgreSQL, SQLite, SQL Server, MariaDB, and MySQL on the release matrix (do not rely on the structural `diff-contracts` migration ledger as semantic parity proof).
- [ ] Provider-specific application and Data Protection migration assemblies are present, generated from the model, and use their governed history tables (`ie_` prefix on SQLite/MariaDB/MySQL; configured schema on PostgreSQL/SQL Server).
- [ ] Quartz scheduler-schema ownership remains idempotent DDL in the API (`QRTZ_` tables created non-destructively across all providers).
- [ ] Migration impact (additive, backfill, destructive, rollback-sensitive), Data Protection key storage impact, seed/lookup changes, and rollback strategy are documented.

## Configuration And Secrets Contract

- [ ] New, changed, and removed environment keys (surfaced by `diff-contracts` and code review) are documented in `CONFIGURATION.md` / `SECRETS.md` and public `environment-variables.md` / `secrets.md` without exposing raw secret values.
- [ ] `Database:*` provider, endpoint, TLS, schema, and runtime/migrator role inputs plus `EmbeddedSqlite` vs. `ExternalDatabase` privacy-erasure authority contracts are documented.
- [ ] Secret-provider paths, Keycloak realm/client/role changes, and optional profiles (`storage`, `authz`) are documented.

## Security And Operations Contract

- [ ] Authentication/authorization, rate-limit, timeout, forwarded-header, CORS, proxy, health-check, metrics, logging, tracing, and backup/restore changes are documented in their authoritative internal and public guides.
- [ ] Known vulnerabilities and dependency warnings are triaged; secret scanning, push protection, Dependabot security updates, dependency graph, and CodeQL alerts are enabled or explicitly waived.

## CI/CD Evidence Contract

- [ ] Retain completed CodeQL analysis, OpenAPI drift / `configuration-manifest-v1alpha2.schema.json` checks, Trivy SARIF/table reports, Buildx SBOM/provenance index inspections, GHCR and file attestation verification results, and `workflow-security-evidence`.
- [ ] For staging/production Coolify deployments (`deploy-coolify-develop.yml` / `deploy-coolify.yml`), retain `container-build-*`, immutable `dev-*` / `sha-*` promotion evidence, smoke-check results, and deployment summaries. Before the first deployment after switching container registries (for example GHCR-only or Docker Hub), verify Coolify API/UI resources target the active registry repository with pull access, and confirm post-deploy that the running container digest in Coolify matches `Expected image digest` (the Coolify GET webhook does not enforce or verify image references).
- [ ] If citing Codecov evidence, identify exact commit and included suite flags without substituting coverage for required tests.
- [ ] Any failed gate rerun or emergency override follows [CI_CD_RUNBOOKS.md](CI_CD_RUNBOOKS.md) with owner, reason, evidence, compensating control, and removal condition.

Expected workflow artifact names:

- `test-results-fast` / `test-results-integration`
- `coverage-fast` / `coverage-runtime` (advisory)
- `openapi-contract-guard`
- `security-test-evidence`
- `workflow-security-evidence`
- `cerbos-policy-evidence` / `cerbos-policy-publish-evidence`
- `container-build-*`
- `release-sealed-<version>` / `release-signed-<version>`
- `deployment-production-evidence` / `deployment-staging-evidence`

## Ticketing Recovery And Capability Status

- [ ] `ticketing-capabilities.json` parses with only `production-approved`, `test-only`, or `disabled` statuses, and every `production-approved` capability retains its required matrix evidence.
- [ ] Protected delayed payout remains `disabled` with zero route, HAL, scheduler, config, secret, client, or UI exposure.
- [ ] Recovery configuration remains disabled by default; enabled deployments bind exact release/schema, key, authority, provider, idempotency, and worker-fence floors with zero-PII health exports.

## Documentation Impact

Choose exactly one docs impact outcome for the release:

| Outcome | Required Evidence |
|---|---|
| Updated | Linked docs PR/commit updates both internal source anchors (`docs/internal/`) and public adopter guides (`docs/public/`). |
| Not needed | Explanation of why runtime/API/operator behavior did not change. |
| Deferred | Follow-up path, owner, and reason the release can proceed. |

## Related

- [RELEASE_POLICY.md](RELEASE_POLICY.md) — normative release governance and trust boundaries.
- [RELEASE_RUNBOOK.md](RELEASE_RUNBOOK.md) — step-by-step release execution, verification, and failure recovery.
- [ADR-025](adr/ADR-025-provider-neutral-release-governance.md) — release governance architecture decision record.
- [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) — backup, restore, upgrade, and rollback runbook.
- [Public Releases and Changelog Guide](../public/documentation/readme/configuration-and-operations/releases-and-changelog.md) — adopter verification and upgrade documentation.
