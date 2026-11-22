# I-VSD Planning Report: Release Engineering Modernization, Automated Contract Diffing & Real Artifact Provenance

Last Updated: 2026-11-22

## Review Metadata
- Mode: planning
- Subject: Release engineering de-bloat, zero-ceremony commit governance, automated contract diffing, persona-segmented native changelog rendering, and SLSA/Sigstore/SHA256 artifact release security
- Workstream: release-engineering-modernization
- Report kind: planning-workstream-review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-22
- Reviewed input: release-engineering-modernization triad (`dev/active/release-engineering-modernization/`, Revision 2) against source revision `e01f18a4e1b671faf8bc7687a02def1c6a97c35a` (`feat/pr79-testing-assurance-follow-up`)
- Supersedes: Revision 1 of `i-vsd-release-engineering-modernization.md` (extends and revalidates constitutional principles from `islamic-value-sensitive-design/governance/i-vsd-release-governance.md`)

## Executive Summary

ISLAMU Event's release engineering subsystem (`ISLAMU.ReleaseEngineering`, `.ci/scripts`, `.github/workflows/release-*.yml`, and `.githooks`) currently imposes 45,631 lines of C# code, 678 pre-release `CHG-*.yaml` fragments, 532 lines of inline YAML Bash, an 8,473-line custom Linux `/proc/<pid>/maps` and socket-inode surveillance harness, a self-inflicted Commit-`B` 4-manifest circular hash ceremony, and an external Rust binary wrapper (`git-cliff`) on a pre-release greenfield codebase that has shipped zero public releases. At the same time, it provides zero automated contract diffing (OpenAPI/HAL, Configuration Manifest, or EF Core migrations), renders a single flat bullet list instead of persona-specific upgrade guides, has 18 broken `CHG-*.yaml` fragments and 11 failing tests on `feat/pr79-testing-assurance-follow-up`, and leaves `Event.Standalone/Dockerfile` and `Event.MigrationService/Dockerfile` pinned to .NET 10 while the repository is on .NET 11.

This report evaluates the provider-responsibility and Islamic Value Sensitive Design (I-VSD) implications of modernizing the release engineering subsystem across four pull-request scopes (`PR-RETIRE-THEATER-AND-FIX-CI-GATES`, `PR-FRICTIONLESS-COMMIT-AND-FRAGMENT-GOVERNANCE`, `PR-AUTOMATED-CONTRACT-DIFF-AND-PERSONA-RENDERER`, `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE`).

**Practical recommendation:** Proceed with the four-PR Revision 2 modernization plan. Retiring internal security theater (custom `/proc` process surveillance, synthetic Commit-`B` hash loops, and custom promotion PKI) and replacing it with **automated contract diffing**, **persona-segmented upgrade views**, and **industry-standard artifact security (`SHA256SUMS`, SLSA v1.0 Build Provenance via `actions/attest`, Sigstore Cosign OIDC bundle signing, CycloneDX/SPDX SBOMs, and signed Git tags)** directly advances **Sidq (truthfulness)**, **Amanah (trustworthy stewardship)**, and **Maslahah (operational clarity for self-hosters and integrators)** while preserving the three non-negotiable ethical boundaries from `i-vsd-release-governance.md`:
1. **Offline and cryptographically verifiable release authenticity** for self-hosters (`git verify-tag`, `sha256sum --check SHA256SUMS`, `gh attestation verify`, `cosign verify-blob` / `cosign verify-attestation`).
2. **Human-owned security embargo disclosure timing** so automation never leaks restricted vulnerability details prematurely or decides disclosure timing without an accountable human owner.
3. **Contributor identity privacy** in authoritative release artifacts (`release-context.v1.json`, `release-notes.md`, `docs/public/changelog/README.md`), stripping author/committer emails, forge handles, and raw commit bodies by construction.

Status `current` and disposition `plan-aligned` mean the planned architecture, scenarios, and tasks satisfy the moral and provider-responsibility constraints mapped below; they are not proof of completed implementation or live production release activation.

## Scope

