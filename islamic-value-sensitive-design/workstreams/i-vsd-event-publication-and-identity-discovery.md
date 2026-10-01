# I-VSD: Event Publication And Identity Discovery

Last Updated: 2026-10-01

## Review Metadata

- Mode: planning
- Subject: Accountable publication capacity and one discoverable card per event
- Workstream: event-publication-and-identity-discovery
- Report kind: planning assessment
- Report status: stale
- Disposition: changes-required
- Evidence cutoff: 2026-09-30
- Reviewed input: consultation, source at `59eafea41d7487409059f6bfe8562ca8e87dd17d`, and implementation triad revision `publication-discovery-r2`
- Supersedes: none; the consultation remains a separate advisory input

### CTO Rewrite Refresh Gate - 2026-10-01

The triad is now `publication-discovery-r3`: four active discovery PRs, with original publication phases 5-9 preserved in [the publication follow-up](../../dev/backlog/event-publication-trust-and-capacity.md). S49-S56 clarify reviewer scope, attendee alias continuity, non-endorsement, cached disclosure races, bounded scans, disclosure at notice dispatch, editorial-review UX and mistaken-moderation remedies. Scope and provider-controlled correction/task mappings changed. The r2 assessment below remains historical evidence, not current approval. Planning-mode revalidation of both deliveries is required before implementation approval; no fresh I-VSD, scholarly/legal or user approval is fabricated.

## Scope

Translate the [publication consultation](../consultations/i-vsd-event-publication-and-identity-discovery-consultation.md) into an executable plan for publisher budgets, capacity review, session-first modeling, identity-safe discovery, and reversible duplicate correction. This assessment reuses the planner's repository investigation rather than treating the consultation's September 24 observations as current implementation proof.

## Claim Boundary

This is provider-responsibility design reasoning, not religious certification, legal advice, implementation approval, or evidence of successful operations. Numeric quotas remain pilot hypotheses. No product build, test, or deployed configuration has been inspected through execution during this planning session.

## Findings

IDs intentionally preserve the consultation's finding/mitigation correspondence.

| Finding | Lifecycle, severity, claim | Principles, domains, stakeholders and controlled decision | Evidence and mitigation | Owner, validation and escalation |
|---|---|---|---|---|
| IVSD-F001 | Open; high; implementation gap and design concern | Justice, truthfulness, trust; technical/design; attendees and publishers; discovery identity allocation | E02-E04: home sections remain independent; merged public counts sum source totals. IVSD-M001: authoritative identity deduplication before allocation and stable continuation, with a separate reviewed alias relationship for semantic duplicates. | Discovery maintainer; prove overlapping sections, source echoes, changing feeds and alias reversal. Contested real-world identity requires human review. |
| IVSD-F002 | Open; high; policy validation risk | Justice and rights of people; governance/design; new personal publishers, informal groups and organizations; eligibility and promotion | E01/E05/E07: actor type and organization approval are not event endorsement. IVSD-M002: finite explainable tiers and an equal route to reviewed increases; no age-only trust score. | Planner selected immediate starter access and manual promotion after seven days for the proposed plan; product steward reviews these hypotheses before implementation approval. |
| IVSD-F003 | Open; high; implementation gap | Trust and non-harm; technical/operations; publishers and operators; public-exposure accounting | E02/E06: explicit publication and publish-on-create are separate transaction paths. IVSD-M003: shared tenant-qualified admission, atomic ledger/occupancy/state/outbox, replay protection, and coverage of every exposure transition. | Application/persistence maintainers; invariant-first tests and real-engine contention at workstream exit; security review before release. |
| IVSD-F004 | Open; high; operational uncertainty | Promise-keeping and justice; operations/governance; applicants and reviewers; review obligations | E01/E05: no verified staffing or response commitments. IVSD-M004: durable in-app requests, reasoned decisions, finite grants, urgent handling and appeal; publish no unsupported deadline. | Instance operator; confirm coverage before enabling immediate-publication pilot or making response promises. |
| IVSD-F005 | Open; high; privacy and ownership risk | Rights of people, avoiding spying, non-harm; design/technical; hosts, contributors and attendees; evidence access and duplicate correction | E01/E04: occurrence disclosure and source authority must survive matching. IVSD-M005: authorize before suggestion, use reversible discovery aliases, never automatically move registrations, payments or organizer ownership. | Privacy/moderation owners; prove inaccessible candidates remain hidden and reversal restores discovery; legal escalation for retention purposes. |
| IVSD-F006 | Open; medium; design concern | Excellence and truthfulness; design/evaluation; volunteer organizers and attendees; session-first workflow and regional relevance | E01/E03: existing event/session structure and home composition should be extended, not rebuilt. IVSD-M006: same-session region/date eligibility, relevant-session cards, accessible session-first actions and data-preserving guidance. | Product/design maintainer; deterministic component/API assertions and organizer evaluation before enforcement. |
| IVSD-F007 | Open; medium; governance risk | Justice, trust, avoiding spying; strategy/governance; small communities and operators; capacity versus visibility | E01: capacity must not buy ranking or endorsement. IVSD-M007: versioned finite operator policy, constrained grants, no fingerprinting or popularity-based trust, minimal retained evidence and bounded telemetry. | Product steward/operator; review concentration and incorrect denials without inventing fairness percentages. |

