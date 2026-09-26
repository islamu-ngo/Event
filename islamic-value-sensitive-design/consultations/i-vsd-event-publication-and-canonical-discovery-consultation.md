# I-VSD Consultation: Event Publication Limits And One Card Per Event

Last Updated: 2026-09-24

## Review Metadata

- Mode: standalone
- Subject: Trust-based event publication and canonical event discovery
- Workstream: none
- Report kind: consultation
- Report status: draft
- Disposition: advisory
- Evidence cutoff: 2026-09-24
- Reviewed input: working-tree at `f48880a30cb69a030dfbe41bee3ae8b710de7b3d`; bounded source inspection and the founder's request
- Supersedes: none

## Scope

Recommend how ISLAMU Event can limit abusive publication without excluding legitimate organizers, teach its richer event/session model, and honor the founder's requirement that **the same event must not occupy two event-card spaces on an event list or home page**.

This covers personal, group and organization publishers; organization approval; new accounts; publication quotas; requests for higher limits; duplicate correction; regional discovery; ranking; privacy; moderation operations; and self-hosted policy. It is a design consultation, not implementation approval, a complete security audit, or an implementation plan.

The related launch consultation records an Official Instance with open personal posting, groups and administrator-approved organizations in one tenant. That is relevant working-tree context, not independently verified deployment state. Recommendations also identify where other operators need configuration. [E02]

**Confirmed:** one real event is represented by one canonical event listing, with its sessions underneath it. Splitting sessions or regional appearances merely to occupy more cards contradicts the requested product policy. **Not yet confirmed:** exact quota numbers, staffing, automatic promotion rules, and whether a new personal publisher's first event requires human review. The latter was asked during this consultation; recommendations below remain provisional pending that answer.

## Executive Recommendation

**Use publication budgets, canonical event identity, and fair discovery together. A rate limiter alone cannot deliver one card per event.**

1. Keep a small, useful starter allowance. Account age is a weak risk signal, not proof of honesty, and a seven-day wait should not be the default barrier to every first event.
2. Assign budgets to the accountable publisher, not merely the logged-in employee. Organization verification can justify greater capacity but must not buy immunity, religious endorsement, or extra ranking.
3. Enforce one canonical identity per discovery result and across home sections. Separately help users correct multiple records that describe the same real event.
4. Make the correct action easy: **add a session, date or location to an existing event**. Show how that event will appear in each relevant region.
5. Offer **Request higher limits** in-product, with a reasoned decision, visible status, scoped grants and an appeal. Self-service submission does not mean automatic approval.
6. Keep edits, cancellation, attendee-safety notices and correction available when publication capacity is exhausted.

The platform can guarantee that a known canonical identity is rendered once. It cannot guarantee that two deliberately disguised submissions describe different real-world events. That residual problem needs evidence, organizer cooperation, reporting and accountable moderation.

## Claim Boundary

This is provider-responsibility reasoning under the I-VSD principles, not a fatwa, religious certification, or proof that these controls prevent abuse. Current-code observations support bounded implementation traceability. Proposed thresholds and workflows have design rationale, not stakeholder or operational validation.

The founder's Hetzner example is used only as an analogy for graduated access and requesting more capacity. Hetzner's current account-age rules, port restrictions and approval process were **not verified**. No competitor source or implementation was consulted. Claims about other event platforms are treated as the founder's reported experience, not independently established market facts.

## Current Repository Evidence

| Area | Observed behavior | Consequence |
|---|---|---|
| Event identity | `Event` has `ActorId`, sessions, session groups, days, optional `EventSeriesId`, and session-derived date summaries. `EventSession` has its own schedule and event-location association. [E03] | The fundamental event/session separation exists. A new event-per-session model is unnecessary. A series of separate events is not automatically one event. |
| Attribution | `Event` distinguishes owner `ActorId`, `SubmittedByUserId`, `OrganizerActorId` and provenance. `Actor` supports users, organizations, groups, external subjects and service principals. [E03, E04] | A community contributor reporting somebody else's event is not necessarily its organizer. Charge accountable submission activity without granting organizer authority. |
| Organization authority | `OrganizationTenant` contains approval, organizer eligibility, suspension and legitimacy-evidence fields. [E04] | Approval is scoped participation evidence, not a universal trust score or approval of every event. |
| Publication | `EventPublicationExecutor` validates lifecycle, concurrency, effective approval policy and readiness inside a serializable unit of work, then plans federation and notification outbox work. [E05] | There is an existing authoritative publication boundary to extend. Ordinary and privileged approval must have explicit quota semantics. |
| Alternate publication path | `CreateEventCommandHandler` can publish during creation, with its own transactional checks and outbox work. [E06] | Adding a quota check only to the explicit publish endpoint would leave another path uncovered. |
| Rate limiting | API configuration includes per-user write requests and global/authenticated request throttles. [E07] | These regulate requests, not successful unique event publications or the number of live listings. No actor publication budget was found in the inspected publication paths; this is not an exhaustive absence claim. |
| List discovery | Local pagination starts from Events, not session cards. The public discovery handler groups local/federated items by stable identity and prefers local representation. [E08, E09] | Preserve existing identity deduplication. It does not identify separately created records for the same real event. |
| Home discovery | Home builds hero, upcoming, spotlight, viewed, curated and recently-added sections independently. The reviewed component projects each section separately without a page-wide seen-identity set. [E10] | The reviewed composition does not enforce the requested cross-section uniqueness. This is source evidence, not a reproduced browser incident. |
| Geography and privacy | Public event discovery rejects direct `locationIds`; home uses selected-area context internally. Existing privacy tests cover coarse area serialization, and the spatial consultation requires event/occurrence-specific disclosure. [E09-E12] | Do not assume arbitrary regional filtering is already available or expose hidden venue information to implement it. |

## Findings

### IVSD-F001: Publication frequency and discovery duplication are different problems