This review covers the provider-controlled decisions in the `release-engineering-modernization` programme:
1. Retiring the 8,473-line user-space `/proc` surveillance harness (`LoadProcessOwner.cs`, `SecurityExecutionEvidence*.cs`), custom "Trusted Bundle Promotion" PKI (`TrustedReleasePolicy.cs`, `PromotedReleaseCommand.cs`), synthetic Commit-`B` 4-manifest circular hash puzzle (`CandidateCommand.cs`, `TagCommand.cs`, `MainCommand.cs`, `BaselineCommand.cs`), and 3-branch GitBook SSH transaction state machine (`SignedPublicationInventoryAuthority.cs`), while fixing `SecurityControlRegister` lifecycle validation and `select-security-changes` path routing.
2. Retiring the 678 pre-release greenfield `CHG-*.yaml` fragments and 17 rename records, auto-omitting non-breaking internal commits without `Changelog: skip` trailer ceremony, deduplicating `.githooks/commit-msg`, and enforcing structural `ReleaseInputPolicy` validation on optional high-impact feature/operator fragments without banning the English word `"restricted"`.
3. Building deterministic native C# contract differs (`OpenApiContractDiffer`, `ConfigurationManifestDiffer`, `MigrationLedgerExtractor`) and a persona-segmented C# release renderer (`PersonaReleaseNotesRenderer`) tailored to Community Admins, Self-Hosters/Operators, and API/Client Integrators.
4. Replacing theatrical `printf`-only release workflows and inline YAML Bash with a real tag-driven release pipeline (`.github/workflows/release.yml`) producing reproducible builds, multi-arch containers (`islamu-event-standalone`, `islamu-event-api`, `islamu-event-ui`, `islamu-event-migration-service`), canonical `SHA256SUMS`, SLSA v1.0 provenance attestations, and Sigstore Cosign signatures.

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
  3. Release notes rely 100% on manual YAML prose instead of diffing the repository's authoritative machine-readable contracts (`schemas/openapi_islamu-event.json`, `schemas/configuration-manifest-v1alpha2.schema.json`, `.env.example`, and EF Core provider migration assemblies), and render a single flat bullet list rather than persona-specific upgrade and integration guides.
- **Practical Impact:** 18 of the 678 committed `CHG-*.yaml` files on `feat/pr79-testing-assurance-follow-up` fail `ReleaseInputPolicyTests.RepositoryChangeFragmentsPassReleaseInputPolicy`, hundreds reference non-existent `docs/*` paths, and self-hosters or API integrators receive no automated, exact ledger of changed `.env` variables, database migrations by provider, or OpenAPI/HAL contract deltas.
- **Evidence Reviewed:** `eng/release/src/ISLAMU.ReleaseEngineering/ChangeWorkflowCommand.cs` (lines 464–505, 749–767), `ReleaseInputPolicy.cs` (lines 354–363, 645–659), `PublicChangelogPolicy.cs` (lines 144–238), live execution of `ReleaseInputPolicyTests.RepositoryChangeFragmentsPassReleaseInputPolicy` (exit code 2, 21 diagnostics across 18 files).
- **Validation Level:** Verified by repository code inspection and live test execution.
- **Linked Mitigation:** `IVSD-M001`
- **Owner / Next Validation:** `PR-FRICTIONLESS-COMMIT-AND-FRAGMENT-GOVERNANCE` and `PR-AUTOMATED-CONTRACT-DIFF-AND-PERSONA-RENDERER` acceptance gates.

### IVSD-F002: Internal Security Theater Displaces Real Artifact Release Security and Breaks Self-Hoster Container Parity (Amanah & Maslahah)
- **Lifecycle:** `open`
- **Severity:** High | **Claim type:** Security architecture & operational portability gap
- **Principle & Domain:** Amanah (Trustworthy supply-chain stewardship) and Maslahah (Protecting self-hosters with real, verifiable artifact security) — Security & Release Architecture
- **Affected Stakeholders:** Self-hosting community operators (mosques, nonprofits, independent deployers) and maintainers
- **Provider-Controlled Decision:**
  1. Embedding 8,473 lines of `unsafe` Linux subreaper and `/proc/<pid>/maps` process surveillance (`LoadProcessOwner.cs`, `SecurityExecutionEvidence*.cs`), a custom "Trusted Bundle Promotion" PKI (`TrustedReleasePolicy.cs`), a 4-manifest Commit-`B` circular hash puzzle, and an external `git-cliff` Rust wrapper inside `ISLAMU.ReleaseEngineering.csproj` (with `<RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>`).
  2. Leaving `.github/workflows/release-adapter-final.yml` as a `printf` stub and leaving `src/Event.Standalone/Dockerfile` and `src/Event.MigrationService/Dockerfile` pinned to .NET 10 (`sdk:10.0.404`) while `global.json` and `Explore.API`/`Explore.Blazor` require .NET 11 (`sdk:11.0.100`).
