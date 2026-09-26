# I-VSD Consultancy Report: v0.1 Launch for Communities and Mosques

Last Updated: 2026-09-23

## Review Metadata

- Mode: standalone
- Subject: ISLAMU Event v0.1 community and mosque launch readiness
- Workstream: none
- Report kind: consultancy-report
- Report status: current
- Disposition: changes-required
- Evidence cutoff: 2026-09-23
- Reviewed input: clean `develop` at `f48880a30cb69a030dfbe41bee3ae8b710de7b3d`, before adding this report and its index entry; founder's subsequent launch-scope clarification on 2026-09-23
- Supersedes: none; existing feature consultations retain their own scope

## Scope

Answer the founder's question: **What is missing that absolutely cannot be neglected before communities and mosques rely on v0.1 to host events?**

This is a launch consultancy, not a proposal to finish every roadmap feature. It reviews provider responsibility across strategy, user experience, implementation, operations, governance, and evidence. Sources include current code, selected tests, operator/adopter documentation, release requirements, and earlier I-VSD reports.

The founder confirmed **two simultaneous deliverables**: the public v0.1 software release and the hosted **ISLAMU Official Instance**, restricted to Islamic events. The hosted instance will use **SingleTenant**, with Amir as instance administrator, user event posting enabled, group creation enabled, and organization creation subject to his approval. Authentication will use **Keycloak**, and **Maileroo** is the planned delivery provider for both Keycloak account mail and application messages such as registration/ticket communications. These are confirmed intentions, not observed deployment state.

This is not an invitation-only or multi-tenant mosque-hosting launch. All communities on the Official Instance share one tenant; separation between their organizers, groups, events and attendee data must still be enforced. "Allow all policies" is interpreted as enabling the described participation features, not bypassing server authorization, granting every user administrator rights, or proving unrelated paid/AI/federation integrations ready.

The exact host/database/worker topology, jurisdiction, event prepublication-review setting, group-approval setting, paid-event activation, expected peak attendance, and whether real registrations already exist remain unconfirmed. Organization approval does not answer whether personal or group events require approval. The public software release also needs reproducible supported installation paths; Official Instance success alone does not validate every advertised self-hosting configuration.

The founder reports existing community trust. That is important relationship context, not evidence that recovery, privacy, support, or event-day operations have been validated. If communities already depend on live events or stored registrations, the obligations below apply **now**, not only after publishing a version tag.

## Executive Recommendation

**The repository has substantial functionality. The missing launch case is a demonstrated, accountable service around it. Do not delay v0.1 for more integrations; do not declare it ready merely because the features exist.**

For the confirmed model, prioritize **open-posting moderation, same-tenant organizer permissions, and both email paths**. Approving organizations does not review every event, and configuring Maileroo once does not prove that both Keycloak and the application's durable dispatcher send correctly.

The review found specific incomplete surfaces and documentation conflicts, alongside essential operational evidence that was not available. These are different:

| Evidence status | What was actually found |
| --- | --- |
| Confirmed incomplete public surface | `/contact` instructs users to contact administrators but provides no address, form, or other direct contact mechanism in the page itself. Other configured surfaces may supply contact information; those were not observed live. |
| Confirmed unfinished release artifact | The accessibility statement/results are explicitly templates; the core-flow manual evidence is unfilled. |
| Confirmed deployment limitation | The documented bounded Standalone SQLite profile disables outbox, notification, email and Quartz processing. Its setup/directory checks do not establish dependent asynchronous workflows. |
| Confirmed documentation conflicts | Payment documentation says refund initiation does not exist, although controllers, handlers, a refund aggregate, and a Stripe refund adapter exist. Tenant-isolation documents disagree about production PostgreSQL RLS. Earlier governance advice makes an unsupported five-nines reliability promise. |
| Not established by this review | Actual published legal policies, responder coverage/fallback, deployed permissions and publishing policy, both Maileroo delivery paths, working alerts, successful restore drills, organizer acceptance, final release checks, and payment-provider rehearsal. Amir is the named administrator; actual operational coverage remains unverified. |

### The non-negotiable closure list

| Gate | Minimum before reliance | Finding |
| --- | --- | --- |
| Reachable help and accountable incident response | A working public contact path, named responder and fallback, honest response expectations, and an acknowledged test alert. | IVSD-F001 |
| Published responsibilities and data rights | Actual operator/organizer identities, readable policies, minimal data collection, and executable privacy/request handling. | IVSD-F002 |
| Recoverable, operational deployment | A completed restore drill and proof that every worker required by the offered features runs on the chosen configuration. | IVSD-F003 |
| A usable event from beginning to failure recovery | Real organizer/attendee rehearsal, separately proven Keycloak and application mail through Maileroo, accessible core flows, and an accepted online-only admission boundary. | IVSD-F004 |
| Protected accounts and private information | Same-tenant user/group/organization authorization checks, protected Keycloak/platform administration, and no unintended attendee/location disclosure. | IVSD-F005 |
| Accountable public posting and safety handling | Organization verification, an explicit personal/group event-publication policy, staffed reports/corrections/appeals, and anti-abuse limits appropriate for open posting. | IVSD-F006 |
| A controlled first release with real-data protection | Exact deployed version, retained verification evidence, explicit limitations, and an upgrade policy that treats adopter data as non-disposable. | IVSD-F008 |
| **Only if taking money:** a complete buyer-remedy path | Verified merchant identity, checkout/refund/cancellation/reconciliation rehearsal, and disclosed responsibility when a refund cannot complete. | IVSD-F007 |

Manual support, organization approval, appeals and privacy-request handling can be sufficient at v0.1 volume. Manual does not mean unowned or untested. It cannot replace authorization, reliable persistence, required background processing or controls that keep an open posting service within one administrator's response capacity.

