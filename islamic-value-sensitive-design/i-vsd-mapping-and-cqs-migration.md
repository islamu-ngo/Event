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
- Reviewed input: `mapping-and-cqs-migration / full-migration-r4` authority in the active grand ledger, plan and context (E11); completed subscription pilot `dee432983931a3b15b32d088461a427f72fa3b51` on develop baseline `97784ef6d22decab97a4da82c9e18f3ed8d7c2b1`, its dependency record and bounded recorded evidence (E12/E13).
- Supersedes: none; applies the [Steward consultation](i-vsd-automapper-mapperly-and-mediatr-replacement-consultation.md) to a concrete implementation plan without replacing that decision record.

## Scope

Evaluate whether the plan implements the provider responsibilities already accepted in the consultation: one supportable build, accurate dependency/configuration promises, safe DTO disclosure, explicit operation dependencies, reliable authorization and notification behavior, and maintainable native composition.

The user explicitly authorizes the entire grand migration: all eleven mapping families (subscription pilot complete; ten remaining), native CQS with authorization/timing decorators, all 124 capability cohorts and special consumers, full AutoMapper/MediatR/MediatR.Contracts removal, single-edition MSBuild/Docker/environment cleanup, documentation and integrated verification. The six delivery boundaries are active implementation ordering, not backlog approval gates. Literal caller-closed slice ownership remains required, not renewed scope approval.

The Steward has decided Mapperly and repository-native command/query handlers with generic decorators and Microsoft DI. This assessment does not reopen those choices. It checks the active plan against the consultation's stable finding/mitigation IDs, the shared evidence packet and bounded pilot evidence. The explicit full-migration-r4 overrides in E11 govern over retained historical pilot-only, deferred, promotion and review-r3 status language; that historical text is not current execution authority.

## Claim Boundary

This is provider-responsibility design reasoning and implementation traceability, with bounded recorded pilot test evidence. It is not a fatwa, legal opinion, security certification, full-migration test pass, stakeholder/operational validation, or a benchmark. **Disposition: aligned with the explicitly authorized full-migration-r4 plan; product assurances remain open.** All F001–F009 remain open; only the proven pilot portions of M001/M005 are complete. Apache-2.0 metadata and scanner success do not themselves settle every distribution or generated-output obligation.

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
- Evidence: E02/E06/E12/E13.
- Mitigation: IVSD-M005; pilot package admission is recorded, not still pending. Old MIT/zero-allocation claims retire with DUAL_VERSIONING; final distribution, locks and dependency-policy closure remain open. The existing dependency scanner is mandatory evidence, not legal counsel.
- Owner: Project Steward/dependency reviewer; validation: completed pilot admission; grand ledger 7.2/7.3/7.V/7.R4 for final dependency review.

### IVSD-F006 — Explicit injection must retain every real trust boundary

- Lifecycle: open; severity: High; claim: design reasoning grounded in current authorization code.
- Principles/domain: Trust, Justice, Rights of People; Technical/Governance.
- Stakeholders: tenants, authenticated users, capability-token holders and background-work subjects.
- Provider decision: apply the authorization decorator to void commands, result commands and queries; preserve resource fact precedence and existing public/capability/worker authority.
- Evidence: E03/E04/E08/E11/E14.
- Mitigations: IVSD-M003, IVSD-M006, IVSD-M007. Deep handler resolution belongs in compiled CI Architecture/Integration tests; runtime startup preflight remains bounded metadata and factory-descriptor checks, not full handler construction. Zero-allocation/<10ms boot overhead is an unmeasured design target, not proof of readiness reliability. Current Performance → Authorization ordering intentionally changes to Authorization → Performance → business handler; denied operations do not enter timing. Preserve authorization evidence, fact precedence, fail-closed denied/unavailable distinctions and zero-payload telemetry.
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

Proceed with the entire authorized migration through bounded, independently reviewable mapper and operation slices, without stopping at the completed pilot or requiring backlog promotion. A request belongs to exactly one dispatch cohort while migrating; every converted callee's direct consumers move in the same slice. No old-to-new dispatcher adapter or compatibility API remains.

