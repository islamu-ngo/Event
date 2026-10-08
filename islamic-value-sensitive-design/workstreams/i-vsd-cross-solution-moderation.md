# I-VSD: Cross-Solution Moderation Implementation Planning

Last Updated: 2026-10-08 Europe/Brussels

## Review Metadata

- Mode: planning
- Subject: Scoped moderation, banning, identity continuity and fair remedies
- Workstream: cross-solution-moderation
- Report kind: implementation-planning-assessment
- Report status: current
- Disposition: plan-aligned
- Revision: P1-2026-10-07
- Evidence cutoff: 2026-10-07
- Reviewed input: working-tree consultation `R7-2026-10-07`, Git blob `e42db61a27df7627e9578d452e21e2e2a6973c1f`, repository HEAD `8633a693a290afb388805616e4f893d189100c44`, and the shared current-state evidence packet
- Supersedes: none; the foundational consultation remains authoritative input, not replaced
- User approval: planning authorized; implementation and live policy activation not approved
- Finding namespace: this workstream uses `IVSD-F101` through `IVSD-F109` and matching mitigation IDs. Consultation IDs `IVSD-F001` through `IVSD-F048` remain unchanged and separately mapped.

## Executive Summary

People need protection from substantiated abuse without losing unrelated rights, support, privacy remedies or access to challenge a mistake. Operators need one complete locally runnable moderation engine, with optional shared authority inside an explicitly enrolled trust domain, rather than a collection of suspension booleans and provider callbacks.

The user requests a concrete implementation plan for the consultation's moderation and banning requirements, explicitly excluding chatting. Chat transport, conversations, contact permissions, youth rooms and comment-product construction are outside this workstream. Non-chat spam controls, service-principal restrictions, event/form fraud protection, age-sensitive audience enforcement, notices and protected case submissions remain included.

Current repository evidence supports native CQS, domain-owned transitions, tenant-qualified persistence, local authorization, transactional outbox work and privacy-erasure authority-first ordering. These are useful foundations, not an implemented general sanction or appeal system. After the intake question timed out, the user instructed completion using best judgment. The planning decision is bounded recognition of qualifying final active restrictions: capability-only restrictions retain their exact scope until the original expiry without blocking ordinary provisioning; admission-prohibiting restrictions block covered replacement provisioning. Lawful processing and retention remain deployment activation gates.

This draft identifies responsibility and approval boundaries. It does not certify legal compliance, issue a religious ruling, approve operating policy or claim any moderation mechanism has been implemented.

## Scope

Included: explicit jurisdiction and trust enrollment; human, service, organizational and content subjects; scoped sanctions and protective controls; policy/catalogue configuration and portability; cases, adjudication, contestation, support correction and notices; organizer continuity, resources, admission and private-location consequences; non-chat spam/fraud defenses; private DOB/age eligibility and classification; recovery, retention and standalone/remote conformance.

Excluded: chat and comment products, their transport and membership/contact/encryption controls; universal cross-host identity correlation; new payment processors; unrelated security-programme repairs; runtime implementation during planning.

### Consultation Origin And Future Workstreams

This I-VSD workstream and its implementation plan originate from [islamic-value-sensitive-design/consultations/i-vsd-banning-system-and-moderation-identity-fence.md](../consultations/i-vsd-banning-system-and-moderation-identity-fence.md). They take the consultation's relevant moderation, banning, identity-continuity, remedy and non-chat safety requirements into this bounded workstream; they do not implement the consultation in its entirety or supersede it.

The consultation's chatting requirements and other proposed work not relevant to this moderation implementation remain outside this workstream and belong to separate future workstreams. They are not discarded, implicitly implemented, or prerequisites for completing this plan. Future workstreams must return to the consultation and establish their own scope, I-VSD assessment and implementation plan. Requirements explicitly rejected by the consultation or this plan remain rejected; exclusion alone does not authorize them as future features.

For mixed requirements, retain the relevant non-chat obligations here and leave only the chat or unrelated product portion to future workstreams. Protected case submissions and transactional notices remain moderation functions, not a chat product. The implementation plan's finding, scenario and profile disposition tables record this selective scope.

