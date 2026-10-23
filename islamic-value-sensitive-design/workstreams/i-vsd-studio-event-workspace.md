# Studio Event Workspace — I-VSD Implementation Assessment

Last Updated: 2026-10-13 Europe/Brussels

## Review Metadata

- Mode: implementation revalidation
- Subject / Workstream: studio-event-workspace
- Report kind: provider-responsibility feature assessment
- Report revision: `r3` (implementation revalidation; historical `r1`/`r2` evidence preserved)
- Report status: current
- Disposition: implementation-aligned / bounded-delivery-reviewed (bounded active creation/workspace scope only; explicit assurance limits and deferred responsibilities below)
- Evidence cutoff: 2026-10-13
- Reviewed input: complete `studio-event-workspace` plan/tasks/context revision `r2` in `.worktrees/studio-event-workspace/dev/active/studio-event-workspace/`, three complete deferred contracts in root `dev/backlog/`, historical repository/source packet at `072f2b6dbd8e4b87e16b795bc02a9ea23f902046`, and explicit user implementation approval dated 2026-10-12. Worktree upstream `c65788c36` and exclusion of three unrelated Configurator-only commits are user-supplied topology facts, not a source-diff verification by this assessment.
- Supersedes: none

## CTO Rewrite Invalidation And Revalidation — 2026-10-12

CTO revision `r2` invalidated the original combined-scope assessment by changing delivery ownership and adding proposed server withdrawal fencing, order-level conversion deduplication and Quartz-based retention availability gates. That stale transition remains in the lifecycle history. This revision reviews all six stable pairs against the complete revised triad and deferred contracts; the original findings, evidence register and r1 mapping remain historical evidence, not proof of current implementation.

The result is **current / plan-aligned for bounded active creation/workspace implementation**. No material provider-responsibility blocker remains in that written scope. Deferred capabilities retain explicit owners and activation gates; they are neither implemented nor independently execution-ready. The user explicitly approved the complete r2 active slice on 2026-10-12. That resolves approval despite older plan/context passages saying it is awaited; this report does not edit the triad or grant approval to implement the backlog features. Existing analytics choices are settled and require no new intake. No product, stakeholder, privacy/legal or scholarly assurance was produced here.

## Executive Summary

### Current implementation revalidation — r3

The bounded creation/workspace slice is implemented against upstream
`c65788c36`: native nullable graph commit `eafc020a8`, local composition commit
`8b959608f`, and the current canonical workspace with independently reviewed
corrections. The current full client suite passes 3,106 cases; Domain passes
1,528 and Application 3,670. The explicit client Release build passed with no
errors or warnings. The solution build passed 65 projects before review
corrections; affected projects were subsequently rebuilt by actual test runs.
These are executed receipts, not planning commands.

Two independent bounded reviews identified four concrete defects. Actual Red
and Green regressions prove duplicate explicit dates now return an indexed
validation error before graph writes instead of HTTP 500; invalid initial
timezones retain metadata and original instants until explicit correction;
resource actions follow current collection management authority; and denied
post-save refreshes discard editor authority without discarding the confirmed
receipt. No review vote waived a defect.

The final gate reviewer then identified three additional bounded P2 defects:
shared Studio cache survived confirmed details saves, selector-only edits
missed dirty tracking, and imported venue/room references could override a new
venue selection. Eight routed editor cases and four imported-venue cases
physically failed before their fixes. Event-scoped cache invalidation, explicit
selector callbacks and reference reconciliation now pass the current complete
3,120-case client suite, including two other-event invalidation cases. Two
unrendered fixtures now supply the required context dependency; their assertions
were not changed. A real SQLite HTTP case also passes fresh-read selected
placement and cleared-room assertions. Its first fixture failed unrelated
new-address admission before those assertions; the accepted fixture uses two
valid existing venues and retains the original room as an independent witness.
These UI-only corrections add one focused API case to the earlier exhaustive
4,331-case snapshot. Earlier unchanged broad inventories are not rerun or
recounted as new evidence. Post-correction release preflight at `ee849240434`
exits 0: `change_range_verified: target=develop head=HEAD commits=10 change-ids=7`.
Fresh delta reviewer `st_01a135f0` returned APPROVE for
`e11062e3204..ee849240434` and the pending documentation correction. All three
reported blockers are closed, with no scoped blocking findings. The reviewer
inspected actual red assertion failures and green identities, not only totals,
including the 3,120-client and one real HTTP/storage receipts. No independent
browser or broad-suite rerun is inferred from this source/evidence review.

| Stable pair | Current bounded evidence | Remaining responsibility |
| --- | --- | --- |
| IVSD-F001 / IVSD-M001 | Real SQLite HTTP graph/storage/rollback, schema/AOT production, local snapshots and unknown outcomes, indexed duplicate-day rejection; complete API/persistence accounting and five-provider graph corpus | Baseline failures and explicit skipped/timeout coverage limits below |
| IVSD-F002 / IVSD-M002 | Generation/auth/disposal, navigation races, collection HAL and denied-refresh cases; independent authority review and accounted provider/API evidence | Baseline coverage limits; deferred consolidated population/PII authority |
| IVSD-F003 / IVSD-M003 | Server status/readiness, explicit unknown participation, truthful saved/unknown outcomes and Answer analytics naming | Deferred counts, currency-separated operations and discovery attribution |
| IVSD-F004 / IVSD-M004 | No collector, reporting store, consent defaults or identity-rich telemetry introduced | Deferred activation/privacy/withdrawal/retention obligations remain accepted |
| IVSD-F005 / IVSD-M005 | Actual routed corpus, native Stay retaining mounted fields, async disposal, status recovery, participation/HAL, RTL/localization and invalid-zone recovery | No live browser/device/screen-reader or stakeholder evidence; no conformance claim |
| IVSD-F006 / IVSD-M006 | Native CQS/EF transaction, generated transport/AOT context, shared form/timeline, no new production dependency or migration model; provider/architecture receipts, baseline-compared formatting, native release preflight and approved delta review | Formatting and provider coverage limits; deferred workload/retention obligations |