## Claim Boundary

This report provides I-VSD design reasoning and implementation traceability, not a fatwa, legal opinion, security audit, accessibility certification, or proof of production readiness.

Evidence levels used below are **design validation**, **implementation traceability**, **stakeholder validation**, and **operational validation**. This review supplies the first two through bounded repository inspection. It does not supply the latter two.

No application build, product test suite, browser journey, production request, restore, provider transaction, vulnerability scan, or live infrastructure audit was performed. Test source demonstrates an intended invariant, not a passing result. Documentation labelled "Implemented" does not demonstrate that its procedure was executed on the launch deployment. An evidence gap is not automatically a software defect.

## Existing Strengths

- **Lifecycle work is durable, not merely UI state.** `CancelEventCommandHandler` uses a transaction to record cancellation and associated refund-campaign, admission-revocation, and notification work. The remaining obligation is to demonstrate downstream completion. [E05]
- **Refunds are not a missing feature in the broad sense.** Authenticated organizer refund endpoints dispatch native command handlers; handlers call `RegistrationRefundService`; `StripeRefundAdapter` implements provider creation/retrieval with connected-account context and idempotency. Buyer initiation has narrower eligibility, including an event-cancelled path. This is not evidence that every desired refund scenario or live provider configuration works. [E09]
- **Privacy and consent have real architectural boundaries.** Named EF tenant filters, consent-scoped contact exports, documented erasure-authority replay, and bounded guest retention/status behavior are present. Configuration portability is explicitly not a personal-data export. [E07, E08, E12]
- **Legal publication does not silently invent policy text.** Public legal pages consume published API composition and show an unavailable state when none exists. The rendering service checks publication state, integrity and public audience. This is a sound mechanism, but someone must publish the correct documents. [E02]
- **Operational limitations are often stated honestly.** The admission contract is online-only; the bounded SQLite recipe explains its disabled processors; backup guidance distinguishes application data from retained erasure authority. These limitations should shape launch promises. [E04, E06, E11]

## Findings

### IVSD-F001: People need a reachable responder, not instructions to find an administrator

- **Lifecycle:** open.
- **Severity / claim type:** High; confirmed incomplete contact-page surface plus missing operational evidence.
- **Principles / domains:** Amanah, Promise-Keeping, Non-Harm; design, operations, governance.
- **Stakeholders / controlled decision:** Attendees, organizers, vulnerable reporters, maintainers; public support routes, coverage, escalation and incident communication.
- **Evidence / validation:** `Contact.razor` contains generic contact instructions without a contact method. `SECURITY.md` supplies a vulnerability email but describes acknowledgment within an undefined number of business days. Site support email is configurable; this does not prove the contact page uses it or that the mailbox is monitored. Operational alert/runbook instructions exist. Implementation traceability only. [E01, E06]
- **Why it cannot wait:** A locked-out organizer, exposed attendee record, or failed event-day check-in needs an accountable response. A developer-facing GitHub security policy is not the complete attendee support experience.
- **Mitigation:** **IVSD-M001** - Provide a public, discoverable contact method that works without signing in. State service hours and achievable response targets; name an operational owner and a fallback person or explicit pause/escalation arrangement. Distinguish ordinary support, urgent security/privacy reports, and organizer event-day contact. Deliver a test alert to the responder and record acknowledgment. Keep a way to inform organizers during an application outage.
- **Owner / next validation:** Amir, as confirmed instance administrator; demonstrate an attendee reaching the published channel, a handled test incident, and what happens when he is unavailable. Naming the owner closes identity, not coverage.
- **Escalation boundary:** Security/privacy incidents go to the responsible specialist and applicable notification process. Do not promise emergency-service coverage.

### IVSD-F002: Legal and privacy mechanisms exist; the launch-specific commitments remain unverified

- **Lifecycle:** open.
- **Severity / claim type:** High; missing publication and operational evidence, not a claim that policy tooling is absent.
- **Principles / domains:** Rights of People, Sidq, Avoiding Spying, Amanah; design, technical, governance.
- **Stakeholders / controlled decision:** Attendees, children/dependents where supported, organizers, people named in event content; identity, collection, disclosure, retention, processing and remedies.
- **Evidence / validation:** `/privacy` and `/terms` depend on published documents; missing publication produces an explicit unavailable message. Operator identity has disclosure/commerce readiness gates. Guest retention and consent-export boundaries are documented, including the inability to retract already downloaded copies. The deployed policy text and actual jurisdiction were not reviewed. Implementation traceability plus design validation. [E02, E07, E11, E12]
- **Why it cannot wait:** Attendance and affiliation can expose religious participation or sensitive relationships. A working registration form is not a reason to collect every available custom field. A legal-document template is not a reviewed policy for the actual operator.
- **Mitigation:** **IVSD-M002** - Publish and inspect the actual legal identity, terms, privacy notice and community rules on the launch host. State who operates the service versus who organizes the event, what data each party sees, necessary processors/transfers, retention, deletion/export requests and contact paths. Separate operational registration from optional future-marketing consent. Demonstrate handling one access/export request and one erasure request with appropriate identity verification and bounded disclosure; a controlled manual workflow is sufficient where no self-service flow is offered. Explain retained records, backup behavior, external identity ownership and downloaded-copy limits truthfully.
- **Confirmed-provider application:** Include Keycloak identity processing and Maileroo message delivery in the actual data/processor inventory. Minimize registration/faith-related detail in email; using the same delivery vendor for account and event mail does not merge their purposes or consent rules.
- **Owner / next validation:** Operator and organizer representative; inspect the anonymous public pages and rehearse the request process using synthetic data.
- **Escalation boundary:** Obtain jurisdiction-specific legal/privacy advice for actual obligations, sensitive data, minors, international transfers and financial retention. This report neither determines legal bases nor certifies compliance.

