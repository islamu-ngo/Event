ABOUTME: Governance reference for GitHub Actions checks, branch protection, environments, and CI/CD evidence.
ABOUTME: Separates repository settings from workflow YAML so required gates stay auditable and maintainable.

# CI/CD Governance

> **Audience:** Maintainers | Release operators | Contributors | AI agents
> **Status:** Implemented + repository-settings required
> **Owner:** Platform/Ops
> **Last Verified:** 2026-05-07
> **Source Anchors:** `.ci/`, `.github/workflows/`, `.github/dependabot.yml`, `docs/TESTING.md`, `docs/GOVERNANCE.md`, `docs/OPERATIONS.md`, `docs/RELEASE_CHECKLIST.md`

This page is the source of truth for CI/CD governance. `.ci/` owns shared CI/CD implementation such as reusable scripts, policy validators, evidence writers, the OpenAPI Spectral ruleset, local composite actions, and mirror-provider CI/CD definitions. GitHub-native workflow discovery files stay in `.github/workflows/` because GitHub Actions requires that path.

GitHub remains the authoritative deployment surface for production and staging because the current release evidence model depends on GitHub environments, retained artifacts, GHCR/GitHub attestation verification, and Coolify deployment evidence. Codeberg and other mirrors should be configured provider-side to read CI/CD definitions from `.ci/`; root-level provider adapter folders remain forbidden because the reviewed release adapter contract lives under `.ci/release/` and `.ci/providers/`.

Do not symlink `.github` to `.ci`. GitHub discovers workflows from `.github/workflows`, local reusable workflow calls require `.github/workflows/{filename}`, and reusable workflow subdirectories are not supported.

Workflow YAML defines what runs; repository settings define which checks are required, which environments require approval, and which organization security features are enabled.

## Prospective Provider-Neutral Release Governance

The current production release process remains the manual SemVer-tag and manually
authored GitHub Release process in [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).
No release-engine workflow, trusted bundle, signer set, or provider adapter is active
yet. This section defines the approved future boundary without claiming activation.

When implemented, every release MUST declare the version-line label
`v<major>.<minor>` it belongs to. That label is a classification, not a ref: nothing
derives a branch name from it, and attestation MUST NOT resolve any `refs/heads/*`.
`develop` MUST NOT receive generated `[Unreleased]` changelog writes. The canonical release contract is provider-neutral:
the engine receives complete local Git objects, explicit inputs, and a previously
promoted trusted tool bundle. Provider adapters MAY transport those inputs, retained
artifacts, and protected ref actions, but MUST NOT classify changes, choose a version,
alter canonical notes, or add provider metadata to canonical checksums.

The authoritative final lane MUST use the promoted bundle rather than candidate
engine source, policy, templates, renderer configuration, or signer roots. It MUST
prove one final preparation commit `B` from immutable objects only: candidate attestation,
SSH-signed annotated tag target, and, for the newest stable release, `main` target
MUST resolve to that same full Git object. Candidate jobs remain unprivileged; final
operators retain approval, tagging, publication, deployment, and protected-ref
authority.

ISLAMU policy MUST own release selection, inclusion, impacts, SemVer validation,
range selection, trust, and evidence. git-cliff MAY render only already-normalized
context with an explicit packaged configuration, offline mode, and executable
processors disabled. The sole human-owned per-release inputs are `release.yaml` and
`summary.md`; `release-notes.md` MUST be fully generated. Canonical bytes MUST be
deterministic and provider-neutral. Embargoed security detail MUST stay in a
restricted lane outside the public checkout and normal public artifacts until an
authorized disclosure boundary.

See [ADR-025](adr/ADR-025-provider-neutral-release-governance.md),
[RELEASE_POLICY.md](RELEASE_POLICY.md), and [RELEASE_RUNBOOK.md](RELEASE_RUNBOOK.md)
for the distinct architecture, normative policy, and future operator procedure.

### Change-Id admission before merge

Release-context validation starts before Git history is written. New public
changes use `create-change` so fragment and footer share one generated
ULID-style ID. Local `pre-commit` and `commit-msg` hooks run the repository-owned
release engine, and merge admission runs `preflight-range --target develop
--head <feature-head>` before conflict resolution.

The range check compares effective IDs in `develop..<feature-head>` against IDs
reachable from `develop`, rejects duplicates in the feature range, and requires
the exact fragment. A committed collision is repaired only by an exact
commit-OID-bound `change-id-rename.v1` record plus a generated replacement
fragment. The record is canonical release input; it does not alter the old
footer or weaken target uniqueness.

### Prospective release provider adapters

The provider adapter contract is defined in `.ci/release/adapter-contract.md` and
machine-checked by `.ci/release/provider-definition.schema.json` plus
`.ci/scripts/validate-release-provider-adapters.cs`. Provider manifests live under
`.ci/providers/<provider-id>/provider-definition.v1.json`; discovery workflows stay
beside the manifest unless the forge requires a fixed discovery path such as
`.github/workflows/`.

The first reviewed provider set is:

| Provider | Definition | Discovery surface | Current status |
|---|---|---|---|
| Forgejo / Codeberg | `.ci/providers/forgejo-codeberg/provider-definition.v1.json` | `.ci/providers/forgejo-codeberg/release-adapter-preview.yml`, `.ci/providers/forgejo-codeberg/release-adapter-final.yml` | No-checkout discovery-only; protected final actions require a trusted self-hosted runner, default-branch proof, and separate activation evidence. |
| Tangled | `.ci/providers/tangled/provider-definition.v1.json` | `.ci/providers/tangled/release-adapter-preview.yml`, `.ci/providers/tangled/release-adapter-final.yml` | No-checkout discovery-only; protected-ref CAS and release publication are unsupported without external operator evidence and future default-branch proof. |
| GitHub | `.ci/providers/github/provider-definition.v1.json` | `.github/workflows/release-adapter-preview.yml`, `.github/workflows/release-adapter-final.yml` | Discovery-only; PR preview is read-only and final discovery is `workflow_dispatch` behind the `production` environment. |

All provider definitions must keep `release-adapter-preview` and
`release-adapter-final` as always-present required check surfaces. Preview lanes are
read-only `contents:read` only and must not receive secrets, write permissions, or
OIDC token authority. Final lanes must run only trusted default-branch code after
candidate code has stopped unless they are transport-only no-checkout discovery jobs.
GitHub discovery checks out `${{ github.event.repository.default_branch }}`, not the
event SHA. Forgejo/Codeberg and Tangled final discovery jobs currently perform no
checkout and declare `trustedRef = "no-checkout-discovery"`; the validator permits that
mode only while the final workflow contains no checkout, candidate ref/path, external
command, mutable action/image, or nonliteral execution. Any activated release execution
must migrate to default-branch proof before running release logic. Provider plans may
transport full Git object IDs, immutable bundle paths and hashes, artifact retention
details, required checks, and protected-ref compare-and-swap inputs; they must not
enrich or replace canonical release identity. Provider manifests cannot self-assert
external operator evidence for unsupported capabilities; Tangled protected-ref or
release publication planning requires a separate bounded external-control evidence
input to the validator.

Final events are allowlisted per provider. GitHub is `workflow_dispatch` only.
Forgejo/Codeberg is the reviewed trusted `workflow_dispatch` lane only. Tangled is
`manual` or reviewed `tag_push` only. Pull-request-origin final events, including
`pull_request_target`, are forbidden even when other final-lane flags claim trusted
code.

