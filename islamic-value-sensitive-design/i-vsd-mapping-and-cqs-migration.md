# I-VSD Planning Assessment: Generated Mapping And Native Operations

Last Updated: 2026-09-11

## Review Metadata

- Mode: planning
- Subject: AutoMapper/Mapperly and MediatR/native-operation migration
- Workstream: mapping-and-cqs-migration
- Report kind: workstream-planning-assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-11
- Reviewed input: develop `1fdcfbe42679a5e2f549171be4090df72ebed504` plus `dev/active/mapping-and-cqs-migration` triad revision `review-r3` (ten-path subscription pilot) and `dev/backlog/mapping-and-native-cqs-rollout.md` (graduated program, boundaries 1-6)
- Supersedes: none; applies the [Steward consultation](i-vsd-automapper-mapperly-and-mediatr-replacement-consultation.md) to a concrete implementation plan without replacing that decision record.

## Scope

Evaluate whether the plan implements the provider responsibilities already accepted in the consultation: one supportable build, accurate dependency/configuration promises, safe DTO disclosure, explicit operation dependencies, reliable authorization and notification behavior, and maintainable native composition.

The Steward has decided Mapperly and repository-native command/query handlers with generic decorators and Microsoft DI. This assessment does not reopen those choices. It checks the concrete plan against current code, the consultation's stable finding/mitigation IDs, and official documentation facts.

## Claim Boundary

This is provider-responsibility design reasoning and planning traceability. It is not a fatwa, legal opinion, security certification, proof that a refactor passed tests, or a benchmark. Plan alignment means the tasks address the findings; implementation findings remain open until their evidence exists. Apache-2.0 metadata does not itself settle every distribution or generated-output obligation.

## Findings

Finding IDs retain the consultation's meaning. New technical qualifications refine mitigations without renumbering the accepted concerns.

### IVSD-F001 — The edition promise must match the supported build

- Lifecycle: open; severity: High; claim: implementation traceability and provider design reasoning.
- Principles/domain: Promise-Keeping, Truthfulness, Avoiding Gharar; Operational/Governance.
- Stakeholders: self-hosters, operators, contributors.
- Provider decision: remove unsupported edition choices from the complete build/configuration/documentation surface.
- Evidence: E01/E02/E05.
- Mitigations: IVSD-M001, IVSD-M004 and the edition cleanup in IVSD-M002.
- Owner: build/setup maintainers; validation: plan S7 and tasks Single Edition cleanup.

### IVSD-F002 — Frozen dependency risk must end with actual package removal

- Lifecycle: open; severity: High; claim: implementation traceability.
- Principles/domain: Non-Harm, Trust, Excellence; Technical/Operational.
- Stakeholders: all deployments and the people relying on them.
- Provider decision: replace the dependency graph and retire its temporary mitigation only when no live dependency remains.
- Evidence: E01/E02.
- Mitigation: IVSD-M001. Mapperly is not claimed universally recursion-proof: the plan includes bounded DTO/cyclic-navigation assurance and preserves the old ceiling until AutoMapper removal.
- Owner: mapping/dependency maintainers; validation: plan S1/S7 and mapping/final dependency tasks.

### IVSD-F003 — Compiler diagnostics do not independently prove privacy

- Lifecycle: open; severity: Medium; claim: source-derived constraint plus design reasoning.
- Principles/domain: Excellence, Truthfulness, Rights of People; Technical/Evaluation.
- Stakeholders: tenants, attendees, API consumers, maintainers.
- Provider decision: combine strict mapping diagnostics with explicit disclosure boundaries and independent serialization tests.
- Evidence: E03/E06. RequiredMappingStrategy reports unmapped members; an automatically matched member can still disclose an unintended value. The plan does not describe it as an automatic PII whitelist.
- Mitigations: IVSD-M001, IVSD-M002; preserve immutable collection ownership and domain aggregate encapsulation (no direct entity property/collection mutation; DTOs map to immutable value objects/commands, and domain entities mutate strictly via aggregate methods).
- Owner: mapping/DTO maintainers; validation: S1/S6 and each mapping-family task.

### IVSD-F004 — Replacement must remove hidden request dispatch completely