## Claim Boundary

The consultation supplies proposed behavior, not proof of runtime coverage. Repository-inspected facts, official functional constraints, design choices and unvalidated operating outcomes remain distinct. DOB is not proof of age, HMAC recognition remains personal-data processing, service-provider routing does not grant adjudication authority, and a nonprofit designation does not supply universal legal exemptions.

## Findings

### IVSD-F101: Protection needs legitimate, bounded jurisdiction

A mistaken local finding must not exclude a person from unrelated communities or solutions. The provider owns enrollment, qualified identity linkage, conflict-free approvals and the exact scope of a decision. Reuse the consultation's jurisdiction and attribution requirements, including independent evidence for participant accusations; never infer wider authority from an organizer role, provider callback or shared identity provider.

- Lifecycle: open; severity: critical; claim type: design risk.
- Principles/domains: justice, trust and accountable governance; affected people, independent hosts and reviewers.
- Provider-controlled decision: accepted issuers, scope ceilings, linkage proof and trust membership.
- Evidence: consultation findings 004, 007, 009-011, 015, 039-040; inspected actor/report handlers and authorization documentation.
- Validation level: source-grounded design, not operating evidence.
- Mitigation `IVSD-M101`: explicit trust constitution and checked subject/issuer/scope/capability contract.
- Owner/next validation: platform steward and authority/invariant implementation slice.
- Escalation boundary: trust-domain participation and qualified independent-review staffing before activation.

### IVSD-F102: Erasure continuity must not become a permanent shadow identity

Account deletion can defeat an active restriction if recognition loses its subject. Conversely, keeping reusable identity fingerprints for every accusation or expired sanction creates excessive surveillance and obstructs fresh participation. Existing erasure intentionally allows an admissible fresh internal identity and does not implement an active-ban ledger. The plan must explicitly decide which final active sanctions qualify for bounded recognition, while preserving erasure of DOB, profiles, consent and unrelated history.

- Lifecycle: open; severity: critical; claim type: unresolved provider-responsibility decision.
- Principles/domains: privacy, dignity, proportional protection and rehabilitation; erased subjects and potential victims.
- Provider-controlled decision: qualifying final active sanctions, purpose, retention, identity proof and key custody; warnings, allegations and ordinary erasure do not qualify.
- Evidence: consultation findings 001-002, 004, 006-007, 016, 018, 045; `PRIVACY_ERASURE.md` sections on serialized enrollment and reserved recognition.
- Validation level: verified current erasure contract plus proposed extension.
- Mitigation `IVSD-M102`: purpose-separated, scope-preserving recognition with expiry, qualified linkage, correction and restore protection.
- Owner/next validation: privacy steward and identity-continuity implementation slice.
- Escalation boundary: scope-preserving capability continuity selected under the user's best-judgment instruction; lawful processing and retention before activation. This is not a user-approved live retention policy.

### IVSD-F103: Current enforcement must preserve remedies during failure

A prior token, queued operation or stale replica must not preserve a prohibited action. Equally, authority failure must not silently turn a restricted session into total loss of safety, complaint or privacy support. The provider owns each supported capability's current check, truthful unavailable response and bounded recovery route.

- Lifecycle: open; severity: critical; claim type: design risk.
- Principles/domains: harm prevention, fairness and truthful service; subjects, other users and operators.
- Provider-controlled decision: freshness, offline limits, unavailable behavior and remedy exceptions.
- Evidence: consultation findings 003, 008, 012-014, 017-018, 028; native operations and fail-closed authorization contracts.
- Validation level: source-grounded design.
- Mitigation `IVSD-M103`: exhaustive non-chat enforcement inventory, current revision checks, idempotent propagation and explicit recovery channels.
- Owner/next validation: capability integration and authority-replication slices.
- Escalation boundary: measured exposure budgets and supported offline behavior before release.

### IVSD-F104: Configuration must not rewrite past decisions or widen power

