# I-VSD Planning Assessment: Pre-Release Contract Foundations

Last Updated: 2026-10-03

## Review Metadata

- Mode: implementation evidence review
- Subject: Event identity/profile and storage foundations before optional Asset/Identity integration
- Workstream: pre-release-contract-foundations
- Report kind: implementation-planning-assessment
- Report status: current
- Disposition: architecture-aligned; final acceptance pending
- Report revision: 2026-10-03-r4
- Evidence cutoff: 2026-10-03
- Reviewed input: committed runtime `80e8070b86ef42b09ce597b8b15e7f2cfe06fce7` and profile recovery `70a1719131bea7f8eb1d66267c8c0220de1d483e`, inspected native acceptance receipts and graduation documents. Corrected owning-case gates reconcile final API/Persistence coverage; actual Development browser acceptance is not inferred from those tests.
- Supersedes: none; consumes the pre-release consultancy and the user's accepted correlation-policy correction

## Scope

Plan clean pre-v0.1 changes to Event's durable identity, profile, file reference and lifecycle contracts. Preserve the standalone path without building the future ISLAMU Asset or ISLAMU Identity services now.

The user explicitly accepts narrowly trusted verified-email auto-matching. That correction supersedes the consultancy's broad removal recommendation: existing issuer+subject/DID binding wins; only explicitly trusted authorities can correlate a verified account identity address to one eligible account; ambiguous/conflicting evidence requires explicit linking; Local-owned accounts retain protection; shared contact addresses never become uniqueness/correlation keys. A later selected Identity service owns correlation and Event consumes its binding.

The user also accepted preserving safe first-time signup under existing admission policy when no safe account match exists, including email-free ATProto enrollment. This never permits guessing an existing account or bypassing verified-identity uniqueness. After the profile question timed out, the user instructed continuation using best judgment. The plan selects profile-edit preservation, exact deployment-owned issuer trust, stable-binding/non-adopting email-conflict handling, shared-reference file protection and fail-closed unbound-file handling. These are explicit planner decisions, not fabricated user answers.

## Claim Boundary

This is provider-responsibility reasoning and current-source traceability, not security certification, a legal opinion, a fatwa or release approval. Scoped native execution now exists; its exact limits are recorded below. The original findings retain their planning rationale, not a claim that their September source descriptions remain current.

## Implementation Revalidation

All nine finding/mitigation pairs were reconciled with source and executed
scoped evidence. The durable [identity decision](../../docs/internal/adr/ADR-036-verified-identity-correlation-authority.md)
and [managed-file decision](../../docs/internal/adr/ADR-037-managed-file-reference-and-retirement-authority.md)
record the implemented contracts; task-local receipts remain working memory.
The gate counts and remaining limits below separate execution from source coverage.

| Pair | Implemented mitigation and scoped evidence | Remaining validation |
|---|---|---|
| F001/M001 | Exact binding precedes trusted-issuer canonical claims; Local-owned adoption remains denied. Shared-contact histories and proof transitions pass native assertions. Normalized claim lookup converges the actual PostgreSQL race with one graph and two active proofs; synchronization cohort 47/47, exit 0. | Real recovery/sign-in and final suites remain open. |
| F002/M002 | Provider names initialize only new profiles. Later edits survive synchronization; free-address updates supersede only the corresponding binding proof and preserve independent proof. | Final edit/relogin and current-recipient browser boundary. |
| F003/M003 | Immutable captured provider bindings/versions precede writes; UUID delivery and explicit external provenance remain distinct. Real generic/CSV/federation provider cohorts and five-engine migration/runtime gates executed. | Remaining API acceptance, release receipt reconciliation and real target/recovery browser flows. |
| F004/M004 | All twelve mapped owner relations use physical-reference authority; shared CAS covers tracked, bulk and cascade changes. Native retirement transfers custody to the existing leased worker. Shared/held files survive conversion, reconciliation and moderation. | Final cross-engine concurrency, tenant HAL desktop/mobile actions and stale-link behavior. |
| F005/M005 | Retained external-identity fences and source-less producer acknowledgements prevent resurrection. Real late-completion/privacy/storage cohorts and final Persistence coverage executed. | Browser startup requires the approved retained key authority and currently fails closed. No replacement key or bypass was introduced. |
| F006/M006 | Standalone embedded identity and actual Local/S3 implementations remain selected; no remote adapter, fake health or fallback was added. Standalone 81/81 passed. ADRs and actionable integration/linking backlogs exist. | Actual Development browser readiness; remote activation remains separately scoped. |
| F007/M007 | OpenAPI/tag clients were regenerated natively; internal/public storage semantics agree on pending202 and bounded409. Obsolete direct deletion documentation removed. | Final release receipts, generated checks, current-candidate tests and reviewed commits. |
| F008/M008 | Separate discovery task ownership confirmed read-only in its actual worktree; durable consultation retained. No quotas/editorial policy adopted here. | Final preserved event/session lifecycle evidence. |
| F009/M009 | SQLite profile/media change re-reads preserved purchaser contact, distinct guest participant, assignment, admission credential and purpose/subject/form/version-pinned consent. Organizer/user shared contacts retain separate history. | Final inherited order/consent/admission suites; this does not redesign commerce or force guest accounts. |