### IVSD-F003: A healthy setup is not a working service, and a backup is not a demonstrated recovery

- **Lifecycle:** open.
- **Severity / claim type:** Critical if live registrations are accepted without recoverability or required processors; confirmed configuration limitation and missing operational evidence.
- **Principles / domains:** Amanah, Non-Harm, Promise-Keeping; technical, operational.
- **Stakeholders / controlled decision:** Every relying community and attendee; deployment topology, worker enablement, durable state, backup independence, recovery and monitoring.
- **Evidence / validation:** The public bounded SQLite profile explicitly disables outbox, notification fanout, email dispatch and Quartz. Its documentation excludes queued workflows and physical cleanup from the verified scope. Cancellation produces asynchronous work. Backup guidance requires off-host copies, matching keys/identity/storage and independent retained authority where replay protection is claimed. No completed launch-deployment restore record was supplied or located in the inspected release artifacts. Implementation traceability and prescribed procedures, not operational validation. [E04, E05, E11, E13]
- **Why it cannot wait:** The page can load while notifications, cleanup or cancellation consequences remain pending. A host loss or stale restore can lose registrations or reintroduce erased information. Merely enabling a processor does not prove provider/database compatibility.
- **Mitigation:** **IVSD-M003** - Declare one launch topology and verify its full dependency/worker set. If using the bounded Standalone profile, do not offer asynchronous-dependent features until their processors and database behavior are verified; do not simply switch everything on to suppress a readiness concern. Rehearse recovery into an isolated environment from off-host backups, including application data, storage, identity, required keys and retained erasure authority. Verify restored events, registrations, authorization, erasure replay and applicable ticket/payment recovery before reopening. Record measured recovery duration and data-loss window, then choose honest targets and backup cadence. Alert on backup failure, service outage and stalled/failed required work.
- **Confirmed-provider application:** SingleTenant does not identify SQLite versus PostgreSQL or enable workers. Include the actual Keycloak realm/database in recovery. Application SMTP delivery starts disabled and requires its explicit administrator action; saving a Maileroo host or seeing HTTP 200 health is insufficient. The documented health model can report disabled or degraded email without failing the whole readiness endpoint. [E19]
- **Owner / next validation:** Deployment operator; retain a dated drill record tied to release/configuration, with failures and remediation.
- **Escalation boundary:** A known incomplete recovery or processing path blocks the dependent use. Disaster-recovery claims require operational evidence, not this review.

### IVSD-F004: End-to-end community use and accessible event-day operation are not yet evidenced

- **Lifecycle:** open.
- **Severity / claim type:** High; explicitly unfinished accessibility evidence and missing stakeholder/runtime validation.
- **Principles / domains:** Ihsan, Adl, Non-Harm, Promise-Keeping; design, operations, evaluation.
- **Stakeholders / controlled decision:** Organizers, volunteers, older attendees, disabled users, mobile users and email-optional guests; offered journeys, communication and outage expectations.
- **Evidence / validation:** Accessibility artifacts label the statement/results as templates and leave core manual flows untested. Admission is explicitly online-only with no emergency-exception admission. A selected HTTP test asserts bounded `503`/no-store behavior during a dependency outage; it was read, not run. Guest status links must be saved, have finite authority, and are not check-in credentials. Downloaded calendars are static, not update subscriptions. Required in-app and optional email notification policies do not imply every guest receives a message. [E03, E05, E06, E07]
- **Why it cannot wait:** A feature list does not prove a mosque volunteer can operate the door or an attendee can complete registration. A last-minute cancellation that exists only in a database is not an adequate communication outcome.
- **Mitigation:** **IVSD-M004** - Have an actual organizer and representative attendee complete publication, discovery, registration, retrieval of the offered status/ticket, capacity handling, a material schedule change and cancellation. For this Official Instance, separately prove Keycloak account verification/reset mail and application registration/ticket/change mail through Maileroo, including actual recipient access to the intended action. Preserve truthful guest/email-optional limits where those flows are offered. Check the core path on a phone, by keyboard, and with a screen reader; verify Arabic/RTL if offered. For gated admission, test venue connectivity, devices, authorized staff and outage recovery. Agree on the documented stop-and-restore procedure; do not invent paper/QR-cache validation or retrospective check-in as an existing fallback.
- **Owner / next validation:** Product owner with pilot organizer and accessibility tester; record the release, scenarios, failures and accepted limitations. An inaccessible core journey needs correction or an agreed equivalent accessible service, not a disclaimer alone.
- **Escalation boundary:** If the venue requires offline admission, the current online-only feature is not sufficient. Resolve that requirement or exclude that use before accepting the event.

### IVSD-F005: Trust boundaries must be proved on the selected deployment