Operators need editable reasons and simple or advanced profiles without changing the meaning of an existing sanction. Imports and extensions must not carry people, secrets, adjudicator privileges or executable authority. The same governed mutation must validate settings, catalogues, profiles and complete consequences before coordinated activation.

- Lifecycle: open; severity: high; claim type: design risk.
- Principles/domains: accountable stewardship and consistent treatment; small hosts, communities and sanctioned subjects.
- Provider-controlled decision: safety floors, versioned definitions, personalization and supported effects.
- Evidence: consultation findings 020-025, 037-038; hierarchical settings and configuration-portability contracts.
- Validation level: proposed design grounded in existing configuration patterns.
- Mitigation `IVSD-M104`: immutable published revisions, explicit overrides, simulated impacts and authority-neutral portable definitions.
- Owner/next validation: policy/configuration implementation slices.
- Escalation boundary: rights-changing policy and new executable/dependency extensions require renewed review.

### IVSD-F105: Restriction must remain understandable and contestable

People must learn the factual reason, interval, exact consequences and remaining actions, and be able to correct a mistake. A support role is not an unban bypass; a qualified remedy must be a revision-bound domain transition. Email supplements durable notices and cannot be the authority to lift or impose restrictions.

- Lifecycle: open; severity: high; claim type: design risk.
- Principles/domains: justice, dignity and meaningful recourse; subjects, reporters, support and independent reviewers.
- Provider-controlled decision: accessible notices, evidence disclosure, conflicts, support delegation and response procedures.
- Evidence: consultation findings 014-015, 026-032; current event-report/UI and notification foundations.
- Validation level: current seams verified, proposed general workflow unimplemented.
- Mitigation `IVSD-M105`: protected contestation, independent review, guided explanation, scoped correction and durable subject/admin notices.
- Owner/next validation: case, remedy, notification and restricted-experience slices.
- Escalation boundary: legal deadlines, safe disclosure and staffing before operational activation.

### IVSD-F106: Event consequences must not punish unrelated participants

An organizer restriction must not orphan booked events; digital discipline is not automatically physical exclusion. Speaker reading, actual-participant admission, private address disclosure and financial settlement need independently justified, deterministic effects. A report is an allegation, and controlled rosters alone are not proof of attendance or identity.

- Lifecycle: open; severity: critical; claim type: design risk.
- Principles/domains: harm prevention, property/contract stewardship and fairness; attendees, guests, speakers, organizers and venue occupants.
- Provider-controlled decision: continuity, qualified resource exceptions, physical jurisdiction and supported settlement recipes.
- Evidence: consultation findings 033-040; existing event, registration, resource, calendar and participant seams.
- Validation level: integration design, not venue or settlement proof.
- Mitigation `IVSD-M106`: explicit impact plans and owning-domain commands, participant-qualified checks and separately approved emergency protection.
- Owner/next validation: event consequence and participant incident slices.
- Escalation boundary: premises authority, financial obligations and offline limits before enabling affected profiles.

### IVSD-F107: Publication safety must work without advisory providers

A badge or initial approval cannot authorize later deceptive content, a substituted destination or a form collecting account passwords or wallet secrets. Local budgets and publication/form guards must exist independently of Coop/Osprey. Protective uncertainty must not be converted automatically into confirmed guilt.

- Lifecycle: open; severity: high; claim type: design risk.
- Principles/domains: truthful dealing, harm prevention and proportional response; publishers, prospective attendees and victims.
- Provider-controlled decision: prohibited collection, material re-review, local budgets and incident handling.
- Evidence: consultation findings 042-044, 047-048; form-authoring/publication seams and report-provider boundaries.
- Validation level: proposed non-chat controls, not proof of scam detection.
- Mitigation `IVSD-M107`: version-bound review, native operational limits, pre-storage secret guards and evidence-based scoped findings.
- Owner/next validation: non-chat abuse and publication/form integrity slices.
- Escalation boundary: invasive profiling, new third-party dependencies and already-collected-secret incidents require separate qualified handling.