The architecture remains aligned with the accepted ownership, standalone and
shared-file safeguards. No finding is closed by source inspection alone.
The final Release build passed 63 projects with 2,079 reported warnings; it is
not a warning-free result. Architecture passed 679/679, Application 3,590/3,590,
and all ten non-matrix Persistence cohorts are reconciled with the reserved
provider cases. Corrected owning-class gates resolve earlier fixture failures;
interrupted or failed full commands remain recorded as such. All 4,197 API
cases are reconciled through exhaustive cohorts and corrected owning gates;
the final combined-host owner passed 2/2 without bypassing startup replay.
Infrastructure's 2,012 non-runtime cases passed and its isolated MinIO runtime
case passed separately with generated test credentials. The documented recursive vault API
inspected 147 secret metadata entries without requesting values and found no
retained key/ID; neither the shared Development store nor current environment
contains that pair. No replacement key or startup bypass was introduced.
Stakeholder usability and operational browser acceptance, final release receipts
and PR review remain outstanding. Future Asset/Identity activation is not
implied by the graduation backlogs.

## Findings

IDs preserve correspondence with the foundational consultancy, but statuses and mitigations apply to this workstream.

### IVSD-F001: Trusted linking must preserve account ownership and legitimate enrollment

- Lifecycle: open; severity: critical security decision; claim type: current-code gap and accepted policy.
- Principles/domains: Amanah, Rights of People, Non-Harm; technical, design, governance.
- Stakeholders/control: account holders, new users, Local-account owners; issuer trust, correlation, verified-address uniqueness and first enrollment.
- Evidence: `SyncUserCommandHandler` gates matching by provider kind, while `UserPii` has a non-unique email index. Existing HTTP tests allow new externally authenticated accounts with missing/unverified email claims and an email-free ATProto graph.
- Validation: implementation traceability; not operational evidence.
- Mitigation **IVSD-M001**: implement the accepted binding-first, explicit-trust, verified-identity-address policy without equating shared contacts with identity. Preserve otherwise permitted safe separate signup; ambiguity/conflicts still require explicit linking. Enforce concurrency/uniqueness transactionally and preserve Local-owned exclusion.
- Acceptance boundary: an existing test currently creates a separate external account using an already verified Local-owned email. Once that address is a verified identity claim, the new uniqueness rule makes it a conflict, not safe unmatched signup. The implementation must update that stale expectation without weakening Local ownership or merging accounts.
- Owner/next validation: identity/security owner for adversarial tests and persisted uniqueness; signup policy is resolved by the user.
- Escalation: none blocking planning; exact deployment-owned trust and changed-email conflict outcomes are selected in plan D1-D3. No scholarly ruling is needed for these technical choices.

### IVSD-F002: Profile mastering and credential verification need separate ownership

- Lifecycle: open; severity: high; claim type: concrete dual-writer behavior with a selected correction.
- Principles/domains: Amanah, Sidq, Rights of People; technical/governance.
- Stakeholders/control: profile owners, administrators; incoming claims versus user edits and identity-email changes.
- Evidence: sync writes names/email/verification and Actor display; profile commands write local names; Local credential lifecycle has its own binding/receipt authority.
- Validation: implementation traceability.
- Mitigation **IVSD-M002**: preserve user-edited profile fields after creation. Keep exact binding resolution during email conflicts, invalidate obsolete proof from that binding, retain independent valid evidence and suppress unsupported email delivery/correlation. A later Identity owner must not race Event's independent writer or grant Event roles.
- Owner/next validation: identity/application owner; PR01-07 prove edit preservation, evidence transitions and erasure safety.
- Escalation: no blocking policy choice; implementation still requires the specified security review.

### IVSD-F003: All persistent file paths need stable identity and pinned storage targets