- **Lifecycle:** open.
- **Severity / claim type:** High; missing deployment validation and contradictory security documentation, not a demonstrated cross-tenant exploit.
- **Principles / domains:** Amanah, Rights of People, Non-Harm, Avoiding Spying; technical, governance, evaluation.
- **Stakeholders / controlled decision:** Separate communities, attendees, organizers and administrators; authentication, tenant/organization authority, privileged access and public/private disclosure.
- **Evidence / validation:** EF named tenant filters are present, and representative cross-tenant API tests exist. `QUICK_REFERENCE.md` describes enforced PostgreSQL RLS, while `MULTI_TENANCY.md` describes RLS as a prototype not enabled on production tenant tables. This review does not settle the deployed database policy state. Implementation traceability only. [E08]
- **Why it cannot wait:** On the confirmed SingleTenant Official Instance, two mosques can have the same tenant ID while still requiring different event/attendee permissions. Tenant filters alone cannot establish that boundary. Personal or group publishing must not let a user act as an unrelated approved organization.
- **Mitigation:** **IVSD-M005** - Run the relevant release security tests and demonstrate negative access with two unrelated organization owners, a group publisher, a personal publisher and ordinary attendees on the same tenant. Include attendee/export routes, event modification, forged publisher identifiers, membership removal, administrative recovery and public/private location outputs. Protect both Amir's Keycloak administration and his platform-administrator identity, with provider MFA and a tested recovery arrangement. Confirm HTTPS, production settings and absence of exposed diagnostics/secrets. Retain cross-tenant tests for the public software's MultiTenant support; do not mislabel them proof of same-tenant organization authorization. Reconcile RLS documentation against actual database configuration where applicable. [E18, E19]
- **Owner / next validation:** Security/technical owner; retain redacted evidence on the release configuration and review remaining risks.
- **Escalation boundary:** A confirmed unauthorized read/write or unsafe privilege path is a launch blocker. RLS rollout is not prescribed as a new universal prerequisite; effective isolation is.

### IVSD-F006: Report intake and community trust need accountable human handling

- **Lifecycle:** open.
- **Severity / claim type:** High; missing operational/stakeholder evidence, with existing implementation.
- **Principles / domains:** Adl, Amanah, Rights of People, Sidq; strategy, design, operations, governance.
- **Stakeholders / controlled decision:** Reporting attendees, organizers accused of misconduct, represented mosques, moderators and maintainers; admission to the service, content decisions, appeals and capacity.
- **Evidence / validation:** Event reporting/correction endpoints and moderation notifications exist. Organization approval changes tenant participation, while personal/group event submission and event prepublication approval have separate controls. `EventPublicationExecutor` rejects ordinary publication when effective policy requires approval; the setting definition itself defaults `events.require_approval` to `false`, which is not evidence of the deployed effective value. Guidelines can render an unpublished state. Existing mechanisms do not prove staffed handling. [E02, E05, E10, E18]
- **Why it cannot wait:** Organization approval is not a moderation perimeter for open personal/group posting. A user may still publish misleading, non-Islamic, impersonating or unsafe content without representing an approved organization. "Official Instance" must not suggest ISLAMU has organized, endorsed or religiously certified every listing.
- **Mitigation:** **IVSD-M006** - Keep the chosen open user-submission model, but explicitly choose and inspect effective event/group approval policy. The lower-risk initial setting is event prepublication approval if Amir can handle the queue; immediate publication instead requires credible prompt review/takedown and abuse controls. Verify representatives before organization approval and check pending/rejected organizations cannot use organization-publisher authority. Test personal and group routes separately. Publish Islamic-events scope, organizer attribution, prohibited conduct, urgent reporting, corrections, appeal and conflict-of-interest handling. Provide a report channel outside login. Exercise reporting and a reversible intake/publishing stop. Verify submission/upload/registration/mail abuse limits and monitor shared capacity; organization approval alone cannot prevent spam from other publisher paths. Document service limits and exit arrangements without requiring an invite-only launch.
- **Owner / next validation:** Amir as organization approver and service steward, with organizer representatives; record effective policies, a handled report and sustainable coverage for expected submission volume.
- **Escalation boundary:** Contested religious-legal judgments require qualified Sunni scholarly authority. Platform moderation and sponsorship must not imply religious certification or preferential access to private data.

### IVSD-F007: Paid events require verified remedies, not an assumption that Stripe solves responsibility

- **Lifecycle:** open.
- **Severity / claim type:** Critical **only if money is accepted**; documentation contradiction and missing provider/operational evidence.
- **Principles / domains:** Amanah, Rights of People, Sidq, avoidance of excessive uncertainty; technical, operational, governance.
- **Stakeholders / controlled decision:** Buyers, organizers and the operator; paid activation, merchant identity, charges, fees, refunds, disputes and communication.
- **Evidence / validation:** `PAYMENTS.md:353-357` says refund initiation, `RefundAttempt`, a provider port and API are absent. Current controller/handler/domain/Stripe-adapter source contradicts that statement; refund-allocation test source is also present. Buyer cancellation-based initiation and organizer initiation are distinct. The correct finding is inconsistent guidance and unverified complete operation, not "implement refunds from scratch." [E09]
- **Why it cannot wait:** A returned browser page or accepted refund command is not proof of provider settlement. An organizer may be unable to fund a refund, and the charge profile does not settle every legal/liability question.
- **Mitigation:** **IVSD-M007** - Keep paid sales disabled unless they are part of the accepted launch scope. If enabled, reconcile the docs, complete operator/organizer identity and payment readiness, disclose total price/fees, refund/cancellation conditions and support responsibility, and rehearse checkout, signed provider confirmation, duplicate/late events, cancellation, partial/full refund where offered, unknown/failure reconciliation and stop-sale recovery. Include the required provider-mode validation before real sales. Assign failed-refund/dispute ownership and verify refund funding/liability against the actual provider agreement. Preserve a support/remedy path while new sales are stopped.
- **Owner / next validation:** Payments operator and organizer, with technical verification; attach provider evidence without customer or credential data.
- **Escalation boundary:** Legal/tax/provider responsibility and religious-legal financial conclusions belong to qualified advisers. No claim of escrow, guaranteed payouts or immunity from losses follows from `OrganizerDirect`.

### IVSD-F008: First adoption must change release discipline and public promises

