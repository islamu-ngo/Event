ABOUTME: Release readiness checklist for code, migrations, configuration, security, and documentation.
ABOUTME: Provides the release documentation contract for self-hostable operators and contributors.

# Release Checklist

> **Audience:** Contributors | Operators | AI agents
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-08-09
> **Source Anchors:** `docs/BACKUP_RESTORE_UPGRADE.md`, `docs/CONTRIBUTING.md`, `docs/TESTING.md`, `.github/workflows/test.yml`

Use this checklist before tagging or publishing a release. A release is not ready until operators can understand what changed, how to upgrade, how to verify, and how to roll back.

## Release Model

The current release model is manual semantic-version tags plus manually authored GitHub Releases. Version scope and release history are tracked in [semantic_versioning/CHANGELOG.md](semantic_versioning/CHANGELOG.md); release readiness evidence is assembled from this checklist and the retained CI/CD artifacts referenced below.

Do not add or require `.github/workflows/release.yml`, Release Drafter, or automatic semantic-release behavior until the release evidence bundle format is stable and the automation can attach or link durable evidence without relying only on expiring GitHub Actions artifacts. Conventional Commits remain the preferred commit-message style, but they do not automatically publish or version releases today.

## Prospective Governed Release Contract

The approved future release architecture is documented in
[ADR-025](adr/ADR-025-provider-neutral-release-governance.md),
[RELEASE_POLICY.md](RELEASE_POLICY.md), and [RELEASE_RUNBOOK.md](RELEASE_RUNBOOK.md).
It is not an active automation path. Until its trusted bundle, release-engine
verification, signer policy, protected provider controls, and advisory dry run are
implemented and accepted, operators MUST follow this manual checklist and MUST NOT
claim automated release approval.

After activation, a release MUST prepare and validate one final commit `B`. `B` MUST be
the signed annotated tag target, the candidate evidence commit, and the stable `main`
target when that tag is the newest stable release. No branch head is part of that
identity: the tag object alone is the release, so any release stays verifiable after the
branch that carried its commits advances or is deleted. The operator still approves the
release; tooling only verifies and records evidence, and it removes no governance gate
from this checklist.

The advisory activation dry run required before any of this becomes required is an
executable specification, not a document: see
`eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ReleaseActivationDryRunTests.cs` and
`TagAnchoredReVerificationTests.cs`. They walk prepare, exact-`B` candidate attestation,
canonical tag message, SSH-signed annotated tag, final evidence, and the stable-`main`
proposal against a disposable repository, then re-verify an already-closed release after
its branch has moved and after it has been deleted.

## Release Evidence Bundle

Before publishing a GitHub Release, download the retained CI/CD artifacts listed in this checklist into a local evidence directory. For governed release-mode bundles, the artifact tree must contain exactly one final canonical manifest from `verify-tag`:

```text
docs/internal/releases/<version>/release-evidence.v1.json
```

That manifest exclusively owns release identity: version, line, tag name, tag object, final commit `B`, candidate-manifest digest, release descriptor/summary/context/notes hashes, and trusted bundle/tool/policy/config/trust hashes. Bundle collection time, workflow/provider run IDs, URLs, CLA status, and transport metadata are noncanonical and cannot override it.

Set `RELEASE_VERSION`, `GITHUB_SHA`, `GITHUB_REF`, `RELEASE_TAG_OBJECT_ID`, `GITHUB_REPOSITORY`, `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT`, and `CLA_STATUS` when generating the bundle outside GitHub Actions. `RELEASE_VERSION`, `GITHUB_SHA`, `GITHUB_REF`, and `RELEASE_TAG_OBJECT_ID` must agree with the final manifest or the bundle fails closed. Then generate the durable bundle:

```bash
dotnet run .ci/scripts/generate-release-evidence-bundle.cs -- artifacts release-evidence
```

The output path must not already exist. Use a new path for every attempt; the script
publishes all four files with one directory rename and never replaces or merges a
prior bundle.

The script writes:

- `release-evidence/release-evidence.json` — machine-readable evidence manifest;
- `release-evidence/release-evidence.md` — full human-readable evidence summary;
- `release-evidence/release-evidence-release-notes.md` — copy/paste GitHub Release evidence section;
- `release-evidence/release-evidence-checksums.sha256` — SHA-256 hashes for every retained evidence file.