- **Lifecycle / severity / claim type:** open / High / design concern with implementation traceability.
- **Principles / domains:** Adl, Sidq, Amanah; strategy, design, technical, evaluation.
- **Stakeholders / controlled decision:** Attendees, small organizers and prolific publishers; what counts as an event and how many cards it receives.
- **Evidence / validation:** Founder requirement; event/session model; event-level local query; stable-identity federation grouping; independent home sections. [E01, E03, E08-E10] Design validation and bounded implementation traceability only.
- **Risk:** A publisher can obey a quota while splitting one workshop into several records. The platform can also repeat a correctly modeled event across home sections.
- **Mitigation:** **IVSD-M001** - Define the event/session/series distinction, enforce page-wide canonical identity, and provide a separate correction process for real-world duplicates.
- **Owner / next validation:** Product steward and discovery maintainer; validate examples with organizers and exercise multi-session, multi-region and overlapping-section cases.
- **Escalation boundary:** Disputed real-world identity requires human review; similarity is not proof of misconduct.

### IVSD-F002: Actor type and age can become unfair substitutes for evidence

- **Lifecycle / severity / claim type:** open / High / design risk.
- **Principles / domains:** Adl, Rights of People, Avoiding Deception; strategy, design, governance.
- **Stakeholders / controlled decision:** New personal organizers, informal groups, mosques without formal documents and verified organizations; access to publication and increases.
- **Evidence / validation:** Proposed age/verification factors; multiple actor types and tenant-scoped organization approval. [E01, E04] Design validation; no observed false-rejection rates.
- **Risk:** Old accounts can be malicious or compromised. New accounts can represent established community work. Formal registration, payment or popularity can unfairly determine who is heard.
- **Mitigation:** **IVSD-M002** - Use explainable capacity tiers, modest age-based eligibility, alternative legitimacy evidence and a non-organizational route to increased capacity.
- **Owner / next validation:** Policy owner; test decisions with small and high-volume organizers, including users unable to supply corporate paperwork.
- **Escalation boundary:** A verification badge must describe what was checked; it must not certify religious correctness.

### IVSD-F003: Incomplete quota enforcement creates bypasses and unreliable decisions

- **Lifecycle / severity / claim type:** open / High / implementation gap in inspected paths and design risk.
- **Principles / domains:** Amanah, Non-Harm, Ihsan; technical, operational.
- **Stakeholders / controlled decision:** Publishers, attendees and operators; authoritative consumption, concurrency, retries and visibility transitions.
- **Evidence / validation:** Separate explicit and create-as-published paths, existing transactions/outbox and request throttles. [E05-E07] Bounded implementation traceability; no quota implementation or concurrency run.
- **Risk:** Concurrent requests exceed the last allowance; retries double-charge; imports, privileged approval, group creation or visibility changes evade the rule.
- **Mitigation:** **IVSD-M003** - Use a shared server-side publication decision and durable atomic accounting across every path that gains public exposure; keep transport throttles separate.
- **Owner / next validation:** Application/persistence maintainers; prove last-slot contention, rollback, replay and all publication entry points.
- **Escalation boundary:** Security review before shipping cross-actor or cross-tenant accounting.

### IVSD-F004: A limit-increase button creates a support obligation

- **Lifecycle / severity / claim type:** open / High / operational design risk.
- **Principles / domains:** Promise-Keeping, Adl, Amanah; design, operations, governance.
- **Stakeholders / controlled decision:** Time-sensitive organizers, reviewers and appellants; review availability, reasons, exceptions and reversals.
- **Evidence / validation:** Requested capacity workflow; launch consultation identifies unverified responder coverage. [E01, E02] Design validation, not operational validation.
- **Risk:** An unanswered queue becomes an arbitrary ban. Personal connections or sponsorship can replace consistent decisions.
- **Mitigation:** **IVSD-M004** - Provide visible request status, scoped grants, reasoned denials, an urgent exception route, logged reviewer authority and a workable appeal process.
- **Owner / next validation:** Instance operator; name a primary responder and fallback, then rehearse submission, approval, denial and appeal.
- **Escalation boundary:** Do not advertise response-time guarantees before staffing supports them.

### IVSD-F005: Duplicate detection can damage privacy, ownership and reputation

- **Lifecycle / severity / claim type:** open / High / design risk.
- **Principles / domains:** Rights of People, Avoiding Spying, Non-Harm, Sidq; design, technical, governance.
- **Stakeholders / controlled decision:** Reported-event contributors, real organizers, private-home hosts and falsely accused users; matching signals, private candidates and merging.
- **Evidence / validation:** Separate submission/organizer attribution; per-occurrence location-disclosure constraints. [E03, E11, E12] Design validation and bounded traceability.
- **Risk:** A warning reveals a private event; two unrelated weekly lessons are merged; registration or payment records change owner; a heuristic publicly labels somebody a spammer.
- **Mitigation:** **IVSD-M005** - Show only authorized candidates, treat fuzzy matches as review evidence, prefer reversible discovery aliasing, and protect underlying registrations and organizer authority.
- **Owner / next validation:** Moderation, privacy and domain owners; exercise contested ownership, inaccessible candidates and events with registrations.
- **Escalation boundary:** Legal/privacy review for evidence retention and identity documents; domain review before moving financial or attendee records.

### IVSD-F006: Teaching is ineffective when the interface rewards duplication

- **Lifecycle / severity / claim type:** open / Medium / design concern.
- **Principles / domains:** Ihsan, Adl, Avoiding Deception; design, technical, evaluation.
- **Stakeholders / controlled decision:** Volunteer organizers and attendees; creation flow, regional relevance, card summaries and freshness ranking.
- **Evidence / validation:** Founder explains session/region splitting; the model supports sessions while current regional surfaces have constraints. [E01, E03, E09-E10] Design validation; organizer usability not reviewed.
- **Risk:** Correctly modeled events look absent from a region or display the wrong session date, so organizers learn to clone them.
- **Mitigation:** **IVSD-M006** - Teach at the point of creation, offer session-first actions and regional previews, show the matching session, and remove edit/repost incentives.
- **Owner / next validation:** Product/design owner; observe organizers adding weekly sessions and a multi-city program without assistance.
- **Escalation boundary:** No automatic punishment for an understandable first modeling mistake.

