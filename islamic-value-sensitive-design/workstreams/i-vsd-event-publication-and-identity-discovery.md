# I-VSD: Event Publication And Identity Discovery

Last Updated: 2026-10-02

## Review Metadata

- Mode: planning
- Subject: Accountable publication capacity and one discoverable card per event
- Workstream: event-publication-and-identity-discovery
- Report kind: planning assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-10-02
- Reviewed input: full worktree triad `publication-discovery-r3`, preserved publication follow-up and explicit user implementation authorization; exact document hashes in E07/E09. Historical source packet remains bound to its original revisions, not freshly executed evidence.
- Supersedes: none; the consultation remains a separate advisory input

### Revalidation Decision - 2026-10-02

The r3 split is plan-aligned: occurrence-correct discovery, unique home allocation, reversible identity correction and bounded traversal can be implemented without enabling publication restrictions. The full plan, context, tasks and [publication follow-up](../../dev/backlog/event-publication-trust-and-capacity.md) were reviewed. All seven stable finding/mitigation pairs remain open and are partitioned below; neither the split nor alignment resolves them.

**No remaining I-VSD decision blocks authorized active-discovery implementation.** The user has explicitly authorized implementation in this worktree and subsequently said "continue", without answering the nonblocking delivery-scope question. One PR containing the four active discovery phases with independently reviewable phase commits is the execution owner's recommended default, not an explicit user instruction. This packaging differs from the triad's four-PR wording without changing provider-responsibility scope. Publication accounting, editorial review, capacity requests and administration remain deferred.

At the full-read cutoff, the triad retained October 1 `stale` / `Awaiting approval` metadata and the backlog retained its stale-review statement. This dated report and the explicit implementation authorization supersede those statements for this revalidation; they are not evidence of absent implementation permission. The execution owner reports that triad user-approval metadata is now updated and I-VSD status reconciliation follows; those edits remain outside this report-only review. No operator staffing, policy deployment, legal acceptance, scholarly judgment or release approval follows from the user's implementation authorization.

## Executive Summary

This assessment evaluates how ISLAMU Event balances accessible, welcoming event publication for grassroots organizers against the need to protect attendees from misleading listings, duplicated event cards, and platform abuse. In an open community platform, attendees can be misled if the same event appears under multiple titles or if a search for Saturday in London returns an event that is actually happening in Paris on Sunday. Conversely, volunteer organizers and informal halaqat can be excluded if publishing requires corporate identity verification, commercial paid tiers, or invasive personal dossiers.

The recommended architecture establishes fair publisher capacity through explainable, finite quotas and transparent review pathways rather than opaque algorithmic scoring. On the technical side, publication is governed by tenant-qualified capacity admission, session-first matching, atomic ledger updates, and transactional outbox dispatch. Discovery searches capture a stable snapshot of event identifiers to prevent pagination jumps, while access controls and event details are freshly verified upon display. When reviewers identify duplicate events, they create reversible discovery aliases; the platform strictly forbids automatically transferring attendee registrations, ticket payments, or host ownership.

**Historical Status & Claim Boundary**: This assessment preserves its recorded status as **stale** with disposition **changes-required** under the 2026-10-01 CTO rewrite refresh gate. It records the evaluation of planning revision `publication-discovery-r2`. The subsequent `r3` split separated discovery into active workstreams while deferring publication trust and quotas to the backlog. Making this document human-readable clarifies the moral and technical reasoning of the recorded review; it does not represent fresh approval or production operational readiness.

## Scope

Revalidate the [publication consultation](../consultations/i-vsd-event-publication-and-identity-discovery-consultation.md) findings against the completed r3 design and execution ledger, reusing the planner's evidence packet rather than treating earlier source observations as current runtime proof.

- **Active:** S01-S11, S33-S36, S45, discovery portions of S48-S53, and supporting discovery accessibility, audit and cleanup obligations in S38-S40. Active tasks are 1.1-4.3 and exit gates 4.R/4.M/4.A/4.O.
- **Deferred:** S12-S32, S37, publication portions of S38-S40, S41-S44, S46-S47, capacity/continuity-review portions of S48, publication-review/grant portions of S49/S51, and S54-S56. Original tasks 5-9 remain in the follow-up, including 6.2a editorial review.
- **Not authorized by discovery:** quota enforcement, new capacity administration, publication-evidence retention activation, moving attendee/financial obligations, or claiming public-posting abuse has been solved. Follow-up graduation requires its own executable triad and authorization under the backlog contract.