- Lifecycle: open; severity: high; claim type: verified generic/resource implementation difference.
- Principles/domains: Amanah, portability, Promise-Keeping; technical/operations.
- Stakeholders/control: uploaders, attendees, operators; logical reference, target and content version.
- Evidence: stable `StorageObject.Id` exists; generic paths use provider labels, while resource paths capture `StorageProviderBinding` and disposal authority. Mixed URI semantics and global Actor images remain relevant.
- Validation: implementation traceability.
- Mitigation **IVSD-M003**: use stable Event object IDs, capture exact binding before I/O, separate source provenance from derived delivery URLs, and preserve governed resource access. Refuse guessed handling of unbound development data; use verified mapping/re-upload or explicitly approved disposable rebuild.
- Owner/next validation: storage owner; writer inventory plus changed-target and delayed-delete invariants.
- Escalation: development-data treatment must be explicit; no destructive action follows from this report.

### IVSD-F004: Shared attachment removal must not destroy another legitimate use

- Lifecycle: open; severity: high; claim type: verified generic deletion gap and material lifecycle choice.
- Principles/domains: Rights of People, Non-Harm; technical/governance.
- Stakeholders/control: file owners, multiple events, evidence subjects; shared use, retention and physical disposal.
- Evidence: generic deletion checks retained evidence but then deletes physical bytes before metadata; resource workflows have fenced retirement/tombstones and generic-route exclusion.
- Validation: implementation traceability.
- Mitigation **IVSD-M004**: retain shared references. Detachment removes the relation; physical deletion is refused while legitimate uses/holds remain. Use a common database fence and durable exact-target disposal, preserving resource authorization and no presign bypass.
- Owner/next validation: storage/privacy owners; DL01-05 and ST07 cover shared use, failure, late producers and generic-route exclusion.
- Escalation: none blocking planning; a newly discovered authority owner requires scope re-baselining rather than silent omission.

### IVSD-F005: New correlation data must participate in existing erasure and scoped consent

- Lifecycle: open; severity: high; claim type: required extension to existing privacy invariants.
- Principles/domains: Rights of People, Avoiding Spying; technical/operations/governance.
- Stakeholders/control: users, guests, independent participants; correlation claims, retention, erasure and replay.
- Evidence: retained-authority workflow and PII inventory already own local deletion/fencing; consent is purpose/subject/version scoped.
- Validation: implementation traceability.
- Mitigation **IVSD-M005**: explicitly include new identity-claim/profile data in existing erasure and replay; do not add global consent or destructive cross-product cascade semantics. Preserve shared live files and retained evidence.
- Owner/next validation: privacy owner; PII inventory, race and restore-replay scenarios.
- Escalation: legal holds/retention require applicable policy; no new legal determination here.

### IVSD-F006: Prepare optional-service boundaries without implementing speculative integrations

- Lifecycle: open; severity: high; claim type: accepted future responsibility boundary.
- Principles/domains: Promise-Keeping, portability; strategic/technical/operations.
- Stakeholders/control: self-hosters and future product maintainers; independent authentication/profile/storage choices.
- Evidence: repository already separates Local/Keycloak/ATProto authentication and supports standalone; user assigns future correlation to Identity.
- Validation: design reasoning and current-source traceability.
- Mitigation **IVSD-M006**: retain a real embedded implementation and narrow application seams. Actual remote adapters, capability negotiation, migration tooling and activation are separately scoped backlog work, with explicit no-silent-fallback and preserved-ID requirements.
- Owner/next validation: architect; standalone invariants and clear backlog acceptance.
- Escalation: no unknown remote API contract may be invented to make the current plan appear complete.

### IVSD-F007: Public contract changes should be deliberate and generated with their owners

- Lifecycle: open; severity: high; claim type: implementation contract obligation.
- Principles/domains: Sidq, Promise-Keeping; technical/governance.
- Stakeholders/control: clients and operators; DTOs, HAL, configuration, generated clients and change disclosure.
- Evidence: source consultancy identifies storage DTO/provider/URI exposure; repository owns OpenAPI/NSwag generation and governed release fragments.
- Validation: implementation traceability.
- Mitigation **IVSD-M007**: replace incorrect pre-release contracts cleanly, regenerate from source, document public/internal parity and stage generated outputs with the smallest owning behavior. No aliases/shims or handwritten migrations/clients.
- Owner/next validation: API/storage/identity owners; machine-consumed schema and runtime contract tests.
- Escalation: none beyond the explicit user approval of breaking development changes.

### IVSD-F008: Avoid duplicating the active event-publication and discovery workstream