### IVSD-F007: Publication privilege must not become purchased visibility or surveillance

- **Lifecycle / severity / claim type:** open / Medium / governance risk.
- **Principles / domains:** Adl, Amanah, Avoiding Spying, Promise-Keeping; strategy, governance, operations, evaluation.
- **Stakeholders / controlled decision:** Small communities, donors, self-hosting operators and maintainers; ranking, evidence collection and capacity allocation.
- **Evidence / validation:** Proposed differentiated limits, existing actor boundaries and launch responsibilities. [E01, E02, E04] Design validation only.
- **Risk:** Paying for a larger allowance crowds out smaller organizers; hidden device profiling becomes a condition of participation; official-instance policies are presented as universal religious rules.
- **Mitigation:** **IVSD-M007** - Separate capacity from ranking and endorsement, collect minimal evidence, publish policy versions and allow operator-configured finite limits with non-bypassable integrity boundaries.
- **Owner / next validation:** Product steward and operator; review grants, complaints, concentration and reversals by relevant cohorts.
- **Escalation boundary:** Contested religious moderation goes to qualified Sunni scholarly authority, not a numerical trust score.

## Recommendations

All numbers, new behavior and proposed data concepts below are recommendations, not existing configuration or approved implementation.

### 1. Define one event before limiting its publication

An **Event** is the coherent public offering an attendee recognizes and an accountable organizer manages. A **Session** is an occurrence or part of that offering, with its own time, location and participation details. A **Series** relates genuinely separate events; it must not become a loophole for making one card per session.

Use continuity of purpose, program, organizer responsibility and attendee expectations together. Shared titles, images, ticket URLs or organizations are evidence, not sufficient identity tests.

| Example | Recommended model |
|---|---|
| A three-day conference with twenty talks | One event; sessions, days and agenda items underneath. |
| A weekly course with twelve lessons | One course event with twelve sessions, including session-level participation where supported. |
| The same coherent workshop program delivered in three cities | One event with city-specific sessions/locations; relevant regional views each find it. |
| An in-person event with a livestream | One event with the appropriate physical/digital participation structure. |
| The same event listed by a user and its actual organizer | One discoverable canonical event; preserve provenance and resolve organizer authority. |
| Two independently organized lectures with the same title | Separate events unless evidence establishes they are the same offering. |
| The 2026 and 2027 editions of a conference | Normally separate events, optionally in a series; they are distinct commitments and lifecycles. |
| Two genuinely independent regional programs under one brand | Separate events can be justified by distinct programs, responsibility and attendee commitments; geography alone does not justify duplication. |
| An ongoing weekly community program | A continuing event with maintainable future sessions, not daily clones; provide renewal/archive hygiene rather than forcing one infinite schedule. |

Do not force unrelated activities into an enormous umbrella event merely to evade quotas. Equally, do not force a split because a current UI cannot express independent session capacity or registration. Resolve that modeling limitation or provide a reviewed exception before sanctioning the organizer.

**Geographic relevance is not geographic duplication.** An event with real sessions in Brussels and Antwerp may appear once in a Brussels search and once in a separate Antwerp search. In a combined search or one home-page composition, it appears once. An online event being accessible everywhere does not justify invented physical locations in every region.

### 2. Make the discovery invariant precise

Within one event-list result set, including its paginated continuation, each canonical identity receives at most one card. Within one home response, the same applies across hero, upcoming, spotlight, popular, curated and recently-added sections. Switching to another search or returning later is a new discovery context, not a lifetime prohibition on seeing an event again.

- Filter eligible **sessions/occurrences**, then select their parent events. When a user requests a region and a date, the **same eligible session** must satisfy both. Do not combine a Brussels session next month with an Antwerp session today and falsely match "Brussels today."
- Choose the card's next matching session, not blindly the event's earliest session anywhere. Show a compact summary such as "Brussels, Saturday; 4 other sessions" where disclosure permits.
- Apply public visibility, session publication and event-location disclosure before matching. A hidden address must not leak through distance, area membership, a duplicate suggestion or a session-count summary.
- Deduplicate authoritative identities before page allocation; use deterministic ordering and a stable tie-breaker. Hiding duplicate cards after pagination produces short pages and misleading totals.
- Preserve existing local/federated identity grouping. Extend with reviewed aliases for separately created records only when their relationship is established; title normalization alone is not an identity authority.
- For home, allocate candidates in an explicit section priority, excluding already allocated identities. Refill from eligible candidates or truthfully show fewer sections/cards; never fill empty space with repeats.
- The current bounded federated merge and its counts need explicit acceptance checks. Grouping records is not proof that counts, deep pagination and every source alias satisfy the new contract.
- For changing feeds, prevent repeats during a pagination traversal with a stable snapshot/cursor strategy. An offset query with changing rank can repeat items even when each individual response is unique.

Do not confuse **one card per event** with **one card per organizer**. A large organizer can have genuinely distinct events. If discovery becomes dominated by one publisher, consider a transparent diversity rule separately; quota size must not multiply relevance or produce extra featured slots.

### 3. Use several small controls instead of one opaque trust score