All five real database providers execute the current six-case native graph
corpus successfully with zero failures or skips: SQLite 110.839 seconds,
PostgreSQL 134.486, SQL Server 144.745, MySQL 136.948 and MariaDB 135.220.
Provider verification and CI enforcement are committed as `d0493b10e`.
Architecture passes 680 cases. MySQL and MariaDB first exposed an incorrect provider
name in the new test; the repository uses Microting, not Pomelo. After that
correction, the artificial commit-interceptor fault exposed a named-lock cleanup
error. The pre-commit test now injects its failure after positively witnessing
the persisted publication outbox in SavedChangesAsync, retaining the exact
exception, graph/owner/outbox witnesses and fresh-read rollback assertions.
All five lanes have now rerun and passed this changed test source. The named-lock
production code is unchanged; commit-error cleanup is a residual observation,
not a repaired or independently revalidated production guarantee.
The original whole-assembly API/persistence attempts were incomplete: API reported 1,199
completed cases before earlyoom termination, while native discovery proves
4,331; persistence timed out after one hour without a full receipt. Overlapping
base comparisons were stopped to release memory. The complete API inventory
now assigns every case to 460 disjoint single-class processes. A direct-apphost pilot
was rejected because it executed unselected classes. The SDK single-class probe
passes exactly 11 cases with verified TRX membership. A combined namespace
filter ran 208 instead of 44 cases and was rejected. Class names are globally
unique, so execution now uses only the verified single-class filter shape.
The shared container engine also hung on container listing; a scratch-isolated
engine has healthy CRUD. Its real four-case PostgreSQL probe completed with
three passes and one failure; untouched upstream matches the same result and
exact assertion (public search expected HTTP OK, received BadRequest).
After required-input correction, complete receipts from all 460 disjoint
API classes cover all 4,331 discovered cases: 4,236 passed, 95 failed,
none skipped. Counts, namespace/class membership and all 4,331 globally
unique accepted case identities are checked. Every current failure has
an actual scoped untouched-upstream receipt; pending attribution is empty.
The native JSON coverage/status audit also exits 0. This proves exhaustive
accounting and baseline attribution, not a green API suite.
Three onboarding classes require SETUP_SECRET in their fixture initializer;
the runner had omitted that documented environment input. Their original
49-case receipts (48 initializer failures) are preserved but withdrawn from
accepted assurance, rather than quarantined as code defects. The resumed
runner injects an ephemeral generated value through the environment and
writes corrected receipts separately. All 49 corrected cases pass with zero
failures or skips and verified identities; rejected originals are not counted
again. The interrupted class and remaining API inventory are complete.
No case omission,
GC heap cap, test weakening or host memory-guard change is used.
Persistence inventory accounting is complete: all 3,071 discovered cases are assigned
to 359 disjoint class-name groups covering 360 classes. The two same-name
classes share one group. Sequential processes completed against the
explicit structured SQLite scratch runtime whose clean migration,
idempotent migration and runtime smoke already passed. Complete TRX counts,
membership, unique identities and all provider-specific skips were audited.
The final 3,068 complete results contain 3,034 passes, 26 baseline-explained
failures and eight reviewed provider skips. Three additional selected management
processes timed out on both branches without TRX, completing the 3,071-case
accounting. This is not a green persistence suite or a full-provider-suite claim.
The management persistence class stalled for over 22 minutes after completing
only two of its 32 cases; its incomplete result is excluded. All 32 cases were
rediscovered with unique identities and attempted in separate exact-UID
processes. Twenty-nine complete receipts passed with exact membership and
disjoint identities. Three selected-case processes timed out at the 150-second
outer guard on both current and untouched-base runs, without producing TRX.
Those outcomes are recorded separately from framework test results; neither
passes nor test-body entry is inferred. Native cancellation alone did not stop
them, so outer process guards now bound the remaining class execution too.
No source test, assertion, memory guard or unrelated lock implementation is
changed. The full inventory is accounted, with these three explained
baseline timeouts explicit in the final assurance limits.
One unrelated
email-preference HTTP 500 reproduces on untouched upstream as PostgreSQL
foreign-key violation 23503. Other accepted failures have scoped baseline
receipts, including visibility/read/filter/group outcomes. The expiry equality
case passes in a 16-case baseline class run, then fails in isolated baseline
execution at the same assertion; both receipts preserve that variability.
All eight current HAL contract failures reproduce in their baseline class;
that baseline additionally reports a Conflict failure in one case that
passed in the current run. The extra baseline failure and both complete
receipts are preserved, without claiming identical class-level counts.
No source repair or test weakening is made outside this scope.
The three location-backfill migration cases also reproduce their exact
PostgreSQL `3F000` missing-`islamu_event`-schema failures on untouched base,
with complete matching case receipts. Generated migrations and their source
configuration are not changed to repair that unrelated failure.
The separate current-migration-set/retry-safety failure now has its own
matching four-case baseline receipt: three passed and the same case failed
with PostgreSQL `3F000`. Attribution uses actual case evidence rather than
assuming that similar missing-schema text proves a common cause.
The final regular persistence inventory covers 358 disjoint groups and
3,039 complete case receipts: 3,005 pass, twenty-six fail and eight skip.
Both Modified/Deleted admission append-only message failures reproduce on
untouched base with exact messages and normalized assertion frames at test
line 237. Nine other cases pass on both. The inner transient-wrapper cause
and successful append-only rejection semantics are not inferred.
Both ticketing exception-translation failures reproduce on untouched base,
with the same case names, messages and complete normalized assertion stacks.
The other twenty-five ticketing cases pass on both. No inner SQLite cause
or successful exception-translation coverage is inferred from failed assertions.
Four branding-provisioning failures reproduce in the same cases on untouched
base. Three normalized raw stacks match; the cancellation stack preserves an
empty-line placement discrepancy while every nonempty frame matches in order.
Reported source paths are unchanged. No savepoint or cancellation rollback
coverage is inferred from a failed storage attachment or exception assertion.
Twenty-five attributed failures
reproduce in the exact cases on untouched base, including both disclosure fixed-read assertions
(expected four SELECTs, observed five). The concurrent location-association
detach cancellation also reproduces in the same parameterized case with
matching reported test and transaction frames; its inner cancellation cause is
not inferred. Three authority-planning lock-order cases (unchanged, actor and
organizer variants) intentionally skip SQLite and require a SQL Server or InnoDB
structured lane. Their explicit reasons and complete identities are recorded;
the separate five-provider native creation corpus does not prove these witnesses.
The fourth skipped case requires explicit PostgreSQL runtime and migrator
authorities with maintenance EXECUTE to verify restricted-runtime forced RLS.
Eleven other snapshot-maintenance cases passed; this is not evidence for the
skipped privileged-topology witness.
Four writer-provider cases also skip SQLite: both Boolean variants of
independent-source insertion ordering and actor identity-review ordering
require native row locking. Three other writer cases passed. Each skipped
variant and reason remains enumerated without inferring its concurrency proof.
Both projection updater failures reproduce in full-class baseline runs, but
all three exact projection methods pass alone on base. Coordination is the
remaining fixture-level failure: its full-class baseline loses a different
method from current, at the same unchanged tenant-seeding and empty-snapshot
enrollment guard frames in the shared PostgreSQL fixture. This is explained
baseline fixture interference, not identical failing-case reproduction.
Class-context failures and isolated passes are retained; failed tenant seeding
does not prove subsequent projection assertions. No test is serialized or
retried until green to conceal the original failure.
The additional EAV tenant-filter case reproduces on base with matching case,
message and tenant-seeding/enrollment frames. Failed seeding establishes neither
a tenant-isolation defect nor successful execution of the filter assertions.
Discovery implementation remains deferred; this is failure
attribution within the required persistence inventory, not a new feature track.
No discovered inventory remains unaccounted. The 29 complete management results
have identities disjoint from all 3,039 regular results; the three baseline
process timeouts remain separate and have no invented framework result.
The operator-identity concurrency case has the same typed-conflict count
failure on base (expected one, observed zero). The mandatory onboarding identity
rollback case likewise reproduces its unexpected PostgreSQL exception on base.
Matching receipts establish pre-existing failures, not their inner cause or
successful coverage of the assertions those failures prevent.
The whole-repository format command returns exit 2 for 1,404 files, and the
untouched upstream reproduces 1,397 files. New owned formatting is verified
clean; three existing owned-path import diagnostics also reproduce on base.
Neither catalog assemblies nor CI dispatch prove runtime parity. Product
verification and baseline attribution are complete with the explicit limits
above. Canonical workspace commit `e11062e3204`
has been recorded. The required post-commit command
`rtk dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- preflight-range --target develop --head HEAD`
exited 0 against that initial HEAD: `change_range_verified: target=develop head=HEAD commits=6 change-ids=4`.
The updated range receipt at `ee849240434` also exits 0 for ten commits and seven change IDs.
Release tooling emitted CA1307/CA1861 warnings; none were suppressed.
The final delta review approved the corrected product and documentation
scope. Publication and parking are delivery steps, not additional runtime or
Islamic responsibility guarantees.