- **Practical Impact:** Over 18,000 lines of code defend against fictional internal threats (a CI script spying on its own child process memory maps or committing a synthetic Commit `B` to prove a Git tag exists), while self-hosters lack a working `.NET 11` `Event.Standalone` release container image and a standard Arch/SLSA/Sigstore release bundle (`SHA256SUMS` + detached OIDC signature + SLSA v1.0 provenance attestation).
- **Evidence Reviewed:** `ISLAMU.ReleaseEngineering.csproj`, `LoadProcessOwner.cs`, `SecurityExecutionEvidence*.cs`, `TrustedReleasePolicy.cs`, `CandidateCommand.cs`, `TagCommand.cs`, `.github/workflows/release-adapter-final.yml`, `.github/workflows/_container-build.yml`, `src/Event.Standalone/Dockerfile`, `src/Event.MigrationService/Dockerfile`.
- **Validation Level:** Verified by repository code inspection.
- **Linked Mitigation:** `IVSD-M002`
- **Owner / Next Validation:** `PR-RETIRE-THEATER-AND-FIX-CI-GATES` and `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE` acceptance gates.

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
- **Evidence Reviewed:** `.github/workflows/release-adapter-preview.yml`, `.github/workflows/security-tests.yml`, `SecurityChangeSelection.cs` (lines 21–39), `SecurityControlRegister.cs` (lines 374–381), `.github/workflows/_build-test.yml`, live test execution of `ISLAMU.ReleaseEngineering.Tests` (994 total, 11 failed).
- **Validation Level:** Verified by repository code inspection and live test execution.
- **Linked Mitigation:** `IVSD-M003`
- **Owner / Next Validation:** `PR-RETIRE-THEATER-AND-FIX-CI-GATES` and `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE` acceptance gates.

### IVSD-F004: Preservation of Embargo Disclosure Authority, Contributor Identity Privacy, and Verifiable Provenance (Amanah, La Darar, & Huquq al-'Ibad)
- **Lifecycle:** `accepted`
- **Severity:** High | **Claim type:** Constitutional invariant preservation
- **Principle & Domain:** La Darar (Non-harm in security disclosure), Huquq al-'Ibad (Contributor privacy rights in immutable records), and Sidq (Truthfulness of public projections) — Security, Privacy & Governance
- **Affected Stakeholders:** Attendees and operators of self-hosted instances, contributors who may later require privacy/distance from immutable release artifacts, and public changelog readers
- **Provider-Controlled Decision:** While retiring internal security theater and pre-release `CHG-*.yaml` debt, the modernized release engine must preserve the three core guarantees of `i-vsd-release-governance.md`: (1) fail-closed rejection of embargoed security disclosures (`Public-Disclosure: embargoed` or `Restricted-Details` key), (2) zero contributor emails, forge handles, or raw commit bodies in authoritative release artifacts (`release-context.v1.json`, `release-notes.md`, `docs/public/changelog/README.md`), and (3) verifiable tag-and-digest attribution linking public release notes to signed Git tags and `SHA256SUMS`.
- **Practical Impact:** Ensures that architectural simplification removes only fake security theater while upgrading real cryptographic provenance and keeping every substantive stakeholder protection intact.
- **Evidence Reviewed:** `islamic-value-sensitive-design/governance/i-vsd-release-governance.md`, `ReleaseArtifactPolicy.cs`, `ReleaseContextPolicy.cs`, `PublicChangelogPolicy.cs`, `.github/workflows/_container-build.yml`.
- **Validation Level:** Verified by repository code inspection and test coverage in `ISLAMU.ReleaseEngineering.Tests`.
- **Linked Mitigation:** `IVSD-M004`
- **Owner / Next Validation:** Enforced across all four PR scopes.

## Recommendations

- **`IVSD-M001` (for `IVSD-F001`)**:
  - Retire the 678 pre-release greenfield `CHG-*.yaml` and 17 `change-id-renames/*.yaml` files before `v0.1.0`.
  - Auto-omit non-breaking internal commit types (`test`, `refactor`, `style`, `build`, `ci`, `chore`) without forcing `Changelog: skip` trailers, while validating optional high-impact feature/operator `CHG-*.yaml` fragments via `ReleaseInputPolicy.Validate` at commit/range preflight using structural YAML keys (`Restricted-Details`, `Public-Disclosure: embargoed`) rather than the English substring `"restricted"`.
  - Build automated contract differs (`OpenApiContractDiffer`, `ConfigurationManifestDiffer`, `MigrationLedgerExtractor`) and a pure C# `PersonaReleaseNotesRenderer` that generates distinct **Community Admin & Organizer Highlights**, **Self-Hoster & Operator Upgrade Guide**, and **Developer & API Integrator Changelog** views.