- **Lifecycle:** open.
- **Severity / claim type:** High; confirmed pre-adoption governance and unsupported reliability language, plus missing final release evidence.
- **Principles / domains:** Sidq, Amanah, Promise-Keeping; strategy, operations, governance, evaluation.
- **Stakeholders / controlled decision:** Existing/future adopters and maintainers; release identity, compatibility/data policy, upgrade decisions, service claims and evidence retention.
- **Evidence / validation:** `AGENTS.md` assumes zero users/adopters and explicitly promises governance refactoring at adoption. The v0.1 note is marked frozen planning/history, not a published release. The release checklist distinguishes active manual release from prospective automation and requires retained evidence. No version-specific release directory was present in the inspected release tree. Earlier shared-hosting guidance claims a small cohort can guarantee five-nines reliability while its own validation gaps lack load evidence. [E13, E14, E15]
- **Why it cannot wait:** "Beta" can disclose API instability; it does not make community registrations disposable. A sophisticated release engine or old green test result does not prove the final deployed version. Capping the number of organizations does not guarantee availability.
- **Mitigation:** **IVSD-M008** - Record the exact release commit/artifact and running deployment identity; retain required release build/test/security/migration results plus the launch checks in this report. Use the existing manual release path rather than blocking v0.1 on future automation. Explicitly separate disposable development data from retained community data; establish backup, tested upgrade, rollback/forward-recovery, maintenance notice and owner approval before destructive live-data changes. Update zero-adopter governance before it can authorize inappropriate live-data handling. Publish only supported capabilities, limits and measured service claims. Do not repeat five-nines, universal conformance or "everything is ready" assertions without evidence.
- **Owner / next validation:** Release owner and project steward; approve the release evidence packet and the first-adopter operating contract.
- **Escalation boundary:** The founder/operator owns launch approval and explicit residual-risk acceptance. This report is not that approval.

## Recommendations

### Smallest useful launch package

Deliver the **public software release and SingleTenant Official Instance as two related but distinct acceptance records**. Rehearse with representative organizers before opening user posting; this does not replace the chosen public model with an invitation-only service. Do not enable paid sales solely because free ticketing exists. Do not invent an arbitrary safe organization count, attendee cap or reliability percentage.

### Confirmed deployment: two email paths, three independent readiness questions

1. **Keycloak account authority:** Configure and validate its intended registration, verification, reset and administrator-MFA/recovery policy. Its account emails need their own Maileroo delivery configuration and end-to-end tests. Event startup does not automatically create the first provider user or establish those realm policies. [E19]
2. **Application communication:** Configure the existing SMTP transport and secret authority, explicitly enable persisted delivery, and run the required outbox/notification processors. Test the exact registration/ticket, change and cancellation messages promised to attendees. Optional/preference-gated channels remain optional; not every in-app notification becomes an email. [E05, E19]
3. **Shared delivery-provider readiness:** Verify sender/domain authentication and alignment using Maileroo's current instructions, actual inbox arrival and usable links, provider quotas, bounce/complaint handling and delivery-failure alerts. Account recovery and event mail share a delivery dependency; ensure registration/event bursts cannot silently exhaust it, and retain an out-of-band support route. Maileroo account configuration, limits and terms were not inspected.

Keycloak is the authentication authority; application handlers own event/ticket state; Maileroo is delivery infrastructure, not ticket or permission authority. A successful connection test proves none of the complete journeys above. This is targeted application of IVSD-M001, IVSD-M003 and IVSD-M004, not a request to build a new mail architecture.

### Three closure deliverables

Close the findings using three practical deliverables, not eight new engineering projects:

1. **A short operating agreement and public information set:** official operator versus event-organizer roles; support and incident contacts; actual privacy/terms/Islamic-events rules; personal/group/organization publication policy; moderation/appeals; service limits and exit arrangements. Closes the policy/ownership parts of IVSD-M001, IVSD-M002 and IVSD-M006.
2. **One recorded Official Instance rehearsal:** Keycloak onboarding/recovery, both Maileroo paths, personal/group/approved-organization posting, inaccessible pending-organization authority, same-tenant negative permissions, accessible attendee use, changes/cancellation, worker completion, an alert, and isolated restore. Add payment-provider cases only if sales are enabled. Closes the evidence parts of IVSD-M001 through IVSD-M007.
3. **One release-specific go/no-go record with two sections:** public v0.1 installation/release evidence and exact Official Instance deployment/operating evidence. Record versions, supported configurations, owners, limitations, retained-data upgrade rules and exclusions separately. Closes IVSD-M008 and records every other mitigation's disposition.

For each mitigation record **owner, evidence location, result and date**. A conditional feature can be excluded only if disabled/not offered and clearly communicated. A mandatory control cannot become "done" through an unsupported assurance.

### What can wait

- Additional AI/MCP, federation, external form, marketing, webhook and payment-provider integrations that are not offered in the initial service.
- Automated appeals, a ticketing helpdesk platform, sophisticated sponsorship tooling and a custom public status service; reliable manual procedures can suffice initially.
- Offline scanning, provided online-only admission is acceptable for every admitted event and venue.
- Broad enterprise scaling and every possible deployment variation beyond the launch support contract. This does not waive the repository's existing release test/migration gates for the software actually shipped.
- Provider-neutral release automation. The documented manual release process remains available.
- Cosmetic completeness and minor non-blocking accessibility defects with an owner and truthful limitations. An unusable core registration path cannot be deferred as cosmetic.

### Rejected alternatives