All ownership below refers to [the active grand ledger](../dev/active/mapping-and-cqs-migration/mapping-and-cqs-migration-grand-tasks.md), revision `full-migration-r4`. Pilot completion does not close a whole-program finding. Per-slice security/architecture gates remain prerequisites for product cutover; the final integrated gate does not replace them.

| Mitigation | Concrete provider-responsibility requirement | Active ledger ownership and evidence state |
|---|---|---|
| IVSD-M001 | All generated mappings preserve disclosure, collection ownership and business-owned mutation; remove the frozen mapping dependency | S1/S6/S7; pilot portion complete (E12/E13). Remaining: 2.1–2.10, 2.V, 6.1, 7.V/7.R4. The second family must exercise real inbound/existing-target semantics (Event or CustomProperty). No direct Mapperly entity-property/private-collection mutation: DTOs become immutable value objects/command arguments; aggregates mutate through business methods. Cyclic/bounded DTO and forged-field evidence remains open. |
| IVSD-M002 | Delete edition plumbing, stale current guidance and exhausted audit exceptions | S7; 6.1/6.2 and 7.1/7.2/7.V/7.R4; open. Remove each vendor/exception only after its compiled/restored graph is exhausted; preserve unrelated exceptions. |
| IVSD-M003 | Native typed operations and all consumers, without a mediator substitute | S2–S5; 3.1–3.5, 4.1–4.3, 5.1–5.4, all annex rows 5.010–5.133, 6.1–6.3; open. Preserve leaf-first dependency sequencing, atomic caller closure and all special consumers; a subscription map is not subscription dispatch completion. |
| IVSD-M004 | Operator docs/catalogue expose only real supported choices | S7; 7.1/7.2/7.4/7.V/7.R4; open. One edition across MSBuild, both Dockerfiles, Compose, environment/configuration inputs, generated catalogue and public/internal guidance; operator-breaking migration evidence accompanies removal. |
| IVSD-M005 | Accurate Apache-2.0 package/role/obligations record and license validation | S7; pilot admission portion complete (E12/E13). Remaining 7.2/7.3/7.V/7.R4: final restored/published closure, retained notices and generated-output disposition. Admission records RMG012/020/037/038 defaults as warnings promoted to errors in `.editorconfig`, not pre-existing error defaults; `RequiredMappingStrategy.Both` is explicit. |
| IVSD-M006 | All three shapes wrapped; original authority and zero-payload telemetry retained | S2/S3/S6; 3.1/3.3/3.5 and all 4/5 cutovers; open. Gate at protected foundation exit before any native product request. Authorization outside timing is the accepted observability change, not unchanged pipeline performance semantics. |
| IVSD-M007 | Bounded composition metadata plus CI proof of actual scoped construction | S3; 3.1/3.2/3.4/3.5, 6.3, 7.R1; open. CI Architecture/Integration (`NativeOperationHostCompositionTests`) deeply resolves all closed handlers. Normal/Testing/standalone runtime preflight checks metadata/factory descriptors before traffic/workers; OpenAPI remains metadata-only. Native re-entry guards, finally cleanup, alias identity and independent scope/disposal need evidence. Zero-allocation/<10ms is unmeasured; no readiness guarantee follows from plan text. |
| IVSD-M008 | Manual validators remain; native requests stay discoverable by compiled assurance | S2/S5; 3.5, 4/5 cutovers, 6.2; open. No validation decorator or DI validator policy change. |
| IVSD-M009 | Existing transactions, durable outbox and authority-first erasure remain | S4/S5; 4.2/4.3, 5.1–5.4 and commerce/erasure/worker rows, 7.R1–7.R3; open. Preserve sequential fail-first notifications, post-commit non-cancellation, cancellation/exception propagation, tenant fences and anti-resurrection ownership; no blanket transaction/retry wrapper. |
| IVSD-M010 | Explicit dependencies remain understandable and transport-compatible | S1/S2; 4.3, 5.2–5.4, 7.R4; open. Capability splits retain routes, verbs, operationIds, explicit `[Tags]`, authorization and HAL; targeted `[FromServices]` remains permitted for heavy fan-in, not a dependency bag or service locator. |