### IVSD-F108: Age protection requires privacy and every disclosure path

The requested ISLAMU debate rule cannot rely on a visible category or a declared birthday alone. Unknown or insufficiently assured age must not become adult eligibility; an adult purchaser must not bypass the actual participant's floor. Private DOB and derived eligibility require correction, retention and erasure, without inferring religion or making children disciplinary subjects merely for ineligibility.

- Lifecycle: open; severity: critical; claim type: design risk.
- Principles/domains: child welfare, privacy, dignity and fair choice; children, adults, guardians and publishers.
- Provider-controlled decision: lawful DOB collection, accepted assurance, classification and safe audience defaults.
- Evidence: consultation findings 019, 045-048; current identity/age/form/discovery contracts.
- Validation level: explicit operator policy and proposed integration, not an age-verification guarantee.
- Mitigation `IVSD-M108`: private purpose-bound age authority, separate content classification, supported-path gates and accessible correction.
- Owner/next validation: age/classification and audience-integration slices.
- Escalation boundary: legal/child-risk assessment, acceptable assurance and qualified classification review before activation.

### IVSD-F109: Advertised trust must match implemented and staffed coverage

Single-binary operation must supply the same supported local moderation behavior as optional remote mode. Recovery must not replay overturned decisions or conceal stale authority. A strict preset cannot be advertised merely because its configuration imports successfully: dependencies, complete handlers, review capacity and measured limits must be checked.

- Lifecycle: open; severity: high; claim type: design and operational risk.
- Principles/domains: truthful claims and accountable trust; operators, affected subjects and participating solutions.
- Provider-controlled decision: readiness, backup horizons, conformance claims and operational capacity.
- Evidence: consultation findings 006, 008, 012, 017-019, 029, 048; standalone and privacy recovery foundations.
- Validation level: source-grounded design; runtime/operational proof missing.
- Mitigation `IVSD-M109`: shared conformance, fail-closed readiness, recoverable outbox/inbox and explicit activation prerequisites.
- Owner/next validation: remote/standalone and recovery/conformance slices.
- Escalation boundary: performance, backup/rotation recovery and independent staff readiness before release claims.

## Recommendations

Draft one dependency-ordered triad using the resolved recognition qualification contract. Keep all mandatory non-chat scope in that triad. Use relational domain state and native CQS rather than forcing event sourcing; embedded and remote bindings use the same semantic contract. Do not reduce standalone to an advisory-only client or infer universal federation.

## Stakeholders

Sanctioned and erased subjects; reporters and victims; children and adults; event participants, guests and speakers; organizers and venue occupants; small self-hosters; official ISLAMU operators; independent reviewers, support staff and moderators; enrolled solution operators; qualified privacy, legal and scholarly advisers.

## I-VSD Principles And Domains

Justice and proportionality govern proof, scope and remedy. Trust and accountability govern issuers, auditable configuration and truthful operating claims. Privacy and dignity govern identity linkage, evidence disclosure and rehabilitation. Harm prevention and child welfare govern bounded urgent protection and age-sensitive access. Contract/property stewardship governs event continuity and financial consequences. These guide provider choices; they do not replace qualified legal or scholarly determinations.

## Common Overlooked Failures And Outcomes

Deleting an account to escape a capability restriction; treating provider failure as a clean record; importing a profile that grants issuer power; replaying a reversed sanction after restore; turning complaint volume into recurrence; allowing a parent buyer to bypass a child's age floor; reusing cleared event content after a form/destination swap; losing support access during a ban.

## Validation Gaps

The complete triad passed document parsing, identifier/range mapping, link, whitespace and native packet-syntax checks. It defines 17 phases, 25 behavior requirements, 55 minimum independent scenario anchors and every consultation scenario/profile disposition. No product build, test, migration, runtime QA, legal applicability assessment, staffing validation or package extraction has been performed. Design alignment cannot close implementation findings.

## Escalation Needed

Planning scope decision is resolved. Activation still requires qualified processing/retention decisions, trust constitution, independent review, safeguarding, premises/financial authority and supported freshness/backup budgets.