## Claim Boundary

This is provider-responsibility design reasoning, not religious certification, legal advice, technical-readiness certification or operational acceptance. Implementation permission comes from the user, not this assessment. `Current / plan-aligned` means the named design addresses the findings through tasks or explicit gates; it does not mean implemented, empirically validated or safe to activate.

No product build, test, browser journey or deployed configuration was executed for this report. The execution owner reported an untouched Domain baseline of seven passing tests, exit 0; that supplied result is not independent verification here and does not prove any proposed discovery invariant. Numeric quotas and traversal budgets remain proposed engineering/pilot bounds.

## Findings

IDs intentionally preserve the consultation's finding/mitigation correspondence.

| Finding | Lifecycle, severity, claim | Principles, domains, stakeholders and controlled decision | Evidence and mitigation | Owner, validation and escalation |
|---|---|---|---|---|
| IVSD-F001 | Open; high; implementation gap and design concern | Justice, truthfulness, trust; technical/design; attendees and publishers; discovery identity allocation | Historical E03-E04; current design E07: IVSD-M001 retains occurrence-correct canonical allocation, eligible representations, explicit alias correction and bounded stable continuation. Home must adopt canonical keys after aliases; summed source totals cannot represent unique inventory. | Discovery maintainer; active 1.1-4.3 and 4.R/4.M/4.A prove overlaps, echoes/tombstones, changing rank and reversal. Contested real-world identity needs authorized human review, not title matching. |
| IVSD-F002 | Open; high; policy validation risk | Justice and rights of people; governance/design; new personal publishers, informal groups and organizations; eligibility and promotion | E01/E05 and current E07/E09: IVSD-M002 retains finite explainable tiers and equal access to reviewed increases without age-only trust. Discovery attribution must not imply organizer ownership or endorsement. | Product steward; policy/grants remain deferred to 5.1-5.3,6.2,7.2,8.1. Immediate starter access and seven-day eligibility for manual promotion are planner defaults, not validated fairness or deployed operator policy. No quota-policy decision blocks active discovery. |
| IVSD-F003 | Open; high; implementation gap | Trust and non-harm; technical/operations; publishers and operators; public-exposure accounting | Historical E02/E06/E08; current E07/E09: IVSD-M003 retains shared tenant-qualified admission, atomic state/accounting/outbox, bound operation replay, exact-content approval and coverage of every exposure transition. | Application/persistence maintainers; deferred 5.1-6.3, including 6.2a, and 9.M/9.A. Active aliases do not change occupancy or introduce admission. Discovery tests cannot close accounting, safety or review findings. |
| IVSD-F004 | Open; high; operational uncertainty | Promise-keeping and justice; operations/governance; applicants, affected publishers and reviewers; correction/review obligations | E01/E05 and current E07/E09: IVSD-M004 preserves durable reasons, correction/reversal routes, finite grants, urgent handling and appeal without unsupported deadlines. Active 3.3/4.O owns safe alias outcomes and recovery; the full review queue stays deferred. | Instance operator; demonstrate discovery correction/revocation handling before offering that service, and publication primary/fallback, urgent and appeal coverage before quota-pilot activation. Authorization to code supplies none of this evidence. |
| IVSD-F005 | Open; high; privacy and ownership risk | Rights of people, avoiding spying, non-harm; design/technical; hosts, contributors and attendees; disclosure, evidence access and correction | E01; current E07/E09: IVSD-M005 requires authorization before candidates, fresh scoped authority over both alias records, expected revisions, eligible-member projection and a consistent current-disclosure boundary for payloads and metadata. Original participation actions never retarget. | Privacy/moderation owners; active 1.1-4.3 and 4.M/4.A must prove S03/S35/S45/S49-S52. Disclosure-at-dispatch, mistaken-moderation remedy and publication retention remain deferred 6.3,9.1-9.3. Purpose/legal acceptance precedes operational retention, not code authoring. |
| IVSD-F006 | Open; medium; design concern | Excellence and truthfulness; design/evaluation; volunteer organizers and attendees; truthful sessions and usable recovery | E01; current E07/E09: IVSD-M006 preserves same-occurrence region/date matching, honest ongoing/end-unspecified cards, safe attribution and accessible alias/restart/partial-state guidance. Session-first authoring and editorial-review UX remain follow-up. | Product/design maintainer; active 1.1-2.2,3.3,4.3/4.A, deferred 6.2a,8.1-8.3,9.2. Test and observe changed surfaces; organizer comprehension and accessibility are not established by this design review. |
| IVSD-F007 | Open; medium; governance risk | Justice, trust, avoiding spying; strategy/governance/operations; small communities and operators; visibility, bounded resources and evidence | E01; current E07/E09: IVSD-M007 retains no purchased ranking/endorsement, no fingerprinting, finite versioned policy and purpose-limited evidence. Active snapshots add atomic reservations, physical storage/scan caps, immediate expiry, same-slice purge and value-free diagnostics. | Discovery maintainer/operator owns 4.1-4.3/4.O; product steward owns deferred 5.3,7.2,9.1-9.3. Measure resource limits, restart burden, concentration and denials without fabricated success percentages or accepted retention purposes. |

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

