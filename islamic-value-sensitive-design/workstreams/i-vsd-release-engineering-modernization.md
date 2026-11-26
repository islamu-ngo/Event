# I-VSD Planning Report: Release Engineering Modernization, Deep `git-cliff` Integration, Automated Contract Diffing & Real Artifact Provenance

Last Updated: 2026-11-24

## Review Metadata
- Mode: planning
- Subject: Release engineering de-bloat, zero-ceremony commit governance, deep `git-cliff` integration (`--context` -> C# I-VSD & contract-diff `extra` enrichment -> `--from-context` Tera rendering), automated contract diffing, and SLSA/Sigstore/SHA256 artifact release security
- Workstream: release-engineering-modernization
- Report kind: planning-workstream-review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-24
- Reviewed input: release-engineering-modernization triad (`dev/active/release-engineering-modernization/`, Revision 6 — 2-PR Option 1 consolidation preserving all Revision 5.1 Senior CTO & independent-audit invariants) against `develop` (`ee149713436c0a501591bbb67f90a03ec105ffe8`)
- Supersedes: Revision 4 of `i-vsd-release-engineering-modernization.md` (extends and revalidates constitutional principles from `islamic-value-sensitive-design/governance/i-vsd-release-governance.md`)

## Executive Summary

ISLAMU Event's release engineering subsystem (`ISLAMU.ReleaseEngineering`, `.ci/scripts`, `.github/workflows/release-*.yml`, and `.githooks`) currently imposes 45,631 lines of C# code, 678 pre-release `CHG-*.yaml` fragments, 532 lines of inline YAML Bash, an 8,473-line custom Linux `/proc/<pid>/maps` and socket-inode surveillance harness, a self-inflicted Commit-`B` 4-manifest circular hash ceremony, and a `PresentationConfigGrammar` censor inside `GitCliffRenderer.cs` that crippled `git-cliff` down to a 3-line template loop—forcing C# to re-implement commit parsing, SemVer bumping, grouping, and changelog formatting while shipping zero public releases. At the same time, it provides zero automated contract diffing (OpenAPI 3.1.1 / HAL `_links`, Configuration Manifest + Tenant Configuration Package + `.env.example`, or the discovered EF Core migration inventories), renders a single flat bullet list instead of persona-specific upgrade guides, and has container base-image drift where `Explore.API/Dockerfile` and `Explore.Blazor/Dockerfile` reference `.NET 11` images while `global.json` (`10.0.404`, `rollForward: disable`), `Event.Standalone/Dockerfile`, and `Event.MigrationService/Dockerfile` are on `.NET 10`. Current `develop` contains 11 model snapshots across 5 persistence assemblies; the earlier 15-tree/8-project claim was stale. The 18 broken fragments and 11 failing tests are historical PR79 evidence, not a newly reproduced result at the reviewed revision.

This report evaluates the provider-responsibility and Islamic Value Sensitive Design (I-VSD) implications of modernizing the release engineering subsystem under **Option A (Deep `git-cliff` Integration)** across two multi-phase pull-request scopes (`PR-RELEASE-CLI-AND-CI-MODERNIZATION` covering Phases 1–5 and `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE` covering Phases 6–7).

**Practical recommendation:** Proceed with the two-PR Revision 6 modernization plan (preserving every Revision 5.1 Senior CTO and independent-audit invariant). Retiring internal security theater (custom `/proc` process surveillance, synthetic Commit-`B` hash loops, 3-branch GitBook SSH transactions, and custom promotion PKI) while **unlocking `git-cliff`'s native offline/no-exec `--context` -> C# enrichment (`extra`) -> `--from-context` Tera rendering pipeline**, adding **automated C# contract diffing**, and shipping **industry-standard artifact security (`SHA256SUMS`, SLSA v1.0 Build Provenance via `actions/attest`, Sigstore Cosign OIDC bundle signing, CycloneDX/SPDX SBOMs, and signed Git tags)** directly advances **Sidq (truthfulness)**, **Amanah (trustworthy stewardship)**, and **Maslahah (operational clarity for self-hosters and integrators)** while enforcing the three non-negotiable ethical boundaries from `i-vsd-release-governance.md`:
1. **Offline and cryptographically verifiable release authenticity** for self-hosters (`git verify-tag`, `sha256sum --check SHA256SUMS`, `gh attestation verify`, `cosign verify-blob` / `cosign verify-attestation`), plus SHA-256 verification of the pinned `git-cliff` binary against `eng/release/toolchain.lock.json` under strict `--offline --no-exec` execution.
2. **Human-owned security embargo disclosure timing** so automation never leaks restricted vulnerability details prematurely or decides disclosure timing without an accountable human owner (`GitCliffContextEnricher` validates all referenced `CHG-*.yaml` fragments before rendering and fails closed on `Public-Disclosure: embargoed` or `Restricted-Details`).
3. **Contributor identity privacy & Model 2 Hybrid Split recognition** (`Huquq al-'Ibad`): Authoritative signed artifacts (`release-context.v1.json`, `release-rendering-context.v1.json`, `contract-diff.v1.json`, `release-images.v1.json`, `release-notes.md`, `docs/public/changelog/README.md`, `SHA256SUMS`) remain 100% offline (`--offline --no-exec`) and identity-free. `GitCliffContextEnricher` constructs a new allowlisted native context instead of relying on a denylist, so authors, footers, repository paths, remote identities, and unknown future fields cannot leak; `ReleaseArtifactPolicy` supplies a second post-render guard. Warm open-source contributor recognition (`@handles`, PR `#123` links, and *first-time contributor* shout-outs, with zero raw emails) is appended exclusively to the **mutable GitHub Release page body** in `.github/workflows/release.yml` via `eng/release/cliff-github-contributors.toml` (`github.contributors`, `--no-exec`), never hashed into `SHA256SUMS`, so any contributor can request edits or removal at any time without breaking cryptographic verification—directly fulfilling the deferred optional forge-enrichment boundary in `i-vsd-release-governance.md`.

Status `current` and disposition `plan-aligned` mean the planned architecture, scenarios, and tasks satisfy the moral and provider-responsibility constraints mapped below; they are not proof of completed implementation or live production release activation.

## Scope

This review covers the provider-controlled decisions in the `release-engineering-modernization` programme (Revision 6):
1. Retiring the 8,473-line user-space `/proc` surveillance harness (`LoadProcessOwner.cs`, `SecurityExecutionEvidence*.cs`), custom "Trusted Bundle Promotion" PKI (`TrustedReleasePolicy.cs`, `PromotedReleaseCommand.cs`, `TrustActivationCommand.cs`), synthetic Commit-`B` 4-manifest circular hash puzzle (`CandidateCommand.cs`, `TagCommand.cs`, `MainCommand.cs`, `BaselineCommand.cs`, `BaselineEvidencePolicy.cs`, `MaintenanceLineCommand.cs`), 3-branch GitBook SSH publication state machine (`SignedPublicationInventoryAuthority.cs`, `AuthorizedInventoryPolicy.cs`, `PublicationInventoryCommand.cs`), and the `PresentationConfigGrammar` censor (with `GitCliffRenderer` deleted completely before PR 1 opens). PR 1 (`PR-RELEASE-CLI-AND-CI-MODERNIZATION`, Phases 1–5) retains the verified toolchain and annotated-tag primitives, repairs `SecurityControlRegister`, `select-security-changes`, and `ReleaseImpactPolicy` with required CI coverage, adds `IGitCliffExecutor`, retires fragment/trailer ceremony, adds conservative contract diffing, and completes allowlisted three-persona rendering.
2. Retiring the 678 pre-release greenfield `CHG-*.yaml` fragments, 17 rename records, and `ChangeIdRenamePolicy.cs`; auto-omitting non-breaking internal commits without `Changelog: skip` trailer ceremony across both `CommitPolicy` and `eng/release/cliff.toml` (`protect_breaking_commits = true`, `require_conventional = true`, `fail_on_unmatched_commit = true`); deduplicating `.githooks/commit-msg`; and enforcing full `ReleaseInputPolicy.Validate` on optional high-impact feature/operator fragments without banning the English word `"restricted"`.
3. Building deterministic C# contract differs (`OpenApiContractDiffer`, `ConfigurationManifestDiffer`, and `MigrationLedgerExtractor`) over discovered base/head inventories and `GitCliffContextEnricher` to reconstruct allowlisted native context, enforce embargo gates, inject structured contract diffs into `release.extra` / `commit.extra`, and render 3-persona release views through `git-cliff --from-context`. Unknown structural changes are `review_required`; migration filenames and coverage are not represented as semantic provider parity.
4. Replacing theatrical `printf`-only release workflows and inline YAML Bash with a real tag-driven release pipeline (`.github/workflows/release.yml`) producing multi-arch `.NET 10` containers (`islamu-event-standalone`, `islamu-event-api`, `islamu-event-ui`, `islamu-event-migration-service`), canonical `SHA256SUMS`, SLSA v1.0 provenance attestations, and Sigstore Cosign signatures for the checksum bundle and all eight ATCR/GHCR `repository@digest` references. Existing-release verification occurs before any build; images are staged by digest only, without registry tags, before their references enter release notes; exact assets are verified in a draft GitHub Release before publish-once. Interrupted attempts may leave disclosed digest-only objects or an unpublished draft, not a false claim of atomic cross-provider publication. No byte-for-byte reproducible-build claim is made without a future two-build equality check.

## Claim Boundary

This document is a provider-responsibility and software-design review under the I-VSD framework. It is not a fatwa, Sharia-compliance certificate, legal opinion, or cryptographic audit certification. Decisions concerning whether a specific security vulnerability disclosure timeline is morally obligatory or permissible in a live incident remain human operator responsibilities subject to qualified Sunni scholarly and legal consultation where applicable.

## Findings

### IVSD-F001: Unvalidated Commit Fragments, Broken Substring Filters, and Manual Prose Drift Corrupt Public Release Readiness (Sidq / Truthfulness & Amanah / Trust)
- **Lifecycle:** `open`
- **Severity:** High | **Claim type:** Design, accuracy & verification gap
- **Principle & Domain:** Sidq (Truthfulness) and Amanah (Custody of the public record) — Governance & Release Operations
- **Affected Stakeholders:** Self-hosting operators, third-party API/client integrators, contributors, and release operators
- **Provider-Controlled Decision:**
  1. `ChangeWorkflowCommand` (`preflight-commit`, `preflight-staged`, `preflight-range`) checks only that `Change-Id` matches the filename without calling `ReleaseInputPolicy.Validate`.
  2. `ChangeWorkflowCommand.Fragment` emits dead `docs/...` paths (instead of `docs/internal/...`), and `ReleaseInputPolicy.ContainsRestrictedMarker` bans any YAML text containing the English substring `"restricted"`.
  3. Release notes rely on manual YAML prose and a narrow 3-line `cliff.toml` loop instead of diffing the repository's authoritative machine-readable contracts (`schemas/openapi_islamu-event.json`, `schemas/configuration-manifest-v1alpha2.schema.json`, `schemas/tenant-configuration-package-v1alpha2.schema.json`, `.env.example`, and discovered EF migration inventories) and injecting conservative structured evidence into `git-cliff`'s `extra` context for persona-specific Tera rendering.
- **Practical Impact:** 18 of the 678 committed `CHG-*.yaml` files on `feat/pr79-testing-assurance-follow-up` fail `ReleaseInputPolicyTests.RepositoryChangeFragmentsPassReleaseInputPolicy`, hundreds reference non-existent `docs/*` paths, and self-hosters or API integrators receive no automated, exact ledger of changed `.env` variables, database migrations by provider, or OpenAPI/HAL contract deltas.
- **Evidence Reviewed:** `eng/release/src/ISLAMU.ReleaseEngineering/ChangeWorkflowCommand.cs` (lines 464–505, 749–767), `ReleaseInputPolicy.cs` (lines 354–363, 645–659), `GitCliffRenderer.cs` (lines 19–111), `eng/release/cliff.toml`, and historical PR79 execution of `ReleaseInputPolicyTests.RepositoryChangeFragmentsPassReleaseInputPolicy` (exit code 2, 21 diagnostics across 18 files).
- **Validation Level:** Current repository code inspection plus historical PR79 test execution; no fresh test result is claimed at `ee149713436c0a501591bbb67f90a03ec105ffe8`.
- **Linked Mitigation:** `IVSD-M001`
- **Owner / Next Validation:** `PR-RELEASE-CLI-AND-CI-MODERNIZATION` (Phases 3–5) acceptance gate.

### IVSD-F002: Internal Security Theater Displaces Real Artifact Release Security and Breaks Self-Hoster Container Parity (Amanah & Maslahah)
- **Lifecycle:** `open`
- **Severity:** High | **Claim type:** Security architecture & operational portability gap
- **Principle & Domain:** Amanah (Trustworthy supply-chain stewardship) and Maslahah (Protecting self-hosters with real, verifiable artifact security) — Security & Release Architecture
- **Affected Stakeholders:** Self-hosting community operators (mosques, nonprofits, independent deployers) and maintainers
- **Provider-Controlled Decision:**
  1. Embedding 8,473 lines of `unsafe` Linux subreaper and `/proc/<pid>/maps` process surveillance (`LoadProcessOwner.cs`, `SecurityExecutionEvidence*.cs`), a custom "Trusted Bundle Promotion" PKI (`TrustedReleasePolicy.cs`), a 4-manifest Commit-`B` circular hash puzzle, and a 3-branch GitBook SSH state machine inside `ISLAMU.ReleaseEngineering.csproj` (with `<RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>`).
  2. Leaving `.github/workflows/release-adapter-final.yml` as a `printf` stub and allowing container Dockerfile drift (`Explore.API/Dockerfile` and `Explore.Blazor/Dockerfile` referencing `.NET 11` base images while `global.json`, `Event.Standalone/Dockerfile`, and `Event.MigrationService/Dockerfile` are pinned to `.NET 10` `sdk:10.0.404`).
- **Practical Impact:** Over 19,500 lines of code defend against fictional internal threats while self-hosters lack a unified `.NET 10` multi-container release pipeline (`Event.Standalone`, `Explore.API`, `Explore.Blazor`, `Event.MigrationService`) and a standard Arch/SLSA/Sigstore release bundle (`SHA256SUMS` + detached OIDC signature + SLSA v1.0 provenance attestation).
- **Evidence Reviewed:** `ISLAMU.ReleaseEngineering.csproj`, `LoadProcessOwner.cs`, `SecurityExecutionEvidence*.cs`, `TrustedReleasePolicy.cs`, `CandidateCommand.cs`, `TagCommand.cs`, `.github/workflows/release-adapter-final.yml`, `.github/workflows/_container-build.yml`, `src/Event.Standalone/Dockerfile`, `src/Event.MigrationService/Dockerfile`, `src/Explore.API/Dockerfile`, `src/Explore.Blazor/Dockerfile`.
- **Validation Level:** Verified by repository code inspection.
- **Linked Mitigation:** `IVSD-M002`
- **Owner / Next Validation:** `PR-RELEASE-CLI-AND-CI-MODERNIZATION` and `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE` acceptance gates.

### IVSD-F003: Misleading CI Gates and Omitted Release Tests Create False Assurance (Sidq / Truthfulness)
- **Lifecycle:** `open`
- **Severity:** High | **Claim type:** Verification & operational truthfulness gap
- **Principle & Domain:** Sidq (Truthfulness — never claiming a check verifies something it does not) — Evaluation & CI/CD Governance
- **Affected Stakeholders:** Maintainers, contributors, and operators relying on CI status checks
- **Provider-Controlled Decision:**
  1. `.github/workflows/release-adapter-preview.yml` runs on every PR and reports a green check (`release-adapter-preview`) by executing a single `printf` statement.
  2. `SecurityChangeSelection.RequiresExecution` purports to detect "Security-Relevant Changes" but returns `true` for every path except `README.md` and `docs/public/*.md`.
  3. `SecurityControlRegister.cs` hardcodes `verificationStatus == "pending"` and `finalDisposition == null`, rejecting any control that transitions to verified.
  4. `ISLAMU.ReleaseEngineering.Tests` is excluded from `.github/workflows/_build-test.yml`, hiding 11 broken tests on the active branch.
- **Practical Impact:** Green checkmarks on PRs give false confidence while actual release-engineering tests are broken and unexecuted in main CI, and non-security changes waste hours of runner capacity.
- **Evidence Reviewed:** `.github/workflows/release-adapter-preview.yml`, `.github/workflows/security-tests.yml`, `SecurityChangeSelection.cs` (lines 21–39), `SecurityControlRegister.cs` (lines 374–381), `.github/workflows/_build-test.yml`, and historical PR79 execution of `ISLAMU.ReleaseEngineering.Tests` (994 total, 11 failed).
- **Validation Level:** Current repository code inspection plus historical PR79 test execution; no fresh test result is claimed at `ee149713436c0a501591bbb67f90a03ec105ffe8`.
- **Linked Mitigation:** `IVSD-M003`
- **Owner / Next Validation:** `PR-RELEASE-CLI-AND-CI-MODERNIZATION` and `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE` acceptance gates.

### IVSD-F004: Preservation of Embargo Disclosure Authority, Contributor Identity Privacy, Offline Execution, and Verifiable Provenance (Amanah, La Darar, & Huquq al-'Ibad)
- **Lifecycle:** `accepted`
- **Severity:** High | **Claim type:** Constitutional invariant preservation
- **Principle & Domain:** La Darar (Non-harm in security disclosure), Huquq al-'Ibad (Contributor privacy rights in immutable records), and Sidq (Truthfulness of public projections) — Security, Privacy & Governance
- **Affected Stakeholders:** Attendees and operators of self-hosted instances, contributors who may later require privacy/distance from immutable release artifacts, and public changelog readers
- **Provider-Controlled Decision:** Unlocking deep `git-cliff` integration (`--context` -> C# enrichment -> `--from-context`) with **Model 2 (Hybrid Split) contributor crediting** upholds every guarantee of `i-vsd-release-governance.md`: (1) authoritative artifact generation always executes with `--offline --no-exec` (`[remote] offline = true`) using a SHA-256 verified binary; (2) `GitCliffContextEnricher` constructs a new allowlisted `release-rendering-context.v1.json` array and correlates its version/range/OIDs with the existing identity-free `release-context.v1.json` authority, while `ReleaseArtifactPolicy` validates rendered notes fail-closed; (3) mutable forge contributor acknowledgements (`@handles`, PR links, *first-time contributor* badges, zero raw emails) are generated strictly for the **mutable GitHub Release page body** via a separate online `--no-exec` config after `SHA256SUMS` is sealed, with guarded missing-remote handling and identity-free fallback; (4) embargoed security disclosures remain fail-closed; and (5) existing-release state is checked before build, images are staged by digest only without registry tags before their references enter notes, then all eight ATCR/GHCR repository@digest references and the file checksum bundle are signed and verified before a draft GitHub Release is published. Raw native JSON, footers, repository paths, remote identities, and unknown fields are never persisted or logged.
- **Practical Impact:** The planned controls are designed to prevent remote telemetry, contributor identity leakage, and shell execution in authoritative rendering while preserving substantive stakeholder protections. Source-level tests and the authorized live rehearsal must prove those outcomes; `plan-aligned` is not evidence that leakage or compromise is impossible.
- **Evidence Reviewed:** `islamic-value-sensitive-design/governance/i-vsd-release-governance.md`, `ReleaseArtifactPolicy.cs`, `eng/release/toolchain.lock.json`, `git-cliff` documentation (`website/docs/usage/load-context.md`, `website/docs/configuration/remote.md`), `.github/workflows/_container-build.yml`.
- **Validation Level:** Verified by repository code and official `git-cliff` documentation inspection.
- **Linked Mitigation:** `IVSD-M004`
- **Owner / Next Validation:** Enforced across both PR scopes (`PR-RELEASE-CLI-AND-CI-MODERNIZATION` and `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE`), with live OIDC/registry verification deferred to an authorized release-candidate rehearsal.

## Recommendations

- **`IVSD-M001` (for `IVSD-F001`)**:
  - Retire the 678 pre-release greenfield `CHG-*.yaml`, 17 `change-id-renames/*.yaml` files, and `ChangeIdRenamePolicy.cs` before `v0.1.0`.
  - Auto-omit non-breaking internal commit types (`test`, `refactor`, `style`, `build`, `ci`, `chore`, `revert`) and engineering scopes across both `CommitPolicy` and `eng/release/cliff.toml` (`protect_breaking_commits = true`), while validating optional high-impact feature/operator `CHG-*.yaml` fragments via `ReleaseInputPolicy.Validate` at commit/range preflight using structural YAML keys (`Restricted-Details`, `Public-Disclosure: embargoed`) rather than the English substring `"restricted"`.
  - Build automated C# contract differs over discovered migration and contract inventories, plus `GitCliffContextEnricher`, to inject structured deltas into `release.extra` / `commit.extra` and render distinct persona views via `git-cliff --from-context`. Unknown structural changes default to `review_required`; asymmetric migrations require review rather than an automatic parity claim.
- **`IVSD-M002` (for `IVSD-F002`)**:
  - Delete the 8,473-line `/proc` surveillance harness (`LoadProcessOwner.cs`, `SecurityExecutionEvidence*.cs`), custom promotion PKI (`TrustedReleasePolicy.cs`, `PromotedReleaseCommand.cs`, `TrustActivationCommand.cs`), synthetic Commit-`B` 4-manifest hash puzzle (`CandidateCommand.cs`, `TagCommand.cs`, `MainCommand.cs`, `BaselineCommand.cs`, `BaselineEvidencePolicy.cs`, `MaintenanceLineCommand.cs`), 3-branch GitBook SSH state machine (`SignedPublicationInventoryAuthority.cs`, `AuthorizedInventoryPolicy.cs`, `PublicationInventoryCommand.cs`), and `PresentationConfigGrammar` censor.
  - Disable `AllowUnsafeBlocks`, re-enable Roslyn analyzers on `ISLAMU.ReleaseEngineering`, align `Explore.API/Dockerfile` and `Explore.Blazor/Dockerfile` to the repository's `.NET 10` pinned digests (`sdk:10.0.404` / `aspnet:10.0.15` matching `global.json`, `Event.Standalone/Dockerfile`, and `Event.MigrationService/Dockerfile`, with future `.NET 11` adoption tracked in `dev/backlog/dotnet-11-adoption-and-container-parity.md`), and build a tag-triggered `.github/workflows/release.yml` pipeline producing multi-arch images (`islamu-event-standalone`, `islamu-event-api`, `islamu-event-ui`, `islamu-event-migration-service`), canonical `SHA256SUMS`, SLSA v1.0 provenance attestations (`actions/attest`), and Sigstore Cosign signatures for the checksum bundle and all eight ATCR/GHCR repository@digest references. Verify existing-release state before build, stage images without registry tags before rendering their references, sign every registry-specific reference, publish only a verified draft, and do not claim reproducible artifacts without a two-build equality check.
- **`IVSD-M003` (for `IVSD-F003`)**:
  - Replace hardcoded snapshot counts and the `verificationStatus == "pending"` lock-in in `SecurityControlRegister` with structural schema validation supporting lifecycle progression (`pending` -> `verified`/`mitigated`/`accepted`).
  - Align `select-security-changes` with actual security/runtime path rules, invoke TUnit / BenchmarkDotNet directly in `security-tests.yml` and `performance-smoke.yml`, and evaluate `ReleaseImpactPolicy` in-process.
  - Delete theatrical `printf`-only adapter workflows (`release-adapter-preview.yml`, `release-adapter-final.yml`, `release-publish.yml`) and add `ISLAMU.ReleaseEngineering.Tests` to `_build-test.yml` and `workflow-security.yml`. Pure unit execution targets < 3s; the real-binary phase gate has a measured < 15s budget excluding one-time provisioning.
- **`IVSD-M004` (for `IVSD-F004`)**:
  - Enforce `--offline --no-exec` (`[remote] offline = true`) and SHA-256 verification on all authoritative invocations. Reconstruct native context through an explicit allowlist, preserve the C# authority artifact separately, and reject raw identity, footer, repository-path, remote, and unknown-field leakage. Verify rendered output and embargo policy fail-closed. Check sealed-release state before build; stage images by digest only without registry tags before rendering their references; sign all eight ATCR/GHCR repository@digest references and `SHA256SUMS`; publish verified draft assets without clobbering. Disclose residual digest-only objects/drafts after interruption and never claim atomic publication across GitHub and two registries. Isolate mutable GitHub Release contributor acknowledgements into `eng/release/cliff-github-contributors.toml` (`--no-exec`) after sealing, with identity-free fallback and contributor edit/removal on request. Document that checksum verification is offline while Sigstore/GitHub provenance verification normally requires network trust materials.

### Rejected Alternatives
1. **Moving the 8,473-line `/proc` and subreaper surveillance harness (`LoadProcessOwner.cs` + `SecurityExecutionEvidence*.cs`) into a separate `ISLAMU.AssuranceRunner` project instead of deleting it** — Rejected: inspecting `/proc/<pid>/maps` and socket inodes from inside a user-space C# test wrapper is fragile security theater adding zero real security over standard `dotnet test` and GitHub ephemeral runner isolation.
2. **Keeping the synthetic Commit-`B` 4-manifest circular SHA ceremony (`release-candidate.v1.json` -> Commit `B` -> tag -> `release-evidence.v1.json`)** — Rejected: committing build outputs back into Git to prove a Git tag exists solves a circular problem of its own creation.
3. **Eliminating `git-cliff` and re-implementing commit parsing, SemVer bumping, grouping, and Markdown templating in custom C# (`StringBuilder`)** — Rejected by explicit user architecture choice (Option A) after reviewing `git-cliff`'s official documentation: `git-cliff` (MIT/Apache-2.0) already excels at Conventional Commit parsing (`protect_breaking_commits = true`), SemVer bumping (`[bump]`), and Tera templating (`--from-context` + `extra` + `format = true`); previous friction came solely from `PresentationConfigGrammar` and `TrustedBundlePolicy` censoring `git-cliff`.
4. **Adding `Cocogitto` (`cog`) alongside `git-cliff` as a required CI dependency** — Rejected: `cog.toml` cannot express `(type, scopeKind)` visibility or validate staged `CHG-*.yaml` embargo schemas, and `cog bump` mutates Git history with synthetic version-bump commits.
5. **Scrubbing a growing denylist from raw `git-cliff` context or treating migration filenames as semantic provider parity** — Rejected: new context fields could bypass a denylist, and migration IDs cannot prove equivalent operations or safe application. Revision 5.1 uses allowlisted reconstruction, conservative `review_required` evidence, and existing provider tests as the semantic authority.

## Stakeholders

| Stakeholder | Impact of This Modernization | Protection Preserved |
|---|---|---|
| Self-hosting operators (mosques, nonprofits, community orgs) | Receive a dedicated **Self-Hoster & Operator Upgrade Guide** per release rendered from conservative contract evidence (`.env`/Config Manifest/Tenant Package changes, discovered migration inventories with review-required asymmetry, and verified `.NET 10` image digests) plus `SHA256SUMS`, SLSA v1.0 provenance, and Cosign verification commands. | Offline checksum/tag verification, documented online provenance verification, offline `--no-exec` rendering, and human-owned embargo windows (`IVSD-M001`, `IVSD-M002`, `IVSD-M004`). |
| Third-party developers & API/Client integrators | Receive an automated **Developer & API Integrator Changelog** generated by diffing `schemas/openapi_islamu-event.json`, `x-endpoint-class`, HAL `_links` capabilities, and wire contracts across pinned OIDs and rendering them via `git-cliff`'s `extra.openapi_diff`. Unknown structural changes require review instead of being called compatible. | Deterministic contract evidence with explicit semantic limits (`IVSD-M001`). |
| Contributors & AI agents | Eliminates daily commit/planning friction: no `Changelog: skip` trailers on internal commits, no Planning-vs-Execution `CHG-*` Catch-22, no false rejection on the English word `"restricted"`, and no 6-hour security CI run on non-security edits. | Contributor identity privacy in authoritative release artifacts remains strictly enforced in `GitCliffContextEnricher` + `ReleaseArtifactPolicy` (`IVSD-M001`, `IVSD-M004`). |
| Release & Platform operators | Truthful CI checks (pure unit target < 3s and real-binary target < 15s; theatrical workflows retired; source-bound tag-driven `release.yml`; declarative Tera templates). Sealed assets are never clobbered, registry consumers receive immutable digest references rather than mutable image tags, and live release claims require an authorized rehearsal. | Explicit human tag-signing, SHA-256 `git-cliff` pinning, direct OCI signatures, and embargo disclosure approval boundaries remain mandatory (`IVSD-M002`, `IVSD-M003`, `IVSD-M004`). |

## I-VSD Principles And Domains

| Principle | Domain | Operationalization in Plan |
|---|---|---|
| **Sidq (Truthfulness)** | CI/CD, Contract Diffing & Release Notes | Retire theatrical checks and `/proc` surveillance; auto-diff OpenAPI, configuration, and discovered migration inventories while marking unknowns `review_required`; run release unit and real-binary tests in CI without semantic parity or reproducibility overclaims. |
| **Amanah (Trust & Stewardship)** | Supply-Chain Provenance & Tool Integrity | Pin `git-cliff` 2.14.2 binaries by fresh SHA-256 archive/executable hashes under `--offline --no-exec` (2.14.x is required for `format = true` and `remote.pr_author`); bind annotated signed tags to source commits; publish canonical `SHA256SUMS`, SBOMs and SLSA provenance; and Cosign-sign both the checksum bundle and all eight ATCR/GHCR `.NET 10` repository@digest references without assigning registry tags. |
| **Maslahah (Stakeholder Benefit)** | Persona-Personalized Upgrade Experience | Render distinct sections for Community Admins, Self-Hosters/Operators, and API/Client Integrators via Tera over the allowlisted `release-rendering-context.v1.json`, correlated with the C# authority context. |
| **La Darar (Non-Harm)** | Security Embargo & Developer Ergonomics | Keep embargoed details fail-closed (`Public-Disclosure: embargoed` / `Restricted-Details` key) while removing false-positive substring bans and unnecessary 6-hour CI runs on non-security edits. |
| **Huquq al-'Ibad (Rights of People)** | Contributor Privacy & Fair Recognition (Model 2 Hybrid Split) | Keep all authoritative signed context, diff, image-manifest, notes, changelog, and checksum artifacts offline and identity-free through allowlisted projection. Append warm `@handle` + PR `#123` + first-time acknowledgements (zero raw emails) only to the mutable GitHub Release body so recognition remains editable/removable on request. |

## Validation Gaps

- Production SSH signer principals (`eng/release/trust/allowed-signers`) remain prospective until the Project Steward provisions the official release signing key prior to tagging `v0.1.0`.
- Live GHCR / ATCR digest-only push, absence of registry tags, GitHub OIDC SLSA attestation, eight-reference Cosign signing, pre-build sealed-release guard, interrupted-draft recovery, and non-clobber publication require an authorized fault-injecting release-candidate rehearsal. Local/source gates can validate deterministic differs, allowlisted enrichment, Tera rendering, checksum generation, tag/manifest logic, and workflow policies, but cannot prove those hosted outcomes. Neither the Revision 5.1 CTO review nor the Revision 6 consolidation reran the historical PR79 test failures or any .NET suite.

## Escalation Needed

- **Project Steward approval** before enrolling production SSH signer keys in `eng/release/trust/allowed-signers` or creating any signed release-candidate or `v0.1.0` tag.
- **Qualified legal/scholarly review** only if future release governance introduces contributor identity attribution or modifies vulnerability embargo policy (no such change is made in this workstream).

## Evidence Reviewed

- `eng/release/src/ISLAMU.ReleaseEngineering/*.cs`, `ISLAMU.ReleaseEngineering.csproj`, `eng/release/cliff.toml`, `eng/release/toolchain.lock.json`
- `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/*.cs`; historical PR79 live test results (994 tests, 11 failures, 3m 41s) retained as prior evidence, not a fresh result at `ee149713436c0a501591bbb67f90a03ec105ffe8`
- Official `git-cliff` documentation (`website/docs/templating/context.md`, `website/docs/usage/load-context.md`, `website/docs/configuration/git.md`, `website/docs/configuration/bump.md`, `website/docs/configuration/changelog.md`, `website/docs/configuration/remote.md`, `website/docs/integration/github.md`, and the 2.14.0/2.14.2 release notes) — MIT/Apache-2.0 external CLI tool (zero source code copied); 2.14.0 introduced `format = true` and `remote.pr_author`, so the target lock is 2.14.2 rather than 2.13.1
- Official Git `verify-tag`/SSH allowed-signers documentation, GitHub artifact-attestation guidance, Sigstore Cosign blob/OCI signing and verification guidance, and Microsoft EF Core migration/pending-model-change documentation
- `.ci/scripts/*.cs`, `.github/workflows/_container-build.yml`, `.github/workflows/workflow-security.yml`, `.github/workflows/release-*.yml`, `.github/workflows/security-tests.yml`, `.github/workflows/performance-smoke.yml`, `.github/workflows/_build-test.yml`
- `src/Event.Standalone/Dockerfile`, `src/Event.MigrationService/Dockerfile`, `src/Explore.API/Dockerfile`, `src/Explore.Blazor/Dockerfile`, `global.json`
- `schemas/openapi_islamu-event.json`, `schemas/configuration-manifest-v1alpha2.schema.json`, `schemas/tenant-configuration-package-v1alpha2.schema.json`, `.env.example`, and the current 11 EF Core `*ModelSnapshot.cs` files across 5 persistence assemblies at `ee149713436c0a501591bbb67f90a03ec105ffe8`
- `islamic-value-sensitive-design/governance/i-vsd-release-governance.md`

## Missing Evidence

- Authorized signed release-candidate evidence for live Git-tag verification, all eight ATCR/GHCR repository@digest signatures, applicable attestations, checksum/blob verification, no-tag staging, pre-build sealed-release guard, interrupted-draft/non-clobber recovery, and contributor API fallback (prospective until the rehearsal).

## Context Inventory

- Workstream triad: `dev/active/release-engineering-modernization/`
- Constitutional governance anchor: `islamic-value-sensitive-design/governance/i-vsd-release-governance.md`
- Plane active tracking: `event/work/active-release-engineering-modernization.yaml` (under Epic `EVENT-6861`)

## Planning Handoff
- Workstream: release-engineering-modernization
- Status: current
- Reviewed input: `dev/active/release-engineering-modernization/release-engineering-modernization-plan.md` (Revision 6 — 2-PR Option 1 consolidation preserving all Revision 5.1 Senior CTO & audit invariants, grounded at `ee149713436c0a501591bbb67f90a03ec105ffe8`)
- Findings and mitigations:
  - `IVSD-F001 -> IVSD-M001`
  - `IVSD-F002 -> IVSD-M002`
  - `IVSD-F003 -> IVSD-M003`
  - `IVSD-F004 -> IVSD-M004`
- Required plan mappings:
  - `IVSD-F001 / IVSD-M001` -> `PR-RELEASE-CLI-AND-CI-MODERNIZATION` (Scenarios 2.A/2.B/3.A/3.B; Phases 3–5)
  - `IVSD-F002 / IVSD-M002` -> `PR-RELEASE-CLI-AND-CI-MODERNIZATION` (Scenario 1.A; Phase 1) and `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE` (Scenario 4.A; Phases 6–7)
  - `IVSD-F003 / IVSD-M003` -> `PR-RELEASE-CLI-AND-CI-MODERNIZATION` (Scenario 1.B; Phase 2) and `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE` (non-clobber evidence and truthful live limits; Phases 6–7)
  - `IVSD-F004 / IVSD-M004` -> Enforced across both scopes: verified offline tools, embargo-safe optional fragments, conservative evidence/redaction, and allowlisted native projection (`PR-RELEASE-CLI-AND-CI-MODERNIZATION`, Phases 1–5), plus signed identity-free artifacts with mutable-only credit (`PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE`, Phases 6–7)
- Escalations required before: release (Project Steward approval for production SSH signers and any release-candidate or `v0.1.0` tag)
- Refresh triggers: any change that enables remote API fetches inside authoritative generation, drops `--no-exec`, replaces allowlisted context projection with denylist scrubbing, persists raw native context, introduces contributor identifiers into any signed context/diff/image-manifest/notes/checksum artifact or raw emails anywhere, weakens human-authorized embargo gating, renders image claims before build, leaves any advertised ATCR/GHCR repository@digest reference unsigned, permits sealed-asset replacement, or removes documented tag/checksum/provenance verification.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-11-22 | none | draft | Initial integrated planning intake for `release-engineering-modernization` | Repository audit at `e01f18a4e1b671faf8bc7687a02def1c6a97c35a` |
| 2026-11-22 | draft | current | Revalidated against completed 4-PR plan, context, and tasks triad (`plan-aligned`) | `dev/active/release-engineering-modernization/` (Revision 1) |
| 2026-11-22 | current | current | Upgraded to Revision 2 incorporating Automated Contract Diffing, Persona-Segmented Release Views, retirement of `/proc` and Commit-`B` theater, and Arch/SLSA/Sigstore artifact security | `dev/active/release-engineering-modernization/` (Revision 2) |
| 2026-11-23 | current | current | Upgraded to Revision 3 aligning with Option A (**Deep `git-cliff` Integration**) and **Model 2 (Hybrid Split) Contributor Crediting** (`eng/release/cliff-github-contributors.toml` appended exclusively to the mutable GitHub Release body, leaving authoritative signed `release-notes.md` and `SHA256SUMS` 100% offline & identity-free) | `dev/active/release-engineering-modernization/` (Revision 4.1) |
| 2026-11-23 | current | stale | Senior CTO Revision 5 materially changed PR/task mappings, native-context privacy projection, and release-artifact sequencing; planning-mode revalidation is required before renewed CTO alignment | `dev/active/release-engineering-modernization/` (Revision 5 review) |
| 2026-11-24 | stale | current | Revalidated every IVSD-F001..F004/M001..M004 mapping against Revision 5; corrected migration evidence and the 2.14.2 feature/version baseline, adopted allowlisted context projection, required direct OCI signatures and build-before-render ordering, and retained live rehearsal limits | `dev/active/release-engineering-modernization/` (Revision 5), report Revision 4 (`plan-aligned`) |
| 2026-11-24 | current | current | Incorporated the independent execution audit as Revision 5.1: hook-valid Phase 1 metadata, complete compile/documentation ownership, pre-build sealed-release guard, digest-only/no-tag staging, eight ATCR/GHCR signatures, and verified-draft recovery | `dev/active/release-engineering-modernization/` (Revision 5.1), report Revision 4 (`plan-aligned`) |
| 2026-11-24 | current | current | Revalidated all `IVSD-F001..F004`/`M001..M004` mappings after user-directed Option 1 consolidation from 5 PRs / 10 phases to 2 multi-phase PRs / 7 phases (10 atomic commits); zero provider-responsibility, privacy, signing, or operational invariants changed | `dev/active/release-engineering-modernization/` (Revision 6), report Revision 5 (`plan-aligned`) |