The validator compares each manifest's declared discovery workflow files against the
manifest: declared actions must be the external actions actually used, final
environment approval must be backed by an environment in the final workflow, GitHub
final checkout must use `${{ github.event.repository.default_branch }}`, transport-only
`no-checkout-discovery` final workflows must stay literal no-op jobs, and preview
workflows must not also expose final events.

### Prospective trust-lane separation

The candidate lane may compile and test candidate release-engine source but has no
signing, protected-ref, publication, deployment, registry-write, OIDC, or promoted
artifact-store credentials. The final lane starts from an independently promoted
bundle plus a separately supplied immutable canonical promotion receipt and detached
SSH signature. The previously promoted verifier resolves the promoter trust root only
from its fixed protected application directory; requests and candidate sibling files
cannot select, replace, or reset promotion authority. The verifier checks the receipt
before reading candidate data and never resolves policy, configuration, tool locks,
signer roots, or promotion authority from the checkout under release. The signed
  receipt binds the canonical manifest digest plus bundle, policy, configuration, and
  trust versions and digests. Reusing it for the same immutable bundle is idempotent;
  using it for any different bundle fails without relying on a verifier-side replay
  registry. Wrong signers or roots, self-created receipts, root aliases, hardlinked
  bundle files, bounded-input violations, normalized or case-insensitive path
  collisions, tampered bundle files, and candidate-local overrides fail before canonical
  candidate data is trusted. Exact receipt reuse for the exact bound manifest adds no
  authority, so the public request has no caller-resettable replay set.

Restricted security input is a separate access-controlled input, not a candidate or
canonical artifact. It is mounted only where candidate executables cannot run. Public
logs and artifacts receive only stable diagnostic codes until disclosure is approved;
after approval, only a reviewed public disposition and advisory reference may cross,
and neither may exactly alias restricted fields after Unicode and whitespace
normalization. The storage provider remains intentionally undecided and cannot affect
canonical identity. Final jobs must reverify the promoted bundle immediately before
use from immutable promoted storage.

Task 5 must derive release-signer booleans, dates, principal, fingerprint, and tag
object identity from local Git and OpenSSH evidence. Forge badges or hosted release
metadata cannot satisfy the signer policy.

Task 5.3 binds the existing durable evidence bundle to that final local evidence.
Release-mode bundle generation MUST find exactly one `release-evidence.v1.json` in
the retained artifact tree, parse it as the canonical final identity, and reject
missing, duplicate, malformed, stale, tampered, or disagreeing manifests. The bundle
MUST verify the final manifest's version, tag name, tag object ID, final `B`,
candidate-manifest digest, release descriptor/summary/context/notes hashes, and
trusted bundle/tool/policy/config/trust hashes against explicit inputs and retained
artifacts. Workflow run IDs, provider URLs, collection time, CLA status, and artifact
transport fields are noncanonical metadata only and MUST NOT change release identity.
Canonical ingestion rejects unknown/duplicate fields, invalid UTF-8, non-NFC or
noncanonical JSON, case/NFC/path aliases, symlinks, oversized trees/files, and
malformed metadata with bounded stable diagnostics. Validation finishes before the
bundle output is published, so rejected input cannot leave a partial final bundle.
The durable checksum manifest MUST be produced through `.ci/scripts/write-artifact-checksums.cs`
and include the final/candidate manifests, release.yaml, summary, generated context
and notes, trusted tool promotion receipt/signature/manifest, signer/tag evidence,
governance policy/config/trust files, and the existing evidence categories.

## Required Repository Settings

These controls cannot be fully enforced from workflow YAML and must be configured in GitHub repository or organization settings.

### Branch Protection / Rulesets

Protect `main` and `develop` with a ruleset or branch-protection rule that requires pull requests and current status checks before merge.

**Reserved version-tag glob.** A branch ruleset MUST include `refs/heads/v*` with a `creation` rule so no branch can be created in that namespace. Version tags own the `v*` glob outright: a branch named `v0.1` beside tag `v0.1.0` would let a bare name resolve to either object, so this is refused at ref-creation time rather than disambiguated afterwards. Maintenance lines use `release/<major>.<minor>` instead and are opened on demand from a verified signed stable tag. `.ci/scripts/validate-repository-settings.cs` reports `hasReservedVersionTagGlobRule` and fails the drift check when the rule is absent.

Recommended checks after the PR1-PR8 governance baseline stabilizes:

| Check | Workflow | Job name | Branch-protection status | Notes |
|---|---|---|---:|---|
| Fast build/test | `Build & Test` (`.github/workflows/test.yml`) | `run-tests` / reusable `build-and-test` | Ready after repository-side check-name verification | Always-present wrapper detects build/test-relevant paths and intentionally no-ops for dedicated docs/schema/ops-only changes. Keep name stable unless branch protection is migrated. |
| Workflow security | `Workflow Security` (`.github/workflows/workflow-security.yml`) | `Workflow Security` | Ready after repository-side check-name verification | Always-present wrapper validates action SHA pins, Dependabot action-update policy, workflow cache policy, actionlint, and medium-or-higher zizmor findings for workflow-governance changes. |
| OpenAPI drift | `OpenAPI Contract Guard` | `OpenAPI Contract Guard` | Required | Always-present workflow with internal no-op detection for unrelated changes. |
| Code scanning | `CodeQL Advanced` | `Analyze (csharp)`, `Analyze (javascript-typescript)`, `Analyze (actions)` | Ready after repository-side check-name verification | Always-present detector keeps matrix checks present, runs analysis for CodeQL-relevant changes and schedule/manual dispatch, and intentionally no-ops for ignored docs/ops-only paths. C# uses manual Release build. Keep schedule enabled. Repository CodeQL default setup must remain disabled while this advanced workflow owns uploads. |
| Dependency review | `Dependency Review` | `Dependency Review` | Yes for PRs | Reviews dependency changes; read-only token permissions. |
| CLA legal status | `Contributor License Agreement` (`.github/workflows/cla.yml`) | `Contributor License Agreement` | Ready after repository-side check-name verification | Metadata-only `pull_request_target` check that validates PR body CLA signatures from trusted base code only. |
| Release impact metadata | `Release Impact Check` (`.github/workflows/release-impact.yml`) | `Release Impact Check` | Ready after repository-side check-name verification | Metadata-only `pull_request_target` check that requires security, migration, configuration, OpenAPI, and operator-impact PR evidence for matching path changes. |
| Security integration | `Security Integration Tests` | `Security Integration Tests` | Ready after repository-side check-name verification | Always-present wrapper detects security-relevant paths, runs on schedule/manual dispatch, supports merge queue, and intentionally no-ops for unrelated changes. |
| Cerbos policy | `Cerbos Policy Validation` | `Cerbos Policy Validation` | Ready after repository-side check-name verification | Always-present wrapper detects authorization-policy/code paths, runs on schedule/manual dispatch, supports merge queue, and intentionally no-ops for unrelated changes. |
| Agent context | `agent-context` | `Validate AI-Context Contract` | Ready after repository-side check-name verification | Always-present wrapper detects AI context/docs/rule paths, supports merge queue/manual dispatch, and intentionally no-ops for unrelated changes. |

Do **not** require workflows that are skipped by `paths` filters unless the workflow contains an always-running no-op job. GitHub can leave skipped required checks pending, which blocks merges without useful feedback. `Build & Test`, `CodeQL Advanced`, `Security Integration Tests`, `Cerbos Policy Validation`, and `agent-context` now use workflow-internal change detection instead of trigger-level path skips, so their checks remain present while avoiding unnecessary work for unrelated paths.