## Evidence Reviewed

- [Foundational consultation R7](../consultations/i-vsd-banning-system-and-moderation-identity-fence.md), including its current-state evidence, 48 finding/mitigation pairs and 180 scenarios.
- [Project boundaries and verification](../../PROJECTS.md).
- [Privacy erasure](../../docs/internal/PRIVACY_ERASURE.md), serialized enrollment and reserved moderation recognition.
- [Authorization](../../docs/internal/AUTHORIZATION.md), local/Cerbos selection, current native authorities and machine ceilings.
- [Implementation plan](../../dev/active/cross-solution-moderation/cross-solution-moderation-plan.md) and [task ledger](../../dev/active/cross-solution-moderation/cross-solution-moderation-tasks.md), revision `P1-2026-10-07`.
- Read-only current-source packets for actor/report moderation, privacy enrollment, event consequences, notices, registration/form and age seams.
- Upstream refresh completed successfully on `develop`; HEAD remained `8633a693a290afb388805616e4f893d189100c44`.
- Tavily primary-source research and three Context7 documentation queries succeeded in the independent research track. Its source-free handoff is recorded in the [planning context](../../dev/active/cross-solution-moderation/cross-solution-moderation-context.md). Main-session failures do not negate the successful child calls, and successful child calls do not imply the main-session servers worked.
- Official GDPR/DSA/EDPB sources establish purpose-limited retention, meaningful remedy and proportional age assurance, subject to actual deployment applicability. Official .NET/EF documentation establishes resource-dependent authorization, portable concurrency tokens, local transaction boundaries, commit-uncertainty handling and explicit startup readiness on .NET 10. These constraints do not select live policy or prove implementation.

## Missing Evidence

Current graph MCP was disabled in the main session and unavailable to repository scouts; the research child's graph session expired. Main-session Tavily and Context7 MCP attempts were disabled. The research child successfully used both, but Context7 mixed versions and supplied unsolicited samples; its sanitized handoff excludes those samples. Prior consultation research remains prior evidence. No runtime outcome is inferred from research or document inspection.

## Context Inventory

The original user requirements, explicit chat exclusion, consultation R7, current repository evidence and contribution/planning contracts are available. No same-scope active or paused triad was found by bounded filename/content inspection. No external solution repository has been inspected or is authorized for modification.

## Planning Handoff

- Workstream: cross-solution-moderation.
- Status: current / plan-aligned.
- Plan/task/context ownership: the single triad in `dev/active/cross-solution-moderation/`, revision `P1-2026-10-07`.
- Reviewed input: consultation R7 and shared current repository evidence.
- Findings and mitigations: `IVSD-F101`-`IVSD-F109` map to `IVSD-M101`-`IVSD-M109`.
- Required plan mappings: every workstream pair plus every consultation pair/scenario/profile, with explicit chat exclusions.
- Escalations required before: deployment-specific legal, safeguarding and policy activation; no material planning question remains open.
- Refresh triggers: scope, identity linkage/recognition, issuer powers, retention, defaults, consequence recipes, age assurance, third-party dependencies or operational guarantees materially change.

### Planning Mitigation Map

These are committed design obligations, not resolved runtime findings. The consultation's separate F001-F048/M001-M048 and SC01-SC180 ledger is fully mapped in plan Section 9, including explicit chat exclusions.