The original findings and mappings below preserve historical r1/r2 reasoning;
they do not claim the original gaps remain in current code. Deferred portfolio,
operational and discovery responsibilities remain owned and unimplemented.
This revalidation does not activate them or grant new money/attendee authority.
Software tests do not supply usability, WCAG, operational, legal or scholarly
certification.

### Historical planning rationale — r2

Organizers should finish creation inside the event they can manage, retain their prepared program, and understand which actions and reports apply. Attendees should not become visible across organizations merely because their organizer can open Studio. The recommended design retains server authorization and participation semantics while making the organizer workspace coherent.

The user selected operational and discovery analytics with optional built-in aggregate reporting and consented conversion. Those requirements survive, but r2 deliberately defers them to three separate contracts: authorized portfolio, operational reporting and optional discovery reporting. The active slice adds no reporting store, collector, settings, scheduler, reporting read port or money/attendee authority. Operational reports need authorized scope, not tracking; discovery introduces a separately governed data responsibility and must not silently enable tracking, expand identified profiles or call external clicks registrations.

Active delivery preserves title-only/mixed draft programs through one native graph, keeps publication readiness and atomic tenant-qualified persistence authoritative, prepares sessions locally, and enters a capability-aware Overview after confirmed save. Generation checks protect same-event reloads, A→B→A transitions, dependent loads and disposal; cancellation alone does not prevent stale authority. Unknown save outcomes must remain unknown without automatic retry. These written mechanisms align with stewardship, truthful outcomes and protection of people's rights.

This is planning alignment, not implementation, usability, operational readiness, legal compliance or scholarly certification. All findings remain accepted with ownership, not resolved. Active executable security/storage/route/accessibility gates remain open. Deferred withdrawal, deduplication, retention and erasure contracts need fresh executable plans and assessment before activation.

## Scope

Active: native creation/draft preservation, inline program preparation and canonical event workspace; existing authority, tenancy, accessibility, localization and self-hosting boundaries. Deferred but reviewed for preservation: organizer portfolio listings/attendees/orders, operational metrics/money reports and discovery measurement/consent/retention. Existing checkout, payments, refunds, participant contact-sharing, moderation and publication authority remain their existing domain boundaries. Reviewing deferred obligations does not import them into the three active phases.

## Claim Boundary

This is provider-responsibility reasoning using the repository's I-VSD framework. It is not a fatwa, religious ruling, product certification, WCAG certification, legal opinion, or evidence that users experience the proposed design positively. Source inspection supports implementation traceability; runtime, stakeholder and operational evidence is missing.