M003/M006/M009 apply to HTTP controllers, nested handlers, MCP proposal/confirmation callbacks/resources, HAL assemblers, middleware, jobs, singleton-hosted workers using established scopes, identity lifecycle delivery, outbox dispatch and Jetstream consumers, including existing service-port aliases. Preserve exact provider-account identity without email fallback, tenant/privacy filtering, public/capability/worker authority, durable replay/idempotency and erasure fences. The active ledger's special-consumer table and nested-call anchors are mandatory closure, not optional cleanup.

Rejected alternatives remain the Steward's rejected mediator facade, third-party mediator, Scrutor, two images, indefinite frozen libraries and blanket textbook decorators. No new moral or design approval is needed. Actual scoped construction in CI plus a native-operation re-entry guard replaces an unprovable promise to inspect arbitrary factory bodies; full graph construction is not a mandatory production boot step.

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

This revalidation ran no dotnet command, host, test, restore or benchmark. It read the pilot commit footprint, dependency record, task-owned context and existing focused/Application/Architecture/license logs (E12/E13). Pilot evidence is reusable only for unchanged inputs; it does not establish native CQS, all mappings, real database visibility, final vendor removal or single-edition correctness.

The recorded pilot completed two one-way scalar projections, both consumers and exhausted-profile deletion. The read logs show 18/18 focused tests, 2,179/2,179 Application tests, 591 Architecture passes with one existing response-metadata skip, and dependency policy passing 475 package/version pairs with six visible exceptions and no Mapperly exception. Context/dependency record report the compiling detail/list Red cases and restored Green, whole-solution Release success, locked restore, clean C# diagnostics and independent privacy/provenance review. Those latter results are inherited execution records, not newly executed checks; the locked-restore log is empty and supplies no independent exit-status proof. Existing baseline warnings and the Architecture skip are not represented as clean universal assurance.

The pilot contains no reverse/existing-target/nested-graph map and no nested mutable DTO collection. Its outer-list order/independence and handler-delegation evidence must not become a claim about PaginatedResult defensive copying or real persistence/discoverability enforcement. Remaining family privacy/serialization, forged-input and aggregate-encapsulation evidence stays open.

The graph is stale/under-indexed and unavailable in current execution context. Slice-owned source inventories and compiled coverage remain required; recorded candidate/handler counts are not exhaustive runtime proof. Full native composition/authority, normal/Testing/standalone startup ordering, zero-payload telemetry, notification, worker-scope, transaction/outbox and five-provider evidence remain open. The architecture and full scope are already decided.

The source-free Mapperly admission and independent pilot review now exist. Package metadata supports the declared license, not a legal opinion. The record explicitly does not certify generated output as license-free; final artifact/notice obligations remain distribution-owned. No allocation, throughput, AOT, trimming or boot-latency improvement has been demonstrated, and no new benchmark deliverable is imposed.

## Escalation Needed

No religious-legal escalation is required for this planning task. If package terms or scanner evidence reject an intended distribution, dependency adoption blocks until the Project Steward obtains a compatible disposition. Optional written legal certainty is not invented as a new approval hurdle when the decision already authorizes adopting Mapperly.

## Evidence Reviewed

E01–E10 retain the previous assessment's evidence identities; except the consultation re-read and E14's targeted source check, their source/web claims are inherited, not a fresh broad audit or new external research. E11–E14 identify this revalidation's bounded reads. Where old summaries conflict, full-migration-r4 scope authority and the newer pinned admission record govern.