The checksum file is produced through `.ci/scripts/write-artifact-checksums.cs` and must include release inputs/generated outputs (`release.yaml`, `summary.md`, `release-context.v1.json`, `release-notes.md`, `release-candidate.v1.json`, `release-evidence.v1.json`), trusted bundle promotion receipt/signature/manifest, signer/tag verification evidence, governance policy/config/trust files, and the existing container, deployment, OpenAPI, test, dependency, workflow-security, secret-scanning, scorecard, and security-test categories when present.

Treat `release_bundle_*` and `artifact_checksums_*` diagnostics as release blockers. Do not rename or normalize rejected paths: rebuild the retained tree with one NFC, case-distinct, non-symlinked path per artifact and rerun from a fresh output path. A failed run must not leave a final output directory or partial bundle files.

Attach the generated bundle files to the GitHub Release or copy them to durable release storage before the source GitHub Actions artifacts expire. Paste the contents of `release-evidence-release-notes.md` into the GitHub Release body so release readers can find the durable evidence even after workflow artifacts expire.

## Pull Request Release Impact Gate

Pull requests that touch security/auth, migration/data/rollback, configuration/secrets/deployment, OpenAPI/client contract, or operator/self-hosting/release-note paths must satisfy the `Release Impact Check` before merge. The check validates the `## Release Impact` section in `.github/PULL_REQUEST_TEMPLATE.md` and requires the matching category checkbox plus non-empty `Details:`.

Use `Not applicable` only when the change has no release-impact category. If the check flags a category, update the PR body and link the relevant documentation or release-note evidence before requesting release approval.

## Change-Id Preflight

- [ ] Public change metadata was created with `create-change`, which emitted
  one collision-resistant fragment ID and its exact commit footer together.
- [ ] Local `pre-commit` and `commit-msg` checks are installed through
  `install-change-hooks --target develop` or equivalent CI checks enforce the
  same commands.
- [ ] `preflight-range --target develop --head <feature-head>` passes before
  merge conflict resolution begins.
- [ ] Any immutable-footer correction has one reviewed
  `docs/internal/releases/change-id-renames/<full-commit-oid>.yaml` record and generated
  replacement fragment; no amend, rebase, force-push, or loose alias was used.

## Release Metadata

- [ ] Version/tag is selected.
- [ ] Commit SHA is recorded.
- [ ] Image tags or deployment artifacts are recorded, including full-commit immutable `sha-*` / `dev-*` promotion tags when container images are published.
- [ ] Image digests, immutable promotion tag evidence, Docker base image digest pins, SBOM/provenance evidence, image scan artifacts, and attestation verification results are recorded when container images are published.
- [ ] Coolify-side consumption evidence is retained for container deployments: application configuration, API output, deployment logs, smoke summary, or deploy summary proves the running resource consumed either `image@sha256:<digest>` or the verified full-commit immutable tag.
- [ ] Deployment environment, approver, expected immutable image tag, expected image digest, webhook result, smoke-check result, whether smoke was required, deployment-freeze state, override reason if any, and rollback note are recorded for staging/production deployments.
- [ ] Supported deployment modes are stated: the minimum standalone API + Blazor + SQLite topology, the split topology, single-tenant or multi-tenant operation, and every enabled optional service.
- [ ] Known incompatible versions are stated.

## Third-Party Software And Outgoing License

- [ ] Release notes and commercial terms apply the ISLAMU license only to ISLAMU-owned or separately sublicensable material and expressly preserve each third-party material's own license or terms.
- [ ] The release inventory separates linked/runtime dependencies, base images, optional service images, datasets/assets/fonts, and hosted provider/API terms instead of treating them as one license surface.
- [ ] The minimum standalone operational topology is documented as one `Event.Standalone` process/container containing the API and Blazor BFF/UI with SQLite persistence; no optional external service is described as a core requirement.
- [ ] Every optional service is identified as operator-pulled or ISLAMU-conveyed. Referenced deployment manifests do not describe third-party artifacts as ISLAMU-licensed software.
- [ ] Every ISLAMU-conveyed third-party binary or image has an exact version and digest, upstream license evidence, SBOM, required notices/attributions, corresponding-source or source-offer evidence where applicable, modification provenance, and any required commercial entitlement.
- [ ] No floating tag (`latest`, an unqualified major tag, or equivalent mutable alias) is accepted as license, provenance, or offline redistribution evidence.

## Code And Test Gates

- [ ] Release build succeeds:

  ```bash
  dotnet build --configuration Release --verbosity quiet
  ```

- [ ] Required per-project tests pass; do not run solution-level `dotnet test`.
- [ ] Architecture tests pass:

  ```bash
  dotnet test --project Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet
  ```