| Control | What it limits | Why it is separate |
|---|---|---|
| Transport throttle | Requests and concurrent expensive operations | Protects service availability, not event quality. |
| Publication budget | Distinct events first gaining public discovery in rolling windows | Limits the arrival rate of new listings. |
| Active listing cap | Currently discoverable events with eligible ongoing/future sessions | Prevents gradual accumulation of a huge live inventory. |
| Reactivation throttle | Repeated withdrawal/republication or visibility cycling | Prevents a single identity from being used for repeated exposure. |
| Session/import/storage budgets | Schedule expansion, draft growth, uploads and bulk ingestion | One event can still contain abusive volume. Sessions should not consume new-event allowance. |
| Distribution budget | Notifications, mail, external publication and invitations | One event can create substantial downstream spam. |
| Discovery policy | Duplicate identity and relevant placement | Preserves the reader's feed independently of write volume. |

Prefer explainable facts: publisher class, time since trusted local enrollment, current approval scope, demonstrated legitimate need, and substantiated moderation outcomes. Do not use unreviewed report counts, follower totals, wealth, religious identity, country or a concealed device fingerprint as automatic reputation.

Account age means a server-controlled timestamp for the relevant local publisher relationship, not a browser claim or an arbitrarily old external identity-provider account. A new staff member acting for an established organization should use its authorized allowance; the staff member's personal age must not reset the organization's legitimate history.

### 4. Pilot concrete, conservative defaults

These are **starting hypotheses**, not evidence-based universal thresholds. Apply all relevant windows and the active cap together. Use rolling UTC durations, not midnight calendar resets.

| Publisher capacity tier | New events / 24 hours | / 7 days | / 30 days | Active discoverable events |
|---|---:|---:|---:|---:|
| New personal publisher or eligible unverified independent group | 1 | 2 | 3 | 2 |
| Established personal publisher or independent group | 2 | 5 | 10 | 5 |
| Newly approved organization | 3 | 10 | 20 | 10 |
| Established approved organization | 5 | 20 | 50 | 25 |
| Reviewed high-volume publisher of any eligible type | Explicit finite grant | Explicit finite grant | Explicit finite grant | Explicit finite grant |

- An ineligible, suspended or pending organization gets **no organization publication authority**. The starter row is not a bypass for required approval. Independently eligible personal publication remains honestly attributed to the person, not falsely to that organization.
- Seven days may make a starter eligible for reassessment or a modest pre-authorized increase. It must not automatically prove good standing, unlock unlimited publication, or dismiss unresolved abuse.
- Treat "established" as policy-defined and inspectable: minimum tenure plus an approved promotion or supported legitimate history. Lack of complaints at very low volume is weak evidence. Provide manual review for an old account with no posting history and for a new account with strong legitimate need.
- Verification checks legitimacy and representative authority; it does not assess every event. Revocation or compromise can reduce future capacity regardless of age.
- Personal publishers must be able to obtain higher limits without forming an organization. A volunteer coordinating several legitimate programs should explain need, not invent a company.
- Do not sum grants accidentally. An active grant explicitly replaces identified limits for its scope; other caps and suspension rules still apply.

For the Official Instance, the recommended first-publication option is one immediate event **only with functioning report handling and prompt moderation**. If that coverage is unavailable, recommend first-event review with a staffed queue rather than pretending immediate publication is controlled. This is a decision still open with the founder; existing tenant approval settings remain authoritative until deliberately changed.

### 5. Account against the right publisher and lifecycle

**Primary scope:** the tenant and accountable publisher actor. Every authorized member of an organization shares its budget. Where branches/groups demonstrably belong to one accountable organization, define a shared parent budget or explicit sub-allocations; creating another group must not automatically mint more capacity. Do not infer shared control from a common IP address.

Separately constrain individual submission bursts and organization/group creation so one user cannot cheaply cycle through fresh actors. Do not charge an employee's organizational work against their tiny personal-publication allowance. A contributor reporting an external event consumes their accountable submission allowance, not an unconsenting organizer's allowance.

Tenant admission and operator-wide abuse containment are separate. The Official Instance's shared tenant still needs actor isolation. Across tenants, enforce any instance-wide containment only within explicit operator authority; do not reveal another tenant's membership, quota usage or private events. A local trust grant is not portable proof of trust on another server.

| Operation | Recommended accounting |
|---|---|
| Save/edit a draft | No publication debit; ordinary storage/request limits apply. |
| First successful entry into public discovery | Consume one unique-publication unit and acquire an active slot atomically. |
| Create directly as published | Exactly the same rules as publishing an existing draft. |
| Human approval that publishes | Check the publisher's capacity, not the moderator's allowance; any exception is explicit and audited. |
| Add or correct a session | No new-event debit; separate session/notification controls apply. |
| Retry the same committed publication | Return its existing outcome without another debit or duplicate delivery intent. |
| Publication transaction fails | No committed debit, active slot or publish outbox work. |
| Withdraw/delete/cancel | Release active occupancy when no longer eligible, but do not refund the rolling publication debit. Cancellation and attendee communication remain available. |
| Republish the same withdrawn event | Recheck eligibility and active capacity, retain prior debit history, and enforce reactivation throttling; do not automatically rank it as a new event. |
| Change private/unlisted to public-discoverable | First public exposure is a publication-budget event even if the record already exists. |
| Replace an old event with an unrelated offering | Require a new event identity and its normal publication checks; preserve the old attendee commitments. Ordinary corrections or a justified reschedule are not automatically new events. |
| Transfer ownership or merge actors | Transfer/check live occupancy with authorized reconciliation; retain historical origin accounting so transfer cannot reset consumption. |
| Moderation merges duplicate identities | Release redundant live occupancy; no automatic rolling refund. Any correction credit is reasoned and recorded. |

Private/unlisted publication still needs anti-abuse and delivery controls; exclusion from discovery is not permission for unlimited public-link or mail spam.

Edits and session additions must not reset an event's original publication age or automatically place it in "recently added." A genuinely useful new session can affect date relevance without creating another card. Material changes after human approval need a proportionate re-review policy, while urgent safety corrections and cancellation remain possible.