## Findings

### IVSD-F001 — Saving work must preserve the organizer's intended program

An organizer can enter a schedule and then save a draft, but the current creation code clears the nested session list for that action. The person entrusted their work to the product and can reasonably believe it was saved. A silent omission wastes effort and can later produce an incomplete published program.

The provider controls draft serialization, validation, save confirmation and post-save navigation. **IVSD-M001** requires preserving valid program data, allowing genuinely unscheduled drafts through the native domain contract, and rejecting incomplete invalid schedules with field-specific feedback while keeping inputs available. Creation and publication must retain atomic graph persistence and existing readiness enforcement. No dashboard checklist may substitute for those authorities.

- Lifecycle / severity / claim: accepted / high / verified implementation gap and proposed mitigation.
- Principles / domains: Amanah (stewardship of entrusted work), Sidq (truthful save outcomes), Non-Harm; design and technical.
- Stakeholders: organizers, program contributors, attendees relying on published schedules.
- Evidence / validation: E01, E02; source traceability only.
- Owner / next validation: creation implementer; draft round-trip, validation, rollback and publication scenarios in the completed triad.
- Escalation: implementation cannot advertise preservation until those scenarios pass.
- r2 disposition: active, Tasks 1.1–1.3, 2.1–2.2 and 3.1–3.3 / S-CREATE-1–6. The truth table preserves scheduled OpenEnded start-only inputs, rejects partial Fixed and publish-with-unscheduled inputs before rollups/writes, and never invents dates. Real storage assertions must prove no partial graph/owner/outbox on pre-commit failure; lost response is not proof of rollback. API-first schema/client production and nullable consumer closure are release gates (E15).

### IVSD-F002 — A consolidated workspace must not consolidate unrelated authority

Membership, event-management access, attendee access and money visibility are distinct. Showing an event in a management table does not grant access to its attendees, orders or financial data. A delayed response from an old organizer scope could also expose personal data after the user switches scope.

The provider controls candidate selection, native authorization, request context, reporting queries and cached client state. **IVSD-M002** requires current tenant-qualified per-event authorization for the relevant action, separate authorized populations for each metric, fail-closed provider errors, cancellation/generation checks on scope changes, and no unauthorized names or counts in totals. Actor membership is candidate selection, never a substitute for permission. UI action authority remains HAL links.

- Lifecycle / severity / claim: accepted / critical / design risk grounded in current scope and request patterns.
- Principles / domains: Amanah, Rights of People, Non-Harm; technical, governance and operations.
- Stakeholders: attendees, organizers, team members, tenants, operators.
- Evidence / validation: E03, E04, E05; implementation traceability, no exploit evidence executed.
- Owner / next validation: authorization and reporting implementers; denied-access, revocation, provider-outage, tenant-spoofing and late-response tests.
- Escalation: security/privacy review before feature release.
- r2 disposition: active tenant/reference guards in Tasks 1.1–1.3 and generation/HAL/native management boundaries in Tasks 3.1–3.3 / S-SPACE-4–5. Generation acceptance must protect returned resources and subordinate capability loads, not just displayed state; failed/null loads require a fresh retry. New consolidated event/participant/order and per-measure rights remain deferred to portfolio and reporting owners, S-SCOPE-1–5 and S-METRIC-1–3 (E15–E18). Public metadata cannot substitute for private management facts.

### IVSD-F003 — Reports must communicate what was actually measured

The current Studio cards mix a server total with counts derived from the first 100 loaded events. Orders, participants, tickets and admission check-ins represent different facts. Adding currencies or reporting external clicks as registrations would give organizers a misleading picture of attendance or earnings.

The provider owns metric definitions and presentation. **IVSD-M003** requires complete authorized aggregates, named populations and time bases, currency-separated integer minor-unit reporting, independent order/participant/admission counts, freshness and availability states, and explicit discovery coverage. A conversion measure must have a defined eligible denominator, observation window and server-verified numerator; absent consent or missing instrumentation cannot become zero conversions or invented attribution.

- Lifecycle / severity / claim: accepted / high / verified count inconsistency and proposed reporting safeguards.
- Principles / domains: Sidq, Avoiding Deception, Avoiding Gharar; design, technical and evaluation.
- Stakeholders: organizers, financial reviewers and attendees affected by capacity decisions.
- Evidence / validation: E06, E07, E08.
- Owner / next validation: metrics implementer; complete-population, duplicate-admission, multi-currency, overflow and missing-data scenarios.
- Escalation: changes to payment, tax, refunds or financial terms require separate domain and qualified review; this assessment approves no such changes.
- r2 disposition: active only for truthful save/status, participation labels and naming existing `/analytics` as Answer analytics (Tasks 2.2, 3.2–3.3 / S-CREATE-4–5, S-SPACE-2–3). Complete portfolio counts, exact currency-separated operations and observational conversion remain deferred (S-SCOPE-1–5, S-METRIC-1–3, S-DISC-3–4). The historical first-page inconsistency is not repaired or waived by Overview; it belongs to the portfolio/reporting gates (E15–E18).

### IVSD-F004 — Discovery reporting creates a new data responsibility

The existing provider abstraction is optional and write-oriented. Discovery reports would require either new provider read access or new retained data. Referrer URLs can contain personal information; persistent journey identifiers can become cross-event profiles. Browser consent alone cannot authorize disclosure to an organizer or override server governance.

The provider controls opt-in defaults, collection scope, identifiers, reporting fields, credentials, retention and abuse controls. **IVSD-M004** requires disabled-by-default collection, existing instance/tenant governance ceilings, minimal event-scoped measurement, no raw URLs/query strings/IP addresses/device fingerprints/contacts in reports or telemetry, bounded retention and deletion, and consent withdrawal behavior. Conversion correlation must be separately consented where it introduces journey linkage, expire quickly, and remain unavailable when consent or authoritative evidence is absent. Optional telemetry must not block registration or payment success. No new session replay, autocapture, identity enrichment or external query-key disclosure is authorized by this feature.