Proceed with the authorized four discovery phases, preserving the phase-owned Red invariants and final runtime gates. One disclosed occurrence must satisfy the whole search. Canonical identity deduplicates allocation, not ownership. Expected-revision alias decisions recheck both records under the same authority fence; safe reasons and reversal preserve the original attendee routes and commitments.

S52 extends the privacy obligation beyond traversal: initial/shared search, home, ordinary list/detail and cached/conditional responses must respect the same current authorized read boundary. Disclosure reduction committed before that boundary must remove item, venue, count, truncation, source and next-link evidence. Previously authorized responses cannot be retracted. Inventory disclosure writers and compatible lock order before enabling snapshot reuse; eventual cache eviction is not the authority.

Keep bounded snapshots in active phase 4: 1000 identities, at most 15 minutes or an earlier eligibility boundary, 200 atomically reserved concurrent snapshots per tenant, 400,000 physical membership rows including unpurged rows, and a shared 10,000-row/32-seek scan ceiling. These are finite starting limits, not proven safe capacity. Purge and key-ring/replica recovery ship with traversal; immediate logical expiry cannot depend on Quartz running. Show partial, truncated, unavailable and restart states honestly without silently appending a new traversal.

Preserve the original publication design as deferred responsibility. The planner-selected immediate starter access, 1/2/3 rolling limits, two active events and seven-day manual-promotion eligibility are neither activated nor empirically accepted by this refresh. Requests/appeals, all exposure paths, durable replay, safety corrections, approved/proposed revisions, disclosure rechecks at dispatch and independent mistaken-moderation remedies must graduate together with their owning follow-up slices before coherent enforcement. Active OpenEnded eligibility does not implement occupancy or the deferred 30-day continuity-review reminder.

Rejected alternatives: retaining stale status solely for missing future runtime/operational evidence; treating implementation approval as staffing/legal acceptance; enabling quotas in discovery; automatic title-based merging; browser-only deduplication; offset or mutable-key traversal that cannot prevent repeats; unbounded snapshots; stale cache/metadata fallback; redirecting attendee obligations through an alias. Still rejected for publication: HTTP throttling alone, unlimited verified organizations, cache-authoritative admission, paid ranking, permanent identity dossiers and compatibility aliases for replaced contracts.

## Stakeholders

Attendees, new and established personal organizers, informal groups, organizations and delegated staff, community contributors, private-home hosts, non-user venue occupants, moderators, operators, self-hosting adopters, and external publishers. Their interests differ: permission to submit does not imply organizer ownership, and larger capacity does not imply greater relevance.

## I-VSD Principles And Domains

Justice governs fair capacity and correction. Trust governs authoritative accounting and reviewer authority. Truthfulness governs counts, status, matching sessions and promises. Rights of people and non-harm govern privacy, attendee continuity and reversibility. Avoiding spying limits evidence and telemetry. Excellence and promise-keeping govern usable workflows and sustainable review.

All six domains apply: strategy, design, technical implementation, operations, governance and evaluation. Religious adjudication is not part of numerical quota policy.

## Common Overlooked Failures And Outcomes