Define active occupancy from authoritative lifecycle/session eligibility, not a client-maintained counter. Open-ended sessions need the existing end-time semantics and an explicit stale-listing review policy; do not silently expire a legitimate event on an invented end time. Never punish an organizer for cancelling a dangerous or mistaken event.

### 6. Implement capacity requests as a capability workflow

The Hetzner-like idea is useful as **graduated capability management**, not as a requirement to reproduce another company's policy.

1. Show the current allowance, consumed units, active inventory, next rolling release time and applicable policy before the organizer finishes a long submission.
2. Offer **Add a session to an existing event**, **Save draft**, and **Request higher limits**. A request must not require failed publication first.
3. Ask which publisher needs capacity, requested amount/duration, expected distinct events versus sessions, and a short explanation. Optionally accept a public program link or private minimal supporting evidence.
4. Show `Submitted`, `Needs information`, `Under review`, `Approved`, `Partially approved`, `Declined` or `Withdrawn`. A resulting grant has its own `Active`, `Expired` or `Revoked` lifecycle.
5. Limit duplicate pending requests per publisher and rate-limit submissions. Preserve the draft and receipt if notification delivery fails. Provide in-app status rather than relying exclusively on email.
6. Record reviewer, policy version, reasons, scope, limits, effective time and expiry. The submitter cannot approve their own request. Allow explicit administrator overrides without silently bypassing the ledger.
7. On denial, explain the relevant reason and what evidence or modeling change could alter it. Offer appeal; a second reviewer is preferable, but a sole operator must disclose the actual reconsideration arrangement.
8. On expiry or ordinary downgrade, block new exposure above the limit rather than automatically cancelling already announced events. Keep corrections, exports and cancellation available. Actual harmful content can be restricted under the separate moderation policy.

**Do not hide the button until day seven.** Day seven can enable a modest automated grant under an explicit policy. Before then, allow an urgent, human-reviewed exception for legitimate time-sensitive work. A funeral-related gathering or newly onboarded mosque should not need to wait a week simply to ask.

Use limited-duration increases for seasonal peaks or bulk onboarding where appropriate. Do not force repeated requests for a proven recurring need solely to create friction. Never promise instant approval or a review deadline the operator cannot meet.

### 7. Teach at the point of action

Recommended introductory copy:

> An event is the whole program. Add its dates, sessions and locations here. We show one event card, with the relevant session for each visitor's search.

Recommended duplicate prompt:

> Is this another session of "Community Learning Weekend"? Add the date or location to that event so people can find the complete program.

Recommended limit message:

> You have used your allowance for new events. You can still edit your events, add sessions, save a draft or request a higher limit.

The actual interface should also show exact remaining counts and server-calculated release times; do not display a reset estimate when several independent caps remain binding.

- Start with "New event" versus "Another date/session of an existing event." Search the publisher's authorized upcoming and recent events.
- Put **Add session** and **Add location** prominently beside existing events. Offer multi-date entry and safe schedule import; forcing twelve repetitive forms invites twelve event copies.
- Show a regional preview: one event, the appropriate local session, and other available sessions where allowed.
- Allow direct session links and session-level calendar entries without creating event cards. A calendar occurrence is not a second event listing.
- Explain series and genuinely separate annual editions using concrete examples.
- Preserve entered data if the user changes from creating an event to adding a session. Where safe conversion is not available, provide a guided correction path rather than losing work.
- Make the flow usable on mobile, with keyboard/screen readers and supported languages/RTL. Do not rely on badge color, a hover tooltip or a long policy page.

Education should not suggest that the provider has no responsibility because it "cannot know." It can remove duplication incentives, explain the model, enforce known identities and provide correction when ambiguity remains.

### 8. Detect and correct semantic duplicates proportionately

Distinguish three cases:

1. **Technical duplicate:** the same authoritative event/source identity is returned more than once. Deduplicate deterministically.
2. **Likely modeling mistake:** overlapping schedule, organizer, public source URL, title or program description suggests another session of an existing event. Prompt with an explanation and allow a reasoned "different event" response.
3. **Suspected deliberate evasion:** repeated substantiated duplication after guidance, ownership cycling or false locations. Human moderation can impose targeted publication restrictions with reasons and appeal.

A normalized title is useful for candidate retrieval, not a uniqueness constraint. Common lecture names, translated titles, reused venue images and recurring community programs produce false matches. Start with structured/public evidence and human handling; an opaque AI score is not necessary for launch.

Never reveal a private or cross-tenant candidate to an unauthorized publisher. Security-limited explanations may be necessary, but should offer review instead of implying publicly proven misconduct.

Prefer a **reversible canonical discovery relationship** before destructive merging. Choose the canonical record by verified responsibility and event continuity, not simply the oldest timestamp or highest view count. Preserve source attribution and links. Existing registrations, tickets, refunds, attendance permissions, notifications and audit history require explicit domain-safe reconciliation; do not move them automatically because two cards look similar.

Begin with guidance and assistance for honest mistakes. Remove repeated exposure where warranted, escalate repeated evasion proportionately, and record correction/reversal paths. Unverified user reports must not automatically reduce somebody's capacity; otherwise coordinated reporting becomes an attack.

### 9. Enforce through existing architecture, not browser-only rules

Use the current Clean Architecture boundaries and native command/query contracts. The following are responsibilities, not prescribed new class names:

- **Domain:** deterministic policy evaluation of trusted publisher facts, windows, grant state and lifecycle; no HTTP dependency or hidden client assumptions.
- **Application:** authorize the publisher, resolve authoritative settings and facts, validate publication, perform quota accounting and state change, and return a typed outcome.
- **Persistence:** durable usage/grant records, uniqueness for replay protection, and atomic concurrency across different events sharing the same last slot.
- **API:** translate failures into existing ProblemDetails conventions and expose authorized capabilities and allowance summaries.
- **Blazor:** use HAL links for action affordances, not role/claim guesses. A stale button cannot authorize publication; the server rechecks at commit.