## Recommendations

Preserve native CQS, entity-first repositories, domain-owned state transitions, server-authored HAL affordances, and the existing transactional outbox. Extend existing discovery and publishing entry points rather than add parallel authorities.

The intake question timed out and the user explicitly directed completion using best judgment. The plan therefore selects one immediate starter event within finite quotas, with configurable stricter operator review; seven days permits manual promotion, never automatic trust. This is a concrete proposed design, not a claimed founder policy answer. Keep personal publication possible without forming an organization. Capacity exhaustion must not block cancellation, correction, attendee-safety communication, or draft saving.

The nine-phase plan also selects bounded server-held discovery membership, current-eligibility rechecks, reversible reviewed aliases, independent reviewer checks and finite resource limits. Request evidence retention is explicitly proposed and requires accepted purpose/legal review before operational activation. A staffed moderation/urgent/appeal arrangement is an activation gate; the plan does not manufacture one.

The technical audit prompted six additional scenarios and seven corrections. Durable operation keys now bind caller/publisher/content and preserve successful replay without reacquiring expired capacity. Disclosure epochs invalidate historical snapshot counts as well as payloads. A safety reschedule can update public detail and attendee notices while renewed listing waits for capacity; a client urgency flag cannot grant an exemption. Material post-approval changes remain proposed revisions, with immediate cancellation/disclosure-reduction/safety overlays. Explicitly open-ended sessions remain ongoing until closed; 30-day review reminders do not invent expiry. Lock-order analysis precedes new fences and Red tests compile fresh before execution. These choices were rechecked against the same findings rather than treated as runtime fixes.

Rejected alternatives: HTTP throttling alone; unlimited verified organizations; automatic title-based merging; browser-only deduplication; cache-based quota admission; paid ranking; permanent identity dossiers; compatibility aliases for replaced contracts.

## Stakeholders

Attendees, new and established personal organizers, informal groups, organizations and delegated staff, community contributors, private-home hosts, non-user venue occupants, moderators, operators, self-hosting adopters, and external publishers. Their interests differ: permission to submit does not imply organizer ownership, and larger capacity does not imply greater relevance.

## I-VSD Principles And Domains

Justice governs fair capacity and correction. Trust governs authoritative accounting and reviewer authority. Truthfulness governs counts, status, matching sessions and promises. Rights of people and non-harm govern privacy, attendee continuity and reversibility. Avoiding spying limits evidence and telemetry. Excellence and promise-keeping govern usable workflows and sustainable review.