- Lifecycle / severity / claim: accepted / critical / proposed data-processing risk.
- Principles / domains: Tajassus (avoid unjustified monitoring), Rights of People, Haya (avoid unnecessary exposure), Non-Harm; strategic, design, technical, operational and governance.
- Stakeholders: public visitors, attendees, organizers, tenant admins and self-hosters.
- Evidence / validation: E09, E10, E14; user-selected foundation and planned retention/conversion contracts, not runtime evidence.
- Owner / next validation: analytics implementer; consent, kill-switch, replay, retention, erasure, invalid-input and zero-PII tests.
- Escalation: privacy/security review before activation; any new jurisdictional claim goes to qualified legal authority.
- r2 disposition: no new collection or receipt processing in active Tasks 1.1–3.3; active constraint is non-introduction and zero sensitive payload logging. The discovery owner retains S-DISC-1–5 and the revised withdrawal/Quartz/dedup/erasure gates assessed below (E18). This finding remains accepted and deferred, not resolved by splitting delivery.

### IVSD-F005 — Rich event management must remain usable by different organizers

A ticket-centric checklist would burden information-only or walk-in events. Requiring every organizer to leave creation for a second workflow would burden complex programs, while showing every advanced option initially would burden simple events. Navigation changes can also make keyboard or assistive-technology users lose their place.

The provider controls progressive disclosure, task grouping, participation-specific actions, labels and focus. **IVSD-M005** requires a simple initial composer with optional inline sessions, a meaningful event overview, applicability-aware actions, semantic controls, visible focus, single announcements, logical CSS, localization and keyboard alternatives. Appearance is independently designed with repository tokens and wrappers, not copied competitor expression.

- Lifecycle / severity / claim: accepted / high / design recommendation.
- Principles / domains: Adl (fair access), Ihsan (quality), Non-Harm; design and evaluation.
- Stakeholders: small community organizers, conference teams, mobile/RTL users and users of assistive technology.
- Evidence / validation: E01, E11, E12; no stakeholder study or accessibility certification.
- Owner / next validation: presentation implementer; rendered component and focus/state behavior tests. Operational accessibility evidence remains unproved.
- Escalation: do not advertise WCAG conformance from component tests alone.
- r2 disposition: active local form/timeline, explicit overnight end date, event-zone normalization, participation-aware Overview and canonical/dirty navigation (Tasks 2.1–2.2, 3.1–3.3 / S-CREATE, S-SPACE, S-UX-1). Existing newly ambiguous/nonexistent-time rejection and unchanged saved-offset behavior remain explicit, not a promised new offset picker. Portfolio/report controls and textual chart equivalents remain with later owners. Blazouter interception, focus/keyboard/RTL/localization assertions and operational assistive-technology validation are still unproved (E15–E18).

### IVSD-F006 — Enterprise quality must preserve a practical self-hosting floor

The workspace should not require an external reporting vendor, a new event platform, or a duplicate domain model merely to improve navigation. Heavy mandatory infrastructure would transfer maintenance costs to small community operators. Conversely, silently truncating reports to make them fast would hide limits from larger operators.

The provider controls layer ownership, optionality, workload bounds and documentation. **IVSD-M006** requires native CQS operations, generated transport contracts, entity-returning repositories, bounded read paths, existing deployment modes and honest workload failures. Reporting values may cross a dedicated read port without changing entity repositories into DTO services. Durable decisions and follow-ups must graduate from local planning memory into tracked documentation.

- Lifecycle / severity / claim: accepted / medium / maintainability and operator-burden risk.
- Principles / domains: Amanah, Promise-Keeping and Ihsan; strategic, technical and operations.
- Stakeholders: maintainers, self-hosters and enterprise operators.
- Evidence / validation: E02, E04, E13.
- Owner / next validation: architecture implementer; dependency, provider and performance contracts plus knowledge graduation.
- Escalation: a new dependency or mandatory service requires renewed scope/provenance review.
- r2 disposition: active native graph/form reuse and generated transport contracts, no new infrastructure/settings/schema migration (Tasks 1.2–1.3, 2.1–2.2, 3.2–3.3). Nullable persisted drafts make an old-code downgrade unsafe; compatible rollback or forward correction is required, not data reset. Bounded complete reads, reporting read port and retention operations remain separate deferred responsibilities. Proposed ADR/backlog graduation is documentation ownership, not adoption/runtime evidence (E15–E18).

## Recommendations

Implement the bounded creation/workspace slice with complete valid program snapshots, native atomic/publication authority, generation-safe management facts and applicable HAL-driven actions. Keep portfolio, operational and discovery delivery separate without cancelling the user's reporting choices. No empty general-report navigation should promise an undelivered capability.

Deferred discovery planning retains reporting/conversion defaults false, `analytics.enabled` as required collection master, memory-only event-scoped journeys valid 30 minutes, aggregate retention default 90 days governed within 7–365, and replay/converted receipt disposal targeted within 24 hours while cleanup is available. These are planned defaults, not shipped behavior or a downtime-proof deletion promise. Operational reporting must remain independent of tracking enablement.

Reject a ticket-only funnel, role-derived browser permission, first-page totals claimed complete, identity-rich tracking, mandatory external reporting infrastructure and a custom retention timer. Provider adapters/imports remain a follow-up, not a replacement for the settled built-in choice. The split reduces immediate implementation/hosting burden but postpones complete portfolio/reporting benefits; those omissions must stay visible.

## Plan Alignment And Mitigation Traceability

### Current r2 active and deferred mapping

Task numbers below refer only to r2 unless explicitly labeled historical. Deferred contract task packets retain r1 numbering; they are inventories requiring new executable plans, not extra active phases.