`EventPublicationExecutor` already supplies a serializable transaction and outbox pattern; create-as-published has a separate path. Reuse a common quota rule/accounting authority across those paths rather than assuming one currently calls the other. Inventory approval, imports, automation/MCP, scheduled publication, restoration and public-visibility changes before implementation. External federation ingestion needs its own source admission policy; do not pretend a remote sender is a verified local publisher. [E05, E06, E09]

The atomic outcome must be **authorized transition + quota ledger + active occupancy + durable delivery intent**, or none of them. A preflight "remaining = 1" followed by an independent insert is insufficient. Replays and transaction retries must not double-debit. Publish notifications/federation through the transactional outbox; downstream delivery still needs idempotency.

If the authoritative quota state cannot be checked, fail closed for new exposure and preserve the draft. Keep independently safe corrective actions available. Cache allowance displays for convenience only; never use stale cached counters to authorize publication.

Keep budget exhaustion, approval required, suspension, duplicate review and temporary service failure distinguishable in machine-readable failure codes and user messages. Set retry guidance only when time alone can resolve the condition.

### 10. Constrain governance, privacy and rollback

- Publish the policy and meaningful default tiers. Do not publish exploit-sensitive detection details, but do explain individual access decisions.
- Restrict overrides to the responsible authority. Publisher-controlled settings must not let a group or organization raise its own enforced ceiling.
- Record policy versions and grant history. Give notice for ordinary reductions; emergency abuse controls should be scoped, reviewable and reversible.
- Keep paid infrastructure capacity separate from community trust and feed priority. Donations, sponsorship and verified status must not silently create unlimited visibility.
- Prefer program links, representative confirmation and other minimal evidence to collecting identity documents by default. Do not require email where the deployment supports another accountable identity/contact route.
- Retain detailed evidence only for a defined review/appeal purpose and duration. A 30-day budget requires reliable relevant-window accounting; that does not justify permanent dossiers. Set retention with privacy review, including deletion, backups and restricted abuse evidence.
- Avoid device fingerprinting, cross-site tracking, precise-location profiling and blanket shared-IP guilt. Aggregate operational metrics where possible and avoid event text or personal identifiers in general logs.
- Self-hosting operators may set local policy and finite allowances. They cannot configure away tenant authorization, atomic accounting, truthful UI, or the distinction between local and external identity.

**Rollback:** policy enforcement can return to a previous version or narrower review mode without deleting events or registrations. Retain ledger/grant history so reenabling controls does not reset consumption. Stop only the affected new-publication path during a defect, preserve safe event operations, and reconcile any incorrect charges or hidden cards. Never roll back by blindly restoring erased personal data or undoing legitimate moderation.

### 11. Validate in a small operational pilot

First validate organizer understanding and the authoritative invariants; then enforce candidate quotas with a named responder and review actual denials. Do not treat simulated counts or a shadow-only rollout as spam protection. Keep existing review/abuse controls active during evaluation.

| Measure | Owner / source / cadence | Initial action threshold | Stakeholders and limitation |
|---|---|---|---|
| Repeated known canonical identities | Discovery owner; deterministic fixtures and sampled composed results; every release and weekly pilot review | Any confirmed repeat triggers correction | Readers/organizers; cannot detect all semantic duplicates. |
| Incorrect publication charges or over-cap commits | Engineering; ledger reconciliation and concurrency evidence; every release and incident | Any confirmed accounting error triggers investigation and correction | Publishers/operators; requires reliable audit evidence. |
| Legitimate publishers blocked | Policy owner; reviewed denials and support cases; weekly | Each urgent or wrongly denied case gets review; repeated same-cause cases reopen the tier | New/small organizers; raw denial count does not establish unfairness. |
| Request/appeal handling | Operator; queue age and outcomes; daily during pilot | Any case exceeds the displayed response expectation, or a time-critical event is at risk | Organizers/reviewers; use an expectation only after staffing is agreed. |
| Duplicate correction accuracy | Moderation; sampled decisions, appeals and reversals; weekly | Every wrongful merge/restriction gets repair and cause analysis | Organizers/attendees; low appeals may indicate an inaccessible appeal route. |
| Feed concentration | Product owner; aggregate unique-event share by publisher; weekly | Persistent concentration or complaints prompt relevance/diversity review | Small organizers/readers; do not impose an invented fairness percentage without data. |
| Model comprehension | Design owner; task-based organizer sessions; before enforcement and after UX changes | Participants repeatedly create copies instead of sessions | Volunteers; a small usability sample is not population proof. |

Evaluate seasonal programs, annual conferences, weekly lessons, new mosques, personal organizers and high-volume legitimate publishers. Low publication volume is not success if it comes from excluding useful community events.

### Rejected alternatives

- **Only HTTP rate limiting:** does not constrain live inventory or semantic duplicates.
- **Seven-day publication ban for everyone:** excludes urgent legitimate use and can be defeated by aged accounts.
- **Unlimited verified organizations:** legitimacy does not eliminate compromise, mistakes or spam.
- **One event per user for all time:** confuses scarcity with safety and encourages account proliferation.
- **Charge each session as a new event:** penalizes the exact modeling behavior ISLAMU wants.
- **Deduplicate only in the browser:** hides symptoms after pagination and leaves APIs, totals and other clients inconsistent.
- **Merge all matching titles automatically:** destroys legitimate distinctions and creates privacy/ownership risks.
- **Use raw reports, popularity or payment as trust:** invites brigading, entrenches incumbents or sells visibility.
- **Preapprove every edit and correction:** blocks safety work and overwhelms moderators.
- **Build a complex ML reputation system first:** increases privacy and false-positive risks without necessary launch evidence.

## Stakeholders