- [ ] Infrastructure-dependent tests are either passed in the release lane or explicitly marked as deferred with the reason; email or messaging changes include the focused `Explore.Infrastructure.Tests` `Email` category evidence.
- [ ] Manual visual checks are completed when the release changes auth, routing, onboarding, or core browser flows.
- [ ] Required GitHub checks match [CI_CD_GOVERNANCE.md](CI_CD_GOVERNANCE.md): fast build/test, OpenAPI drift, CodeQL, dependency review, and any path-relevant security/Cerbos checks. If `CodeQL Advanced` owns uploads, confirm GitHub CodeQL default setup is `not-configured`.
- [ ] Advisory/nightly failures are triaged or explicitly deferred with owner and reason.

## Migration And Data Contract

- [ ] New EF migrations are named, reviewed, and tied to the feature/release.
- [ ] Clean-database migration and a second idempotent MigrationService run pass for PostgreSQL, SQLite, SQL Server, MariaDB, and MySQL on the release matrix.
- [ ] Provider-specific application and Data Protection migration assemblies are present, generated from the model, and use their governed history tables; generated migrations and snapshots were not hand-edited.
- [ ] Namespace evidence proves PostgreSQL/SQL Server use the configured schema with clean unprefixed table names, while SQLite/MariaDB/MySQL force `ie_` for application and history tables.
- [ ] Quartz scheduler-schema ownership remains in the API as idempotent DDL (no EF Core migration, no second `DbContext`); every provider release lane proves the `QRTZ_` tables are created and that re-running startup is non-destructive.
- [ ] Migration impact is documented: additive, data backfill, destructive, or rollback-sensitive. Do not claim below-floor compaction or DR rehearsal coverage, or any RPO/RTO number, until it is shipped and linked in evidence.
- [ ] Every changed primary provider passes generated application and Data Protection apply/rollback/reapply, pending-model, runtime behavior, and lock contracts.
- [ ] A five-provider migration rebaseline records every generated initial ID and states explicitly whether existing databases require recreation; removed development histories are never stamped as already applied.
- [ ] Persistence evidence records bounded query shape, a critical owned mutation score above 85%, and zero-sensitive logs/reports.
- [ ] Data-protection/key storage impact is documented if changed.
- [ ] Seed data or lookup table changes are documented.
- [ ] Rollback strategy is documented in release notes.

## Configuration And Secrets Contract

- [ ] New or changed environment keys are documented in `CONFIGURATION.md` or `SECRETS.md`.
- [ ] Removed keys are listed with replacements.
- [ ] `Database:*` provider, endpoint, TLS, and runtime/migrator role inputs are documented without raw connection-string secrets; MariaDB/MySQL release inputs pin matching server flavor and version.
- [ ] `Database:Schema` / `DATABASE_SCHEMA` is documented as the PostgreSQL/SQL Server namespace only; flat providers retain `ie_`, and multi-instance deployments sharing one database give each instance a distinct `Scheduler:Quartz:SchedulerName` (or enable clustering deliberately).
- [ ] The default `EmbeddedSqlite` privacy-erasure authority uses a dedicated local volume, one writer, independently rehearsed backup/restore, and no primary-database credential. Any `ExternalDatabase` topology uses a distinct structured PostgreSQL target and separate roles.
- [ ] Secret-provider paths and key names are documented.
- [ ] Keycloak realm/client/role changes are documented.
- [ ] Optional profiles (`storage`, `authz`) and dependencies are documented.

## Security And Operations Contract

- [ ] Authentication/authorization changes are documented in `SECURITY-MODEL.md`, `SECURITY_OVERVIEW.md`, or `AUTHORIZATION_PATTERNS.md`.
- [ ] Rate-limit, timeout, forwarded-header, CORS, or proxy changes are documented.
- [ ] Health-check, metrics, logging, or tracing changes are documented in `OPERATIONS.md`.
- [ ] Backup/restore impact is documented in `BACKUP_RESTORE_UPGRADE.md` when data shape changes. The runbook must preserve the authority independently from the primary database, cover embedded SQLite file/WAL handling or the external PostgreSQL target as selected, and require restore rehearsal plus MigrationService idempotency proof.
- [ ] Known vulnerabilities or dependency warnings are triaged.
- [ ] Secret scanning, push protection, Dependabot security updates, dependency graph, and CodeQL alerts are enabled or explicitly waived at repository/organization level. Current credential rotation status is documented as restart-based when that is the proven behavior; do not imply live reload or zero-downtime rotation unless it is separately proven.

## CI/CD Evidence Contract