All six domains apply: strategy, design, technical implementation, operations, governance and evaluation. Religious adjudication is not part of numerical quota policy.

## Common Overlooked Failures And Outcomes

- A session addition or private-to-public edit creates exposure without visiting the publish action.
- A moderator accidentally spends their own allowance instead of the publisher's.
- Region and date match different sessions, producing a misleading local card.
- Unique cards on each page still repeat across a changing offset traversal.
- Summed source counts claim more unique results than the bounded merge can serve.
- Grant expiry or a policy downgrade silently removes announced events.
- Duplicate suggestions disclose inaccessible events or redirect attendee obligations.
- Another organization member, child group, transfer or restore resets capacity.

Each failure maps to a named scenario and task below. Planning alignment does not resolve the implementation or operational finding.

## Validation Gaps

Current source is not runtime proof. Required implementation evidence includes atomic last-slot contention, transaction rollback and replay; every public-exposure path; tenant/actor negative tests; changing-feed traversal and alias reversal; same-session disclosure; requests, urgent review and appeal; accessible and localized UI; and finite policy/retention operations.

## Escalation Needed

- Before implementation approval: product steward reviews the planner-selected first-publication, finite-tier, resource-limit and retention defaults. There is no remaining unanswered design branch in the plan.
- Before operational activation: operator records review coverage, urgent handling and appeal arrangements; privacy owner records retention and erasure purposes.
- Before release: security/privacy review of accounting, grants, aliases and evidence access.
- Qualified Sunni scholarly authority only if a contested religious-legal moderation question arises; no such ruling is needed for capacity arithmetic.

## Evidence Reviewed

| ID | Source | Scope and level |
|---|---|---|
| E01 | [Publication consultation](../consultations/i-vsd-event-publication-and-identity-discovery-consultation.md) | Requirements, examples and unresolved policy; advisory design evidence |
| E02 | [EventPublicationExecutor](../../src/Explore.Application/Features/Events/EventPublicationExecutor.cs), `ExecuteAsync` | Serializable lifecycle/readiness/approval plus outbox; current source traceability |
| E03 | [GetHomeDiscoveryQueryHandler](../../src/Explore.Application/Features/PublicExperience/Handlers/Queries/GetHomeDiscoveryQueryHandler.cs), `QueryAsync` | Independent section allocation; current source traceability |
| E04 | [GetPublicEventDiscoveryRequestHandler](../../src/Explore.Application/Features/Federation/Atproto/Handlers/Queries/GetPublicEventDiscoveryRequestHandler.cs), `QueryAsync` | Bounded source merge, identity grouping, offset slicing and summed total; current source traceability |
| E05 | [Launch consultation](../consultations/i-vsd-v0-1-launch-consultancy-report.md), scope and IVSD-F006 | Open posting context and unverified operational coverage; advisory |
| E06 | [CreateEventCommandHandler](../../src/Explore.Application/Features/Events/Handlers/Commands/CreateEventCommandHandler.cs):278-358 | Directly inspected separate create/publish transaction and side effects |
| E07 | [Implementation plan](../../dev/active/event-publication-and-identity-discovery/event-publication-and-identity-discovery-plan.md), [tasks](../../dev/active/event-publication-and-identity-discovery/event-publication-and-identity-discovery-tasks.md), [context](../../dev/active/event-publication-and-identity-discovery/event-publication-and-identity-discovery-context.md) | Local planning revision `publication-discovery-r2`; Sections 3, 5 and 9 plus tasks supply design validation, not runtime evidence |
| E08 | [Event schedule summary](../../src/Explore.Domain/Event.cs):251-281; [temporal query](../../src/Explore.Persistence/Database/ProviderPrimitives/EventDirectoryTemporalQuery.cs):24-49; [create command](../../src/Explore.Application/Features/Events/Requests/Commands/CreateEventCommand.cs) | Directly verified open-ended summary/query mismatch and missing durable create operation ID; audit corrections in D02/D04 and S43-S48 |

