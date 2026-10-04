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

## Executive Summary

This assessment evaluates how ISLAMU Event balances accessible, welcoming event publication for grassroots organizers against the need to protect attendees from misleading listings, duplicated event cards, and platform abuse. In an open community platform, attendees can be misled if the same event appears under multiple titles or if a search for Saturday in London returns an event that is actually happening in Paris on Sunday. Conversely, volunteer organizers and informal halaqat can be excluded if publishing requires corporate identity verification, commercial paid tiers, or invasive personal dossiers.

The recommended architecture establishes fair publisher capacity through explainable, finite quotas and transparent review pathways rather than opaque algorithmic scoring. On the technical side, publication is governed by tenant-qualified capacity admission, session-first matching, atomic ledger updates, and transactional outbox dispatch. Discovery searches capture a stable snapshot of event identifiers to prevent pagination jumps, while access controls and event details are freshly verified upon display. When reviewers identify duplicate events, they create reversible discovery aliases; the platform strictly forbids automatically transferring attendee registrations, ticket payments, or host ownership.

**Historical Status & Claim Boundary**: This assessment preserves its recorded status as **stale** with disposition **changes-required** under the 2026-10-01 CTO rewrite refresh gate. It records the evaluation of planning revision `publication-discovery-r2`. The subsequent `r3` split separated discovery into active workstreams while deferring publication trust and quotas to the backlog. Making this document human-readable clarifies the moral and technical reasoning of the recorded review; it does not represent fresh approval or production operational readiness.

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

### Detailed Findings & Technical Mechanisms

#### IVSD-F001: Discovery Identity Allocation and Duplicate Card Clutter
- **Concrete Scenario**: An event featured in multiple homepage categories (e.g., "Trending Community", "This Weekend", and "Near You") can render multiple identical cards on the screen, crowding out other volunteer initiatives. When browsing federated sources or advancing through paginated search results, an attendee may encounter duplicate entries across page boundaries as underlying timestamps update.
- **Provider-Controlled Choice**: The platform operator controls the discovery snapshot mechanism and display deduplication logic. The operator must decide whether deduplication destroys independent publisher records or operates strictly at the presentation layer.
- **Technical Mechanism**: The discovery engine assigns a single authoritative identity to each event before card allocation. Paged browsing captures a server-held query snapshot and returns continuation tokens (fenced with ASP.NET Core Data Protection) rather than relying on unstable offset pagination. When semantic duplicates are detected across publishers, reviewers link them via a reversible alias mapping rather than destructively merging event records.
- **Ethical Reasoning**: Guided by *Adl* (fairness in visibility) and *Sidq* (truthfulness in discovery). Attendees must receive an accurate picture of available community gatherings, and no single publisher should monopolize visual space through multiple uncoordinated submissions.
- **Mitigation & Limits**: Implemented via `IVSD-M001`. Does not automatically infer real-world organizational mergers, which remain subject to explicit human confirmation.

#### IVSD-F002: Explainable Publisher Tiers vs. Age-Only Trust Scores
- **Concrete Scenario**: A newly formed local student halaqah or volunteer charity seeks to announce their first gathering, while a dormant account registered months ago attempts bulk event submission. If the platform bases posting privileges strictly on account age or social graph density, legitimate grassroots initiatives are silenced while abandoned accounts can be weaponized.
- **Provider-Controlled Choice**: Determining publication eligibility, starter allowances, and the criteria for capacity increases.
- **Technical Mechanism**: The system implements explicit, transparent publisher tiers (e.g., a starter tier permitting one active published event with finite capacity) and an equal, documented administrative path to request increased allowance. Opaque algorithmic reputation scores, age-only trust heuristics, and pay-to-publish schemes are strictly prohibited.
- **Ethical Reasoning**: Guided by *Adl* and *Huquq al-Ibad* (rights of individuals). Community members must be treated equitably regardless of corporate backing or historical platform tenure.
- **Mitigation & Limits**: Implemented via `IVSD-M002`. Starter quotas remain pilot hypotheses and require operator review before finalization.

#### IVSD-F003: Public-Exposure Accounting, Atomic Ledger & Outbox Dispatch
- **Concrete Scenario**: An organizer publishes an event at the exact millisecond their monthly quota reaches its limit, or an event is published simultaneously via "Publish on Create" and an explicit "Publish Event" action. If capacity checks and status updates execute across separate database transactions, a race condition can double-allocate capacity, or publish an event while failing to update the tenant's accounting ledger.
- **Provider-Controlled Choice**: The transaction boundary governing event lifecycle transitions and capacity deduction.
- **Technical Mechanism**: The publication pipeline enforces tenant-qualified admission within a single serializable database transaction that atomically mutates event status, decrements available capacity in the tenant ledger, and enqueues the publication event into the transactional outbox (`EventPublicationExecutor.cs`). Replay protection using idempotency keys prevents duplicated quota deductions during network retries.
- **Ethical Reasoning**: Rooted in *Amanah* (faithful stewardship) and *La Darar* (prevention of harm). Inconsistent data states betray user trust and create administrative chaos.
- **Mitigation & Limits**: Implemented via `IVSD-M003`. High-concurrency race barriers must be validated under PostgreSQL and SQLite contention tests before production deployment.