| Stable pair | Active r2 scenarios and owners | Deferred contract ownership | Disposition / remaining evidence |
| --- | --- | --- | --- |
| IVSD-F001 / IVSD-M001 | S-CREATE-1–6; Tasks 1.1–1.3 native graph, 2.1–2.2 local program, 3.1–3.3 navigation/recovery | None required for creation preservation | Accepted, active; draft/publish truth table, atomic/tenant storage, generated wire shape, input retention and dirty/unknown-outcome evidence |
| IVSD-F002 / IVSD-M002 | S-CREATE-4, S-SPACE-4–5; Tasks 1.1–1.3 tenant guards and 3.1–3.3 management generation/native authority | Portfolio S-SCOPE-1–5; operational per-measure authority; discovery native order/erasure fences | Accepted, split; active races/denial/tenant tests and independent review; later full authorized population/read/PII evidence |
| IVSD-F003 / IVSD-M003 | S-CREATE-4–5, S-SPACE-2–3; Tasks 2.2 and 3.2–3.3 save truth/participation/Answer analytics naming | Portfolio complete counts including new global orders; operations S-METRIC-1–3; discovery S-DISC-3–4 | Accepted, split; no new active metrics; exact arithmetic/population/cohort/coverage gates deferred |
| IVSD-F004 / IVSD-M004 | Plan Section 4/10 non-introduction of tracking; Tasks 1.1–3.3 stay within existing authority/data | Discovery S-DISC-1–5, corrected withdrawal/dedup/Quartz/disposal contract | Accepted, deferred data-processing mitigation; no active collector; fresh privacy plan/revalidation and runtime gates before activation |
| IVSD-F005 / IVSD-M005 | S-CREATE-1–6, S-SPACE-1–5, S-UX-1; Tasks 2.1–2.2 and 3.1–3.3 form/Overview/route/focus | Portfolio scoped tables, operational text/table reports, discovery consent/report controls | Accepted, split; rendered and operational usability evidence still absent; no conformance claim |
| IVSD-F006 / IVSD-M006 | Tasks 1.2–1.3, 2.1–2.2, 3.2–3.3 native reuse/generated contracts/no infrastructure | Portfolio bounded reads; operations dedicated read port; discovery optional store/Quartz/retention; documentation graduation owner | Accepted, split; provider/architecture/rollback and documentation evidence open; deferred load/retention/hosting gates remain |

### Deferred discovery corrections: reviewed design, not implementation

The corrected backlog addresses concrete shortcomings in r1 without treating a proposed mechanism as tested protection (E18):

- **Withdrawal fencing:** clearing browser memory stops normal sends but cannot revoke a copied token. The proposed typed relay invalidation requires protected-receipt possession and must serialize withdrawal versus conversion; later token use must be no-op without disclosing order status. Offline withdrawal stops browser sends immediately but cannot promise immediate server invalidation; reconnect handling within validity and treatment of already aggregated conversions must be specified in the fresh plan. Retroactive aggregate removal is not promised without provenance.
- **Receipt and order deduplication:** receipt nonce uniqueness alone allows two receipts to credit one order. Portable tenant/event/receipt and converted-order uniqueness, winner association and cohort increment must commit atomically. The accepted order must be created and confirmed after issuance and before expiry; the cleanup fence must outlive every still-acceptable replay. Cross-receipt order races and fence deletion need real-provider evidence, not only scheduler or mock tests.
- **Quartz and availability:** `AddSweepJob<TJob>`, a single-pass API job and scheduler-neutral Application cleanup replace the custom timer proposal. Scheduling overlap protection does not prove cross-process database idempotency. Activation requires available retention composition; loss must halt new collection/linkage as specified, fail retention health and have drain/re-enable recovery. Sweeps remain registered after collection is disabled. Logical expiry is immediate; physical 24-hour disposal depends on healthy cleanup and must be monitored.
- **Erasure and checkout separation:** native guest/account order access and current confirmation precede the optional analytics transaction; erasure/version fencing is revalidated inside it. Order-linked receipts participate in authoritative local erasure before linkage removal. Concurrency, startup/restore replay, backup handling and zero-PII sinks remain release gates. Analytics failure may lose measurement, never change or block checkout/payment success.

These corrections are directionally aligned with IVSD-M002/M003/M004/M006. Missing detailed withdrawal settlement, dedup fence lifetime, scheduler health and concrete owner binding are explicit deferred activation work, not blockers to a slice that introduces none of that processing. Historical C7/C8 packets and intermediate gates cannot be executed unchanged: the later plan must incorporate the corrections and selected-provider evidence before collection logic commits. No separate deferred implementation approval or privacy readiness is inferred.

### Historical r1 mapping (preserved, no longer active ownership)

| Finding / mitigation | Observable scenario coverage | Implementation ownership | Required evidence before release |
| --- | --- | --- | --- |
| IVSD-F001 / IVSD-M001 | S-CREATE-1–6 | Tasks 1.1–1.5 and 2.1–2.4 | Draft graph round-trip, invalid-input retention, atomic failure, readiness and dirty navigation |
| IVSD-F002 / IVSD-M002 | S-SPACE-4–5, S-SCOPE-1–4 | Tasks 2.3, 3.1–3.4, 4.1–4.3, 6.1–6.3 | Native authority, tenant isolation, provider failure/revocation, complete totals and stale-response tests |
| IVSD-F003 / IVSD-M003 | S-METRIC-1–3, S-DISC-3–4 | Tasks 3.3, 5.1–5.4, 6.1–6.4 | Correct populations, exact currencies/overflow, admission semantics, confirmed conversion and unavailable/coverage states |
| IVSD-F004 / IVSD-M004 | S-DISC-1–5 | Tasks 5.1–5.5, 6.2, 6.5 | Effective defaults/consent, native order proof, replay/races, expiry/cleanup/erasure/restore, zero-PII sinks and security/privacy review |
| IVSD-F005 / IVSD-M005 | S-CREATE-1–6, S-SPACE-1–3, S-UX-1 | Tasks 1.3–1.5, 2.1–2.4, 3.4, 4.3, 6.4 | Rendered participation-aware, keyboard/focus/localized/RTL/table behavior; no unsupported conformance claim |
| IVSD-F006 / IVSD-M006 | S-SCOPE-4, S-METRIC-3, S-DISC-1 | Tasks 3.2–3.3, 5.2–5.5, 6.2–6.5 | Optional vendor-free deployment, genuine CQS read boundaries, honest workload limits, retention and tracked graduation |