- **`IVSD-M002` (for `IVSD-F002`)**:
  - Delete the 8,473-line `/proc` surveillance harness (`LoadProcessOwner.cs`, `SecurityExecutionEvidence*.cs`), custom promotion PKI (`TrustedReleasePolicy.cs`, `PromotedReleaseCommand.cs`), synthetic Commit-`B` 4-manifest hash puzzle (`CandidateCommand.cs`, `TagCommand.cs`, `MainCommand.cs`, `BaselineCommand.cs`), external `git-cliff` wrapper (`GitCliffRenderer.cs`), and 3-branch GitBook SSH state machine (`SignedPublicationInventoryAuthority.cs`).
  - Disable `AllowUnsafeBlocks`, re-enable Roslyn analyzers on `ISLAMU.ReleaseEngineering`, align `Event.Standalone/Dockerfile` and `Event.MigrationService/Dockerfile` to .NET 11 pinned digests, and build a tag-triggered `.github/workflows/release.yml` pipeline producing reproducible artifacts, multi-arch images (`islamu-event-standalone`, `islamu-event-api`, `islamu-event-ui`, `islamu-event-migration-service`), canonical `SHA256SUMS`, SLSA v1.0 provenance attestations (`actions/attest`), and Sigstore Cosign signatures.
- **`IVSD-M003` (for `IVSD-F003`)**:
  - Replace hardcoded snapshot counts and the `verificationStatus == "pending"` lock-in in `SecurityControlRegister` with structural schema validation supporting lifecycle progression (`pending` -> `verified`).
  - Align `select-security-changes` with actual security/runtime path rules and invoke TUnit / BenchmarkDotNet directly in `security-tests.yml` and `performance-smoke.yml`.
  - Delete theatrical `printf`-only adapter workflows (`release-adapter-preview.yml`, `release-adapter-final.yml`) and add the fast (< 3s) `ISLAMU.ReleaseEngineering.Tests` suite to `.github/workflows/_build-test.yml`.
- **`IVSD-M004` (for `IVSD-F004`)**:
  - Preserve and test contributor identity stripping (`ReleaseArtifactPolicy` / `ReleaseContextPolicy`), fail-closed embargo gating (`Public-Disclosure: embargoed` / `Restricted-Details`), and offline self-hoster verification (`git verify-tag`, `sha256sum --check SHA256SUMS`, `gh attestation verify`, `cosign verify-attestation`).

### Rejected Alternatives
1. **Moving the 8,473-line `/proc` and subreaper surveillance harness (`LoadProcessOwner.cs` + `SecurityExecutionEvidence*.cs`) into a separate `ISLAMU.AssuranceRunner` project instead of deleting it** — Rejected: inspecting `/proc/<pid>/maps` and socket inodes from inside a user-space C# test wrapper is fragile security theater that breaks across local builds and kernel updates while adding zero real security over standard `dotnet test` + Microsoft.Testing.Platform and GitHub ephemeral runner isolation.
2. **Keeping the synthetic Commit-`B` 4-manifest circular SHA ceremony (`release-candidate.v1.json` -> Commit `B` -> tag -> `release-evidence.v1.json`)** — Rejected: committing build outputs back into Git to prove a Git tag exists solves a circular problem of its own creation. Industry-standard release security signs the Git tag directly on the source commit (`git tag -s vX.Y.Z`) and signs/attests the compiled deliverables and `SHA256SUMS` outside Git via SLSA v1.0 provenance and Sigstore Cosign.
3. **Relying exclusively on manual `CHG-*.yaml` prose without automated contract diffing** — Rejected: manual prose inevitably misses exact OpenAPI route/DTO diffs, `.env` variable additions, and per-provider EF Core migration IDs that self-hosters and API integrators depend on for safe upgrades.

## Stakeholders