| Finding / mitigation | Behavior and independent anchors | Concrete implementation ownership / evidence |
| --- | --- | --- |
| `IVSD-F101` / `IVSD-M101` | MR01/04/05/14/22; MC01/07/08/27/43 | 01.1-01.4, 03.1-03.4, 11.1-11.5, 16.1-16.5; scope/quorum/participant/enrollment public-seam and real-engine tests |
| `IVSD-F102` / `IVSD-M102` | MR08/09/24; MC15-MC18/47/48 | 06.1-06.5; durable computed recognition before purge, original-input destruction/reversal witness, key/purpose/restore and admission races |
| `IVSD-F103` / `IVSD-M103` | MR02/03/10/22; MC03-MC06/19/44/55 | 02.1-02.4, 07.1-07.4, 08.1-08.4, 16.1-16.5; typed current/unknown decisions and deadline-qualified receipts |
| `IVSD-F104` / `IVSD-M104` | MR06/07/25; MC11-MC14/49 | 04.1-04.4, 05.1-05.4; all supported profile dimensions, proposal/import races, neutral definitions and complete activation |
| `IVSD-F105` / `IVSD-M105` | MR10-MR12; MC19-MC24/52/54 | 08.1-08.4, 09.1-09.5; free protected eligibility/external routes, scoped proof/remedy, safe notices and reviewer exposure controls |
| `IVSD-F106` / `IVSD-M106` | MR13-MR16; MC25-MC32/53 | 10.1-10.4, 11.1-11.5, 12.1-12.4; continuity/material/admission/reschedule/settlement/derivative owning-domain witnesses |
| `IVSD-F107` / `IVSD-M107` | MR17/18/21; MC33-MC36/41/42 | 13.1-13.5, 15.3-15.4; provider-independent protection, material/form pre-storage guard and distinct final recurrence |
| `IVSD-F108` / `IVSD-M108` | MR19-MR21; MC37-MC42/51 | 14.1-14.5, 15.1-15.4; individual lawful collection gate, private/correctable evidence, actual participant and every disclosure path |
| `IVSD-F109` / `IVSD-M109` | MR22-MR25; MC43-MC50/55 | 16.1-16.5, 17.1-17.5, W01-W05; semantic conformance, measured clock/completion/coverage, real providers and prerequisite/readiness claims |

The independent plan invariant audit identified five gaps. Main-agent corrections require computed recognition before destruction, subject-level pre-collection prerequisites, separate remote clock/completion bounds, exact sequential green commit candidates, and explicit complaint/reschedule/reviewer controls. The audit was not a CTO approval or runtime validation.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
| --- | --- | --- | --- | --- |
| 2026-10-07 | none | draft / escalation-required | Implementation-plan request excluding chat | Shared repository evidence and consultation R7; recognition qualification still requires a scope decision. |
| 2026-10-07 | draft / escalation-required | draft / ready-for-planning | Intake timeout followed by explicit instruction to complete using best judgment | Scope-preserving continuity selected; implementation and live-policy approval remain separate. |
| 2026-10-07 | draft / ready-for-planning | current / plan-aligned | Complete P1 triad and source/mitigation mapping validation | All 180 source scenarios and 33 profile IDs have explicit dispositions; nine workstream pairs and all consultation pairs mapped; five audit gaps corrected. Product/user/CTO/activation proof is not implied. |

## Planning Verification Record

Executed native checks, not product tests:

- `pandoc --from=gfm --to=json` for the four Markdown artifacts; `jq` identifier predicates passed 25 MR, 55 MC and 57 distinct finding/mitigation IDs in the plan, and nine workstream pairs here.
- Parsed inclusive source tables expand exactly to SC01-SC180 and CFG01-CFG33 with no gap/duplicate. Six chat-only profiles are excluded; remaining mixed profiles have explicit non-chat boundaries.
- All 18 repository-relative link occurrences resolved to files.
- Parsed task Bash code blocks piped into `bash -n`: `PACKET_SYNTAX_OK`. Packet predicates returned `true` for 34 commits, 31 unique native CHG IDs and three explicit skip reasons. Commands were syntax-checked, not executed.
- Native `git diff --no-index --check /dev/null` checks produced no whitespace diagnostics for all four artifacts; the wrapper recognizes status 1 as the expected new-file difference and returned exit 0.
- Planned existing test/migration projects exist; the triad is ignored; HEAD/source R7 blob/index object were verified unchanged.

Exact query predicates and command evidence are retained in the session/tool output and summarized in the planning context. No dotnet build/test, live UI/engine/provider or legal/staffing verification is claimed. Revalidate this report when source scope, implementation contracts, operational guarantees or accepted policy change.