- Lifecycle: accepted; severity: medium; claim type: ownership/sequence constraint.
- Principles/domains: Ihsan, Adl; strategic/evaluation.
- Stakeholders/control: organizers and maintainers; event identity, discovery and scope.
- Evidence: the [publication and identity discovery consultation](../consultations/i-vsd-event-publication-and-identity-discovery-consultation.md) records the separate scope. Its task triad currently lives in `.worktrees/event-publication-and-identity-discovery/dev/active/event-publication-and-identity-discovery/`, not this worktree's `dev/active/`.
- Validation: read-only inspection of the separate worktree's context on 2026-10-03 confirms active discovery implementation; publication quotas, admission and editorial review remain separately deferred. This is ownership evidence, not runtime acceptance of those policies.
- Mitigation **IVSD-M008**: explicitly map event/discovery work to that separate workstream; preserve event/session/time/provenance invariants here. Do not silently absorb its pending publication-policy decision.
- Owner/next validation: project steward and planners; final plan scope/mapping review.
- Escalation: dependency only if shared contracts materially change.

### IVSD-F009: Historical transactions remain local facts, not editable profile projections

- Lifecycle: accepted; severity: high invariant importance; claim type: preservation requirement.
- Principles/domains: Amanah, Rights of People; technical/governance.
- Stakeholders/control: buyers, participants and organizers; order/consent/admission history.
- Evidence: existing consultancy traces immutable order/consent snapshots and distinct purchaser/participant/credential identities.
- Validation: source-grounded inherited invariant; not fresh execution evidence.
- Mitigation **IVSD-M009**: do not rewrite orders, consent or admission ownership during profile/file refactoring; no requirement that guests become Identity accounts. Trace representative regression scenarios without redesigning commerce.
- Owner/next validation: domain/privacy owners; final preserved-behavior mapping.
- Escalation: broader commerce changes are outside this task.

## Recommendations

Execute the decision-complete triad only after its review/implementation approval. Keep the accepted trusted-email and safe-signup policy; do not re-open answered choices or treat the timeout-selected profile policy as a direct user answer.

Rejected alternatives: blanket email matching, blanket removal of auto-matching, general contact-email uniqueness, two simultaneous correlation owners, provider-label deletion after a target changes, invented remote service contracts, and compatibility shims in greenfield development.

## Stakeholders

Account holders and first-time users; Local and external identity owners; organizers sharing contact addresses; attendees/guests; owners and subjects of shared files; self-hosters; future Asset/Identity maintainers; administrators responsible for erasure and recovery.

## I-VSD Principles And Domains

Amanah protects stable identity and storage obligations. Rights of People and Non-Harm constrain linking, deletion and access. Avoiding Spying prevents unnecessary cross-context correlation. Promise-Keeping preserves credible optionality and standalone operation. Sidq requires truthful failure/migration states. Ihsan favors focused reusable domain boundaries over speculative infrastructure.

Strategic, design, technical, operational, governance and evaluation responsibilities are all relevant; operational and stakeholder validation remain future evidence.

## Common Overlooked Failures And Outcomes

- Denying legitimate first-time signup because no account could safely be linked can exclude email-free users; the accepted safe-signup policy prohibits this conflation.
- Treating shared organization contact email as a person key can expose another person's history.
- Existing bindings can be stable while incoming profile email still creates a uniqueness collision; identity resolution and profile update need separate outcomes.
- Removing one attachment can delete another event's file unless shared use and disposal are modeled.
- Delayed upload/deletion or erasure replay can act on the wrong provider after configuration changes without pinned authority.
- Preparing a future remote service by introducing a no-op selectable provider would misrepresent functionality; only implemented modes may activate.

## Validation Gaps

No new tests/builds or migrations executed. Remote Asset/Identity contracts are unavailable. Complete file-producer and profile-field inventories belong to bounded remaining design work after ownership choices. Current tests were inspected as behavioral records, not claimed green.

## Escalation Needed

No blocking intake decision remains. The user's best-judgment instruction resolves the unanswered profile branch through the documented recommendation and permits the explicit architecture defaults in plan D1-D7. Actual legal-retention disputes, new authority owners, or changes to accepted scope still require the appropriate owner review during implementation.

No religious-legal ruling is requested. Security/privacy specialists review the resulting critical invariants during implementation.

## Evidence Reviewed