| Stakeholder | Impact of This Modernization | Protection Preserved |
|---|---|---|
| Self-hosting operators (mosques, nonprofits, community orgs) | Receive a dedicated **Self-Hoster & Operator Upgrade Guide** per release (exact `.env`/Config Manifest diffs, per-provider EF Core migration tables, working .NET 11 `islamu-event-standalone` image digests) plus standard `SHA256SUMS`, SLSA v1.0 provenance, and Cosign verification commands. | Offline cryptographic verification (`sha256sum`, `git verify-tag`, `gh attestation verify`, `cosign`) and human-owned embargo windows (`IVSD-M001`, `IVSD-M002`, `IVSD-M004`). |
| Third-party developers & API/Client integrators | Receive an automated **Developer & API Integrator Changelog** generated by diffing `schemas/openapi_islamu-event.json`, HAL `_links` capabilities, and wire contracts across `<base-tag>..<target-ref>`. | Deterministic contract accuracy (`IVSD-M001`). |
| Contributors & AI agents | Eliminates daily commit/planning friction: no `Changelog: skip` trailers on internal commits, no Planning-vs-Execution `CHG-*` Catch-22, no false rejection on the English word `"restricted"`, and no 6-hour security CI run on non-security edits. | Contributor identity privacy in authoritative release artifacts remains strictly enforced (`IVSD-M001`, `IVSD-M004`). |
| Release & Platform operators | Truthful CI checks (`ISLAMU.ReleaseEngineering.Tests` runs in fast CI in < 3s; theatrical `printf` workflows and 532-line self-hosted `release-publish.yml` retired; standard tag-driven `.github/workflows/release.yml`). | Explicit human tag-signing and embargo disclosure approval boundaries remain mandatory (`IVSD-M002`, `IVSD-M003`, `IVSD-M004`). |

## I-VSD Principles And Domains

| Principle | Domain | Operationalization in Plan |
|---|---|---|
| **Sidq (Truthfulness)** | CI/CD, Contract Diffing & Release Notes | Retire `printf`-only theatrical checks and `/proc` surveillance theater; auto-diff OpenAPI, Config Manifest, and EF Core migrations so release notes state exact technical truth; run `ISLAMU.ReleaseEngineering.Tests` in fast CI. |
| **Amanah (Trust & Stewardship)** | Supply-Chain Provenance & Documentation | Sign Git tags directly on source commits; publish canonical `SHA256SUMS`, CycloneDX/SPDX SBOMs, SLSA v1.0 provenance attestations, and Cosign signatures for all release artifacts and container images (`Event.Standalone`, `Explore.API`, `Explore.Blazor`, `Event.MigrationService`). |
| **Maslahah (Stakeholder Benefit)** | Persona-Personalized Upgrade Experience | Render distinct sections in every release for Community Admins, Self-Hosters/Operators, and API/Client Integrators from one unified `release-context.v1.json`. |
| **La Darar (Non-Harm)** | Security Embargo & Developer Ergonomics | Keep embargoed details fail-closed (`Public-Disclosure: embargoed` / `Restricted-Details` key) while removing false-positive substring bans and unnecessary 6-hour CI runs on non-security edits. |
| **Huquq al-'Ibad (Rights of People)** | Contributor Privacy | Preserve complete stripping of contributor emails, names, handles, and raw commit bodies from all authoritative release artifacts. |

## Validation Gaps

- Production SSH signer principals (`eng/release/trust/allowed-signers`) remain prospective until the Project Steward provisions the official release signing key prior to tagging `v0.1.0`.
- Live GHCR / ATCR container registry push and GitHub OIDC SLSA attestation steps execute in GitHub Actions on tag push (`v*`) or workflow dispatch; local ring verification validates the deterministic C# contract differs, persona renderer, checksum generator, and workflow security policies offline.

## Escalation Needed

- **Project Steward approval** before cutting the first release tag (`v0.1.0`) and enrolling production SSH signer keys in `eng/release/trust/allowed-signers`.
- **Qualified legal/scholarly review** only if future release governance introduces contributor identity attribution or modifies vulnerability embargo policy (no such change is made in this workstream).

## Evidence Reviewed