| ID | Locator | What it supports |
|---|---|---|
| E01 | [Steward consultation](i-vsd-automapper-mapperly-and-mediatr-replacement-consultation.md), 2026-09-11 | Accepted replacement decisions, F001–F009 and M001–M010. |
| E02 | QUICK_REFERENCE; GOVERNANCE; IP_GOVERNANCE; CI_CD_GOVERNANCE; RELEASE_POLICY; DOCUMENTATION_ARCHITECTURE | Repository authority, dependency/configuration and documentation duties. |
| E03 | ApplicationServicesRegistration, AuthorizationBehavior, PerformanceBehavior, Profiles, SettingUpsertService, InstanceSmtpSettingService | Actual current composition, mapping and post-commit behavior. |
| E04 | CurrentUserResolutionExtensions; large controller families; MCP projected factory; outbox/identity/Jetstream consumers; current authorization architecture tests | Trust/caller surface beyond ordinary controllers. |
| E05 | Directory.Packages.props, Directory.Build.props, API/Blazor Dockerfiles, docker-compose, canonical environment catalogue/metadata, API configuration mapping | Real edition plumbing targeted for removal. |
| E06 | [Mapperly publisher package](https://www.nuget.org/packages/Riok.Mapperly/), [mapping configuration](https://mapperly.riok.app/docs/configuration/mapper), [analyzer diagnostics](https://mapperly.riok.app/docs/configuration/analyzer-diagnostics), [v4 diagnostic changes](https://mapperly.riok.app/docs/breaking-changes/4-0); historical planning-r1/review-r2 external research, not repeated here | Selected 4.3.1/Apache-2.0; `RequiredMappingStrategy` defaults to `Both`; reference handling/deep cloning opt-in. Correction from pinned admission E12: RMG012/020/037/038 are warning defaults, explicitly promoted to errors by this migration; previous "strict defaults" wording did not establish error defaults. No present latest-version claim. |
| E09 | `dev/active/mapping-and-cqs-migration` triad revision `review-r2` and `dev/backlog/mapping-and-native-cqs-rollout.md` (Graduation Decision, boundaries 1-6, "I-VSD Ownership After Split") | Ten-path pilot ownership; every non-pilot mitigation assigned to a backlog boundary; Senior CTO refinements (three handler shapes, shared evaluator, re-entry guard, scoped preflight, capability splits). |
| E10 | `dev/active/mapping-and-cqs-migration` triad revision `review-r3` | Four refined architectural decisions: CI deep DI validation + bounded 0-allocation boot preflight (M007), domain aggregate encapsulation (M001), OpenAPI tag protection + targeted `[FromServices]` (M010), leaf-first DAG rollout (M003). |
| E07 | [Microsoft DI overview](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview), [ValidateOnBuild](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.serviceprovideroptions.validateonbuild); Context7/Tavily 2026-09-11 | Scope/construction contract and validation limitations. |
| E08 | dev/active/mapping-and-cqs-migration plan/context/tasks revision planning-r1 and 1,744-path candidate inventory | Scenarios, exact ownership protocol, 124 feature cohorts, verification and release/graduation tasks. |
| E11 | [Grand ledger](../dev/active/mapping-and-cqs-migration/mapping-and-cqs-migration-grand-tasks.md) Execution Authority/Revised Delivery Boundaries/complete inventory; [plan](../dev/active/mapping-and-cqs-migration/mapping-and-cqs-migration-plan.md) Full Migration Authority/S1–S7/§5; [context](../dev/active/mapping-and-cqs-migration/mapping-and-cqs-migration-context.md) Full Migration Reactivation/Pilot Verification Evidence; full-migration-r4, 2026-09-11 | Explicit whole-program authorization supersedes historical split restrictions; all mapping families, 124 cohorts, special consumers, vendor/edition/docs closure and final assurance active. Design/task traceability, not completion. |
| E12 | [Mapperly admission](../docs/internal/legal/dependencies/mapperly.md); `git show --stat --oneline dee432983` | Ten-path committed pilot footprint; recorded pinned package/lock hash, generator role, notice/output limitations, diagnostic warning-to-error policy, independent review and bounded pilot verification. No native or edition completion. |
| E13 | Existing `/home/amir/.cache/agent-tmp/subscription-{final-focused,application-exit,architecture-exit,license-policy}.log`; context Pilot Verification Evidence; `subscription-locked-restore.log` (empty) | Read test/policy summaries substantiate the bounded counts above. Red/build/restore/diagnostic/review outcomes rely on task-owned records; commands were not rerun. No provider or live-host evidence inferred. |
| E14 | `src/Explore.Application/ApplicationServicesRegistration.cs:143–144`, targeted source read | Existing PerformanceBehavior registration precedes AuthorizationBehavior; E11 deliberately reverses that nesting for native operations. No authorization implementation re-audit claimed. |

## Missing Evidence

Remaining execution obligations are the open rows in the active mitigation table: all remaining mapping privacy/collection/inbound/cyclic vectors; every native shape, caller and scoped authority; CI deep construction plus bounded boot/worker ordering; notification/transaction/outbox/erasure invariants; final restored and published zero-vendor graph; MSBuild/Docker/environment/generated-output and public/internal documentation parity; governed release evidence; touched-layer suites and PostgreSQL/SQLite/SQL Server/MariaDB/MySQL assurance. No such whole-program completion is established by the pilot. Stakeholder or deployed operational validation was not reviewed. Do not close findings from plan text or claim unmeasured benchmark/boot improvements.

## Context Inventory

Reused the shared evidence packet and re-read the consultation, complete active grand ledger/plan/context, current report, I-VSD skill and integration/report/scope/evidence/architecture resources. Bounded additional reads were the pilot admission, commit footprint, existing summary logs and old behavior registration order. No broad source scan, third-party implementation ingestion or external research was performed. The active grand ledger owns execution, plan owns architecture and context owns resume evidence; this report alone owns this revalidation's provider-responsibility traceability. The candidate inventory remains planning memory, not an assurance fixture.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
|---|---|---|---|---|
| 2026-09-11 | none | draft | Apply accepted consultation to concrete migration workstream | E01–E08 |
| 2026-09-11 | draft | current | Revalidate completed triad: every F001–F009/M001–M010 maps to a scenario/task; independent technical findings resolved; no new moral or architecture decision left open | E08; native factory guard, atomic caller rewrite and explicit inbound-construction refinements |
| 2026-09-11 | current | stale | Senior CTO review-r2 split the program into a ten-path pilot plus graduated backlog; mitigation task ownership changed | E09 |
| 2026-09-11 | stale | current | Planning-mode revalidation against review-r2: every mitigation re-owned by the pilot ledger or a numbered backlog boundary; Mapperly diagnostic/strategy facts independently re-verified; two technical refinements added (hardest mapping family second, measured startup preflight delta). No finding closed; no new moral decision opened | E06 (re-verified), E09 |
| 2026-09-11 | current | current | Planning-mode revalidation against review-r3: deep DI resolution shifted to CI with bounded 0-allocation boot preflight (M007); domain aggregate encapsulation invariant enforced against direct entity mutation (M001); controller splits protect OpenAPI client tags with targeted `[FromServices]` (M010); leaf-first DAG sequencing for 124 cohorts (M003) | E10 |
| 2026-09-11 | current | stale | User corrected the backlog split and explicitly activated the entire grand migration; mitigation ownership and completed pilot evidence changed | E11–E13 |
| 2026-09-11 | stale | current | Revalidated full-migration-r4: all F001–F009/M001–M010 retained and bound to active ledger rows; only proven pilot portions of M001/M005 complete; all findings remain open. Corrected diagnostic-default and unmeasured boot/performance claims; no design decision reopened | E11–E14 |

## Planning Handoff

- Workstream: mapping-and-cqs-migration
- Status: current / plan-aligned; explicitly aligned with the authorized full-migration-r4 design, not a full-product assurance pass.
- Reviewed input: active grand ledger, plan and context full-migration-r4 overrides; committed pilot `dee432983931a3b15b32d088461a427f72fa3b51` and bounded E12/E13 evidence.
- Findings and mitigations: all consultation IDs retained (F001–F009 -> M001–M010); every finding remains open. Recommendations binds each mitigation to S1–S7 and literal grand-ledger task IDs.
- Required mappings: all eleven mapping families, native protected foundation, all 124 cohorts/special consumers, notifications, vendor removal and single-edition/docs cleanup are active. Only subscription projection/package-admission portions of M001/M005 are complete. Historical split ownership/promoted-packet language in the triad is superseded, not a new approval boundary.
- Escalations before implementation/release: only an actual dependency/provenance rejection or newly discovered material authority/architecture conflict; no redundant approval of the selected architecture or full scope, and no invented legal certification gate.
- Refresh triggers: material scope/authority or transaction/notification changes, new package/distribution evidence, unsafe mapping boundary, changed edition contract or material plan rewrite. Revalidate against final implementation evidence before whole-program closure; routine execution-slice ownership within approved scope requires no renewed scope approval.