- Different sessions jointly satisfy region/date filters, or an open-ended session silently disappears: active S02/S48, tasks 1.1-1.3.
- Unique pages still repeat across changing ranks or home allocation ignores new aliases: active S05/S07/S36, tasks 2.1-2.2,3.2,4.1-4.3.
- A private primary lends fields or registration actions to a public alias: active S35/S50, tasks 3.1-3.3.
- A revoked reviewer or implicit instance privilege changes another tenant's identity: active S49, tasks 3.1-3.3.
- A cache/304 or shared snapshot reveals revoked membership through counts or links: active S45/S52, the growing disclosure-race cohort in 1.1,2.1,3.1,4.1.
- A 1000-identity cap hides unlimited duplicate scanning or unpurged storage: active S53, tasks 4.1-4.3/4.O.
- A grant badge suggests endorsement or contributor attribution claims organizer ownership: active S51 in 1.3/3.3; grant controls remain deferred.
- A session/visibility mutation bypasses admission, an employee is debited instead of the publisher, or a child group resets capacity: deferred S12/S16/S23, tasks 5.1-6.3.
- A queued venue notice ignores later disclosure restrictions, or a mistaken moderation decision cannot restore an admitted event: deferred S54/S56, tasks 6.3,9.1,9.3.

These are foreseeable failure cases with named acceptance work, not newly reproduced runtime defects.

## Validation Gaps

Active implementation still owes same-occurrence/OpenEnded/DST evidence, canonical allocation and echo/tombstone tests, concurrent alias conflict/reversal and fresh reviewer authority, and two-tenant disclosure races across cached reads and snapshot metadata. Finite creation/scan/storage limits, expiry while purge stops, key-ring outage and replica reuse need actual results. The active ledger assigns these to 1.1-4.3 and 4.R/4.M/4.A/4.O, including five-engine and generated-client evidence. This report supplies none of those passes.

The publication follow-up separately owes last-slot contention, transaction rollback/replay, every exposure transition, requests/appeals, safety and editorial continuity, dispatch-time disclosure and purpose-limited evidence maintenance. Discovery verification cannot substitute for these. Neither suite proves staffing, comprehension, appropriate pilot thresholds or legal retention acceptance.

## Escalation Needed

### Discovery Technical Runtime Evidence - 2026-10-03

The active discovery implementation now has executed native authority14 and
writer3 witnesses on PostgreSQL, SQLite, SQL Server, MariaDB and MySQL, with no
skipped provider. These exercise current disclosure/identity revision, alias
invalidation, independent connection exclusion, capacity reservation, physical
versus logical expiry, tenant isolation and source-before-epoch ordering.
Generated identity/traversal migrations passed up/down/up on disposable
databases and all four catalogs had clean model checks.

Additional PostgreSQL evidence covers cold lookup bootstrap, stale empty/
nonempty tenant catalogs, and bounded orphan retention under a distinct
non-owner, non-bypass runtime after the production migration/RLS bootstrap.
Real HTTP PNG/strong ETag/eligible304 and post-commit privacy concealment passed.
The execution ledger contains exact counts, commands and the unrelated
architecture failures reproduced on an untouched baseline.

Independent review subsequently identified future-clock retention enumeration
and two native lock-order defects. The restricted PostgreSQL witness now rejects
future-cutoff access to live-only foreign ownership and live deletion. Source
writer races passed seven cases each on PostgreSQL, SQL Server, MariaDB and
MySQL; three actual ownership/grant-planning races also passed on each
retaining-read engine, without skips. Browser inspection found and repaired a
bundle-schema namespace regression and stale language-dependent controls.
Existing bundle tests10 and affected list tests54 passed, including a subscribed
locale-change regression; the real mobile RTL list then rendered translated
search/results labels, membership summary, terminal state and batch selector.
This is bounded surface evidence, not completion of persona or pilot evaluation.

This is technical runtime evidence only. It does not close open findings,
establish staff availability or response deadlines, validate representative
pilot thresholds, prove legal retention acceptance, or substitute for
desktop/mobile/RTL, keyboard and task-based attendee/organizer evaluation.
Those human/surface gates remain open. Deferred publication trust, capacity,
moderation and appeal work remains separately scoped and unactivated.