- **Finish every planned feature first:** increases complexity without closing the essential operational evidence.
- **Launch publicly because trusted mosques are interested:** relationship trust does not verify technical or operational obligations.
- **Treat every absent artifact as an absent capability:** would incorrectly recommend rebuilding refunds and existing privacy/reporting machinery.
- **Require SMTP for everyone:** contradicts the supported email-optional participation model. The requirement is truthful, usable access and communication.
- **Treat organization approval as approval of all posts:** personal and group publication have separate authority/policy paths.
- **Assume SingleTenant means everyone may read everything:** organizer, group, owner and attendee authorization remain necessary inside the one tenant.
- **Assume connecting Maileroo once finishes email:** Keycloak account mail and application outbox mail have different configuration and authority.
- **Use a cached QR or paper list as if offline admission already exists:** contradicts the implemented authority model and runbook.
- **Accept a broad waiver instead of recovery/privacy controls:** disclosure cannot repair data loss, unauthorized access or an unhandled serious incident.

## Stakeholders

| Group | Essential interest at launch |
| --- | --- |
| Mosque/community leadership and organizers | Authority to represent the organization, reliable event operation, clear division of responsibility and a way to leave. |
| Attendees, including guests and non-account users | Correct event information, usable registration/status access, privacy and reachable help. |
| Disabled, older and less technically confident users | An accessible core journey and respectful assistance without unnecessary disclosure. |
| Children/dependents, if in scope | Minimized collection, appropriate authority/consent and no implied marketing permission. |
| People named in content and complainants | Correction, safe reporting, fair treatment and limited evidence disclosure. |
| Volunteers and moderators | Bounded permissions, training, outage instructions and clear escalation. |
| Maintainers and service operators | Sustainable workload, protected access, realistic promises and continuity arrangements. |
| Buyers and payment providers, if enabled | Accurate financial responsibility, provider-confirmed outcomes and executable remedies. |

## I-VSD Principles And Domains

The selected framework principles are Amanah (stewardship), Sidq (truthfulness), Adl (fairness), Non-Harm, Rights of People, Promise-Keeping, Ihsan, avoidance of spying and avoidance of excessive uncertainty. They guide software/provider decisions here; they do not supply religious-legal rulings.

| Domain | Launch application |
| --- | --- |
| Strategic | Limit intake and promises to sustainable capacity; avoid growth that outruns support. |
| Design | Reachable contact, understandable policies, accessible journeys and honest email/offline limits. |
| Technical | Enforced authority, private data boundaries, durable workflows and recoverable state. |
| Operational | Running workers, observed delivery, alerts, responders and completed restore exercises. |
| Governance | Organizer verification, accountable moderation, appeals, financial ownership and retained-data protection. |
| Evaluation | Release-specific evidence and real organizer/attendee feedback rather than feature counts. |

## Common Overlooked Failures And Outcomes

| Overlooked failure | Foreseeable consequence | Minimum responsible outcome |
| --- | --- | --- |
| Setup succeeds while required processors are disabled | Cancellation, notifications or cleanup never finish | Rehearse downstream completion on the exact launch profile. |
| A guest closes the browser without saving private status access | No supported way to recover that capability by guessing an identity | Explain and test explicit save/download; provide the organizer contact for unsupported situations. |
| Cancellation is treated as proof every recipient was notified | Attendees travel to a cancelled event | Verify enabled channels and agree on organizer communication duties, without inventing email consent. |
| A stale database restore includes an old erasure ledger | Erased information may return or replay guarantees may not apply | Follow topology-specific retained-authority recovery and verify before reopening. |
| A scanner outage is treated as permission to validate locally | Admission decisions leave the supported authority/audit model | Stop validation and follow the documented recovery process. |
| Organization approval is mistaken for review of all event listings | Open personal/group posting bypasses the assumed human review perimeter | Verify event-publication policy for every publisher mode and staff report/takedown handling. |
| Keycloak mail succeeds but application delivery stays disabled | Users can log in but do not receive expected event communications | Exercise both paths, persisted delivery enablement and required processors independently. |
| Community interest is marketed as proven reliability | Organizations rely on an unsupported service promise | Publish actual service limits and evidence-backed claims. |

## Validation Gaps

- **Stakeholder validation:** No organizer acceptance record, attendee usability session, signed pilot agreement or observed event-day operation was reviewed.
- **Operational validation:** No launch-host configuration, alert acknowledgment, restore output, production access review, SMTP/provider delivery result or refund settlement record was reviewed.
- **Release validation:** No fresh product tests/build, final CI result, immutable deployment-consumption proof or completed v0.1 release evidence packet was reviewed.
- **Publication validation:** Actual hosted policy documents, displayed support contact, selected language content and current moderation staffing were not observed.
- **Security validation:** This was not exhaustive source review or a penetration test. Existing test fixtures and helper code are not proof of the complete live trust boundary.
- **Scope validation:** Dual release/hosting, SingleTenant, Amir's administration, open user posting, group creation, approved organizations, Keycloak and Maileroo are confirmed intentions. Real-data status, jurisdiction, load, infrastructure/database topology, effective event/group review policy and paid activation remain unconfirmed.

## Escalation Needed

- Operator/founder: finalize effective publication/review settings, infrastructure, service limits, coverage and residual risks within the confirmed launch scope.
- Qualified legal/privacy adviser: actual operator jurisdiction, sensitive information/minors, required notices, data requests, retention and any paid-event terms.
- Security/operations specialist: unresolved access-boundary failures, unsafe recovery, suspected compromise or provider/configuration uncertainty that blocks a launch gate.
- Organizer and accessibility representatives: whether the offered journeys and online-only admission fit the people and venue.
- Qualified Sunni scholarly authority: contested religious-content decisions, religious-legal financial questions or claims of religious certification. No such ruling is issued here.

## Evidence Reviewed

All repository evidence below is bound to the reviewed commit. Line locators identify inspected portions, not a claim of exhaustive file review. Earlier reports are context, not automatically accepted current facts.