| Stakeholder | Interest and provider duty |
|---|---|
| Attendees and prospective attendees | Accurate local/date information, no duplicated feed, dependable updates and clear organizer identity. |
| New personal organizers and informal communities | A useful starting allowance, respectful explanations, alternative evidence and accessible review. |
| Organizations and their staff | Shared predictable capacity, proper delegation and no penalty merely for a new staff account. |
| Community contributors and represented organizers | Preserved provenance, safe claims/corrections and no accidental transfer of organizer authority. |
| Private-home hosts and non-user venue occupants | No disclosure through matching, inferred geography or review evidence. |
| Moderators, operator and maintainers | Manageable queues, finite resource obligations, accountable overrides and recovery tools. |
| Self-hosting adopters and external publishers | Clear local policy, no implied universal trust, and transparent federation/admission boundaries. |
| Future users with existing registrations | Changes and duplicate correction must preserve event continuity and existing rights. |

## I-VSD Principles And Domains

The selected Sunni ethical principles guide provider decisions; they do not supply numeric quotas or a religious ruling about a particular organizer.

| Principle | Concrete derivation |
|---|---|
| Adl / Justice | A shared feed is scarce access: discourage repeated occupation while retaining a fair path for small and new publishers. |
| Amanah / Trust | Capacity and moderation powers need authoritative accounting, constrained overrides and responsible human handling. |
| Sidq / Truthfulness | Explain limits, provenance, verification scope and region/session relevance without claiming more certainty than the evidence supports. |
| Non-Harm and Rights of People | Keep safety corrections possible, avoid wrongful merging and preserve attendee/organizer rights. |
| Avoiding Spying | Use the least intrusive evidence needed; do not turn anti-spam into pervasive behavioral surveillance. |
| Promise-Keeping and Ihsan | Make review promises sustainable and make correct event modeling easier, accessible and recoverable. |
| Avoiding Deception | Do not reward clones, hidden paid priority, misleading badges or false "new event" refreshes. |

All six domains are addressed: **strategy** in access and ranking incentives; **design** in session-first creation and explanations; **technical** in identity and atomic accounting; **operations** in review, correction and rollback; **governance** in appeals and authority; **evaluation** in measured denials, duplicates and outcomes.

## Common Overlooked Failures And Outcomes

| Overlooked failure | Possible bad outcome | Responsible outcome |
|---|---|---|
| One identity repeats in hero and upcoming | The platform itself violates its anti-duplication rule | Page-wide allocation preserves distinct cards. |
| Region and date match different sessions | Someone travels for an event not happening locally that day | Same-session matching and contextual card details. |
| Adding twenty sessions sends twenty broad announcements | A correct event model still produces notification spam | Separate distribution budgets and meaningful update rules. |
| New account is assumed untrustworthy | A legitimate time-sensitive gathering misses publication | Modest starter access plus urgent reviewed exceptions. |
| Multiple organization members each receive a full budget | Organizational capacity grows accidentally with staff count | Shared publisher budget and authorized delegation. |
| Approval, import or private-to-public bypasses the quota | A protected button hides an unprotected publication path | Every public-exposure transition is covered. |
| Deletes immediately refund all budget | Publish/delete cycles flood discovery and notifications | Preserve rolling usage and throttle reactivation. |
| Annual editions are mistaken for duplicates | Distinct attendee commitments are merged | Explicit event-versus-series criteria and review. |
| Quota expiry hides existing events | Registered attendees lose important information | Prospective restrictions, continuity and safety correction. |
| Similarity suggestions reveal hidden events | Private participants or venues become discoverable | Authorization precedes candidate disclosure. |
| Raw abuse reports reduce capacity automatically | Coordinated reporting silences legitimate organizers | Investigated findings and appealable decisions. |
| Review needs more staff than exist | Self-service requests become an unanswered gate | Sustainable intake, disclosed expectations and fallback handling. |

Positive outcomes are plausible rather than proven: less repetitive discovery, more complete programs, fairer access, fewer incorrect local matches, lower avoidable support load and stronger accountability. Evidence must establish whether these outcomes occur.

## Validation Gaps

Before an implementation can be considered ready, demonstrate:

1. One event with many sessions/locations produces one card per discovery context, including overlapping home sections, local/federated echoes and accepted aliases.
2. Region/date filters match the same authorized published session and display its relevant information without leaking private locations.
3. Last-slot concurrent publications for different events cannot exceed capacity; retries, rollback and delivery intent remain consistent.
4. Direct publication, create-as-published, approval and every other supported public-exposure path enforce the same policy.
5. Organization staff share the right budget; unauthorized actor/tenant substitutions, new-group evasion and quota-grant escalation fail.
6. Withdraw/republish, transfer, deletion, merge and downgrade cannot reset consumption or block legitimate cancellation/correction.
7. Date boundaries use a controllable clock; tests do not depend on sleeps or elapsed-time luck.
8. Limit requests, appeals, urgent exceptions, expiry and reversal work through the real UI/API with truthful status and authorized HAL actions.
9. Organizers can successfully model recurring and multi-city programs, using accessible desktop/mobile flows, without being coached into the answer.
10. Operator staffing, published policy, response expectations and privacy retention are actually in place.

These are proposed acceptance scenarios, not tests executed in this report. Existing location-privacy test source was inspected; it does not prove the broader discovery or quota behavior. No product code, deployment, build or test suite was changed or run.

## Escalation Needed

- **Founder/product steward:** decide first-publication review versus a monitored starter allowance; approve event identity examples, initial tiers and what qualifies as established.
- **Instance operator:** choose request-review capacity, urgent handling, appeal ownership, bounded grants and the contingency when review is unavailable.
- **Security/privacy maintainers:** review actor grouping, cross-tenant containment, evidence access, retention/erasure and grant abuse before implementation.
- **Organizer/attendee representatives:** validate course, touring-program, independent regional-event and annual-edition distinctions.
- **Qualified Sunni scholarly authority:** resolve contested religious-legal moderation questions if they arise. Routine capacity arithmetic must not be presented as a religious verdict.
- **Legal/domain specialists where relevant:** review disputed ownership, private evidence and any proposed reassignment of registration or financial obligations.