- **Before active implementation:** none from this I-VSD revalidation. Scope and implementation authorization are supplied; missing runtime evidence is work to perform, not a reason to block authoring it.
- **Before discovery release/activation:** execute the active security/privacy/provider/surface gates; validate finite limits with representative workload; record correction/revocation recovery, applicable retention purposes and an accountable operating arrangement. Do not offer a staffed service or response deadline without evidence.
- **Before publication execution:** graduate the backlog into separately approved executable slices with fresh source binding and exact trust-refinement manifests. Identify/reuse moderation reconsideration or define its bounded event-scoped mechanism; S56 is not proof one exists.
- **Before publication activation:** the operator/product steward must record effective policy, primary responder and fallback/absence procedure, urgent handling and appeal limitations; the privacy owner and appropriate legal authority must accept retention/erasure purposes and durations. Organizer task-based evaluation and complete coherent-enforcement evidence remain required.
- Qualified Sunni scholarly authority is needed only for a contested religious-legal moderation determination. No such determination is requested or supplied, and none is necessary to begin discovery implementation.

## Evidence Reviewed

| ID | Source | Scope and level |
|---|---|---|
| E01 | [Publication consultation](../consultations/i-vsd-event-publication-and-identity-discovery-consultation.md) | Retained advisory input from the original assessment; stable F001-F007/M001-M007 correspondence, not new runtime evidence |
| E02 | [EventPublicationExecutor](../../src/Explore.Application/Features/Events/EventPublicationExecutor.cs), `ExecuteAsync` | Historical source packet: serializable lifecycle/readiness/approval plus outbox; not freshly executed or reread here |
| E03 | [GetHomeDiscoveryQueryHandler](../../src/Explore.Application/Features/PublicExperience/Handlers/Queries/GetHomeDiscoveryQueryHandler.cs), `QueryAsync` | Historical source packet: independent section allocation |
| E04 | [GetPublicEventDiscoveryRequestHandler](../../src/Explore.Application/Features/Federation/Atproto/Handlers/Queries/GetPublicEventDiscoveryRequestHandler.cs), `QueryAsync` | Historical source packet: bounded source merge, identity grouping, offset slicing and summed total |
| E05 | [Launch consultation](../consultations/i-vsd-v0-1-launch-consultancy-report.md), scope and its IVSD-F006 | Advisory open-posting and coverage boundaries; an administrator's identity does not establish actual staffing |
| E06 | [CreateEventCommandHandler](../../src/Explore.Application/Features/Events/Handlers/Commands/CreateEventCommandHandler.cs):278-358 | Historical direct inspection of separate create/publish transaction and side effects |
| E07 | [Implementation plan](../../dev/active/event-publication-and-identity-discovery/event-publication-and-identity-discovery-plan.md), [tasks](../../dev/active/event-publication-and-identity-discovery/event-publication-and-identity-discovery-tasks.md), [context](../../dev/active/event-publication-and-identity-discovery/event-publication-and-identity-discovery-context.md) | All three read in full on October 2; revision `publication-discovery-r3`, including S01-S56, D04-D07, Section 9 partition, action matrix and active task/exit gates. Design validation only. |
| E08 | [Event schedule summary](../../src/Explore.Domain/Event.cs):251-281; [temporal query](../../src/Explore.Persistence/Database/ProviderPrimitives/EventDirectoryTemporalQuery.cs):24-49; [create command](../../src/Explore.Application/Features/Events/Requests/Commands/CreateEventCommand.cs) | Retained r2 source observations, not new verification: open-ended mismatch and missing durable create operation ID; active/deferred obligations now separated |
| E09 | [Publication follow-up](../../dev/backlog/event-publication-trust-and-capacity.md) | Read in full from root read-only source; exact task-owned copy in this worktree. Retains original tasks 5-9 and mandatory S49/S51/S54-S56 graduation refinements; not executable authorization. |
| E10 | User implementation authorization and execution-owner handoff, October 2 | User authorized implementation in this worktree and later said "continue" without selecting delivery scope. One PR with four phase commits is the execution owner's recommended default after the unanswered nonblocking question, not explicit user direction. Domain baseline seven tests/exit 0 reported by execution owner, not independently run here. No staffing/legal acceptance supplied. |

Exact reviewed document binding (SHA-256):

| Artifact | SHA-256 |
|---|---|
| E07 plan | `6d1e17269345cb50c5a27bb5c7816b0c60999538ab351cbbe9ef6693fbc8982d` |
| E07 tasks | `af8d4c416abba82bbf0458880a353bb80ec73d90639327e82a0732bab060d8b5` |
| E07 context | `93c00fa6db3cc8ffc3d8496a509b2ac11ddfd5489430f325cc5375cc260c1a65` |
| E09 root source / exact worktree copy | `25c5756d732701d0d2f677de6b6230ce10bf85e23c018f55321794af15a98d8d` |