| ID | Source and inspected locator | Contribution |
| --- | --- | --- |
| E01 | [Contact page](../../src/Explore.Blazor.Client/Pages/Contact/Contact.razor), complete component; [Security policy](../../SECURITY.md), Reporting and Our Commitment; [Self-hosting](../../docs/internal/SELF_HOSTING.md):85-97 | Missing direct contact mechanism on the page; existing vulnerability address, undefined acknowledgment commitment and configurable site-support identity. |
| E02 | [Privacy route](../../src/Explore.Blazor.Client/Pages/Legal/PrivacyPolicy.razor); [Terms route](../../src/Explore.Blazor.Client/Pages/Legal/TermsOfService.razor); [Public legal component](../../src/Explore.Blazor.Client/Pages/Legal/PublicLegalDocumentPage.razor):9-74; [Rendering service](../../src/Explore.Application/Features/ConfigurationManifest/LegalDocuments/LegalDocumentRenderingService.cs):102-181; [Community guidelines](../../src/Explore.Blazor.Client/Pages/Legal/CommunityGuidelines.razor):23-51; [Legal-page tests](../../tests/Explore.Blazor.Client.Tests/Pages/Legal/PublicLegalDocumentPageTests.cs):19-91 | Published-document composition, integrity/availability boundaries and missing-publication behavior; tests read, not executed. |
| E03 | [Accessibility artifacts](../../docs/internal/ACCESSIBILITY_ARTIFACTS.md):12-130 | Unreleased statement, unverified result template, empty manual-flow matrix and required core checks. |
| E04 | [Standalone recipe](../../docs/public/documentation/readme/self-hosting/docker-standalone.md):88-148; [Self-hosting](../../docs/internal/SELF_HOSTING.md), Bounded SQLite Operational Profile | Explicitly disabled asynchronous processors and limits of the retained verification profile. |
| E05 | [Cancellation handler](../../src/Explore.Application/Features/Events/Handlers/Commands/CancelEventCommandHandler.cs):33-164; [Email and notifications](../../docs/internal/EMAIL_NOTIFICATIONS.md):12-66, Basic Dispatch Test Evidence | Transactional cancellation work, notification channel policies and provider-handoff limits; documented existing test seams. |
| E06 | [Admission](../../docs/internal/ADMISSION_AND_REGISTRATION.md):308-381; [Operations](../../docs/internal/OPERATIONS.md):323-368; [Admission outage test](../../tests/Event.API.IntegrationTests/Features/Admissions/AdmissionCheckInHttpHalRedTests.cs):365-398 | Online-only admission, no emergency exception, stop/restore/reconcile runbook and outage test assertions. |
| E07 | [Email-optional participation](../../docs/public/documentation/readme/events-and-ticketing/email-optional-participation.md), private status, retention and calendar sections; [Contact sharing](../../docs/internal/CONTACT_SHARING.md):12-100 | Existing guest access/retention and consented export behavior; no recovery or external-copy deletion overclaim. |
| E08 | [Tenant filters](../../src/Explore.Persistence/ExploreDbContext.QueryFilters.cs):16-65; [Multi-tenancy](../../docs/internal/MULTI_TENANCY.md):97-113; [Quick reference](../../docs/internal/QUICK_REFERENCE.md), Critical Rules and Multi-Tenancy Reminder; [Cross-tenant tests](../../tests/Event.API.IntegrationTests/Features/CrossTenantIsolationTests.cs):27-137 | Application isolation implementation, representative test setup/assertions and contradictory RLS descriptions. |
| E09 | [Payment documentation](../../docs/internal/PAYMENTS.md):349-386; [Organizer refund controller](../../src/Explore.API/Controllers/StudioRegistrationOrderPaymentController.cs):21-117; [Refund handlers](../../src/Explore.Application/Features/RegistrationOrders/Handlers/Commands/RegistrationPaymentCommandHandlers.cs):106-168; [Refund aggregate](../../src/Explore.Domain/RefundAttempt.cs), declaration; [Stripe refund adapter](../../src/Explore.Infrastructure/Payments/Stripe/Refunds/StripeRefundAdapter.cs):9-107; [Refund allocation tests](../../tests/Event.Domain.UnitTests/Entities/RefundAttemptTests.cs):11-109 | Documentation/source mismatch; real refund command and provider paths; bounded buyer eligibility and test source, not settlement proof. |
| E10 | [Event reporting controller](../../src/Explore.API/Controllers/EventReportsController.cs):24-160; [Code of Conduct](../../CODE_OF_CONDUCT.md), reporting/enforcement/scope sections | Existing intake mechanisms and repository community-policy context, not proof of staffed product moderation. |
| E11 | [Backup/restore](../../docs/internal/BACKUP_RESTORE_UPGRADE.md):92-193; [Privacy erasure](../../docs/internal/PRIVACY_ERASURE.md):17-85; [Self-hosting](../../docs/internal/SELF_HOSTING.md):107-140 | Backup units, authority-first recovery/topology limits, and operator identity/publication readiness. |
| E12 | [Privacy erasure](../../docs/internal/PRIVACY_ERASURE.md), Configuration Portability Privacy Boundary; [Contact sharing](../../docs/internal/CONTACT_SHARING.md), Privacy Boundary and Verified Export Behavior | Distinguishes configuration export, attendee/contact export and personal-data rights; bounds retention/withdrawal claims. |
| E13 | [Release checklist](../../docs/internal/RELEASE_CHECKLIST.md):14-163; [Release tree](../../docs/internal/releases/README.md) and directory inventory; [CI/CD governance](../../docs/internal/CI_CD_GOVERNANCE.md):200-216 | Manual release path, required evidence and dated configuration evidence. Tree inspection found change records but no version-specific release directory; external release storage was not inspected. |
| E14 | [Shared-community governance report](../governance/i-vsd-shared-community-instance-and-maintainer-sustainability.md):105-135, Validation Gaps and Missing Evidence | Prior intake/sustainability proposals, missing pilot evidence and unsupported reliability guarantee. Its assumptions and normative conclusions were not independently validated or adopted. |
| E15 | [Agent contract](../../AGENTS.md), Development Mode Notice and rule 11; [Operations](../../docs/internal/OPERATIONS.md):631-660; [v0.1 planning note](../../docs/internal/semantic_versioning/v0.1.0.md):4-16 | Zero-adopter assumptions, disposable-versus-retained database handling and non-release status of the historical planning note. |
| E16 | [Project context](../../docs/internal/PROJECT.md), scope and deployment positioning; [Earlier compliance review](i-vsd-compliance-check.md), Findings and Missing Evidence; user request of 2026-09-23 | Product/community context and prior questions. Earlier negative claims were not imported as current absence findings. |
| E17 | [I-VSD principles](../../.agents/skills/i-vsd/resources/principles-and-domains.md); [Evidence levels](../../.agents/skills/i-vsd/resources/evidence-and-validation-levels.md); [Report contract](../../.agents/skills/i-vsd/resources/report-contract.md) | Normative design framework, authority boundaries and report identity/lifecycle. |
| E18 | [Event setting definitions](../../src/Explore.Domain/Settings/Definitions/EventSettingDefinitions.cs):13-54; [Group settings](../../src/Explore.Domain/Settings/Definitions/GroupSettingDefinitions.cs):5-22; [Creation context](../../src/Explore.Application/Features/Events/Handlers/Queries/GetEventCreationContextRequestHandler.cs):39-147; [Publication executor](../../src/Explore.Application/Features/Events/EventPublicationExecutor.cs):99-129; [Organization approval handler](../../src/Explore.Application/Features/Organizations/Handlers/Commands/UpdateOrganizationApprovalStatusCommandHandler.cs):31-56 | Distinct personal/group/organization publishing and event-approval controls; organization approval is a tenant-participation mutation, not blanket review of all posts. Effective deployed policy was not read. |
| E19 | [SMTP adopter guide](../../docs/public/documentation/readme/communications-and-notifications/email-smtp.md):9-88; [Authentication](../../docs/internal/AUTHENTICATION.md):88-101 | Separate provider-owned identity prerequisites, persisted application email enablement, worker/health boundaries and connection-versus-delivery distinction. |
| E20 | Founder's launch clarification, 2026-09-23, in this conversation | Public v0.1 plus Islamic-only Official Instance, SingleTenant, Amir as administrator, user/group creation and posting, organization approval, Keycloak authentication and planned Maileroo delivery for account/application messages. |