- [ ] OpenAPI drift artifacts are clean, or generated `openapi.json` / NSwag client changes are reviewed and committed.
- [ ] `schemas/configuration-manifest-v1alpha2.schema.json` passes the generator `--check` command, is staged with release contract assets, and its exact SHA-256 is included in durable release evidence.
- [ ] Intentional breaking API contract changes include a matching `docs/API_CHANGELOG.md` entry with affected route/schema/client method, old/new behavior, affected clients, migration guidance, release target, and retained OpenAPI / advisory `oasdiff` evidence links when available.
- [ ] Container image digest, immutable promotion tag evidence, Docker base image digest pins, SBOM/provenance, Trivy scan output, attestation verification JSON, checksum manifest, and image tag evidence are recorded when images are published.
- [ ] Deployment evidence includes environment, component, commit SHA, expected immutable image tag, expected image digest, promotion evidence path, webhook result, smoke-check result, whether smoke was required, deployment-freeze state, override reason if any, workflow run link, and rollback note.
- [ ] Production deployment approval and branch restrictions are configured in GitHub Environment settings.
- [ ] Long-lived release evidence is copied from expiring GitHub Actions artifacts into release notes or durable storage when required.
- [ ] The durable release evidence bundle accepted exactly one `release-evidence.v1.json`; its canonical identity matched `RELEASE_VERSION`, `GITHUB_SHA`, `GITHUB_REF`, `RELEASE_TAG_OBJECT_ID`, and all retained source/tool/checksum artifacts.
- [ ] Any failed gate rerun or emergency override follows [CI_CD_RUNBOOKS.md](CI_CD_RUNBOOKS.md) and records owner, reason, evidence, compensating control, and removal condition.

Expected artifact names:

- `test-results-fast`
- `test-results-integration`
- `openapi-contract-guard`
- `configuration-manifest-v1alpha2.schema.json`
- `security-test-evidence`
- `cerbos-policy-evidence`
- `cerbos-policy-publish-evidence`
- `container-build-*`
- `deployment-production-evidence` / `deployment-staging-evidence`

## Ticketing Recovery And Capability Status

- [ ] `ticketing-capabilities.json` parses with only
  `production-approved`, `test-only`, or `disabled` statuses.
- [ ] Every `production-approved` ticketing capability has retained provider,
  legal/tax, scholarly, accessibility, privacy, security, operator-readiness,
  and restore/takeover evidence required by its matrix entry.
- [ ] Protected delayed payout remains `disabled` and has no route, HAL
  relation, scheduler job, configuration key, secret, client method, or UI
  action.
- [ ] Recovery configuration is disabled by default; enabled deployments bind
  exact release/schema, key, authority, provider, idempotency, and worker-fence
  floors.
- [ ] A production-like timed restore proves declared RPO/RTO, cancellation of
  pre-restore bearer authority, one reissue per ticket, unknown-provider
  reconciliation, multi-replica Quartz takeover, workers-first reopening, and
  sales-last reopening.
- [ ] Recovery health exports only status, bounded counts, and age. No tenant,
  actor, event, ticket, order, amount, provider object, capability, digest,
  secret, or exception text is present.

## Documentation Impact

Choose exactly one docs impact outcome for the release:

| Outcome | Required Evidence |
|---|---|
| Updated | Linked docs PR/commit updates affected docs. |
| Not needed | Explanation of why runtime/API/operator behavior did not change. |
| Deferred | Follow-up path, owner, and reason the release can proceed. |

Release-blocking docs usually include operator, security, API contract, onboarding, configuration, and migration changes.

## Release Notes Template

Use this structure for release notes:

```markdown
## Version X.Y.Z

### Highlights
- ...

### Upgrade Notes
- Backup requirements:
- Migration behavior:
- Config/secret changes:
- Rollback notes:

### Security Notes
- ...

### Operator Verification
- ...

### CI/CD Release Evidence
- Commit SHA:
- Attached evidence bundle files:
  - `release-evidence.json`
  - `release-evidence.md`
  - `release-evidence-checksums.sha256`
  - `release-evidence-release-notes.md`

### Documentation Impact
- Updated | Not needed | Deferred: ...
```

## Related

- [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) — backup, restore, upgrade, rollback runbook.
- [CONTRIBUTING.md](CONTRIBUTING.md) — PR validation checklist.
- [TESTING.md](TESTING.md) — test project taxonomy and commands.
- [DOCUMENTATION_ARCHITECTURE.md](DOCUMENTATION_ARCHITECTURE.md) — docs impact contract.