The report can inform a later implementation plan after these choices; it does not declare that plan approved or technically ready.

## Evidence Reviewed

| ID | Source and bounded locator | What it supports |
|---|---|---|
| E01 | Founder request in this conversation, 2026-09-24 | Proposed age/actor/verification factors, capacity-request analogy and explicit one-event-card requirement. |
| E02 | [Launch consultation](i-vsd-v0-1-launch-consultancy-report.md), Scope and IVSD-F006 | Working-tree launch context, open posting, organization approval and unresolved moderation coverage. Pre-existing untracked report; not deployment proof. |
| E03 | [Event](../../src/Explore.Domain/Event.cs):37-125; [EventSession](../../src/Explore.Domain/EventSession.cs):24-81 | Ownership/provenance, sessions, series, date summaries and session location/schedule. |
| E04 | [Actor](../../src/Explore.Domain/Actor.cs):8-47; [OrganizationTenant](../../src/Explore.Domain/OrganizationTenant.cs):5-46 | Multiple actor types; tenant-scoped approval, eligibility, evidence and suspension. |
| E05 | [EventPublicationExecutor](../../src/Explore.Application/Features/Events/EventPublicationExecutor.cs):19-169 | Ordinary/privileged mode, transaction, approval/readiness, idempotent published-state handling and outbox planning. |
| E06 | [CreateEventCommandHandler](../../src/Explore.Application/Features/Events/Handlers/Commands/CreateEventCommandHandler.cs):279-362 | Separate publish-on-create transaction and delivery intent. |
| E07 | [RateLimitingExtensions](../../src/Explore.API/Extensions/RateLimitingExtensions.cs):22-84; [Quick Reference](../../docs/internal/QUICK_REFERENCE.md), API Rate Limiting | Request-rate controls and their distinction from proposed publication budgets. |
| E08 | [GetEventListRequestHandler](../../src/Explore.Application/Features/Events/Handlers/Queries/GetEventListRequestHandler.cs):55-155; [EventRepository](../../src/Explore.Persistence/Repositories/EventRepository.cs):403-444 | Published/public local selection, event-root pagination and ordering. |
| E09 | [EventController](../../src/Explore.API/Controllers/EventController.cs):82-150; [GetPublicEventDiscoveryRequestHandler](../../src/Explore.Application/Features/Federation/Atproto/Handlers/Queries/GetPublicEventDiscoveryRequestHandler.cs):27-96, 266-267 | Public location-filter restriction; local/federated grouping by stable identity and merged pagination. |
| E10 | [GetHomeDiscoveryQueryHandler](../../src/Explore.Application/Features/PublicExperience/Handlers/Queries/GetHomeDiscoveryQueryHandler.cs):40-164, 360-386; [HomeDiscoveryExperience](../../src/Explore.Blazor.Client/Components/Discovery/HomeDiscoveryExperience.razor):282-289, 531-532 | Independent section composition and per-section display projection without a shared identity allocation step. |
| E11 | [HomeDiscoveryLocationPrivacyTests](../../tests/Event.Application.UnitTests/Features/PublicExperience/HomeDiscoveryLocationPrivacyTests.cs):7-54 | Existing coarse-location serialization checks; inspected, not executed. |
| E12 | [Address geocoding and spatial discovery consultation](i-vsd-address-geocoding-and-spatial-discovery.md), IVSD-AG-08 and Required Before Spatial ADR Acceptance | Existing guidance that spatial eligibility follows per-event/occurrence disclosure authority. |
| E13 | [I-VSD report contract](../../.agents/skills/i-vsd/resources/report-contract.md); [principles and domains](../../.agents/skills/i-vsd/resources/principles-and-domains.md); [evidence levels](../../.agents/skills/i-vsd/resources/evidence-and-validation-levels.md) | Report identity, stable findings and evidence/authority boundaries. |

## Missing Evidence

- The founder's final first-publication choice and accepted numeric thresholds.
- Actual publication volume, abuse distribution, false-positive outcomes, account-age effects and legitimate high-volume demand.
- Observed deployed settings, operator/moderator coverage and review/appeal service expectations.
- Runtime/browser evidence for repeated cards, pagination, federation counts and regional matching.
- Complete inventories of every publication/import/visibility path and existing moderation merge capabilities.
- Organizer usability/accessibility results, including small informal communities and session-specific registration needs.
- Approved retention rules and legal assessment for identity/abuse evidence.
- Current Hetzner documentation or independent competitor research; none is necessary to approve the underlying ISLAMU design principles.

## Context Inventory

- **Workspace:** requested I-VSD skill and routed resources, repository governance, relevant domain/application/API/persistence/Blazor source, one existing privacy test, and related consultation/launch material.
- **Project integrations:** code-review-graph tools were sought but unavailable in the exposed catalog; bounded native source discovery was used instead. No external support, analytics, incident or issue system supplied evidence.
- **User input:** concrete anti-spam and one-card requirements, with a material first-publication question left open rather than treated as consent.
- **External material:** no competitor source, web research, dependency selection or external writes.
- **Contribution boundary:** report-only Tier 4 generic flow; required output is this consultation. Verification is document structure, link existence and whitespace, not product build/test execution. Existing modified index and launch report are outside this edit.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-24 | none | draft | Founder requested a standalone design report; evidence gathered and recommendations written, with publication policy decisions still open | E01-E13; IVSD-F001-F007 and IVSD-M001-M007 |

Refresh after the first-publication decision, approved quota policy, changed event/series semantics, implemented accounting or discovery allocation, new verification/retention rules, or stakeholder/operational evidence. A completed advisory document is not an approved enforcement policy.
