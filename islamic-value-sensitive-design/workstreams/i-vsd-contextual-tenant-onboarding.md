# I-VSD: Contextual Tenant Onboarding And Separate Learning Resources

Last Updated: 2026-10-28

## Review Metadata

- Mode: planning
- Subject: contextual-tenant-onboarding
- Workstream: contextual-tenant-onboarding
- Report kind: implementation-plan-review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-10-28
- Reviewed input: revision-4 plan/task-contract and context, both ordered successor briefs, all 18 inherited findings/mitigations, and the current user's explicit implementation request. October 11 source/research receipts below remain historical, not fresh runtime evidence.
- Supersedes: none; in-place revalidation of the revision-1 report
- Source consultation: [full consultation](../consultations/i-vsd-tenant-onboarding-consultancy-report.md) and [readable companion](../consultations/i-vsd-tenant-onboarding-readable-summary.md)
- Plan binding: [plan revision 4](../../dev/active/contextual-tenant-onboarding/contextual-tenant-onboarding-plan.md), [task-contract revision 4](../../dev/active/contextual-tenant-onboarding/contextual-tenant-onboarding-tasks.md), and [shared context/evidence](../../dev/active/contextual-tenant-onboarding/contextual-tenant-onboarding-context.md)
- Decision authority: the user retained onboarding core and explicitly requested full revision-4 implementation on 2026-10-28. This authorizes implementation, not ethical outcomes, operational approval or scholarly certification.

## Executive Summary

The confirmed design protects learners by separating practice from real events
and real AI execution, while keeping the interface recognizable. Initial shell
guidance retains the brief Studio destination explanation when Studio is actually
available. Studio guidance begins inside consolidated management, and a separate
tour begins inside an individual event's management workspace. These are
connected explanations, not forced navigation or prerequisite courses.

The reviewed baseline already had separate Studio/event navigation, reusable
chat composer/timeline/action presentation, current-user preferences, and privacy
erasure. It did not yet provide the proposed learning resource, durable tour
family state, or safely simulated chat composition. Existing live AI orchestration
must not be used merely with a demo badge.