- Lifecycle: open; severity: High; claim: implementation traceability.
- Principles/domain: Non-Harm, Promise-Keeping, Avoiding Gharar; Technical/Strategic.
- Stakeholders: contributors, operators and all operation consumers.
- Provider decision: typed command/query handlers, direct dependencies and deletion of MediatR, without a substitute generic dispatcher.
- Evidence: E01/E04.
- Mitigation: IVSD-M003; inventory includes nested handlers, MCP callbacks, jobs, middleware, HAL assemblers, infrastructure services and test helpers, not only controllers. Rollout sequences the 124 cohorts via a leaf-first DAG to avoid cross-module PR cascading and merge conflicts.
- Owner: Application/API maintainers; validation: S2–S5 and complete capability inventory/caller closure.

### IVSD-F005 — Dependency and license records must be accurate

- Lifecycle: open; severity: Low; claim: metadata evidence and implementation traceability.
- Principles/domain: Truthfulness, Trust; Governance.
- Stakeholders: contributors, downstream operators and Project Steward.
- Provider decision: record Mapperly 4.3.1 as Apache-2.0 with its actual generator role, retained terms/notices, locks and generated-output disposition.
- Evidence: E02/E06/E07.
- Mitigation: IVSD-M005; old MIT/zero-allocation claims retire with DUAL_VERSIONING. The existing dependency scanner is mandatory evidence, not legal counsel.
- Owner: Project Steward/dependency reviewer; validation: first package-owning task and final dependency review.

### IVSD-F006 — Explicit injection must retain every real trust boundary

- Lifecycle: open; severity: High; claim: design reasoning grounded in current authorization code.
- Principles/domain: Trust, Justice, Rights of People; Technical/Governance.
- Stakeholders: tenants, authenticated users, capability-token holders and background-work subjects.
- Provider decision: apply the authorization decorator to void commands, result commands and queries; preserve resource fact precedence and existing public/capability/worker authority.
- Evidence: E03/E04/E08.
- Mitigations: IVSD-M003, IVSD-M006, IVSD-M007. Deep handler resolution is placed into compiled CI Architecture/Integration tests; runtime startup preflight is bounded to 0-allocation metadata and factory checks to prevent readiness timeout loops on low-power self-hosted hardware.
- Owner: Application security maintainer; validation: S2/S3/S6 and native composition/authorization invariant tests.

### IVSD-F007 — The validator convention remains a separate governance decision

- Lifecycle: open; severity: Medium; claim: implementation traceability.
- Principles/domain: Promise-Keeping, Excellence; Technical/Governance.
- Stakeholders: contributors, reviewers and all alternate operation adapters.
- Provider decision: keep validators manually instantiated inside existing operation ownership.
- Evidence: E01/E02.
- Mitigation: IVSD-M008; no DI validator/validation decorator is introduced.
- Owner: Application maintainers; validation: every handler cutover, native-aware compiled convention checks.

### IVSD-F008 — Transaction and outbox ownership must survive dispatch changes

- Lifecycle: open; severity: High; claim: implementation traceability and design reasoning.
- Principles/domain: Non-Harm, Trust; Technical/Operational.
- Stakeholders: registrants, payment subjects, privacy-erasure subjects and tenants.
- Provider decision: keep current transaction/commit/authority ordering and durable side-effect ownership.
- Evidence: E01/E03/E04.
- Mitigation: IVSD-M009; no blanket transaction/retry wrapper. Post-commit cache invalidation preserves existing deliberate non-cancellation; failure does not mean a committed write rolled back.
- Owner: Application/Persistence maintainers; validation: S4/S5 and final real-engine transaction/concurrency/erasure lanes.

### IVSD-F009 — Explicit dependencies must remain understandable

- Lifecycle: open; severity: Low; claim: implementation traceability.
- Principles/domain: Excellence; Technical/Governance.
- Stakeholders: maintainers and future contributors.
- Provider decision: capability-split controllers with constructor injection under current repository authority.
- Evidence: E02/E04.
- Mitigation: IVSD-M010. Preserve route/verb/operationId/tag/authorization/HAL semantics; do not replace the mediator with a dependency bag or generic controller facade. Split controllers explicitly preserve [Tags] annotations to protect client SDKs, and permit targeted [FromServices] for heavy fan-in endpoints to prevent constructor allocation bloat.
- Owner: API maintainers; validation: S1/S2 and settings/provider/guest capability split tests.