- [Source consultancy](../consultations/i-vsd-pre-release-breaking-change-prevention-consultancy-report.md).
- [Verified identity authority decision](../../docs/internal/adr/ADR-036-verified-identity-correlation-authority.md) and [managed-file retirement authority decision](../../docs/internal/adr/ADR-037-managed-file-reference-and-retirement-authority.md), including concrete implementation and test anchors.
- [User synchronization](../../src/Explore.Application/Features/Users/Handlers/Commands/SyncUserCommandHandler.cs).
- [User repository](../../src/Explore.Persistence/Repositories/UserRepository.cs) and [PII mapping](../../src/Explore.Persistence/Configurations/Entities/UserPiiConfiguration.cs).
- [External verification HTTP tests](../../tests/Event.API.IntegrationTests/Features/ExternalProviderEmailVerificationHttpTests.cs) and [Local synchronization tests](../../tests/Event.API.IntegrationTests/Features/LocalIdentitySynchronizationTests.cs).
- [Generic storage deletion](../../src/Explore.Application/Features/StorageObjects/Handlers/Commands/DeleteStorageObjectCommandHandler.cs), [provider binding](../../src/Explore.Domain/StorageProviderBinding.cs), [resource exclusion tests](../../tests/Event.API.IntegrationTests/Features/NativeStorageObjectHttpTests.EventResources.cs).
- [Privacy erasure](../../docs/internal/PRIVACY_ERASURE.md), [release-fragment contract](../../docs/internal/releases/changes/README.md), current planning workflow and matched intent/rule excerpts.
- User's explicit correlation policy in this conversation, 2026-09-30.

## Missing Evidence

Automated native project, exhaustive API/Persistence and five-engine migration/runtime evidence is reconciled in the implementation revalidation. Real browser acceptance and stakeholder validation remain missing because the selected Development authority lacks its retained key pair. Future remote contracts remain out of scope, assigned to the substantive graduation backlogs.

## Context Inventory

Original planning inventory: fresh `develop`, consultancy and two bounded scouts; graph unavailable. Current review adds the task-owned implementation worktree, native scoped runtime/test receipts, explicit owner inventory, separate discovery context and ADR/backlog graduation. No external product research or third-party code ingestion was used.

## Planning Handoff

- Workstream: pre-release-contract-foundations.
- Status: current / architecture-aligned, real-surface acceptance pending; revision `2026-10-03-r4`.
- Reviewed input: HEAD above and shared context evidence.
- Findings/mitigations: IVSD-F001-F009 paired with IVSD-M001-M009.
- Plan: `dev/active/pre-release-contract-foundations/pre-release-contract-foundations-plan.md`.
- Tasks: `dev/active/pre-release-contract-foundations/pre-release-contract-foundations-tasks.md`.
- Context: `dev/active/pre-release-contract-foundations/pre-release-contract-foundations-context.md`.
- Escalations required before: implementation approval and any subsequent material scope change, not completion of planning.
- Refresh triggers: trust/defaults, admission, profile authority, shared-file lifecycle, schema/API scope, erasure or optional-service contract changes.

| Finding / mitigation | Plan scenarios | Task mapping / disposition |
| --- | --- | --- |
| IVSD-F001 / IVSD-M001 | ID01-ID09, OP03 | T1.1-T2.5 |
| IVSD-F002 / IVSD-M002 | PR01-07 | T2.1-T3.3 |
| IVSD-F003 / IVSD-M003 | ST01-06 | T4.1-T5.5 |
| IVSD-F004 / IVSD-M004 | ST07, DL01-05 | T6.1-T6.5 |
| IVSD-F005 / IVSD-M005 | PR07, DL05/06 | T2.4, T6.3, T7.2 |
| IVSD-F006 / IVSD-M006 | OP01/02 | T1.2, T7.1/T7.3; remote adapters/activation explicitly deferred |
| IVSD-F007 / IVSD-M007 | OP04 | Owning generation/docs tasks and T7.3 |
| IVSD-F008 / IVSD-M008 | Preserve event identity/time contracts | T7.1/T7.3; implementation owned by existing publication/discovery workstream |
| IVSD-F009 / IVSD-M009 | DL06, ID08 | T3.3/T7.2; preserve snapshots, no commerce rewrite |

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
| --- | --- | --- | --- | --- |
| 2026-09-30 | none | draft | Implementation-plan request and explicit controlled-email-linking decision | Current repository evidence; material intake question pending |
| 2026-09-30 | draft | draft | User accepted safe signup without unsafe account correlation | IVSD-M001 clarified; profile-mastering question now pending |
| 2026-09-30 | draft | current | User instructed best-judgment completion after timeout; architecture defaults selected and full triad mapped | Revision `2026-09-30-r1`; plan-aligned, implementation not started |
| 2026-10-03 | current | current | Native implementation and focused acceptance evidence reviewed across all nine pairs | Revision `2026-10-03-r2`; architecture aligned, final candidate and browser/provider gates remain open |