- `eng/release/src/ISLAMU.ReleaseEngineering/*.cs` and `ISLAMU.ReleaseEngineering.csproj` at `e01f18a4e1b671faf8bc7687a02def1c6a97c35a`
- `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/*.cs` and live test run results (994 tests, 11 failures, 3m 41s duration)
- `.ci/scripts/*.cs`, `.github/workflows/_container-build.yml`, `.github/workflows/deploy-coolify.yml`, `.github/workflows/release-*.yml`, `.github/workflows/security-tests.yml`, `.github/workflows/performance-smoke.yml`, `.github/workflows/_build-test.yml`
- `src/Event.Standalone/Dockerfile`, `src/Event.MigrationService/Dockerfile`, `src/Explore.API/Dockerfile`, `src/Explore.Blazor/Dockerfile`, `Directory.Build.props`
- `schemas/openapi_islamu-event.json`, `schemas/configuration-manifest-v1alpha2.schema.json`, `.env.example`, and `src/Explore.Persistence*/Migrations`
- `docs/internal/RELEASE_POLICY.md`, `docs/internal/RELEASE_CHECKLIST.md`, `docs/internal/RELEASE_RUNBOOK.md`, `docs/internal/adr/ADR-025-provider-neutral-release-governance.md`
- `docs/internal/releases/changes/CHG-*.yaml` (678 files) and `docs/internal/releases/change-id-renames/*.yaml` (17 files)
- `islamic-value-sensitive-design/governance/i-vsd-release-governance.md`

## Missing Evidence

- Live production release tag (`v0.1.0`) attestation bundle in GHCR (prospective until first public release).

## Context Inventory

- Workstream triad: `dev/active/release-engineering-modernization/`
- Constitutional governance anchor: `islamic-value-sensitive-design/governance/i-vsd-release-governance.md`
- Plane active tracking: `event/work/active-release-engineering-modernization.yaml` (under Epic `EVENT-6861`)

## Planning Handoff
- Workstream: release-engineering-modernization
- Status: current
- Reviewed input: `dev/active/release-engineering-modernization/release-engineering-modernization-plan.md` (Revision 2, grounded at `e01f18a4e1b671faf8bc7687a02def1c6a97c35a`)
- Findings and mitigations:
  - `IVSD-F001 -> IVSD-M001`
  - `IVSD-F002 -> IVSD-M002`
  - `IVSD-F003 -> IVSD-M003`
  - `IVSD-F004 -> IVSD-M004`
- Required plan mappings:
  - `IVSD-F001 / IVSD-M001` -> `PR-FRICTIONLESS-COMMIT-AND-FRAGMENT-GOVERNANCE` (Scenarios 2.A, 2.B, 2.C; Phases 3–4) & `PR-AUTOMATED-CONTRACT-DIFF-AND-PERSONA-RENDERER` (Scenarios 3.A, 3.B; Phases 5–6)
  - `IVSD-F002 / IVSD-M002` -> `PR-RETIRE-THEATER-AND-FIX-CI-GATES` (Scenarios 1.A, 1.B; Phases 1–2) & `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE` (Scenarios 4.A, 4.B; Phases 7–8)
  - `IVSD-F003 / IVSD-M003` -> `PR-RETIRE-THEATER-AND-FIX-CI-GATES` (Scenarios 1.B, 1.C; Phase 2) & `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE` (Scenario 4.B; Phase 8)
  - `IVSD-F004 / IVSD-M004` -> Enforced across `PR-FRICTIONLESS-COMMIT-AND-FRAGMENT-GOVERNANCE` (Scenario 2.B), `PR-AUTOMATED-CONTRACT-DIFF-AND-PERSONA-RENDERER` (Scenario 3.B), and `PR-ARTIFACT-SECURITY-AND-RELEASE-PIPELINE` (Scenario 4.A)
- Escalations required before: release (Project Steward approval for production SSH signers and first release tag `v0.1.0`)
- Refresh triggers: any change that reintroduces contributor personal identifiers into authoritative release artifacts, weakens human-authorized security embargo gating, or removes offline `SHA256SUMS` / SLSA provenance / signed-tag verification for self-hosters.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-11-22 | none | draft | Initial integrated planning intake for `release-engineering-modernization` | Repository audit at `e01f18a4e1b671faf8bc7687a02def1c6a97c35a` |
| 2026-11-22 | draft | current | Revalidated against completed 4-PR plan, context, and tasks triad (`plan-aligned`) | `dev/active/release-engineering-modernization/` (Revision 1) |
| 2026-11-22 | current | current | Upgraded to Revision 2 incorporating Automated Contract Diffing, Persona-Segmented Release Views, retirement of `/proc` and Commit-`B` theater, and Arch/SLSA/Sigstore artifact security | `dev/active/release-engineering-modernization/` (Revision 2) |