External research belongs to the implementation plan's source register as source-free functional constraints; it cannot establish numeric quotas or operational staffing.

## Missing Evidence

User approval of the proposed policy; actual reviewer availability; workload and abuse measurements; organizer comprehension; deployed settings; runtime verification; approved evidence-retention purposes and durations. Historical home-workstream test failures are unverified on this revision and must not be reported as current failures.

## Context Inventory

Main repository on `develop`, upstream already current on September 30. Graph discovery was attempted twice but no graph tools were exposed; bounded native source reads and read-only scouts are the fallback. Existing Home Discovery work overlaps composition and privacy; its historical blockers are not inherited as verified failures. No branches, worktrees, product edits or commits were created.

## Planning Handoff

- Workstream: event-publication-and-identity-discovery
- Status: stale
- Reviewed input: consultation, current source packet and triad revision `publication-discovery-r2`
- Findings and mitigations: IVSD-F001 -> IVSD-M001 through IVSD-F007 -> IVSD-M007
- Required plan mappings: revalidated against plan Section 9 and the task ledger; see table below
- Escalations required before: planning approval, operational activation and release as listed above
- Refresh triggers: first-publication policy, tier/promotion rules, actor budget scope, identity aliases, pagination guarantees, disclosure, retention, or moderation authority changes

| Finding / mitigation | Scenario coverage | Implementation / escalation |
|---|---|---|
| IVSD-F001 / IVSD-M001 | S01-S11, S33-S36 | Tasks 1.1-4.3; occurrence matching, home allocation, alias authority and snapshot traversal |
| IVSD-F002 / IVSD-M002 | S12-S13, S23, S25-S27, S30 | Tasks 5.1-5.3, 6.2, 7.2, 8.1; finite tiers and non-organizational review route |
| IVSD-F003 / IVSD-M003 | S14-S24, S43-S44, S46-S47 | Tasks 5.1-6.3, including 6.2a; durable request replay, safety/admission separation and reviewed-content authority |
| IVSD-F004 / IVSD-M004 | S27-S32 | Tasks 7.1-8.2, 9.3; actual staffing/urgent/appeal arrangement before activation |
| IVSD-F005 / IVSD-M005 | S03-S04, S33-S36, S40, S45 | Tasks 1.1, 3.1-4.3, 9.2; no private candidates or historical count leakage, reversible correction and retention |
| IVSD-F006 / IVSD-M006 | S01-S03, S17, S37-S38, S48 | Tasks 1.1-2.2, 8.1-8.3, 9.2; truthful ongoing-session semantics and organizer comprehension evidence |
| IVSD-F007 / IVSD-M007 | S26, S30, S39-S42 | Tasks 5.3, 7.2, 9.1-9.3; no purchased rank or surveillance, purpose-limited evidence |

Technical refinements were Applied & Aligned (2026-10-01); the approval verdict is Changes required pending planning-mode I-VSD revalidation for r3. User approval is Awaiting approval. Active discovery graduation occurs in Phase 4; original Phase 9 now belongs to the linked follow-up. All r2 mappings below/above require that partition before fresh alignment.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-30 | none | draft | Planning intake and current source verification; material policy decision remains open | E01-E06 |
| 2026-09-30 | draft | current | User directed best-judgment completion after intake timeout; concrete policy/design selected and all seven finding/mitigation pairs mapped | E07; planning revision `publication-discovery-r1`, plan-aligned |
| 2026-09-30 | current | current | Revalidated material audit corrections for replay, disclosure, safety, review and open-ended eligibility; sequencing/compilation corrections preserve the same mitigations | E07-E08; planning revision `publication-discovery-r2`, plan-aligned |
| 2026-10-01 | current | stale | CTO split discovery from publication delivery and added S49-S56 trust/correction requirements; planning-mode revalidation is required, not inferred | Triad `publication-discovery-r3` and linked publication follow-up; changes-required |