The worktree HEAD observed at intake was `09203ff9dca889b9d90d93560373a2c1f748ef19`. The inherited source packet was reviewed at `59eafea41d7487409059f6bfe8562ca8e87dd17d`, with selected seams rechecked by the CTO at `22909436883ea90f3e1477685ce3020b230c41da`. No claim is made that all source observations were refreshed at the worktree HEAD. This revalidation rests on the fully reviewed design/task evidence and preserves source/runtime limitations.

External research belongs to the implementation plan's source register as source-free functional constraints; it cannot establish numeric quotas or operational staffing.

## Missing Evidence

Actual reviewer availability and exercised fallback; workload/abuse measurements and representative traversal plans; organizer/attendee comprehension and accessibility observation; deployed settings; active and deferred runtime results; accepted retention purposes/durations and legal review where required. User implementation authorization is no longer missing, but it is not acceptance of these operational facts. Historical home-workstream failures remain unverified on this revision and are not current failures.

## Context Inventory

Review confined to `/home/amir/ISLAMU/Github/Event/.worktrees/event-publication-and-identity-discovery`; root AGENTS/local instructions and the missing backlog were permitted read-only inputs. The worktree already existed. Root instructions, worktree local overrides, I-VSD scope/integration/report contracts and relevant documentation constraints were consulted. Graph tools were unavailable in the exposed catalog; no fresh product investigation is claimed.

Only this report is revised; the authorized exact backlog copy supplies self-contained relative links and remains ignored/uncommitted. No runtime code, triad, root file, other worktree or commit is changed by this revalidation. Execution owns its concurrent work and metadata reconciliation. Existing Home Discovery work remains an integration seam, not a reason to inherit historical blockers or rerun unrelated research.

## Planning Handoff

- Workstream: event-publication-and-identity-discovery
- Status: current
- Reviewed input: full triad `publication-discovery-r3` and preserved publication follow-up at E07/E09 hashes; explicit user instructions E10
- Findings and mitigations: IVSD-F001 -> IVSD-M001 through IVSD-F007 -> IVSD-M007
- Required plan mappings: Section 9 partition revalidated against full scenario/task text; tables below distinguish active implementation from deferred responsibility
- Escalations required before: discovery release/activation and publication graduation/activation as listed above; none remains before authorized active-discovery implementation
- Refresh triggers: changed provider-controlled scope, occurrence/alias authority, attendee obligations, traversal completeness/bounds, disclosure boundary, retention, publication policy, reviewer/correction rights or mitigation/task mappings. Metadata-only reconciliation and PR packaging without behavior changes do not invalidate alignment.

| Finding / mitigation | Active discovery mapping | Deferred publication mapping / gate |
|---|---|---|
| IVSD-F001 / IVSD-M001 | S01-S11,S33-S36: 1.1-4.3; canonical home integration in 3.2; 4.R/4.M/4.A prove results | Accounting effects of aliases remain 6.2; no active budget mutation |
| IVSD-F002 / IVSD-M002 | S51 attribution/non-endorsement only: 1.1,1.3,3.3 | S12-S13,S23,S25-S27,S30: 5.1-5.3,6.2,7.2,8.1; finite equal-access/promotion policy and activation acceptance |
| IVSD-F003 / IVSD-M003 | Not applicable as admission implementation; discovery cannot claim quotas, receipts or editorial authority | S14-S24,S43-S44,S46-S47: 5.1-6.3, including 6.2a; capacity part of S48 and restoration integration also remain follow-up; 9.M/9.A |
| IVSD-F004 / IVSD-M004 | Safe alias decision/correction outcome S33-S36,S49-S50: 3.3/4.O; honest recovery, no staffing promise | S27-S32 and publication S49: 7.1-7.3,8.1-8.2,9.3; actual primary/fallback/urgent/appeal coverage before activation |
| IVSD-F005 / IVSD-M005 | S03-S04,S33-S36,S45,S49-S50,S52: 1.1-1.3,2.1-2.2,3.1-4.3; discovery S39-S40 via 3.3,4.2-4.3/4.O; 4.M/4.A | Publication S40/S49: 7.1-7.2,8.2,9.2; S54: 6.3,9.1; S56: 6.3,9.3; dispatch disclosure, reconsideration and retention approval remain open |
| IVSD-F006 / IVSD-M006 | S01-S03, discovery S38/S48,S50-S51: 1.1-2.2,3.3,4.3/4.A; truthful session, attribution and accessible recovery | S17,S37-S38, occupancy/continuity-review S48,S55: 6.2/6.2a,8.1-8.3,9.2; session-first and editorial UX plus organizer evaluation |
| IVSD-F007 / IVSD-M007 | Discovery S39-S40,S51,S53: 1.3,3.3,4.1-4.3/4.O; no endorsement, bounded storage/scans, immediate expiry/purge and minimal telemetry | S26,S30,S39-S42 and grant S51: 5.3,7.2,8.1-8.3,9.1-9.3; no paid rank/surveillance; policy calibration and evidence-purpose acceptance |