If merge queue is enabled, ensure required workflows that gate merges include `merge_group`. `Build & Test`, `OpenAPI Contract Guard`, `CodeQL Advanced`, `Security Integration Tests`, `Cerbos Policy Validation`, and `agent-context` include merge-queue triggers; add the same event to any newly required workflow before marking it merge-queue-required.

### Repository Settings Evidence Checklist

Record evidence for these settings before treating the repository as enterprise-ready. Store screenshots, GitHub API output, or maintainer attestations with the release or security evidence package; workflow YAML alone is not sufficient.

| Control | Expected setting | Evidence required | Current evidence |
|---|---|---|---|
| Default branch protection / ruleset | `main` requires pull requests, current required checks, linear history or reviewed merge policy, and stale review dismissal when available. | Ruleset export, branch protection API output, or maintainer screenshot. | 2026-06-01 API evidence: branch protection endpoint returns 404; active `main` branch ruleset only includes deletion, non-fast-forward, and Copilot code-review rules. Missing PR/review/required-check controls. |
| Development branch protection / ruleset | `develop` requires pull requests and current required checks before merge. | Ruleset export, branch protection API output, or maintainer screenshot. | 2026-06-01 API evidence: branch protection endpoint returns 404 and no `develop` ruleset was returned. Missing expected controls. |
| Required check names | Check names match the table above and are stable before branch protection is updated. | Branch protection required-check export. | 2026-06-01 API evidence: no branch-protection status-check configuration returned. Required checks are not configured yet. |
| Merge queue | Enabled only after all required workflows have `merge_group` or always-present wrappers. | Ruleset export showing queue status and required checks. | 2026-06-01 API evidence: repository ruleset export showed no merge-queue rule. |
| Reserved version-tag glob | A branch ruleset includes `refs/heads/v*` with a `creation` rule so version tags keep sole ownership of the `v*` namespace. | Ruleset export showing the `refs/heads/v*` include and `creation` rule; `repository-settings-evidence` reports `hasReservedVersionTagGlobRule`. | Not yet configured. `.ci/scripts/validate-repository-settings.cs` now reports this control; the rule must be created in repository settings before the first governed release tag. |
| Environments | `staging` and `production` exist with environment-scoped secrets. Production requires reviewers and branch/tag restrictions. | Environment settings screenshot/API output with secret names redacted. | 2026-06-01 remediation: `staging` and `production` environments created. `production` requires reviewer `@amirakrari`; custom deployment branch policies allow `main` and `v*`. `staging` custom deployment branch policy allows `develop`. Environment secrets still need maintainer verification with values redacted. |
| Actions policy | Repository allows only GitHub-owned, verified, or SHA-pinned actions according to the organization policy. | Actions policy API output or maintainer screenshot. | 2026-06-01 API evidence: Actions are enabled and `allowed_actions` is `all`; policy is not restricted at the repository level. |
| Security features | Secret scanning, push protection, Dependabot security updates, dependency graph, and CodeQL alerts are enabled; CodeQL default setup is disabled while `CodeQL Advanced` owns uploads. | Security settings screenshot/API output. | 2026-06-07 API evidence: CodeQL default setup set to `not-configured` to unblock advanced workflow uploads. 2026-06-01 API evidence: secret scanning and push protection enabled; dependency graph/vulnerability alerts enabled; code-scanning API accessible with an open CodeQL alert; Dependabot security updates / automated security fixes enabled (`enabled: true`, `paused: false`). |
| CODEOWNERS owner resolution | Every team/user referenced by `.github/CODEOWNERS` exists and has write access. | GitHub CODEOWNERS validation or maintainer confirmation. | 2026-06-01 API evidence: `@islamu-ngo/platform-ops` team lookup returned 404, so `.github/CODEOWNERS` now uses `@amirakrari`; collaborator permission API reports `admin`. Replace with an org team after it exists. |

### Repository Settings Drift Check

`.github/workflows/repository-settings.yml` runs scheduled/manual repository-settings drift checks through `.ci/scripts/validate-repository-settings.cs`. The workflow reads GitHub repository metadata, branch protection, rulesets, environments, Actions policy, security features, code scanning access, and CODEOWNERS owner resolution, then retains redacted JSON/Markdown evidence in `repository-settings-evidence`.

The lane is expected to fail until the release-blocking settings above are configured. Do not suppress it or remove findings without either fixing the GitHub setting or recording an owner, date, compensating control, and removal condition in the release evidence package.

### Deployment Environments

Create GitHub Environments named exactly:

- `staging`
- `production`