## Recommendations

Proceed with the decided migration through the plan's bounded mapper and operation slices. Each primitive family is independently reviewed; the entire 1,400-file dispatch migration is not a single review unit. A request belongs to exactly one dispatch cohort while migrating; no old-to-new dispatcher adapter or compatibility API remains.

Ownership under `review-r3`: the active ledger owns only the ten-path subscription pilot; every other mitigation is owned by a numbered boundary in the graduated backlog and requires its own promoted packet before implementation. No mitigation is unowned.

| Mitigation | Concrete provider-responsibility requirement | review-r3 owner |
|---|---|---|
| IVSD-M001 | Complete generated mapping with explicit disclosure/collection behavior and final removal of frozen mapping dependency | Partial: active pilot (ActorSubscription detail/list disclosure vectors, exhausted profile deleted). Remainder: backlog boundary 1. **Refinement:** the pilot deliberately has no `ReverseMap`, existing-target, or nested-graph map; the *second* promoted mapping family must be one that has them (Event or CustomProperty). Domain aggregate encapsulation is strictly enforced: Mapperly never sets entity properties directly. |
| IVSD-M002 | Delete edition plumbing, stale current guidance and exhausted audit exceptions | Backlog boundaries 5 and 6 |
| IVSD-M003 | Native typed operations and complete consumer migration, no mediator substitute | Backlog boundaries 2 and 4. **Refinement:** rollout sequences the 124 cohorts via a leaf-first DAG (handlers with 0 internal callers first) to avoid cross-module PR cascading. |
| IVSD-M004 | Operator docs/catalogue expose only real supported choices | Backlog boundary 6 |
| IVSD-M005 | Accurate Apache-2.0 package/role/obligations record and license validation | Partial: active pilot (dependency record `docs/internal/legal/dependencies/mapperly.md`, scanner run, locked restore). Remainder: backlog boundary 5. **Verified 2026-09-11 (E06):** Mapperly 4.x ships RMG012/RMG020/RMG037/RMG038 as strict defaults and `RequiredMappingStrategy` defaults to `Both`; the `.editorconfig` entries pin those severities against future default drift rather than create them. |
| IVSD-M006 | Every operation shape is wrapped; original authority and zero-payload telemetry are preserved | Backlog boundary 2 (gate at that PR's exit, not the final sweep) |
| IVSD-M007 | Bounded native composition catches missing/duplicate/late entries and actual construction errors | Backlog boundary 2. **Refinement:** deep handler resolution moves to CI Architecture/Integration tests (`NativeOperationHostCompositionTests`); runtime preflight inside the container boot is bounded to 0-allocation metadata and factory checks, ensuring cold-boot latency on standalone SQLite / Raspberry Pi instances remains negligible (<10ms) and prevents orchestrator readiness timeout loops. |
| IVSD-M008 | Manual validators remain | Backlog boundary 4 and native-aware architecture assurance |
| IVSD-M009 | Handler transactions, outbox, and authority-first erasure remain | Backlog boundaries 3 and 4 (settings, commerce, erasure slices) |
| IVSD-M010 | Capability splits retain understandable explicit dependencies | Backlog boundaries 3 and 4 (settings, provider-management, guest-order splits). **Refinement:** split controllers preserve explicit `[Tags]` annotations to protect client SDKs, and permit targeted `[FromServices]` on high-fan-in endpoints to avoid constructor bloat. |

Rejected alternatives remain the Steward's rejected mediator facade, third-party mediator, Scrutor, two images, indefinite frozen libraries and blanket textbook decorators. No new alternative needs a moral decision. Within the selected Microsoft DI design, actual scoped construction plus a native-operation re-entry guard replaces an unprovable promise to inspect arbitrary factory bodies.

## Stakeholders

Community self-hosters gain a single honest dependency/build story; contributors gain explicit mapping and operation contracts while accepting substantial conversion work. Tenants and registrants need unchanged authority, transaction and disclosure boundaries. People whose data is erased need existing anti-resurrection fencing to remain effective in every worker. The Project Steward retains a component-specific licensing duty even when a dependency is permissively licensed.

## I-VSD Principles And Domains

Trust requires control over the authorization path and honest evidence of composition. Non-Harm requires removing the frozen dependency without creating data-loss or bypass risks. Truthfulness and Promise-Keeping require current documentation to describe the actual build. Avoiding Gharar requires one supported edition rather than inactive switches. Excellence favors explicit, bounded contracts and meaningful tests. Rights of People requires no expanded PII disclosure or weakened erasure guarantees.

## Common Overlooked Failures And Outcomes

- A strict mapper can still map an automatically matching sensitive field; explicit disclosure and serialized-output tests remain necessary.
- Void requests need their own protected handler shape. The current code has a non-generic IRequest in addition to two IRequest<Unit> types.
- ValidateOnBuild excludes open generics and does not substitute for executing a factory's construction path. Native operation re-entry guards must release state after failures and remain scope-specific.
- DI factory aliases already exist; preserve their shared-instance semantics. No general factory-dependency analyzer is assumed.
- Annotation-free public/capability/worker handlers are not automatically unauthenticated operations. Preserve their actual authority and native discovery rather than adding broad bypass metadata.
- A post-commit notification error can accompany a committed write. Do not swallow it or imply transaction rollback.
- Capability splits can move generated clients through tag changes even when routes remain unchanged. Preserve both route and tag contracts.

## Validation Gaps

All product build/test, package-license scanner, startup construction, mapping compilation and benchmark evidence remains execution-owned. The planning session did not execute these checks. The current graph is stale/under-indexed, so source inventories and later compiled coverage are required. Individual reverse-map need and slice-owned caller closures are bounded per-slice investigations; the architecture is already decided.

Mapperly package metadata is authoritative for the selected package's declared license, not a legal opinion on every generated-output/distribution question. A source-free dependency record and independent review remain required before the first package commit.

## Escalation Needed

No religious-legal escalation is required for this planning task. If package terms or scanner evidence reject an intended distribution, dependency adoption blocks until the Project Steward obtains a compatible disposition. Optional written legal certainty is not invented as a new approval hurdle when the decision already authorizes adopting Mapperly.

## Evidence Reviewed

| ID | Locator | What it supports |
|---|---|---|
| E01 | [Steward consultation](i-vsd-automapper-mapperly-and-mediatr-replacement-consultation.md), 2026-09-11 | Accepted replacement decisions, F001–F009 and M001–M010. |
| E02 | QUICK_REFERENCE; GOVERNANCE; IP_GOVERNANCE; CI_CD_GOVERNANCE; RELEASE_POLICY; DOCUMENTATION_ARCHITECTURE | Repository authority, dependency/configuration and documentation duties. |
| E03 | ApplicationServicesRegistration, AuthorizationBehavior, PerformanceBehavior, Profiles, SettingUpsertService, InstanceSmtpSettingService | Actual current composition, mapping and post-commit behavior. |
| E04 | CurrentUserResolutionExtensions; large controller families; MCP projected factory; outbox/identity/Jetstream consumers; current authorization architecture tests | Trust/caller surface beyond ordinary controllers. |
| E05 | Directory.Packages.props, Directory.Build.props, API/Blazor Dockerfiles, docker-compose, canonical environment catalogue/metadata, API configuration mapping | Real edition plumbing targeted for removal. |
| E06 | [Mapperly publisher package](https://www.nuget.org/packages/Riok.Mapperly/), [mapping configuration](https://mapperly.riok.app/docs/configuration/mapper), [analyzer diagnostics](https://mapperly.riok.app/docs/configuration/analyzer-diagnostics), [v4 diagnostic changes](https://mapperly.riok.app/docs/breaking-changes/4-0); first accessed via Tavily/Context7 during planning-r1, independently re-verified via Context7 (`/websites/mapperly_riok_app`) and web search on 2026-09-11 for review-r2 | 4.3.1 current stable; Apache-2.0 (`LICENSE` file and NuGet metadata); RMG012/020/037/038 strict by default since 4.0 with `.editorconfig` `dotnet_diagnostic.<id>.severity` override; `RequiredMappingStrategy` defaults to `Both`; `UseReferenceHandling` and `UseDeepCloning` are opt-in, so assignable references are reused by default. |
| E09 | `dev/active/mapping-and-cqs-migration` triad revision `review-r2` and `dev/backlog/mapping-and-native-cqs-rollout.md` (Graduation Decision, boundaries 1-6, "I-VSD Ownership After Split") | Ten-path pilot ownership; every non-pilot mitigation assigned to a backlog boundary; Senior CTO refinements (three handler shapes, shared evaluator, re-entry guard, scoped preflight, capability splits). |
| E10 | `dev/active/mapping-and-cqs-migration` triad revision `review-r3` | Four refined architectural decisions: CI deep DI validation + bounded 0-allocation boot preflight (M007), domain aggregate encapsulation (M001), OpenAPI tag protection + targeted `[FromServices]` (M010), leaf-first DAG rollout (M003). |
| E07 | [Microsoft DI overview](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview), [ValidateOnBuild](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.serviceprovideroptions.validateonbuild); Context7/Tavily 2026-09-11 | Scope/construction contract and validation limitations. |
| E08 | dev/active/mapping-and-cqs-migration plan/context/tasks revision planning-r1 and 1,744-path candidate inventory | Scenarios, exact ownership protocol, 124 feature cohorts, verification and release/graduation tasks. |

## Missing Evidence

Compiled complete native handler registration, privacy output vectors, real startup preflight, actual license/lock evidence and multi-provider invariants do not exist yet. Do not close findings based on plan text. Advisory benchmark improvement is unmeasured and not a success claim.

## Context Inventory

The planner reused one shared repository evidence packet and the supplied consultation. The triad owns architecture, execution tasks and resume state respectively; this report owns provider-responsibility traceability. The candidate inventory is local planning evidence, not an assurance test fixture. Research source identity is retained in task context without third-party implementation snippets.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
|---|---|---|---|---|
| 2026-09-11 | none | draft | Apply accepted consultation to concrete migration workstream | E01–E08 |
| 2026-09-11 | draft | current | Revalidate completed triad: every F001–F009/M001–M010 maps to a scenario/task; independent technical findings resolved; no new moral or architecture decision left open | E08; native factory guard, atomic caller rewrite and explicit inbound-construction refinements |
| 2026-09-11 | current | stale | Senior CTO review-r2 split the program into a ten-path pilot plus graduated backlog; mitigation task ownership changed | E09 |
| 2026-09-11 | stale | current | Planning-mode revalidation against review-r2: every mitigation re-owned by the pilot ledger or a numbered backlog boundary; Mapperly diagnostic/strategy facts independently re-verified; two technical refinements added (hardest mapping family second, measured startup preflight delta). No finding closed; no new moral decision opened | E06 (re-verified), E09 |
| 2026-09-11 | current | current | Planning-mode revalidation against review-r3: deep DI resolution shifted to CI with bounded 0-allocation boot preflight (M007); domain aggregate encapsulation invariant enforced against direct entity mutation (M001); controller splits protect OpenAPI client tags with targeted `[FromServices]` (M010); leaf-first DAG sequencing for 124 cohorts (M003) | E10 |

## Planning Handoff

- Workstream: mapping-and-cqs-migration
- Status: current / plan-aligned
- Reviewed input: triad revision review-r3 at develop 1fdcfbe42679a5e2f549171be4090df72ebed504, plus `dev/backlog/mapping-and-native-cqs-rollout.md` Graduation Decision
- Findings and mitigations: all consultation IDs retained (F001–F009 -> M001–M010); ownership table in Recommendations; plan §9 mirrors it by phase name.
- Required mappings: S1–S7; active pilot owns partial M001 and M005; backlog boundaries 1–6 own every remaining mitigation as stated in the ownership table. Each promoted packet re-enters planning-mode revalidation for its own scope before implementation.
- Escalations before implementation: only an actual dependency/provenance rejection or a newly discovered material architectural conflict; no redundant approval of the already-decided replacement pattern.
- Refresh triggers: promotion of any backlog boundary to an active packet (revalidate that packet's scope); native authority changes; transaction/notification semantics change; new package/license evidence; unsafe mapping boundary; changed edition scope; material triad rewrite.