Current plan [Section 9](../../dev/active/studio-event-workspace/studio-event-workspace-plan.md#9-i-vsd-mapping) owns implementation binding; the [task ledger](../../dev/active/studio-event-workspace/studio-event-workspace-tasks.md) owns executable gates, and [context](../../dev/active/studio-event-workspace/studio-event-workspace-context.md) records absent runtime evidence. These links identify the current worktree inputs from this root-owned report; its later transfer must preserve provenance and use destination-relative links. CTO technical refinement and user approval are recorded, while older awaiting/stale passages in the reviewed triad remain historical metadata to be reconciled by its owner, not a request for new intake.

### Remaining release and activation gates

Active implementation must supply genuine nullable-draft behavioral Red/Green and preserve existing passing atomic/tenant/publication guards; selected SQLite native HTTP/storage assertions before the native graph outcome; API-first generation and actual nullable JSON/client shape; local snapshot/end-date/event-zone consumer tests; actual routed dirty-navigation, deep-link, save-recovery and same-ID/ABA/sub-load/disposal regressions. Final affected suites, actual PostgreSQL/SQLite/SQL Server/MySQL/MariaDB graph dispatch, architecture/format evidence and independent security/tenant/persistence review remain open. Record attributable failures and omissions; an unrelated baseline failure is quarantined, not silently fixed here. Baseline in progress is not a pass.

Public/internal documentation and release artifacts must describe delivered creation/workspace behavior and compatible rollback, not deferred reporting promises. Revalidate this assessment against the actual implementation and receipts before release; plan-aligned status alone cannot close that gate. Rendered accessibility evidence is bounded; live browser/device/screen-reader evidence remains missing and no WCAG claim follows from automated tests.

Each deferred owner needs a fresh bounded triad and I-VSD mapping before execution: portfolio complete authorized counts/pages/orders and workload target; operational exact per-currency sums/consistency/populations and per-measure authority; discovery withdrawal/dedup/erasure/restore/retention/telemetry and activation health. Legal or scholarly conclusions remain with qualified authority.

## Stakeholders

Organizers and program contributors need reliable saves and clear work scope. Attendees and visitors need privacy and truthful participation information. Tenant admins control permitted configuration; operators own retention, isolation and availability. Maintainers own testable boundaries and documentation. Qualified scholarly/legal authorities own conclusions beyond software design reasoning.

## I-VSD Principles And Domains

Applicable principles are Amanah, Sidq, Adl, Non-Harm, Rights of People, Avoiding Gharar, Avoiding Deception, Promise-Keeping, Ihsan, Haya and Tajassus. Avoiding Riba is not newly assessed: reporting existing commercial snapshots creates no financing or financial-term change. All six domains apply: strategic optionality, design defaults, technical enforcement, operational retention, governance authority and evaluation limits.

## Common Overlooked Failures And Outcomes

Opening Studio must not publish a draft. A live event can contain internal draft sessions. A disabled analytics panel must say unavailable, not zero. A purchaser is not necessarily the attendee. Repeated admissions are not unique people. External participation clicks are not confirmed registration. A dashboard is not an authorization source. Late requests must not repopulate old attendee scope. Referrer input and analytics identities are untrusted even when submitted from a first-party browser.

## Validation Gaps

No product build, test suite, live application, provider integration, stakeholder interview, operational audit or scholarly review was run for this planning assessment. Planned mitigations require future executable evidence. Accessibility tests provide bounded structural evidence, not full conformance.

## Escalation Needed

The analytics intake and active implementation approval are resolved. No material decision blocks bounded creation/workspace implementation on the reviewed written design. Independent security/tenant/persistence review precedes its release. New consolidated reads and discovery collection retain separate security/privacy, local-erasure/restore and provider concurrency gates before their own release/activation. Financial lifecycle, religious/legal conclusions, intrusive tracking or new dependencies require refreshed assessment and appropriate authority.

## Evidence Reviewed

E01–E14 below are preserved r1 evidence, not source inspection or research repeated in this revalidation. Claims of implementation gaps refer to that historical source revision; no current implementation completion is inferred.

- E01: `src/Explore.Blazor.Client/Pages/Events/CreateEvent.razor.cs` — submission destinations and inline-session serialization; `CreateEvent.razor` — multi-session handoff.
- E02: `src/Explore.Application/DTOs/Event/CreateEventDto.cs`, `CreateEventGraphSessionDto.cs`, and `Features/Events/Handlers/Commands/CreateEventCommandHandler.cs` — nested graph and serializable creation boundary.
- E03: `src/Explore.Blazor.Client/Pages/Studio/StudioAttendees.razor`, `StudioAttendeeList.razor`, and `Services/StudioContextService.cs` — event/order aggregation and participant display.
- E04: `docs/internal/AUTHORIZATION.md`, `AUTHORIZATION_PATTERNS.md`, `SECURITY-MODEL.md`, and repository native authorization guidance.
- E05: `src/Explore.Application/Features/Studio/Handlers/Queries/GetStudioContextQueryHandler.cs` and managed-event query handlers.
- E06: `src/Explore.Blazor.Client/Pages/Studio/StudioEventPageBase.cs`, `StudioHome.razor` — first-page card populations.
- E07: `docs/internal/DOMAIN.md` — participation, sessions, entitlements and integer minor-unit commercial snapshots.
- E08: `src/Explore.Blazor.Client/Pages/Studio/StudioRegistrationAnalytics.razor` — governed form/version answer aggregates.
- E09: `docs/internal/CONFIGURATION.md#analytics-settings-governance`, `AnalyticsRuntimeProfileResolver.cs`, `AnalyticsGovernanceService.cs`.
- E10: `RelayAnalyticsEventCommandHandler.cs`, `Contracts/Infrastructure/IAnalyticsProvider.cs`, `Shared/AnalyticsInitializer.razor` — optional provider emission and absent inbound reporting port in the inspected interface.
- E11: `docs/internal/ACCESSIBILITY.md`, `DESIGN_SYSTEM.md`, `BLAZOR.md` — existing shell, focus, generated contracts and tokens.
- E12: Tavily research of public product behavior and NN/g UX principles, 2026-10-12; no external implementation source copied.
- E13: `AGENTS.md`, `PROJECTS.md`, Clean Architecture/CQS/EF guidance and implementation-plan resources.
- E14: User's two analytics intake answers, completed triad revision `r1`, W3C privacy research and `docs/internal/PRIVACY_ERASURE.md` authority/local settlement contract.
- E15: Complete r2 plan, tasks and context read from `.worktrees/studio-event-workspace/dev/active/studio-event-workspace/` on 2026-10-12. Plan Sections 3–5, 7, 9–14 and Tasks 1.1–3.3 establish active semantics/owners/gates; full manifests, historical command packets and context limits were read. Commands in those documents are proposals or inherited receipts, not commands executed by this assessment.
- E16: Complete [authorized portfolio contract](../../dev/backlog/studio-authorized-portfolio.md), root-owned, 2026-10-12: complete event/attendee/order populations, S-SCOPE-1–5, independent order slice, workload/permission/paging and historical r1 inventories.
- E17: Complete [operational reporting contract](../../dev/backlog/studio-operational-reporting.md), root-owned, 2026-10-12: S-METRIC-1–3, metric dictionary, per-measure authority, exact arithmetic/consistency and tracking-independent rollout; historical mixed reporting packets require replacement.
- E18: Complete [discovery reporting contract](../../dev/backlog/studio-discovery-reporting.md), root-owned, 2026-10-12: six corrections, S-DISC-1–5, lifetimes/defaults, typed withdrawal, cross-receipt order dedup, Quartz availability/disposal, erasure/restore and historical inventories requiring fresh planning.
- E19: Explicit current user instructions, 2026-10-12: full r2 active implementation approved; single triad moved to the worktree; root report is this assessment's sole edit owner and will be transferred by the execution owner. Root SHA/upstream/Configurator exclusion are supplied facts. No new external research or third-party source was used.

## Missing Evidence

Actual organizer needs for every metric; production volumes and query plans; consent/legal review for deployment jurisdictions; runtime failure/erasure/concurrency evidence; keyboard, visual and assistive-technology operational evidence. Completed plan/task mappings exist, but their test assertions have not been executed.

## Context Inventory

The shared r1 source packet and complete current r2 artifacts are available. The single triad lives in the execution worktree; the three deferred contracts were read in root `dev/backlog/` (the probed worktree portfolio copy was absent). This assessment edits only the root report, not the triad, backlogs, product or unrelated dirty files. The execution owner will transfer it. Both analytics choices and active implementation approval are resolved. Readiness/baseline activity in context is not runtime assurance; no build, test, Git write or application startup was performed here.

## Review Lifecycle

| Date | Previous | New | Trigger | Evidence |
| --- | --- | --- | --- | --- |
| 2026-10-12 | none | draft / ready-for-planning | Shared evidence intake and expanded analytics scope | E01–E13; pending foundation choice |
| 2026-10-12 | draft / ready-for-planning | current / plan-aligned | User selected optional built-in reporting; completed triad revalidated | E01–E14; mapped scenarios/tasks and release gates, no runtime claim |
| 2026-10-12 | current / plan-aligned | stale / changes-required | CTO r2 split changes scope/task ownership and proposes revised discovery withdrawal/dedup/retention gates | r2 plan Section9 and three Studio backlog contracts; revalidation pending |
| 2026-10-12 | stale / changes-required | current / plan-aligned (bounded active slice) | Complete r2 triad/backlog revalidation; explicit user approval; all six pairs assigned active/deferred ownership | E15–E19; deferred corrections reviewed as proposals; no runtime or release assurance |

## Planning Handoff

- Workstream: studio-event-workspace
- Status: current / plan-aligned for bounded active creation/workspace implementation; findings accepted, not resolved.
- Reviewed input: historical r1 source packet plus complete r2 plan/tasks/context in the execution worktree and three root backlog contracts, E15–E19; explicit approval 2026-10-12.
- Findings / mitigations: IVSD-F001→IVSD-M001 through IVSD-F006→IVSD-M006.
- Required plan mappings: F001 active creation/navigation; F002 active tenant/generation plus deferred consolidated authority; F003 active truthful save/labels plus deferred counts/money/conversion; F004 active non-introduction plus deferred discovery; F005 active inclusive form/workspace plus deferred reports; F006 active native reuse plus deferred bounded reads/retention. Exact r2 tasks/scenarios and deferred owners appear above; historical r1 table is not execution ownership.
- Escalations required before: no new intake or provider-responsibility blocker before bounded active implementation; baseline and executable active security/storage/state/route evidence plus independent review and implementation-bound revalidation before release. Deferred privacy/concurrency/erasure/withdrawal/Quartz/dedup assurance is required before later activation. Stakeholder, accessibility-conformance and jurisdictional/scholarly conclusions remain unproved.
- Handoff boundary: execution owner reconciles triad metadata and transfers this root assessment; that transfer is outside this edit scope. No tests, commits or product changes were performed.
- Refresh triggers: analytics data foundation, consent/retention/identity changes, authority scope, money definitions, accessibility defaults, dependency or deployment changes, or material changes to mapped tasks.