## Missing Evidence

The reviewed repository did not supply a complete launch-specific packet containing:

- Jurisdiction, effective event/group approval settings, exact infrastructure/database and enabled worker profile, load envelope and whether live personal or financial data already exists.
- Published deployment-specific policy versions, organizer agreement and demonstrated contact/appeal routes.
- A completed recovery drill, required-worker completion evidence and acknowledged alert/responder coverage.
- Representative organizer/attendee/accessibility acceptance and event-day connectivity validation.
- Final release CI/security/migration results and proof of the exact artifact running on the host.
- Payment-provider activation, reconciliation/refund evidence and responsibility/funding agreements if paid events are enabled.
- Keycloak realm/account-policy evidence, independently successful Keycloak/application Maileroo delivery, sender-domain posture, quota/abuse limits and failure handling.

These are bounded evidence gaps, not assertions that the founder has done none of this. Supply existing records instead of repeating valid work; revalidate only where the release/configuration or relevant assumptions changed.

## Context Inventory

- **Workspace:** Clean `develop` revision, current implementation, selected test source, internal/public docs, release records and existing I-VSD consultations/governance.
- **Tools and retrieval:** Native file/search/git inspection and three bounded read-only evidence tracks. Knowledge-graph tool discovery returned no available matching tool, so file/search fallback was used.
- **External operational context:** No hosting dashboard, authenticated CI/provider evidence, support mailbox, incident records or community research export was inspected. No external web research or production interaction was required for the source-grounded findings.
- **User context:** The founder clarified the dual software/Official Instance launch before final delivery. This revision replaces the initial invited/multi-tenant working assumptions with the confirmed SingleTenant, open user-posting, organization-approval, Keycloak and Maileroo model. Unconfirmed operational facts remain explicitly open.
- **Change boundary:** This task adds the consultancy and its catalogue entry only. It does not change product behavior, configuration, existing policies, earlier reports, or launch approval.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
| --- | --- | --- | --- | --- |
| 2026-09-23 | none | current | Requested v0.1 launch consultancy; completed bounded source/document assessment | Commit `f48880a30cb69a030dfbe41bee3ae8b710de7b3d`; disposition `changes-required`, not operational sign-off |
| 2026-09-23 | current | stale | Founder clarified simultaneous software release and public SingleTenant Official Instance with Keycloak/Maileroo | Initial invited/shared-tenant assumptions no longer matched the request |
| 2026-09-23 | stale | current | Revalidated publication/organization controls and identity/application mail boundaries; revised scope, findings and closure checks | E18-E20; existing IVSD-F001-F008 / IVSD-M001-M008 retained; no deployment sign-off |

Refresh when the launch scope, deployment/worker profile, paid activation, identity/policy publication, user population, critical implementation or operational evidence changes. Resolve each finding against its mitigation and retained evidence; do not mark the launch ready by updating the date alone.