### Active Scenario Coverage And Split Boundaries

| Scenarios | Active task evidence required | Preserved boundary |
|---|---|---|
| S01-S04 | 1.1-1.3; 3.1 for distinct-event/private candidate identity | One disclosed occurrence, not mixed sessions; no automatic title merge |
| S05-S06 | 2.1-2.2; 3.2 canonical-key integration | Honest partial/short sections, no duplicate fallback |
| S07-S11 | 4.1-4.3; 3.1-3.3 also owns S08 source/tombstone authority | Bounded continuation, no exhaustive total or stale metadata |
| S33-S36 | 3.1-3.3; 4.1-4.3 for alias-epoch restart | Original records/actions preserved; occupancy is deferred |
| S45 | 4.1-4.3,4.M/4.A | New shared-search reuse cannot disclose historical membership |
| S48 discovery | 1.1-1.3 | Explicit OpenEnded remains ongoing; active-slot accounting and 30-day review/reminders remain 5.2,6.2,9.2 |
| S49 discovery | 3.1-3.3,4.A | Fresh authority over both records; explicit tenant-scoped instance entry. Capacity cases remain 7.1-7.2,8.2 |
| S50 | 3.1-3.3,4.A | Canonical guidance never retargets registrations/payments or leaks a private primary |
| S51 discovery | 1.1,1.3,3.1,3.3,4.A | Contributor is not organizer; grant/organization status is not endorsement. Capacity UI remains 8.1-8.3 |
| S52 | 1.1-1.3,2.1-2.2,3.1-3.3,4.1-4.3,4.M/4.A | Growing `EventDiscoveryDisclosureRaceTests` cohort covers list/detail/home/cache/304/alias/snapshot boundaries |
| S53 | 4.1-4.3,4.M/4.O | Atomic reservations, scan/physical-row limits and same-slice purge; no unbounded completeness promise |
| Supporting discovery S38-S40 | 1.3,3.3,4.2-4.3,4.A/4.O | Accessible guidance, purpose-limited audit and snapshot cleanup; no claim to implement deferred capacity UI/private request retention |

The October 1 CTO refinements remain technical design evidence; this report removes the stale I-VSD blocker, not any future verification or operational gate. Execution may proceed with task 1.1 under the user's authorization. The backlog copy deliberately preserves its original text, including historical status and whole-program claims; its ownership/activation contract and the split above control deferred applicability.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-30 | none | draft | Planning intake and current source verification; material policy decision remains open | E01-E06 |
| 2026-09-30 | draft | current | User directed best-judgment completion after intake timeout; concrete policy/design selected and all seven finding/mitigation pairs mapped | E07; planning revision `publication-discovery-r1`, plan-aligned |
| 2026-09-30 | current | current | Revalidated material audit corrections for replay, disclosure, safety, review and open-ended eligibility; sequencing/compilation corrections preserve the same mitigations | E07-E08; planning revision `publication-discovery-r2`, plan-aligned |
| 2026-10-01 | current | stale | CTO split discovery from publication delivery and added S49-S56 trust/correction requirements; planning-mode revalidation is required, not inferred | Triad `publication-discovery-r3` and linked publication follow-up; changes-required |
| 2026-10-02 | stale | current | Read the full r3 triad and deferred backlog; revalidated each stable finding against active/deferred tasks, privacy/correction refinements and separate activation gates; explicit implementation authorization supplied | E07/E09 exact hashes and E10; plan-aligned for active discovery and preserved deferred responsibilities, not runtime or operational acceptance |