Revision-4 execution adds the bounded private backend and generated private API,
not the rendered learning workspace or chat presentation. Actual scoped results
and the independently reproduced baseline release blockers are recorded in
[the technical learning anchor](../../docs/internal/CONTEXTUAL_LEARNING.md#verification-and-release-readiness).
That implementation evidence does not resolve the 18 ethical findings by itself
or certify the required presentation, accessibility and comprehension outcomes.

The user resolved the scope gate: deliver onboarding core and preserve existing
archive/federation behavior. New public archives and historical recovery are
excluded and must graduate as separately actionable backlog work. This is a
resolved boundary, not an open question transferred to execution.

Revision 4 is ethically plan-aligned: every inherited pair has a concrete backend
owner, required successor, or explicit exclusion below. The active deliverable is
the durable private backend, followed by isolated practice presentation and then
contextual guidance. Backend completion does not deliver the rendered onboarding
experience. No finding is resolved by this document review; actual admission,
erasure races, simulation callbacks, accessibility and comprehension remain
evidence gates in their respective workstreams.

## Scope

Confirmed core: separate tenant-qualified learning resources; learner-private
practice; deterministic simulated AI using actual presentation components;
capability-aware shell, Studio, event-management, and AI interaction tours;
restart/dismissal; member/self-context learning without publication authority;
accessible contextual help and truthful learning entry/empty-state copy.

Excluded: new public history/archive behavior, historical federation recovery,
new production analytics/team/ticketing capabilities, live demo AI, conversion of
demo resources into events, and a tenant-editable lesson authoring platform.
Existing history actions remain truthful when actually available. Existing
production capabilities are taught, not rebuilt or advertised before they exist.

This assessment revalidates the plan, not implementation. The active backend
implements Domain/storage/erasure, authorized native CQS and private API/HAL with
generated contracts. Two separately approved successors own actual practice
presentation and contextual guidance. The paused enterprise tenant-setup
programme concerns configuration/provisioning, not learner tours.

## Claim Boundary

This is a provider-responsibility and software-design assessment, not a fatwa,
legal opinion, security certification, usability result, or runtime assurance
receipt. User confirmation establishes product direction, not implementation.
Official documentation establishes bounded framework behavior, not that this
application uses it correctly.

## Stakeholders

Learners who only browse; first-time and experienced organizers; administrators;
organization/group collaborators with different permissions; event teams;
visitors who must not mistake demos for community activity; account owners whose
learning data must be private and erasable; and operators responsible for
dependency provenance, resource limits, and accurate feature promises.

## I-VSD Principles And Domains

- Sidq and Avoiding Deception: distinguish authored simulation from real
  provider answers, executed actions, published events, and operational readiness.
- Amanah and Rights of People: honor dismissal, authorization, tenant boundaries,
  private learning state, account erasure, and non-resurrection.
- Non-Harm: prevent practice from producing notifications, orders, admission,
  publication, live AI calls, uploads, or production tools.
- Adl and Ihsan: provide learning without assuming publishing intent; teach
  controls in their actual context, with accessible dismissal and recall.
- Avoiding Spying: minimize progress/practice data and do not infer personas or
  measure conversion through unnecessary surveillance.

Domains: design, technical architecture, data/AI, deployment/dependencies,
operations, and evaluation.

## Findings

The inherited IDs preserve consultation meaning. "Accepted" concerns and
mitigations are planned, not proved implemented; no finding is resolved from
planning alone. The traceability index and explanations below bind revision 4;
historical evidence IDs retain their original dates and limits.

| Finding / mitigation | Assessment and controlled decision | Lifecycle / severity | Evidence / owner / required validation |
| --- | --- | --- | --- |
| IVSD-F001 / IVSD-M001 | Learning must be voluntary and available beyond administrators; real publishing authority is separate | accepted / high | User consultation; product and authorization owners; admitted versus denied learner/actor contexts |
| IVSD-F002 / IVSD-M002 | Separate resource identity and commands must prevent every real side effect, not just hide buttons | accepted / high | Current Event and AI boundaries; Domain/Application/API owners; direct-request and dispatch-negative invariants |
| IVSD-F003 / IVSD-M003 | Demos must not imply public activity or distort real inventory/accounting | accepted / high | Separate-resource decision; learning/discovery owners; ordinary inventories remain unchanged without demo predicates |
| IVSD-F004 / IVSD-M004 | Teach the installed product's real configuration model, including Studio versus selected-event management and ticket/payment prerequisites | accepted / medium | Consultation and bounded Studio reads; product/feature owners; capability inventory and comprehension review |
| IVSD-F005 / IVSD-M005 | Historical relevance/recovery is outside the selected core workstream; preserve current ingestion behavior | accepted / high | User scope selection; non-applicability is explicit, with discovery owner and actionable knowledge graduation |
| IVSD-F006 / IVSD-M006 | One tenant learning source requires atomic identity and intentional-removal semantics, with bounded private state | open / high | UUIDv7/tenant configuration precedents; persistence owner; concurrent creation, version changes, quota separation, deletion |
| IVSD-F007 / IVSD-M007 | Learning must remain accessible, restartable, maintainable, and usable without federation/live providers | accepted / medium | Consultation decision and accessibility guidance; learning/operator owners; keyboard/RTL/content versioning and honest unavailable-feature copy |
| IVSD-F008 / IVSD-M008 | A friendly empty message must describe the actual query state, including filters and operational errors | open / medium | Consultation public-experience finding; UI/discovery owners; scope-specific rendering and truthful available actions |
| IVSD-F009 / IVSD-M009 | Public welcome and private administrative/learning help need distinct audiences and server authority | accepted / high | Consultation and current HAL contracts; UI/authorization owners; anonymous versus admitted-user/administrator affordances |
| IVSD-F010 / IVSD-M010 | An empty upcoming schedule must not hide history that is actually available | open / medium | Consultation history finding; discovery owner; expose existing history affordances truthfully without assuming approval for new archives |
| IVSD-F011 / IVSD-M011 | Optional learning must be independent of participation and inferred publishing intent | accepted / high | Explicit user audience correction; product/authorization owners; no participation prerequisite, inferred persona, forced course, or fabricated creation powers |
| IVSD-F012 / IVSD-M012 | Self/member panels and actor-context practice remain private, separately authorized, and non-transferable | accepted / high | Consultation and current actor context; privacy/authorization owners; anonymous, nonmember, cross-tenant, revocation, cache/export tests |
| IVSD-F013 / IVSD-M013 | Family progress is distinct from suppressing all automatic onboarding; interruption and cross-tab claims cannot rely on browser storage alone | open / high | Current preferences and user consultation; persistence/UI owners; durable suppression, material versions, voluntary recall, deterministic concurrency |
| IVSD-F014 / IVSD-M014 | Demo resources belong to learning identity, persistence, and commands, not production Event flags | accepted / high | Explicit user confirmation; Domain/persistence owners; production IDs cannot resolve learning resources and no production effects |
| IVSD-F015 / IVSD-M015 | Keep Studio discovery in the shell, but start detailed tours only in their entered workspace | accepted / medium | User confirmation, Studio source, contextual-help guidance; shell/accessibility owners; exact entry/deep-link/focus/second-event scenarios |
| IVSD-F016 / IVSD-M016 | Actual chat presentation must use a separate deterministic simulation source and persistent truthful labels | accepted / high | User confirmation and shared components; AI/learning owners; no live service initialization, calls, history, tools, search, or uploads |
| IVSD-F017 / IVSD-M017 | User-owned learning state must enroll in current erasure fencing, cleanup, coverage, and tenant deletion | open / high | Current `PrivacyErasureApplier`, `UserLocationPrivacyErasureRepository`, and subject-state contracts; privacy/persistence owners; fence-versus-write race and completeness evidence |
| IVSD-F018 / IVSD-M018 | Tour-driver adoption needs package provenance and actual accessibility verification, not README claims | open / high | GuideFlow metadata/manual checklist, WAI-ARIA guidance; dependency/accessibility owners; exact artifact/license admission and rendered behavior |

### IVSD-F001: Learning Access Is Not Production Management Authority

A browsing learner should not need administrator or publishing power to learn.
Equally, opening a lesson must not grant those powers. The provider separates
active authenticated self access from current actor-member context and from
real creation/publication, with server-derived identity and fresh authorization.

- Claim / principles / domains: confirmed requirement; Adl, Amanah, Rights of People; design, technical, privacy.
- Stakeholders / control: browsing users, collaborators, administrators; admission and operation-specific authority.
- Evidence / validation: P01/P03/P04/P09, source/decision traceability only.
- Mitigation / owner / binding: IVSD-M001; authorization/product; S01/S02/S18/S21/S23; authorized-operations Red/Green and private-contract Red; guidance successor owns voluntary self/member entry.
- Escalation: any admission expansion or permission shortcut needs security review.

### IVSD-F002: Familiar Controls Cannot Cause Real Demo Effects

Learners recognize normal event/chat controls and may trust a successful receipt.
If those controls retain real callbacks, a tutorial can send invitations or create
financial/publication state. Learning-only commands and composition prevent the
effect; disabled buttons and a warning alone do not.

- Claim / principles / domains: confirmed invariant; Non-Harm, Sidq, Amanah; technical, data/AI.
- Stakeholders / control: learners, attendees, operators; resource identity, callbacks, dispatch.
- Evidence / validation: P01/P03/P04, architecture/source traceability, no execution receipt.
- Mitigation / owner / binding: IVSD-M002; learning/API/AI; S19/S20/S24-S27; authorized-operations Red/Green and private-contract Red/final assurance; practice successor proves actual callback/result navigation with a live positive control.
- Escalation: any real provider/payment/publication exercise is a scope change.

### IVSD-F003: Samples Cannot Stand In For Community Activity

A public visitor can mistake a synthetic card or count for genuine community
activity. Separate learning storage and private panels leave production lists,
accounting, and discovery untouched, rather than requiring every future reader
to remember an exclusion.

- Claim / principles / domains: confirmed requirement; Sidq, Avoiding Deception; design, technical.
- Stakeholders / control: visitors, organizers; public presentation and business inputs.
- Evidence / validation: P01/P09/P11, requirement/source traceability only.
- Mitigation / owner / binding: IVSD-M003; learning/discovery; S18/S28/S29; protected-storage Green, closed operations and unchanged-inventory API contracts; practice/guidance successors forbid synthetic public activity.
- Escalation: review any public learning placement or metrics change.

### IVSD-F004: Teach Actual Configuration And Management Scope

Studio consolidates events; selected-event management owns that event's controls.
Conflating them or showing unimplemented analytics/team functions misleads users
about what they can accomplish. The authored inventory maps installed
capabilities and explains payment prerequisites without implying connectivity.

- Claim / principles / domains: confirmed design and capability-inventory obligation; Sidq, Ihsan; design, evaluation.
- Stakeholders / control: organizers, event teams; lessons, destinations, readiness claims.
- Evidence / validation: P01/P03/P08/P11; bounded source/metadata, not complete feature audit.
- Mitigation / owner / binding: IVSD-M004; product/content; S03/S05/S06/S25/S34/S38-S40/S43; authorized-operations Green owns six capability-aware recipes; successors own installed-feature explanations and all audience/readiness evidence.
- Escalation: feature-owner review for newly advertised capabilities.

### IVSD-F005: Historical Recovery Is Explicitly Outside Core

An old-created event can still be relevant, and broad historical recovery needs
its own deletion/update/authority assurance. The user selected a narrower core
scope. Preserving current ingestion and graduating actionable recovery work
avoids falsely marking the consultation's larger discovery concern solved.

- Claim / principles / domains: scope decision and non-applicability; Amanah, Sidq; operations, technical.
- Stakeholders / control: external publishers, visitors, operators; ingestion scope and deferred work.
- Evidence / validation: P01/P09, explicit user decision; no recovery implementation reviewed here.
- Mitigation / owner / binding: IVSD-M005; discovery; explicit exclusion in plan Sections 1/6.1/9 and programme traceability, preservation S29; new historical recovery requires a separately approved workstream, not backend implementation.
- Escalation: a later recovery workstream needs its own source/deletion/replay scenarios.

### IVSD-F006: Shared Learning Lifecycle Must Respect Removal

Provisioning races can duplicate learning sources; reseeding can undo an
administrator's removal. Explicit-start creation, tenant uniqueness, durable
tombstones, and authorized recreation preserve intentional lifecycle decisions.
Private reset does not alter the shared source or other learners.

- Claim / principles / domains: proposed implementation invariant; Amanah, Rights of People; technical, operations.
- Stakeholders / control: administrators, learners; provisioning, versions, reset/removal.
- Evidence / validation: P01/P04/P15, current precedents, no migration/concurrency receipt.
- Mitigation / owner / binding: IVSD-M006; persistence; S15-S18/S20/S22; protected-storage Red/Green, authorized space lifecycle and private tombstone contracts; guidance successor owns remove/recreate controls.
- Escalation: destructive lifecycle changes require explicit user authority.

### IVSD-F007: Learning Must Be Accessible And Maintainable

Users who need keyboard, assistive technology, RTL, or reduced motion must be able
to learn and dismiss guidance. A provider outage or unconfigured federation must
not erase the value of synthetic lessons. Versioned original content and
in-context recall keep learning usable without promising unavailable features.

- Claim / principles / domains: design obligation; Ihsan, Adl; accessibility, evaluation, operations.
- Stakeholders / control: diverse learners, content maintainers; rendering, recall, offline lessons.
- Evidence / validation: P01/P06/P07/P08, published guidance/metadata, no accessibility certification.
- Mitigation / owner / binding: IVSD-M007; accessibility/learning; S09/S10/S14/S26/S32/S41; authored provider-independent catalogue in authorized-operations Green; successors own recall, actual keyboard/RTL/focus and unavailable guidance.
- Escalation: assistive/representative-user release review.

### IVSD-F008: Empty Copy Must Describe The Actual Query

A filtered empty result, failed load, and genuinely empty schedule are different.
Calling all three "no events" hides actionable information. Existing query status
and available actions determine restrained copy; private setup guidance belongs
to its authorized audience.

- Claim / principles / domains: confirmed UX obligation; Sidq, Ihsan; design, evaluation.
- Stakeholders / control: visitors and members; empty/error/filter states.
- Evidence / validation: P01/P09/P11, consultation/current-host evidence, no runtime rendering receipt.
- Mitigation / owner / binding: IVSD-M008; UI; S28; guidance successor's truthful empty/filtered/past/recovery/failed rendering. Backend tests do not discharge a rendering obligation.
- Escalation: review changes to public status semantics or unavailable-history claims.

### IVSD-F009: Public Welcome And Private Help Have Different Authority

Public pages must not leak private progress, membership, or administrator setup.
A private learning island checks current capability without adding personal data
to cached public SSR. The same server boundary applies to direct URLs, not just
visible cards.

- Claim / principles / domains: privacy requirement; Amanah, Avoiding Spying; design, privacy, technical.
- Stakeholders / control: visitors, members, administrators; private entry/API/cache placement.
- Evidence / validation: P01/P03/P04/P11, source/decision traceability only.
- Mitigation / owner / binding: IVSD-M009; UI/API/privacy; S02/S23/S28/S31; current-authority operations and private/no-store API/HAL contracts; successors prove public-safe prerender, authenticated fetch and no private-state transfer/cache.
- Escalation: any private prerender/cache/export expansion requires privacy review.

### IVSD-F010: Preserve Available History Without Building New Archives

An empty upcoming list does not prove no event has ever existed. Existing history
affordances remain visible when authoritative data provides them, while new
archive APIs are excluded. Neither a fabricated history button nor a claim that
the broader archive requirement is implemented is acceptable.

- Claim / principles / domains: narrowed applicability; Sidq, Ihsan; design, operations.
- Stakeholders / control: visitors, discovery owners; existing navigation/copy.
- Evidence / validation: P01/P09, core boundary; broader archive completeness not reviewed.
- Mitigation / owner / binding: IVSD-M010; discovery/UI; S28/S29; guidance successor preserves actual existing history affordances. New archive APIs/recovery remain excluded.
- Escalation: new archive capability is a separate scoped plan.

### IVSD-F011: Optional Learning Cannot Infer Publishing Intent

Attending, browsing, and collaborating are valid uses without becoming a
publisher. A forced course, participation prerequisite, or guessed persona would
constrain those users. Learning is optional and restartable; actual action
authority comes from existing capabilities, not progress or inferred intent.

- Claim / principles / domains: confirmed audience decision; Adl, Amanah; design, privacy.
- Stakeholders / control: browsing/participating users and collaborators; admission, prompts, telemetry.
- Evidence / validation: P01/P09, explicit product direction.
- Mitigation / owner / binding: IVSD-M011; product/authorization; S01/S04/S12-S14/S35-S40; authorized self/member admission is independent of publication; guidance successor owns optional Start/Skip/Help and audience coverage without personas.
- Escalation: conversion profiling or progress-gated permissions changes scope.

### IVSD-F012: Member Context Does Not Make Practice Shared

Collaborators may learn in an actor context without managing that actor's
publication. Their progress and choices remain private. Every contextual read
rechecks membership; revocation/deletion makes the context unavailable rather
than transferring its data or authority.

- Claim / principles / domains: confirmed privacy boundary; Amanah, Avoiding Spying, Rights of People; technical, privacy.
- Stakeholders / control: self/member learners; scope keys, member checks, private storage.
- Evidence / validation: P01/P03/P04, current authority/ownership precedents only.
- Mitigation / owner / binding: IVSD-M012; privacy/authorization; S02/S20/S23/S30/S31; protected ownership/erasure, live membership/revocation and owner-only clearing, private transport; successors preserve private interaction boundaries.
- Escalation: shared practice or new retention/export policy requires review.

### IVSD-F013: Dismissal And Account-Level State Must Be Honest

Skipping one tour is not suppressing all onboarding. Browser-only state cannot
honor a tenant/account promise across devices. Durable family outcomes, separate
global suppression, bounded claims, and voluntary recall make the controls
explainable without claiming impossible offline exactly-once display.

- Claim / principles / domains: selected design contract; Amanah, Sidq; technical, evaluation.
- Stakeholders / control: returning/multi-device users; preferences, claims, versioning.
- Evidence / validation: P01/P04/P07, design/framework guidance, no concurrency receipt.
- Mitigation / owner / binding: IVSD-M013; Domain/persistence/UI; S12-S17; protected-storage history/suppression/claim bounds and authorized claim/outcome/preferences; guidance successor proves invitation skip, replay preservation and explicit-activity renewal without heartbeat.
- Escalation: changes to global suppression or version replay need product review.

### IVSD-F014: Separate Learning Identity Is The Safety Boundary

Normal Event rows with special flags make every future query/worker a risk.
Learning aggregates and commands have no production effect dependency or
resolvable Event identity. Shared UI gives fidelity without shared execution;
the real workflow starts separately.

- Claim / principles / domains: user-confirmed architecture; Non-Harm, Amanah, Sidq; technical, operations.
- Stakeholders / control: learners, event owners, operators; resource/persistence/effect ownership.
- Evidence / validation: P01/P04/P09/P15, explicit requirement/current patterns, not isolation proof.
- Mitigation / owner / binding: IVSD-M014; Domain/persistence/Application; S18-S22/S29; separate storage plus operations/API effect-denial and provider assurance; practice successor forbids production hosts, fictional production IDs and conversion.
- Escalation: combining demos with production persistence or dispatch requires replanning.

### IVSD-F015: Connected Tours Belong To Their Entered Workspace

Removing Studio discovery is unhelpful, but teaching its complete navigation
before entry asks users to remember unrelated information. A short destination
explanation followed by specific-context tours preserves discovery and timing,
with one focus owner and no repeated full tour per event.

- Claim / principles / domains: user-confirmed design, usability unproved; Ihsan, Adl; design, evaluation.
- Stakeholders / control: organizers and browsing users; triggers, readiness, arbitration.
- Evidence / validation: P01/P03/P06/P09, decision/source/guidance, not observed optimum.
- Mitigation / owner / binding: IVSD-M015; shell/Studio/accessibility; S03-S11/S45-S48; backend family/context contracts support guidance successor's actual Studio discovery, explicit-Start invitations, one overlay and generation/readiness cancellation.
- Escalation: materially changed automatic triggers need representative-user review.

### IVSD-F016: Actual Chat Must Not Pretend To Be Live AI

The real composer and action card create expectations of real answers and
execution. Authored responses, persistent accessible simulation labels, and
learning-only callbacks make the mode truthful. Unsupported input cannot become
a live fallback, and normal AI receives no transferred demo history/actions.

- Claim / principles / domains: user-confirmed invariant; Sidq, Non-Harm, Avoiding Spying; data/AI, technical.
- Stakeholders / control: learners, providers, operators; composition, calls, status/history.
- Evidence / validation: P01/P03/P09, inspected shared presentation, no demo runtime proof.
- Mitigation / owner / binding: IVSD-M016; AI/learning/privacy; S24-S27/S33; closed deterministic catalogue and no-effect operations; practice successor proves actual chat composition, persistent simulation disclosure, safe rendering and no cross-mode transfer.
- Escalation: live rehearsal or cross-mode transfer is a material scope change.

### IVSD-F017: New Private State Must Participate In Erasure

Progress and contextual choices are entrusted user data even when examples are
fictional. A new table can escape existing purge enumeration or reappear during a
write/erasure race. Actual enlistment, coverage, remaining-data checks, and
internal-user ownership must be adopted before the API can persist it.

- Claim / principles / domains: implementation risk/invariant; Rights of People, Amanah, Avoiding Spying; privacy, technical.
- Stakeholders / control: account owners and operators; fenced writes and complete cleanup.
- Evidence / validation: P04/P14, current-source traceability, no new-table assurance.
- Mitigation / owner / binding: IVSD-M017; privacy/persistence; S30/S31; protected-storage retained fencing and common purge/absence before coverage/native exits; final admission and five-engine erasure/replay/rollback assurance at backend exit.
- Escalation: incomplete authority/cleanup blocks storage adoption and release.

### IVSD-F018: Dependency Claims Are Not Hosted Assurance

A README's keyboard/accessibility claims do not prove behavior in this shell.
Unresolved artifact/license metadata and known manual-checklist issues must be
settled before adoption. Final hosted tests must prove focus, dismissal,
positioning, and isolation across actual contexts.

- Claim / principles / domains: admission/evaluation gate; Amanah, Ihsan; deployment, evaluation.
- Stakeholders / control: maintainers and assistive users; package artifact, hosting, release.
- Evidence / validation: P06/P07/P08, public guidance/metadata only.
- Mitigation / owner / binding: IVSD-M018; dependency/accessibility; S32/S34; no backend dependency; guidance successor must admit the exact artifact/transitive/license/framework before dependent code. Both successors retain actual-host accessibility gates.
- Escalation: failed admission requires a concrete dependency decision, not an unapproved fallback.

## Recommendations

Keep confirmed product decisions fixed. Use one shared evidence packet for the
plan, tasks, and report. Keep the resolved core scope during decomposition. Choose
small learning-specific contracts, not an imitation of the broad live AI service
or a new general workflow framework. Reuse pure presentation through explicit
callbacks and data, with no live service wiring in simulation.

Use server-derived tenant and internal user identity. Bind learner storage to the
existing privacy/erasure authority, not invented generic catalogue or fencing
interfaces. Learning purge and all-tenant absence verification belong in the
common protected `PrivacyErasureApplier` transaction after retained recognition
and subject fencing, before both coverage replay and native PostgreSQL early
exits. Extending only `UserLocationPrivacyErasureRepository` misses the native
path. A failed absence check must roll back settlement, not issue coverage.

The inspected hard-purge implementation explicitly enumerates owned data,
including current user preferences. Do not assume a new table is erased merely
because it has a user foreign key or cascade: extend the existing owned-data
purge, remaining-data check, and coverage contract with regression evidence.

Keep completion, global automatic suppression, template lifecycle, and practice
as distinct state. No tour is a permission gate. A completed management family
must not repeat for each event or actor, and changing AI host must not repeat
normal chat guidance.

## Common Overlooked Failures And Outcomes

Live history/provider initialization hidden behind a simulation badge; ordinary
Event DTO links that accidentally call production mutations; event-management
tours on public details; stacked tours on a deep link; a skipped family mistaken
for global suppression; progress keyed to every event; orphan learning data after
erasure; practice recreation while deletion is fenced; and unavailable analytics
or team features advertised as shipped.

## Evidence Reviewed

| ID | Verified evidence and limit |
| --- | --- |
| P01 | Both tenant-onboarding consultation documents and explicit user corrections; product inputs, not runtime proof |
| P02 | `git fetch origin develop && git pull --ff-only origin develop` completed without branch changes; HEAD remains `072f2b6dbd8e4b87e16b795bc02a9ea23f902046`; unrelated local edits remain |
| P03 | Current Studio navigation, `StudioEventContextState`, shell workspace availability, and shared AI composer/timeline/action components; bounded source inspection only |
| P04 | Current `User`, `UserPreference`, appearance handler/configuration, `IUnitOfWork`, `IPrivacyErasureStateRepository`, `PrivacyErasureApplier`, and registered erasure repository; no product tests run |
| P05 | `AGENTS.md`, matched intent/rule slices, `PROJECTS.md`, architecture/API/Blazor conventions, planning and I-VSD contracts, paused tenant-setup context, and related active discovery/localization contexts |
| P06 | Tavily: [NN/g contextual help](https://www.nngroup.com/articles/onboarding-tutorials/) and [WAI-ARIA modal-dialog pattern](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/); functional guidance, not product assurance |
| P07 | Context7 resolved ASP.NET Core and EF Core documentation; [Blazor lifecycle](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/lifecycle) and [EF concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency); no documentation sample copied into implementation |
| P08 | GuideFlow README, project/package metadata (`GuideFlow`, `1.0.1`, `net8.0`, MIT expression), license text, and manual checklist only; no third-party implementation source inspected or dependency added |
| P09 | Historical user answer "Scope: Onboarding core (Recommended)"; authorized planning the core while preserving archive/federation behavior |
| P16 | 2026-10-28 document-only revalidation of exact plan/task revision 4, context and ordered practice/guidance successor briefs; all 18 pairs preserved and mapped, with no runtime or scholarly assurance |
| P17 | 2026-10-28 current user explicitly requested full implementation of `dev/active/contextual-tenant-onboarding/` using `implement-tasks`; implementation approval, not proof of outcomes or successor-plan approval |

The context packet additionally binds P10-P15 generation, host, test, overlap,
source-conflict and provider evidence. It is the detailed authoritative packet;
this section is its bounded index, not a second independent investigation.

## Context Inventory

The historical graph/source notes above belong to October 11. Revision 4's
context supplies October 28 source/graph anchors at
`20737e450db0df748b3e5737bcb3405f54c37cd8`; this ethical revalidation consumed
those documents, not an independent runtime investigation. Current application-
owned persistence, common erasure exits and generated wire ownership supersede
the old seam assumptions.

No copied third-party code, expressive assets, or implementation structure enters
the plan. External research is reduced to functional requirements for contextual
timing, truthful capability disclosure, focus/dismissal, and portable concurrency.

## Validation Gaps

Revision 4 separates backend evidence from required successor evidence. Backend
execution must prove original-transaction admission, erasure/write races, covered
replay/native PostgreSQL purge and five-engine concurrency/migrations. Practice
must prove actual simulation callbacks/navigation with a live positive control.
Guidance owns package admission; both successors retain hosted accessibility and
representative comprehension release gates. This document review ran no product
tests, migrations or hosted sessions.

## Escalation Needed

The adjacent-discovery boundary is resolved. No additional ethical scope
decision or scholarly escalation was identified in revision-4 revalidation.
Security/privacy assurance remains a backend completion gate; hosted
accessibility/comprehension remains a programme release gate after successors.
Live AI simulation, actual payment/provider exercises, real publication, or
cross-mode prompt/history transfer would change the confirmed scope.

## Missing Evidence

Generated-client/migration receipts, package admission, operational/UI assurance,
and erasure race/completeness results remain missing. The aligned triad and
finding/scenario/task coverage are planning artifacts, not runtime receipts.

## Review Lifecycle

| Date | State | Reason |
| --- | --- | --- |
| 2026-10-11 | draft / needs-revision | Shared evidence and confirmed direction captured; adjacent scope intake and triad mappings must complete before plan alignment |
| 2026-10-11 | draft / advisory | User selected onboarding core; discovery expansion is excluded and triad/finding binding is the remaining planning gate |
| 2026-10-11 | current / plan-aligned | Bound plan revision 1 and its single tasks/context triad; all 18 findings map to scenarios/tasks or explicit core-scope non-applicability; implementation assurance remains open |
| 2026-10-28 | stale / changes-required | Material revision-4 delivery split and current persistence/erasure/wire boundaries invalidated revision-1 bindings |
| 2026-10-28 | current / plan-aligned | Revalidated all 18 pairs against exact plan/task-contract revision 4 and both successor boundaries; current user authorizes backend implementation, with operational evidence still open |

## Planning Handoff

- Workstream: contextual-tenant-onboarding.
- Status: current; disposition: plan-aligned.
- Reviewed input: plan revision 4, task-contract revision 4, context and ordered practice-presentation then contextual-guidance briefs.
- Findings and mitigations: all IVSD-F001-F018 map to the identically numbered IVSD-M001-M018 in the binding entries above; no IDs or historical lifecycle states were removed.
- Required plan mappings: protected storage owns lifecycle/erasure/claims; authorized operations own current human scope and closed simulation; private contracts own transport/HAL/generated and provider assurance. Presentation/guidance successors own actual UI and release evidence; F005 historical recovery is explicitly excluded, F010 preserves only existing history.
- Escalations required before: backend completion requires physical privacy/security/provider assurance; dependent tour code requires exact package admission; programme release requires hosted accessibility and representative comprehension. No new ethical decision blocks the specified backend.
- Refresh triggers: changed scope, consent, retention, telemetry, authority, actual simulation effects, private rendering, dependency choice or successor boundaries.

Refresh this assessment for material changes to admission, storage/retention,
resource identity, tour triggers/suppression, live AI boundaries, erasure,
dependency choice, or discovery/federation scope.
