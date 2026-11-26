# Release Runbook

> **Audience:** Platform/Ops | Release operators | Maintainers | AI agents
> **Status:** Implemented at source level; production SSH signer enrollment and live release rehearsal require Project Steward approval
> **Owner:** Platform/Ops and release operators
> **Last Verified:** 2026-11-26
> **Source Anchors:** `eng/release/`, `.github/workflows/release.yml`, `.github/workflows/_container-build.yml`, `.github/workflows/workflow-security.yml`, `docs/internal/RELEASE_POLICY.md`, `docs/internal/RELEASE_CHECKLIST.md`

This runbook provides the day-to-day commit/fragment workflow, verified toolchain provisioning, signed release execution, failure recovery, and authorized live-rehearsal procedures for ISLAMU Event.

## Complete release-impact metadata

Before the license audit, CI restores the conditional Standalone container graph
and the moderation launcher, browser installer, migration scaffolder, and shared
Keycloak tool owners without running their operations. It finishes with ordinary
solution assets for the build. For a missing-metadata failure, verify package and
version coverage for these profiles before changing policy or relying on a warm
local cache.

Review complete changed-file metadata, including previous names for renames.
A large PR must not bypass impact classification or lower its declared file count.
See [the trusted metadata contract](CI_CD_GOVERNANCE.md#complete-pr-file-metadata).
A workflow correction on a PR branch does not replace the trusted-base version
used by `pull_request_target`; arrange an independently reviewed base update
before relying on its rerun.

## Operational gates before tagging

- **CodeQL:** if CodeQL is cancelled, inspect the actual workflow conclusion and phase
  timestamps before interpreting the result. Follow
  [the operational procedure](OPERATIONS.md#codeql-analysis-cancellation)
  and retain a completed analysis for the candidate; a traced build alone does not satisfy the gate.
- **Codecov:** treat Codecov feedback as advisory shipped-product evidence. Check commit and
  included suite flags; never combine different commits or substitute coverage for required tests.
  See [coverage publication operations](OPERATIONS.md#codecov-coverage-publication).
- **Browser prerequisites:** prepare the owned browser through `eng/tools/InstallAssuranceBrowser.cs`
  and its dedicated committed lock without disturbing shared tool locks. See
  [security fixture prerequisites](CI_CD_GOVERNANCE.md#security-integration-fixture-execution).
  Only actual case execution satisfies the browser portion of candidate assurance.
- **SDK and `.NET 10` container parity:** verify the official SDK (`global.json` `10.0.404`) and
  digest-pinned `.NET 10` base images across all four Dockerfiles (`Explore.API`, `Explore.Blazor`,
  `Event.Standalone`, `Event.MigrationService`). Follow
  [the servicing baseline](OPERATIONS.md#sdk-and-runtime-servicing) and
  [operator rebuild instructions](../public/documentation/readme/self-hosting/docker-standalone.md).

## Native authority provisioning and recovery

Native authorization and read paths require a database provisioned through the
matching migrator/provisioning path. Disposable development databases can be recreated;
do not hand-edit roles, grants, functions, or RLS policies to imitate the expected
state.

- **Category and lookup writes:** Runtime receives a narrow, attested DML contract
  for `categories` and `category_type_categories`. `category_types` remains a
  read-only global lookup for Runtime; its controller exposes only GET operations
  and admin/test seeding uses the migrator context. The grants are installed by
  `PostgresCategoryMutationPermissions`; a partial or widened grant set fails the
  catalogue attestation instead of being silently accepted.
- **Instance bootstrap reads:** HTTP-runtime reads use the Global
  `IInstanceBootstrapStateReader`. Runtime direct access to
  `instance_bootstrap_states` remains denied with PostgreSQL `42501`. Do not grant
  Runtime table access to work around an onboarding-status failure; verify the
  reader/Global pool configuration instead.
- **Instance administration:** `/api/admin/instance/*`, instance onboarding, and
  instance settings are installation-scoped and do not require `X-Tenant-Slug`.
  Do not add a synthetic default-tenant header or a controller-wide tenant
  exemption. Missing HAL actions should be diagnosed through instance authority
  and the permission-bound batch path, not by granting tenant authority.
- **Provider-configuration discovery:** instance-scoped secret binding references
  are read through the Global provider-configuration contract. Runtime direct
  `secret_bindings` access stays denied.

This pre-release development scope uses freshly provisioned databases; it does
not provide an in-place database downgrade or old-data compatibility path.
To return to an earlier candidate, provision fresh databases with that candidate's
migrator and re-run its native attestations before starting the matching binary.
Never widen a runtime role to make an older binary start. See
[native runtime qualification](testing/NATIVE_RUNTIME_QUALIFICATION.md) for fresh
migration, actual-role readiness, and original-operation recovery evidence.

## Verified `git-cliff` `2.14.2` toolchain

`eng/release/toolchain.lock.json` pins official `git-cliff` `2.14.2` archives and extracted executables across `linux-x64`, `linux-arm64`, `osx-arm64`, and `windows-x64`.

1. **Provision explicitly (sole network-capable command):**
   ```bash
   dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- ensure-git-cliff
   ```
2. **Verify strictly offline:**
   ```bash
   dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- verify-tools
   ```

All authoritative invocations (`prepare`, tests) verify executable SHA-256 and version locally and run with `--config`, `--offline`, `--no-exec`, and `GIT_CLIFF_OFFLINE=1` via argument arrays without a shell. Set `ISLAMU_RELEASE_TOOL_BUNDLE` or `ISLAMU_RELEASE_TOOL_CACHE` to customize local storage.

## Day-to-day commit and optional fragment workflow

1. Confirm `.githooks` is active (`git config --get core.hooksPath` → `.githooks`). `.githooks/commit-msg` is a thin wrapper around `preflight-commit`.
2. Routine non-breaking internal commits (`test`, `refactor`, `style`, `build`, `ci`, `chore`, or engineering scopes such as `release`, `ci`, `documentation`, `testing`) omit automatically without `Changelog: skip` trailers.
3. Breaking changes require both `!` in the header and a non-empty `BREAKING CHANGE:` footer and stay release-visible.
4. For high-impact user/operator/API/migration/security changes requiring structured evidence, create an optional fragment under `docs/internal/releases/changes/`:
   ```bash
   dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
     create-change --target develop --type <type> --scope <scope> \
     --title "<title>" --summary "<summary>"
   ```
   `preflight-commit` and `preflight-range` validate every staged/referenced fragment with full `ReleaseInputPolicy.Validate`, rejecting `Public-Disclosure: embargoed` and `Restricted-Details` keys while allowing ordinary English prose.
5. Before merging a feature branch, verify the bounded range:
   ```bash
   dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
     preflight-range --target origin/develop --head HEAD
   ```

## Enrolling production SSH release signers (Project Steward gate)

`eng/release/trust/allowed-signers` is intentionally comment-only until the Project Steward approves production signer enrollment, so `verify-release-tag` and `.github/workflows/release.yml` fail closed (`tag_allowed_signers_empty`).

When the Project Steward authorizes enrollment:

1. Each authorized release operator generates a dedicated Ed25519 signing key on their own hardware/workstation and shares only the `.pub` file (private keys never enter Git, CI secrets, or chat):
   ```bash
   ssh-keygen -t ed25519 -C "islamu-release-operator"
   ```
2. Add the reviewed principal line (`<principal> namespaces="git" ssh-ed25519 <base64-key>`) to `eng/release/trust/allowed-signers`, record the rotation/enrollment in `eng/release/trust/rotation-history.md`, and merge through protected review.
3. Configure the GitHub `release` environment (required reviewers, tag protection `v*` only), a GitHub **tag ruleset** restricting updates and deletions on `v*` (with reviewed minimal bypass actors), and optional container registry settings in GitHub Repository or Environment (`release` / `production` / `staging`) **Settings → Secrets and variables → Actions** (zero registry hosts or personal handles are hardcoded in workflow YAML):

   | Setting | Kind | Required | Purpose |
   |---|---|---|---|
   | `CONTAINER_REGISTRY` | Variable (`vars`) | Optional | External OCI registry host (e.g., `docker.io`, `quay.io`, `atcr.io`). Leave unset or set `ghcr.io` for **GHCR-only** releases (`4` canonical digest references; namespace always resolves to lowercase `github.repository_owner`). |
   | `CONTAINER_REGISTRY_USER` | Variable (`vars`) | When `CONTAINER_REGISTRY` is non-GHCR | OCI repository namespace (e.g., Docker Hub org/user or Quay namespace; supports OCI separators `.`, `_`, `__`, `-+`). Ignored in GHCR-only mode. |
   | `CONTAINER_REGISTRY_LOGIN_USER` | Variable (`vars`) | Optional | Login username for the external registry when it differs from the OCI namespace (for example a Quay robot `org+deploy` or a personal Docker Hub username pushing to a Docker Hub org). Defaults to `CONTAINER_REGISTRY_USER` when unset. |
   | `CONTAINER_REGISTRY_PASSWORD` | Secret (`secrets`) | When `CONTAINER_REGISTRY` is non-GHCR | Registry password or token for the external OCI registry. Unused in GHCR-only mode (uses built-in `GITHUB_TOKEN`). |

   **Coolify registry cutover check:** Coolify webhooks (`deploy-coolify.yml` / `deploy-coolify-develop.yml`) are bearer-authenticated `GET` triggers that neither set nor verify the deployed container image reference or digest. Before the first staging or production deployment after changing `CONTAINER_REGISTRY` (for example cutting over from ATCR to GHCR-only, or later to Docker Hub), update the Coolify API and UI resource image repositories and pull credentials to match `<registry>/<namespace>/islamu-event-{api,ui}`, and verify in Coolify after deployment that the running container digest equals `Expected image digest` from the deployment summary.

## Executing a governed release

### Step 1 — Preview conservative contract diff locally

Run `diff-contracts` between the pinned previous release OID (or signed baseline OID) and the candidate `HEAD` OID:

```bash
base_oid="$(git rev-parse <previous-tag>^{commit})"
head_oid="$(git rev-parse HEAD^{commit})"
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  diff-contracts "$base_oid" "$head_oid" /tmp/contract-diff-preview.json
```

Review every `breaking` and `review_required` entry across OpenAPI/HAL, configuration contracts (`.env.example` secret/default values remain redacted), and discovered EF Core migration inventories. Validate migration semantics with real provider apply/idempotency tests and EF Core pending-model checks rather than treating the migration ledger as parity proof.

### Step 2 — Commit human-owned release inputs only

Create and review ONLY the two human-owned files under `docs/internal/releases/<version>/`:

- `docs/internal/releases/<version>/release.yaml` (selected version, release line `v<major>.<minor>`, fixed release date, `Base-Stable-Tag`, `Previous-Published-Tag`, full base/previous OIDs, compatibility, and six impact dispositions).
- `docs/internal/releases/<version>/summary.md` (concise maintainer-owned summary, UTF-8 LF, no Markdown headings or raw HTML).

Do **not** commit pre-generated `release-notes.md`, `release-context.v1.json`, `release-rendering-context.v1.json`, or `contract-diff.v1.json` before tagging. `.github/workflows/release.yml` runs `prepare ... --image-manifest` after container staging so published release notes bind to real built image digests.

For the first governed release only (when no stable SemVer tag is reachable), create and verify one signed annotated `changelog-baseline-YYYY-MM-DD` tag targeting the baseline commit using `verify-release-tag`.

### Step 3 — Create and verify the SSH-signed annotated tag

```bash
target_oid="$(git rev-parse HEAD^{commit})"
git -c gpg.format=ssh -c user.signingKey=/path/to/release-signing-key \
  tag -s "v<version>" "$target_oid" -m "Release v<version>"

dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  verify-release-tag --tag "v<version>" --target "$target_oid"
```

Push the verified tag (`git push origin "v<version>"`). Pushing a matching SemVer tag triggers `.github/workflows/release.yml`.

### Step 4 — Automated `.github/workflows/release.yml` execution order

The workflow executes six least-privilege jobs under per-tag concurrency (`cancel-in-progress: false`) and the protected `release` environment:

1. **`preflight` (`contents: read`):** runs `verify-release-tag --tag <tag> --event-sha <sha>` against `eng/release/trust/allowed-signers`, runs the pre-build sealed-release guard BEFORE requiring mirror credentials:
   - HTTP `404` → proceeds (`should-publish=true`) and then validates `vars.CONTAINER_REGISTRY` / `vars.CONTAINER_REGISTRY_USER` / optional `vars.CONTAINER_REGISTRY_LOGIN_USER` / `secrets.CONTAINER_REGISTRY_PASSWORD` (GHCR-only `4` refs when unset or `ghcr.io`, or `8` refs with an external OCI mirror).
   - HTTP `200` → downloads existing published assets and verifies `SHA256SUMS`, `cosign verify-blob` against `release.yml@refs/tags/<tag>`, and `release-artifact-manifest.v1.json` (`version`, `tag`, `sourceCommitOid`, 4 images, symmetric canonical references). Matching sealed releases exit `0` as an idempotent no-op with zero builds even if mirror credentials were rotated or removed; any mismatch fails closed.
   - Any other HTTP status → fails closed before building.
2. **`build-images` (`_container-build.yml`, `push-mode: digest-only`):** builds all four multi-arch `.NET 10` OCI indexes (`islamu-event-api`, `islamu-event-ui`, `islamu-event-standalone`, `islamu-event-migration-service`) with zero GHA build cache, exports Buildx SBOM/provenance inspections, scans with Trivy (`CRITICAL,HIGH`), stages by digest only (`push=true,push-by-digest=true,name-canonical=true`) to GHCR (and the configured external OCI mirror, if any) with **no registry tags**, and creates/verifies GHCR SLSA attestations. Artifact uploads set `overwrite: true` so job reruns do not collide on `409 Conflict`.
3. **`prepare-and-seal` (`contents: read`):** assembles canonical `release-images.v1.json` (4 images and every staged canonical tagless reference), provisions verified `git-cliff` `2.14.2`, runs offline `prepare <release-dir> --image-manifest release-images.v1.json` (Stage 1 transient `--context` → Stage 2 allowlisted `release-rendering-context.v1.json` → Stage 3 `--from-context -` three-persona `release-notes.md`), and runs `seal-artifacts` into a fresh temp directory to produce `release-artifact-manifest.v1.json` and sorted `SHA256SUMS` without clobbering.
4. **`sign-and-attest` (`packages: write`, `id-token: write`, `attestations: write`, `artifact-metadata: write`):** verifies sealed checksums and exact 7-file inventory, generates GitHub SLSA file attestations (`actions/attest`), Cosign-signs and verifies **every** canonical `registry/repository@sha256:<digest>` reference across every configured registry individually, signs and verifies `SHA256SUMS` via `cosign sign-blob --yes --bundle SHA256SUMS.sigstore.json`, and verifies every GitHub file attestation via `gh attestation verify`.
5. **`publish-release` (`contents: write` only):** re-verifies `SHA256SUMS` and `SHA256SUMS.sigstore.json`, verifies remote tag-object binding (`refs/tags/<tag>` object type `tag`, SHA `== tag-object-id`, target commit `== target-commit`), creates or resumes a single draft GitHub Release by numeric ID without `--clobber` (refusing any published release, multiple drafts, wrong target commit, unexpected assets, or differing deterministic asset bytes; preserving an existing draft `SHA256SUMS.sigstore.json` only if it independently verifies `SHA256SUMS` via Cosign), downloads all 8 draft assets to verify byte-for-byte equality + `sha256sum --check SHA256SUMS` + `cosign verify-blob`, re-verifies tag binding and exact draft asset identities `(id, name, size, updated_at, digest)` immediately before publishing once (`PATCH /releases/<draft-id>` `draft=false`), and immediately runs a post-publish round-trip verification (failing loudly and blocking `append-contributors` if any mutation occurred in the publish window).
6. **`append-contributors` (`contents: write`, `continue-on-error: true`):** runs AFTER sealed publication and post-publish verification using verified `git-cliff` `2.14.2` with `eng/release/cliff-github-contributors.toml` and `--no-exec` (the sole online exception). Rejects any email address pattern, degrades cleanly to unchanged identity-free signed notes if remote metadata is absent or fails, and appends `@handle` / PR links exclusively to the mutable GitHub Release body.

### Step 5 — Synchronize the public changelog ledger (post-tag sealed archive)

Because generated release artifacts are built and sealed **after** tagging (so they include real built OCI image digests), synchronize `docs/public/changelog/README.md` in a normal post-release PR without ever moving the signed tag:

1. Download the published eight release assets for `v<version>` into `docs/internal/releases/<version>/` (leaving committed `release.yaml` and `summary.md` untouched) — or into a staging root `<dir>/<version>/` passed via `--sealed-artifacts-root <dir>`.
2. Authenticate `SHA256SUMS` with `cosign verify-blob` (`SHA256SUMS.sigstore.json`) and `gh attestation verify` against `.github/workflows/release.yml@refs/tags/v<version>` before trusting the downloaded files.
3. Run `sync-public-changelog` (and verify with `--check`):

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  sync-public-changelog --publication-base https://github.com/islamu-ngo/Event/blob/

dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  sync-public-changelog --publication-base https://github.com/islamu-ngo/Event/blob/ --check
```

`sync-public-changelog` cryptographically verifies the SSH-signed annotated tag `v<version>`, confirms `release.yaml`, `summary.md`, and every linked `CHG-*.yaml` byte-match the signed tag's target commit tree, verifies the sealed post-tag archive (`release-artifact-manifest.v1.json`, `SHA256SUMS`, `SHA256SUMS.sigstore.json`, `release-images.v1.json`, `release-context.v1.json`, `release-notes.md`) against that exact `sourceCommitOid`, writes the updated entries-only projection and `publication-manifest.v1.json` (preserving YAML frontmatter), and fails closed on any tamper or accepted-page drift. Commit the verified sealed release archive plus `docs/public/changelog/README.md` and `docs/public/changelog/publication-manifest.v1.json` via PR so future CI `--check` runs and subsequent releases verify 100% offline.

## Failure recovery and non-atomicity rules

GitHub Releases, GHCR, and any configured external OCI mirror do **not** commit atomically, and GitHub's REST API does not provide atomic compare-and-swap on `draft=false`. Restrict repository `contents: write` access and enforce a `v*` tag ruleset to close the authorized-editor window. Follow these exact recovery rules:

| Failure point | Observable staging state | Required operator action |
|---|---|---|
| Failure in `preflight` | Zero images built, zero drafts, zero public release. | Fix signer enrollment, tag binding, or release inputs; never force-move an already-shared tag. |
| Failure during `build-images` or `sign-and-attest` | One or more unadvertised digest-only OCI objects may exist in GHCR (and any configured external OCI mirror), with zero registry tags assigned and no draft or public release. | Diagnose the failure (Trivy CVE, registry outage, OIDC/Cosign error) and rerun failed jobs for the same tag. Digest-only objects remain unadvertised until signed and published. |
| Failure during `publish-release` before `draft=false` | An unpublished draft GitHub Release with partial or complete assets may exist alongside staged digest-only OCI indexes. | Prefer GitHub Actions **"Re-run failed jobs"** so `publish-release` reuses the original verified `release-signed-<version>` workflow artifact without minting a second Sigstore bundle. Even on a full rerun, `publish-release` verifies all 7 deterministic assets byte-for-byte without `--clobber` and preserves an existing draft `SHA256SUMS.sigstore.json` only after independently verifying it with Cosign. If an interrupted draft contains corrupted partial assets, delete the **unpublished draft** after recording incident evidence and rerun (never delete a published release). |
| Failure in post-publish verification (`publish-release`) | A published release diverged from the pre-publish verified snapshot or tag object. | Treat as a security incident (`append-contributors` is blocked automatically): inspect GitHub audit logs, do not silently overwrite assets, and publish a security advisory and corrective patch release. |
| Failure in `append-contributors` | Published release is 100% valid, signed, attested, and identity-free; only the mutable contributor footer was omitted. | Optional: rerun or leave identity-free. Never rebuild, re-sign, or modify sealed assets. |
| Defect discovered AFTER publication | Sealed release and assets are immutable. | **Never** replace assets, overwrite `SHA256SUMS`, delete/recreate the tag, or rewrite history. Publish a corrective patch release (`v<major>.<minor>.<patch+1>`) and security/operational advisory. |
| Contributor edit or removal request | Contributor `@handle` appears only in the mutable GitHub Release page body. | Edit the GitHub Release page body directly to adjust or remove the requested handle. Signed `release-notes.md`, `SHA256SUMS`, `SHA256SUMS.sigstore.json`, and all attestations remain cryptographically valid untouched. |

## Authorized post-merge release-candidate rehearsal

Source-level tests, `actionlint`, `zizmor`, and CI validators do **not** prove live GitHub OIDC, GHCR or external OCI registry digest-only push behavior, Cosign OCI/blob verification, or draft recovery. Before declaring operational readiness for `v0.1.0`, execute this rehearsal with explicit Project Steward approval:

1. Enroll the reviewed release signer key in `eng/release/trust/allowed-signers` and configure the protected `release` environment (required reviewers, `v*` tag protection + tag ruleset, plus optional `vars.CONTAINER_REGISTRY` / `vars.CONTAINER_REGISTRY_USER` / `vars.CONTAINER_REGISTRY_LOGIN_USER` / `secrets.CONTAINER_REGISTRY_PASSWORD` if mirroring beyond GHCR).
2. Tag a signed release candidate (for example `v0.1.0-rc.1`) targeting an approved commit with `release.yaml` and `summary.md` (and confirm `deploy-coolify.yml` does NOT trigger on the tag push).
3. **Fault-injection drill:** abort or inject a failure after digest-only staging and before signing completes. Confirm:
   - Zero public GitHub Release exists.
   - Zero mutable or version tags exist in GHCR (or any configured external OCI registry) for the staged images.
   - Residual staging state is accurately logged.
4. **Recovery & full verification drill:** rerun the workflow for the same signed tag without replacing assets. Independently verify (using an independently obtained trusted `allowed-signers` copy):
   - `git -c gpg.format=ssh -c gpg.ssh.allowedSignersFile=/path/to/trusted-allowed-signers verify-tag v0.1.0-rc.1`
   - `cosign verify-blob --bundle SHA256SUMS.sigstore.json ... SHA256SUMS` (authenticate `SHA256SUMS` FIRST)
   - `sha256sum --check SHA256SUMS` (verify assets against the authenticated checksums)
   - `cosign verify` on **every** canonical `registry/repository@sha256:<digest>` reference listed in `release-artifact-manifest.v1.json` (4 in GHCR-only mode; 8 when a secondary registry is configured)
   - `gh attestation verify` on all seven attested files and all four GHCR OCI digests
   - Re-triggering the workflow on the already-published tag exits `0` at `preflight` as an idempotent no-op with zero container builds (even when mirror credentials are absent).
   - Contributor API failure/omission preserves the valid identity-free release.