Configure environment secrets and variables as described in [CONFIGURATION.md](CONFIGURATION.md#deployment-cicd-secrets). Production must require reviewer approval and should restrict deployments to `main` and version tags. Staging can deploy automatically from `develop` unless the release process requires review.

Workflow YAML references the environments, but reviewers, branch restrictions, wait timers, and environment-scoped secrets are GitHub settings.

`Cerbos Policy Validation` also owns production policy-store publishing. The validation job remains always-present for pull requests, merge queue, schedules, manual runs, `develop`, and `main`; the publish job starts only after that validation job succeeds on a `push` to `refs/heads/main` and only when Cerbos-relevant files were compiled. The publish job uses the `production` environment approval gate, read-only repository contents, the digest-pinned `ghcr.io/cerbos/cerbosctl:0.53.0` container, repository secrets `CERBOS_SERVER` / `CERBOS_USERNAME` / `CERBOS_PASSWORD`, and optional repository secret `CERBOS_CA_CERT_PEM`. Do not expose these Cerbos Admin API secrets to pull requests, merge queue, schedules, manual validation, `develop`, or non-production workflows without a separate threat model and environment contract.

### Security Features

Confirm these GitHub security features at repository or organization level:

- Secret scanning enabled.
- Push protection enabled where available.
- Dependabot security updates enabled.
- Dependency graph enabled.
- Code scanning alerts enabled for CodeQL results. When `CodeQL Advanced` is enabled, GitHub CodeQL default setup must be `not-configured`; default setup and advanced workflow SARIF uploads cannot both own analysis.

### Contributor Legal Governance

The repository uses a CLA-only contribution posture. Every non-bot contributor must sign the [ISLAMU Event Contributor License Agreement](../../legal/CLA.md), which grants the ISLAMU project steward broad inbound rights to maintain, provide, and relicense ISLAMU Event under alternative terms for enterprise internal-use on-premises compliance, nonprofit, humanitarian, public-sector, or procurement-restricted needs—subject to the Project Steward's binding covenant against closed-source proprietary SaaS commercialization (all SaaS providers must use AGPL-3.0).

The decision record in [CONTRIBUTION_GOVERNANCE.md](legal/CONTRIBUTION_GOVERNANCE.md) captures the legal posture, inbound copyright/patent scope, signature storage model, bot allowlist, archived CLA Assistant risk decision, and `pull_request_target` threat model.

`.github/workflows/cla.yml` is metadata-only. It uses `pull_request_target` and `issue_comment` events with `contributor-assistant/github-action`, pinned to a full commit SHA. It never checks out, builds, tests, caches, restores packages, or executes pull-request head code. It uses explicit `GITHUB_TOKEN` permissions for same-repository signature storage, pull-request comments, issue comments, and commit statuses: `contents: write`, `pull-requests: write`, `issues: write`, and `statuses: write`. A pre-flight `actions/github-script` step (also SHA-pinned) fetches stored signatures and PR commit authors to short-circuit the full action when all contributors are already signed or allowlisted, conserving runner minutes and API quota. Signature evidence for CLA v1.0 is stored in `signatures/v1.0/cla.json` on the dedicated `cla-signatures` branch (isolated from code branches) plus the GitHub PR/comment audit trail. The bot allowlist includes `dependabot[bot]`, `github-actions[bot]`, `renovate[bot]`, `codecov[bot]`, `*[bot]` (GitHub App bot suffix convention), and `web-flow` (GitHub Web UI commit identity). A `$GITHUB_STEP_SUMMARY` step provides CLA check observability in the GitHub Actions Summary tab.

### Release Impact PR Metadata Gate

Release-impacting pull requests must document operator-visible risk before merge. `.github/workflows/release-impact.yml` runs as a metadata-only `pull_request_target` check, checks out the trusted base commit only, and runs repository-owned `.ci/scripts/validate-release-impact-pr.cs` against the pull request body and changed-file metadata. It uses read-only `contents` and `pull-requests` permissions and must not checkout, build, test, cache, or execute pull-request head code.

The check requires the `## Release Impact` section in `.github/PULL_REQUEST_TEMPLATE.md` to match the changed files. Security/auth, migration/data/rollback, configuration/secrets/deployment, OpenAPI/client contract, and operator/self-hosting/release-note path changes must select the corresponding checkbox and provide non-empty `Details:`. `Not applicable` is only valid when the changed files do not imply one of those release-impact categories.

### GitHub Actions Supply-Chain Pins

The reusable API integration test step creates disposable setup and Keycloak
client-rotation credentials, masks them before running tests, and injects them
only into its process environment using the existing `.env.example` keys.
This test authority grants no access to operator or repository secrets.

External `uses:` references in `.github/workflows/*.yml` are pinned to full-length commit SHAs with a same-line version comment, for example `owner/action@<sha> # vX.Y.Z`. This makes the executable action reference immutable while preserving human-readable upgrade intent.

Local reusable workflows remain path-based (`./.github/workflows/...`) because they are controlled by this repository's review history. Dependabot's `github-actions` ecosystem in `.github/dependabot.yml` keeps external SHA pins maintainable through a weekly grouped update lane with conventional `ci` commit messages.

`Workflow Security` enforces this policy with `.ci/scripts/validate-action-pins.cs` and `.ci/scripts/validate-dependabot-policy.cs`, both run as file-based C# scripts with `dotnet run <script>.cs -- <args>`. The check always reports a status, scans workflow-security inputs when `.github/workflows/**`, `.ci/**`, `.github/dependabot.yml`, deployable Dockerfiles, or this governance document changes, and intentionally no-ops for unrelated changes. Do not add external GitHub Actions without a full SHA and a same-line version comment, and do not remove the `github-actions` Dependabot update lane without replacing it with an equivalent pinned-action maintenance process.

Deployable Dockerfiles must use tag-plus-digest base image references, for example `mcr.microsoft.com/dotnet/aspnet:10.0@sha256:<digest>`. The human-readable tag preserves maintainer intent while the digest fixes the resolved image. `Workflow Security` enforces this with `.ci/scripts/validate-dockerfile-base-images.cs` for `Explore.API/Dockerfile` and `Explore.Blazor/Dockerfile`. Dependabot's `docker` ecosystem entries update those digests weekly through grouped `docker-base-images` PRs.

Repository-owned helper scripts under `.ci/scripts/` must be file-based C# scripts (`*.cs`) unless a future change documents why C# is not viable. Keep shell blocks in workflows for orchestration only; policy, JSON parsing, and evidence-generation logic belongs in C# so it uses the repository's pinned .NET SDK and remains reviewable by the same maintainers as the application code. Each helper script declares `#:property RestorePackagesWithLockFile=false` so ad hoc `dotnet run <script>.cs -- <args>` execution does not create transient `.ci/scripts/packages.lock.json` files. Third-party tools can still use their required runtime, such as `zizmor` running from an isolated Python virtual environment.

### Multi-Forge CI/CD Policy

The repository should expose only two CI/CD directories by default:

- `.github/` for GitHub-required metadata, GitHub Actions discovery, issue templates, PR templates, CODEOWNERS, and Dependabot.
- `.ci/` for shared CI/CD implementation and mirror-provider CI/CD definitions.

Provider-specific root directories such as `.forgejo/`, `.woodpecker/`, or `.tangled/` are intentionally not used. Configure Codeberg or other mirrors to load CI/CD from `.ci/` when the provider supports a custom pipeline path. Do not add deploy secrets, registry publish credentials, Coolify webhooks, or environment promotion behavior to non-GitHub mirrors until this document defines equivalent secret isolation, artifact retention, immutable image evidence, smoke-check evidence, and rollback evidence for that provider. Release-adapter definitions are the only approved provider-specific CI/CD folders today, and they stay under `.ci/providers/`.

### Workflow Static Analysis Policy

`Workflow Security` treats workflow definitions as security-sensitive code. For workflow-governance changes it:

- sets up the pinned .NET SDK from `global.json`, then runs the local C# validators for action pins and Dependabot `github-actions` update coverage;
- runs `.ci/scripts/validate-workflow-cache-policy.cs` so privileged deploy, container, and release workflows cannot consume unreviewed GitHub Actions caches;
- runs `.ci/scripts/validate-deploy-workflow-contract.cs` so production and staging deploy callers continue to pass expected digest, promotion evidence, deployment-freeze, smoke-check, and immutable-tag inputs into `.ci/actions/deploy-coolify`;
- installs `actionlint` `1.7.12` from the upstream release archive after checking the expected SHA-256 digest, then blocks on workflow syntax, expression, and shell-in-workflow lint findings;
- installs `zizmor` `1.25.2` in an isolated Python virtual environment, runs it offline, exports SARIF/text evidence, and blocks on medium-or-higher severity findings;
- uploads `workflow-security-evidence` for 30 days.

If future `zizmor` findings must be temporarily accepted, document each exception with owner, date, rule ID, affected workflow, compensating control, and removal condition before weakening the workflow.

### Workflow Cache Poisoning Policy

Fork pull requests and untrusted contribution events must not write caches that are later consumed by trusted deployment, release, or publish workflows. `Workflow Security` enforces this with `.ci/scripts/validate-workflow-cache-policy.cs`.

Current policy:

- direct `actions/cache` usage is not approved in workflow YAML; use tool-specific caches only after a documented threat-model review;
- `actions/setup-dotnet` package caching is allowed in CI validation workflows, but not in deploy, container, or release workflows;
- Docker Buildx GitHub Actions cache writes (`cache-to: type=gha`) are approved only in the trusted reusable container build workflow (`.github/workflows/_container-build.yml`), where registry pushes, SBOM/provenance, scan, promotion, and attestation evidence are produced together;
- deployment workflows and CLA/legal metadata workflows must not restore or write caches from untrusted pull-request code.

Any future cache added to a privileged workflow needs an explicit owner, event model, cache-key strategy, and proof that fork PRs cannot poison release or deployment inputs.

### Local Secret-Scanning Feedback

`Secret Scanning` runs `gitleaks` `8.30.1` from the upstream release archive after verifying the Linux x64 archive SHA-256. Pull request, push, and merge-queue runs scan only the relevant commit range and fail when newly introduced secrets are detected. Scheduled and manual runs scan repository history as advisory evidence because the existing history currently contains legacy findings that need triage before this lane can become globally blocking.

The workflow redacts findings, retains SARIF/text output in `secret-scanning-evidence`, and does not replace GitHub secret scanning or push protection. Repository or organization secret scanning and push protection remain required settings in the evidence checklist above.

### NuGet Locked Restore Policy

GitHub Actions restore steps and deployable Docker build stages use `dotnet restore --locked-mode`. All tracked project files have committed `packages.lock.json` files, and `Directory.Build.props` enables `RestorePackagesWithLockFile` plus CI-only `RestoreLockedMode` for `GITHUB_ACTIONS`.

Package input changes must commit the matching lock-file changes in the same PR. Regenerate lock files with normal restore or `dotnet restore --force-evaluate`; never hand-edit `packages.lock.json`.

Dockerfiles must copy the root restore inputs (`global.json`, `Directory.Build.props`, `Directory.Packages.props`), project files, and relevant `packages.lock.json` files before running `dotnet restore --locked-mode` inside the build stage. This preserves Docker layer caching while keeping NuGet resolution deterministic.

### Setup Assistant Foundation CI Boundary

The `Build & Test` change detector treats the six Setup source trees (including
the nested `Event.SetupAssistant.SetupLive` project), all six focused test
trees, the exact cross-layer SetupLive owners, `eng/setup-assistant/**`, the
audited Terminal.Gui package inputs, every
nested Setup `packages.lock.json`, and all three generated ratchet JSON files as
Setup-relevant. Those paths route both `Event.Architecture.Tests` and the
reusable workflow's `Setup Project Gates`; root dependency, solution, CI-script,
or workflow changes continue to route every lane. The top-level check remains
always present and reports an intentional no-op for unrelated documentation or
dedicated-workflow changes.

`Event.Setup.Core` points inward to `Event.Wire.Contracts` and uses the approved
YamlDotNet graph. The shared Assistant points inward through Core and uses the
approved CommunityToolkit graph; the disabled Browser/Desktop shells point
through Assistant; the CLI points only to Core. The separate SetupLive outer
adapter points to Core and the generated Blazor client while keeping network and
ephemeral capability custody outside the shared presentation assembly. The Setup
lane restores in locked mode, builds all six focused test projects in Release,
then executes every project with a nonzero TUnit test-count safeguard. It also
executes the isolated 35-test `SetupLiveAuthoritySecurityTests` Tier 1 class and
retains its TRX report. The main Assistant suite currently executes 52 tests.
The Terminal suite independently guards canonical Core output, localization,
and the secret boundary. Live release capabilities and browser runtime remain
disabled.

All fourteen Setup lock files are tracked and discovered automatically by the
recursive NuGet license-policy scan. Official `Terminal.Gui`, `TextMateSharp`,
Avalonia, Sharprompt, and unapproved replacement GUI/TUI packages remain
blocked. `ISLAMU.Terminal.Gui` `2.4.17-islamu.1` is the separately approved,
MIT-preserving package built from the pinned official revision. CI verifies its
recorded patch, package identity, final closure, SBOM, notices, and absence of
the grammar/editor graph. The exact
YamlDotNet and CommunityToolkit graphs are pinned by architecture and lock-file
ratchets; Browser/Desktop activation still requires independent dependency,
I-VSD, CTO, user, and target approval.

`eng/setup-assistant/GenerateSetupAssistantRatchets.cs` owns the tracked browser
capability, SetupLive capability, and frozen-contract JSON ratchets. CI runs only its non-mutating
`--check` mode immediately after locked restore and fails on missing or stale
bytes; CI must never run `--write`. Source project files, C# source, tests,
locks, generator source, generated ratchets, and future browser source
`wwwroot` remain tracked. Only generated `bin/`, `obj/`, build-output
`wwwroot`, publish output, package staging, `releases/`, and `artifacts/` remain
ignored.

This lane inherits top-level `permissions: contents: read`, disables checkout
credential persistence, receives no PR secrets, and has no write, OIDC, signing,
publish, deployment, or release authority.

### NuGet Vulnerability Audit Policy

Fast CI runs `dotnet list Explore.sln package --vulnerable --include-transitive --format json --output-version 1 --no-restore` after locked restore, then parses the report with `.ci/scripts/validate-nuget-vulnerabilities.cs`. The parser writes retained JSON and markdown summary evidence under `artifacts/dependencies/`, splitting findings by direct/transitive package relationship and advisory severity. Any vulnerable direct or transitive package reported by NuGet fails the `Build & Test` lane; temporary advisory exceptions require an owner, date, advisory URL, affected package/version, package relationship, severity, compensating control, and removal condition recorded in this document before the workflow may be weakened.

The current policy is remediation-first. `MailKit` was upgraded from `4.15.1` to `4.16.0` to clear GitHub Advisory `GHSA-9j88-vvj5-vhgr` / `CVE-2026-41319` rather than making the audit advisory.

The one approved advisory suppression is deliberately exact and remains visible in retained CI evidence:

| Owner / review date | Package / relationship | Advisory / severity | Compensating control | Removal condition |
|---|---|---|---|---|
| ISLAMU maintainers / 2026-07-17 | `AutoMapper` `14.0.0`, direct and transitive | `GHSA-rvv3-g6hj-g44x` / High | `ApplicationServicesRegistration` applies `MaxDepth(64)` globally, bounding the advisory's uncontrolled-recursion path; production commercial builds use patched `16.1.1`. | Remove when AutoMapper is replaced, the default build moves to a patched version with approved licensing, or the advisory no longer applies. |

### Dependency License Policy

ISLAMU Event is licensed under AGPL-3.0-or-later, and the ISLAMU CLA grants the ISLAMU project steward broad inbound rights for contributor work. That inbound CLA does not override third-party dependency licenses, so CI must keep runtime, build, and test dependency license risk explicit before alternative-license, commercial, nonprofit, public-sector, procurement-restricted, hosted-service, or special social-impact distribution is offered.

`Build & Test` runs `.ci/scripts/validate-dependency-license-policy.cs` after locked restore and the NuGet vulnerability audit. The validator scans product `packages.lock.json` files, reads restored NuGet package metadata from the local package cache, rejects denied or unknown license metadata unless a package-specific exception is encoded in the policy script, and guards future product npm or container OS package dependency surfaces until dedicated license scanning exists for those ecosystems.

Reviewed license identifiers currently allowed by policy are `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `CC0-1.0`, `ISC`, `MIT`, `MPL-2.0`, `PostgreSQL`, `Unicode-DFS-2016`, `Unlicense`, and `Zlib`. Strong reciprocal, copyleft, source-available, and business-source families such as AGPL, GPL, LGPL, BUSL, Commons Clause, RPL, and SSPL are denied unless an explicit temporary exception is recorded in `.ci/scripts/validate-dependency-license-policy.cs`.

Current visible exceptions are intentional debt, not blanket approvals:

| Package | Risk | Removal condition |
|---|---|---|
| `AutoMapper` | Runtime dependency with RPL-1.5 metadata. | Replace, remove, or obtain legal approval before alternative-license distribution. |
| `MediatR` | Runtime dependency with RPL-1.5 metadata. | Replace, remove, or obtain legal approval before alternative-license distribution. |
| `SonarAnalyzer.CSharp` | Source-available analyzer license. | Keep analyzer/build-only or replace before treating as shipped runtime dependency. |
| `Microsoft.VisualStudio.Azure.Containers.Tools.Targets` | Microsoft EULA build tooling. | Keep build-only or replace before treating as shipped runtime dependency. |
| `NetArchTest.Rules` | Missing NuGet license metadata. | Replace or document package metadata before removing the exception. |

Product `package-lock.json` files outside excluded tooling directories fail until an npm license scanner is added. Deployable Dockerfiles fail if they introduce OS package-manager installs such as `apt-get install` or `apk add` before a container OS package license scanning path is documented.

### Standalone And Optional Service License Boundary

The minimum operational distribution is one `Event.Standalone` application
image containing the ISLAMU Event API and Blazor BFF/UI, with SQLite
persistence. It runs application, Data Protection, and embedded
privacy-erasure migrations in-process. External database servers,
`Event.MigrationService`, Redis or Valkey, Keycloak, Cerbos, MinIO/S3, SMTP or
Mailpit, Svix, Weblate, Formbricks, Coop, Osprey, AI providers, federation
services, and observability backends are optional deployment capabilities, not
requirements of that standalone topology.

This operational boundary does not erase the licenses of libraries and other
third-party materials contained in the standalone image. The ISLAMU CLA and
any alternative outgoing license cover only material ISLAMU owns or is
separately authorized to license. Every third-party library, base image,
container image, service, dataset, font, and asset remains governed by its
respective license, public-domain status, or other applicable terms.
Repository-owned integration code and deployment manifests do not relicense
the software they reference.

Release evidence must distinguish two delivery modes:

1. **Operator-pulled optional service:** ISLAMU provides configuration or a
   manifest, and the operator obtains the third-party artifact from its
   upstream distributor. Documentation must identify the separate licensing
   boundary and must not present the artifact as ISLAMU-licensed material.
2. **ISLAMU-conveyed optional service:** ISLAMU mirrors, bundles, preloads, or
   delivers the third-party binary or image, including in an air-gapped
   package. Before release, retain its exact version and digest, upstream
   license evidence, image/package SBOM, required notices and attributions,
   corresponding-source or source-offer evidence where applicable,
   modification provenance, and any commercial entitlement.

The current dependency-license validator proves only its documented NuGet and
repository guard scope; it is not a complete license audit of referenced
Compose images or their transitive operating-system packages. A floating tag
such as `latest` or an unqualified major tag is never sufficient commercial or
offline redistribution evidence. Until a container-license inventory covers a
third-party image, ISLAMU may reference it as an optional operator-pulled
integration but must not represent a bundled copy as cleared for alternative-
license distribution.

### Coverage Publication Policy

Coverage collection is artifact-only. `Coverage Evidence` runs on schedule/manual dispatch and currently collects Cobertura coverage for the stable `Event.Domain.UnitTests` lane, retaining the coverage file, TRX, HTML report, build log, and test log as `coverage-evidence`.

Do not add Codecov, SonarCloud, or coverage-percentage badges until the corresponding workflow publishes verified coverage data for the intended scope and has a documented owner for triage. Badge changes must land in the same PR as the verified workflow that backs the badge.

### Runtime Test Reliability Policy

Runtime, stress, and manual visual lanes remain advisory until their known flaky or deferred tests are tracked with owner, first-seen date, evidence source, and promotion/removal criteria in [TEST_RELIABILITY.md](TEST_RELIABILITY.md). This keeps nightly/manual failures actionable instead of silently normalizing noisy failures.

Do not promote stress, security, or runtime lanes to required status while a blocking reliability item lacks an owner or removal condition. When a tracked item is fixed, remove the skip in code and update `TEST_RELIABILITY.md` in the same PR. API-contract-specific skips remain governed by [API_CONTRACT_TEST_DEBT.md](API_CONTRACT_TEST_DEBT.md).

### OpenAPI Breaking-Change Evidence

`OpenAPI Contract Guard` blocks stale generated contract artifacts and verifies deterministic second-run regeneration for `schemas/openapi_islamu-event.json`, `docs/API_CONTRACT_INVENTORY.md`, and `Explore.Blazor.Client/Clients/EventApiClient.g.cs`.

The guard also runs `.ci/scripts/validate-api-contract-skip-inventory.cs` against [API_CONTRACT_TEST_DEBT.md](API_CONTRACT_TEST_DEBT.md). Any skipped integration test whose code skip reason includes `Category: API contract` must be listed in that inventory with a source file, owner, and removal condition. This keeps deferred route-name/HATEOAS contract enforcement visible while the owning `api-contract-stabilization` work finishes.

Intentional breaking API changes must also update [API_CHANGELOG.md](API_CHANGELOG.md#breaking-change-evidence) in the same pull request. The changelog entry must identify the affected route, operation, schema, or generated client method; explain old and new behavior; identify affected clients or operator workflows; provide migration guidance or a compatibility window; name the release or target milestone; and link retained `openapi-contract-guard` evidence when available.

`OpenAPI Contract Guard` also emits `oasdiff` markdown and JSON breaking-change reports against the base branch when OpenAPI-relevant paths change. For PR, push, and merge-queue runs, detected breaking changes fail the workflow unless `docs/API_CHANGELOG.md` changed in the same diff. Weekly scheduled and manual runs still execute the full guard as evidence-only runs so maintainers get recurring contract drift and breaking-change evidence even when no PR touched OpenAPI paths.

OpenAPI linting uses the project-owned `.ci/spectral.yaml` ruleset, not the full built-in Spectral OpenAPI ruleset. The current low-noise advisory rules cover invariants already expected by generated-client and inventory tooling: API title, API version, operation IDs, operation tags, and response descriptions. `OpenAPI Contract Guard` runs checksum-independent version-pinned `@stoplight/spectral-cli@6.16.0` through `npx`, retains JSON and Markdown reports in `openapi-contract-guard`, and keeps findings advisory until the ruleset has stable signal over multiple PRs.

This is now a missing-evidence gate for breaking OpenAPI changes, not full automated release approval. `oasdiff` findings with a same-diff changelog update remain reviewer evidence that must be checked against the changelog and release notes. Blocking Spectral remains future work until its rules are documented, low-noise, and ready for promotion.

## Required vs Advisory Gates

| Gate | Required | Advisory / scheduled | Promotion rule |
|---|---:|---:|---|
| Release build + fast tests | Yes | No | Required for all code PRs. |
| Coverage evidence | No | Yes | Artifact-only scheduled/manual Cobertura evidence for stable unit coverage. Keep non-blocking until scope, thresholds, and publication owner are documented. |
| Infrastructure unit tests | Yes | No | Included in fast CI with `[Category!=Runtime]` so Docker-backed provider tests do not become implicit required checks. |
| Infrastructure email runtime tests | Conditional | Integration callers | `Explore.Infrastructure.Tests` `Email` category runs in the integration job as Mailpit/Testcontainers evidence; promote beyond conditional only after reliability data is tracked. |
| PostgreSQL-backed integration tests | Conditional | Deploy callers | Required for integration/deploy callers; add a schedule only after reliability and runtime cost are acceptable. |
| OpenAPI generated-artifact drift | Yes | No | Required after PR2 baseline. |
| Skipped API contract test inventory | Yes | No | `OpenAPI Contract Guard` fails when a `Category: API contract` skip is missing from `docs/API_CONTRACT_TEST_DEBT.md` or lacks owner/removal evidence. |
| OpenAPI breaking-change changelog evidence | Yes | Scheduled/manual evidence | `OpenAPI Contract Guard` fails PR/push/merge-queue runs when `oasdiff` detects breaking changes without a same-diff `docs/API_CHANGELOG.md` update. Breaking changes with changelog evidence remain reviewer/release evidence. |
| Release-impact PR metadata | Yes | No | `Release Impact Check` blocks PRs whose security, migration, configuration, OpenAPI, or operator-impact paths lack matching PR-template evidence. |
| `oasdiff` breaking-change report | Changelog gate | Yes | PR/push/merge-queue runs fail on detected breaking changes unless `docs/API_CHANGELOG.md` changes in the same diff. Scheduled/manual reports remain evidence-only. |
| Spectral/OpenAPI lint | No | Yes | `OpenAPI Contract Guard` retains advisory JSON/Markdown reports from the low-noise `.ci/spectral.yaml` ruleset; keep non-blocking until rules have stable signal. |
| Security/Cerbos path workflows | Conditional | Yes | Always-present wrappers intentionally no-op for unrelated changes; nightly schedule covers drift outside path-relevant PRs. |
| OpenSSF Scorecard | No | Yes | Scheduled/manual supply-chain posture evidence. Uploads SARIF to code scanning and retains `scorecard-evidence`; keep advisory until repository permissions and signal quality are proven. |
| Local secret scanning | New findings only | Yes | `gitleaks` blocks on PR/push/merge-queue ranges for newly introduced leaks and keeps scheduled/manual history scans advisory until legacy findings are triaged or baselined. |
| Dependency license policy | Yes | No | `Build & Test` blocks denied or unknown product dependency licenses unless a package-specific exception with removal condition is encoded in the repository-owned C# validator. |
| Container SBOM/provenance/Trivy/attestation/promotion verification | Deploy-only | No | Required before deployment workflows call Coolify; retained evidence includes registry manifest/index output, immutable primary-registry tag promotion evidence, vulnerability scan artifacts, attestation verification JSON, and checksum manifests. |
| Production smoke checks | Deploy-only | No | Required for production deploys; `PRODUCTION_API_URL` and `PRODUCTION_UI_URL` must be configured and both `/alive` and `/health` must pass for deployed components. Staging smoke checks run when staging URL variables are configured. |

The reusable container build must verify each pushed GHCR digest with `gh attestation verify` before any dependent Coolify deploy job can start. Verification must constrain the expected repository, reusable signer workflow, source ref, source digest, SLSA provenance predicate, and GitHub-hosted runner trust boundary; do not rely on workflow-controlled predicate fields as the sole trust source.

GitHub artifact attestations are the chosen SLSA-compatible provenance evidence path for this repository. The container build already produces Buildx SBOM/provenance attestations and then verifies the pushed GHCR digest with `gh attestation verify` against the SLSA provenance predicate, repository, signer workflow, source ref, source digest, and GitHub-hosted runner trust boundary. Do not add a second provenance verifier unless it verifies the same digest without weakening the current trust constraints and produces retained evidence with an owner/date/removal condition.

The reusable container build must also record immutable primary-registry promotion evidence before deploy jobs can start. Public Coolify v4.x source evidence shows Docker Image applications support SHA-256 hash input: `DockerImageParser` parses `image@sha256:<digest>`, the Docker Image UI labels the field as `Docker Image Tag or Hash`, and `ApplicationDeploymentJob` normalizes `sha256-*` values into `image@sha256:<digest>` for deployment. ISLAMU's live Coolify resources have not yet been proven to consume those digests, so the next required evidence is Coolify application configuration, API output, deployment logs, or retained deploy summaries proving the running resource consumed either `image@sha256:<digest>` or the verified full-commit immutable tag. Until that live proof exists, the temporary fallback is an immutable `sha-*` production tag or `dev-*` staging tag whose primary-registry reference is inspected and verified to resolve to the built digest. Mutable tags such as `latest` and `develop` remain convenience aliases only and must not be treated as release evidence.

ATCR currently uses a scoped environment secret (`ATCR_PASSWORD`) with the fixed registry user configured in the deploy workflows. Public ATCR docs describe ATProto OAuth with DPoP, a Docker credential helper/device authorization flow, short-lived registry JWTs behind that helper, and fallback `docker login` with an ATProto app password; they do not document a GitHub Actions OIDC federation flow for non-interactive image pushes. Treat `ATCR_PASSWORD` as a deployment credential: scope it only to `staging` and `production`, rotate it at least every 90 days and after every suspected exposure or maintainer access change, and verify it only grants the package push/pull permissions required for `atcr.io/amirakrari.bsky.social/islamu-event-*`. If ATCR later documents GitHub OIDC or another non-interactive short-lived token exchange for CI pushes, replace the static secret with the short-lived path in the same PR that updates this section and the workflows.

Coolify deploy workflows use the local composite action `.ci/actions/deploy-coolify` for webhook triggering, smoke checks, redacted failure summaries, and retained deployment evidence. Before invoking Coolify, deploy jobs download the retained `container-build-*` evidence, resolve the component's full-commit immutable tag and digest with `.ci/scripts/resolve-deploy-image-evidence.cs`, and pass that digest into the deploy action. The production workflow publishes and records full-commit `sha-${{ github.sha }}` immutable tags; staging publishes and records full-commit `dev-${{ github.sha }}` immutable tags. Production calls set `require-smoke-check: "true"`, so missing production smoke URLs block the webhook call and both `/alive` and `/health` must return `200` for the deployed component. Staging keeps smoke URLs optional but uses the same `/alive` and `/health` checks when configured. This centralizes deploy behavior while keeping environment-scoped secrets and approvals on the caller jobs. Coolify-side proof that the platform consumed that exact digest or full-commit tag remains required before final release readiness.

`Workflow Security` validates this deploy-caller contract with `.ci/scripts/validate-deploy-workflow-contract.cs`. If a deploy workflow stops downloading retained container build evidence, resolving component digest evidence, passing freeze/override inputs, requiring production smoke checks, or calling the shared local action for both API and UI, workflow security fails before the deploy workflow can merge.

Deployment freeze control is an operator-owned GitHub Environment/Repository variable named `DEPLOYMENT_FREEZE`. When it is set to `true`, `.ci/actions/deploy-coolify` refuses to call the Coolify webhook unless a manual `workflow_dispatch` run supplies `override_reason`. The override reason is written to the retained deployment summary so urgent security releases are auditable without weakening environment approvals.

## Fork Pull Request Policy

External fork pull requests are untrusted.

- Do not use `pull_request_target` for build, test, generation, container, or deployment workflows unless a separate threat-model review approves the exact pattern.
- Fork PRs must not receive deployment secrets, registry write credentials, environment secrets, OIDC tokens, or privileged `GITHUB_TOKEN` scopes.
- Validation jobs should use `permissions: contents: read` unless a specific GitHub API write is required and safe for the event.
- Deployment workflows must remain push/manual/environment-gated only.

## Generated Artifact Review Rules

Generated files are reviewed product artifacts, not disposable output.

| Artifact | Generated by | Review focus |
|---|---|---|
| `schemas/openapi_islamu-event.json` | `OpenAPI Contract Guard` / `Explore.API` build-time OpenAPI generation | Routes, verbs, response types, auth metadata, endpoint classification, schema shape. |
| `schemas/configuration-manifest-v1alpha2.schema.json` | `ISLAMU.ConfigurationManifest.SchemaGenerator` | Immutable Draft 2020-12 identity, explicit instance and tenant-section catalog membership, closed objects, deterministic bytes, and runtime-validator parity. |
| `Explore.Blazor.Client/Clients/EventApiClient.g.cs` | NSwag target in `Explore.Blazor.Client.csproj` | Method names, renamed/removed operations, optional API-version parameters, generated client ergonomics. |
| Container digest JSON | `_container-build.yml` via `.ci/scripts/write-container-digest-evidence.cs` | Image name, digest, commit SHA, tags, workflow run, scan evidence. |
| Docker base image pins | `Workflow Security` via `.ci/scripts/validate-dockerfile-base-images.cs` | Deployable Dockerfiles keep explicit tag-plus-digest base references and Dependabot Docker update coverage. |
| Container OCI inspect/index evidence | `_container-build.yml` via `docker buildx imagetools inspect` | Downloadable registry evidence for the digest that carries Buildx SBOM/provenance attestations. |
| Container immutable promotion evidence | `_container-build.yml` via `.ci/scripts/write-image-promotion-evidence.cs` and `docker buildx imagetools inspect` | Primary-registry `sha-*` / `dev-*` tag references and proof that each resolves to the built digest. |
| Container Trivy SARIF | `_container-build.yml` via `aquasecurity/trivy-action` | Critical/high vulnerability evidence in a machine-readable retained artifact. |
| Container attestation verification JSON | `_container-build.yml` via `gh attestation verify` | Verification evidence for the pushed GHCR digest, constrained to the repository, reusable signer workflow, source ref/digest, SLSA provenance predicate, and GitHub-hosted runner trust boundary. |
| Container evidence checksum manifest | `_container-build.yml` via `.ci/scripts/write-artifact-checksums.cs` | SHA-256 integrity manifest for retained digest, OCI, Trivy, and related container evidence artifacts. |
| Deployment summaries | `.ci/actions/deploy-coolify` via Coolify deploy jobs | Environment, component, commit SHA, expected immutable image tag, expected image digest, promotion evidence path, webhook result, smoke-check result, whether smoke was required, deployment-freeze state, override reason, workflow run, rollback note. |

Never hand-edit OpenAPI, ConfigurationManifest schema, or NSwag generated client artifacts.
Regenerate OpenAPI/NSwag through the workflow-compatible commands in
[TROUBLESHOOTING.md](TROUBLESHOOTING.md#openapi--nswag-drift). Regenerate or
check the ConfigurationManifest schema with the commands in
[CONFIGURATION.md](CONFIGURATION.md#configuration-manifest-contract).
`Build & Test` routes changes under `eng/configuration-manifest-schema/**` and the exact
checked schema artifact to `Event.Architecture.Tests`, where byte equality is a hard
gate.

## Artifact Retention Policy

| Evidence | Workflow(s) | Retention |
|---|---|---:|
| Fast/integration TRX, build/analyzer warning logs, and NuGet vulnerability summaries | `Build & Test (Reusable)` | 14 days |
| OpenAPI drift, `oasdiff`, and advisory Spectral artifacts | `OpenAPI Contract Guard` | 30 days |
| Workflow security evidence | `Workflow Security` | 30 days |
| OpenSSF Scorecard SARIF | `OpenSSF Scorecard` | 30 days |
| Secret-scanning SARIF/text evidence | `Secret Scanning` | 30 days |
| Repository settings drift evidence | `Repository Settings Drift` | 30 days |
| Security and Cerbos logs/TRX | `Security Integration Tests`, `Cerbos Policy Validation` | 30 days |
| Coverage Cobertura/TRX/log evidence | `Coverage Evidence` | 30 days |
| Performance smoke logs/results | `Performance Smoke` | 30 days |
| Container digest, OCI inspect/index output, immutable promotion evidence, Trivy text/SARIF output, attestation verification JSON, checksum manifest, SBOM/provenance evidence | `Container Build (Reusable)` | 90 days; preserve release evidence externally for release lifetime |
| Deployment summaries/logs | Coolify deploy workflows | 90 days minimum |

Release notes must copy or link long-lived evidence that GitHub artifact retention will eventually delete.

For manual releases, generate the durable release evidence manifest after downloading retained CI/CD artifacts:

```bash
dotnet run .ci/scripts/generate-release-evidence-bundle.cs -- artifacts release-evidence
```

The output path must be fresh and absent before invocation. Bundle generation stages
all four outputs beside that path and publishes them with one directory rename; it
fails closed instead of replacing or merging an existing bundle.

The generated JSON, markdown summary, release-notes evidence section, and SHA-256 checksum manifest are the bridge between expiring GitHub Actions artifacts and the manually authored GitHub Release. Attach them to the release or copy them to durable release storage. Paste `release-evidence-release-notes.md` into the GitHub Release body so the release keeps durable evidence pointers even after workflow artifacts expire.

## Artifact Triage Guide

Maintainers should be able to triage CI/CD failures from GitHub Actions evidence before reproducing locally. Use this guide when a workflow fails or when preparing release evidence.

Use [CI_CD_RUNBOOKS.md](CI_CD_RUNBOOKS.md) for the approved rerun and emergency override paths. Do not bypass required gates just because evidence is hard to interpret; missing or unclear evidence is a CI/CD defect.

| Evidence | First triage question | Expected maintainer action |
|---|---|---|
| Fast/integration TRX, build/analyzer warning logs, and NuGet vulnerability summaries | Which project/test failed, and did the failure occur before or after build? Are new analyzer/compiler warnings visible in the retained build log? Did the NuGet audit classify any direct/transitive advisory by severity? | Open the TRX/build/dependency artifact first, then the job summary. Assign test failures to the owning project area, warning regressions to the project that introduced the new warning output, and dependency advisories by direct/transitive relationship plus severity. |
| OpenAPI drift, skipped contract inventory, `oasdiff`, and advisory Spectral artifacts | Did `schemas/openapi_islamu-event.json`, API inventory, or generated NSwag client change intentionally? Are skipped API contract tests still listed in `docs/API_CONTRACT_TEST_DEBT.md` with owner/removal criteria? Did `oasdiff` detect breaking changes, and did the same diff update `docs/API_CHANGELOG.md`? Did low-noise Spectral report API metadata or operation-shape drift? | If intentional, require API changelog/release evidence; if accidental, regenerate through the documented OpenAPI workflow commands. Treat `oasdiff` findings with changelog evidence and all Spectral findings as reviewer evidence until stricter rules are promoted. |
| Workflow security evidence | Did action pins, Dependabot policy, cache policy, actionlint, or zizmor fail? | Treat as a CI/CD security defect. Fix workflow YAML or document a time-bounded exception with owner/date/removal condition. |
| OpenSSF Scorecard SARIF | Is the finding actionable for this repository, or informational posture drift? | Keep advisory unless a repeated high-signal finding is accepted into the required baseline. Record false positives before promotion. |
| Secret-scanning SARIF/text | Is this a newly introduced secret or a legacy history finding? | Newly introduced findings block and require secret rotation/removal. Legacy scheduled/manual findings need triage or baseline before history-wide blocking. |
| Security and Cerbos logs/TRX | Did the security test fail in code, fixture setup, Keycloak/Cerbos startup, or policy contract expectations? | Use TRX for failing test identity and retained logs for container/service context. Assign to API/security or policy owner. |
| Coverage evidence | Did the stable unit coverage lane generate Cobertura, TRX, HTML, build, and test evidence? | Keep coverage advisory and artifact-only. Expand scope or add thresholds only after the target lane is stable and the publication owner is documented. |
| Performance smoke evidence | Did a representative API endpoint benchmark fail to build/run, or did runtime behavior change enough to invalidate the benchmark? | Review `performance-smoke-evidence` logs and BenchmarkDotNet results. Keep the lane advisory until enough scheduled runs prove stable signal and explicit thresholds are documented. |
| Container evidence | Do digest, scan, SBOM/provenance, attestation, promotion, and checksum artifacts agree for the same image digest? | Treat mismatches as release blockers. Preserve evidence externally for release lifetime before GitHub artifact expiry. |
| Deployment summaries/logs | Did the webhook, expected digest/tag resolution, smoke checks, freeze override, or rollback evidence fail? | Do not promote production until the failed component has a retained summary with redacted failure context and a rollback/override note. |

If an artifact is missing, fix the workflow evidence path before weakening the gate. Missing evidence is a CI/CD bug, not a reason to bypass review.

## Badge Policy

README badges must represent implemented gates. Do not show Codecov or SonarCloud badges until workflows upload coverage or publish SonarCloud analysis. Re-add those badges only in the same PR that introduces and verifies the corresponding workflow.