#### IVSD-F004: Review Obligations, Operational Staffing & Appeal Routes
- **Concrete Scenario**: An applicant submits a request for increased publishing allowance before an urgent Ramadan charity drive. The application indicates a "24-hour response time," but the volunteer operator team is unstaffed, stranding the request without recourse.
- **Provider-Controlled Choice**: What commitments, service levels, and escalation mechanisms are presented in the user interface.
- **Technical Mechanism**: In-app request workflows record structured applicant justifications and durable timestamps. The system prohibits advertising fixed turnaround deadlines unless administrative staffing is verified. When an application is rejected or quota is withheld, the operator must record a reasoned explanation, and the applicant receives an explicit in-app appeal affordance.
- **Ethical Reasoning**: Grounded in *Wafa bi al-Ahd* (honoring covenants and promises). Making promises without the organizational capacity to fulfill them violates Islamic contractual ethics.
- **Mitigation & Limits**: Implemented via `IVSD-M004`. The software coordinates the process, but operational availability depends on the deploying organization.

#### IVSD-F005: Privacy, Occurrence Disclosure & Reversible Aliases
- **Concrete Scenario**: Two distinct event submissions for the same conference are identified by a moderator as duplicates. If the system automatically merges their attendee lists, registrations, and payment entries, private attendee data is exposed to unauthorized co-organizers, and refund accounting is hopelessly corrupted.
- **Provider-Controlled Choice**: How duplicate resolution operates across security, privacy, and financial boundaries.
- **Technical Mechanism**: Deduplication operates strictly as a discovery-layer alias (`EventDiscoveryAlias`). The underlying event aggregates, registration records, ticket balances, and host permissions remain completely isolated. If a reviewer mistakenly marks two events as duplicates, the alias is reversed instantly without data loss. Private venue addresses (such as private residential halaqat) remain shielded until attendee registration is confirmed.
- **Ethical Reasoning**: Guided by *La Darar wa la Dirar* (neither harming nor reciprocating harm), prohibition of *Tajassus* (spying/unauthorized exposure), and preservation of property (*Hifz al-Mal*).
- **Mitigation & Limits**: Implemented via `IVSD-M005`. Reversal must be tested against active cache layers.

#### IVSD-F006: Session-First Workflow & Regional Relevance
- **Concrete Scenario**: An educational seminar is held in London on Saturday and Birmingham on Sunday. A user searching for "London events this weekend" is shown a card advertising "Birmingham Session" because the search engine matched the London location from Saturday and the Birmingham date from Sunday across the multi-session parent event.
- **Provider-Controlled Choice**: The indexing and querying model for multi-session and touring events.
- **Technical Mechanism**: Discovery operates on a session-first indexing model where date, time, and geographic proximity filters must match the exact same session instance (`EventDirectoryTemporalQuery.cs`). The rendered event card displays the specific matching session details rather than an arbitrary first session of the parent aggregate.
- **Ethical Reasoning**: *Sidq* (truthfulness) and *Ihsan* (craft excellence). Misleading attendees regarding time or venue causes lost travel time and frustration.
- **Mitigation & Limits**: Implemented via `IVSD-M006`. Validated via unit and projection tests.

#### IVSD-F007: Prohibiting Purchased Ranking & Limiting Telemetry
- **Concrete Scenario**: A commercial entity offers to pay the platform operator for "top search placement" or "sponsored recommendations," or an analytics plugin tracks attendee browsing histories across events to construct marketing profiles.
- **Provider-Controlled Choice**: Monetization models, search ranking algorithms, and user telemetry policies.
- **Technical Mechanism**: Search result ordering is strictly deterministic (relevance, proximity, chronological date) and completely decoupled from publisher financial contributions or platform donations. Telemetry is constrained to operational aggregate counts without recording attendee browsing histories, IP fingerprinting, or cross-event profiling.
- **Ethical Reasoning**: *Adl* (justice) and protection against *Ghabn* (deceptive exploitation). Sacred gatherings and community learning must never be commodified into pay-to-win advertising arenas.
- **Mitigation & Limits**: Implemented via `IVSD-M007`. Versioned operator configuration locks ensure self-hosted instances adhere to transparent defaults.

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
