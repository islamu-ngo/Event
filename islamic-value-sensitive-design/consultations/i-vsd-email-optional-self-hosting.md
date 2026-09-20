<!-- ABOUTME: Canonical I-VSD report for email-optional self-hosting, community directories, and administrative provisioning. -->
<!-- ABOUTME: Consolidates consultancy evidence, planning analysis, and user decisions with explicit correction and review history. -->

# Email-Optional Self-Hosting - I-VSD Consultancy And Planning Report

Last Updated: 2026-09-09

## Review Metadata

- Mode: planning
- Subject: email-optional self-hosting and mailbox-free participation
- Workstream: email-optional-self-hosting
- Report kind: consolidated consultancy and planning assessment
- Report status: stale
- Disposition: changes-required
- Evidence cutoff: 2026-09-09, including the integrated five-engine migration lifecycle, complete Domain/Application checks, bounded native host/restart, onboarding and relational ATProto verification.
- Reviewed input revision: the integrated working tree of feature parent `9966068b9affe5b72df1503291011acc22367747` and upstream `425e4b48343690094637dda860304f3bfb04a5cd`, with natively consolidated application migrations and recorded fixture/contract corrections. Earlier exact planning/graduation SHA-256 snapshots and phase IDs remain historical provenance, not hashes of this integration.
- Supersedes: the two separate subject reports identified in Evidence Reviewed; this file remains their single canonical successor, not a new consultation or report identity.
- Review boundary: targeted integration verification is current, including 12 five-engine lifecycle cases, 1,181 Domain and 2,161 Application tests, and successful native zero-email/restart and relational ATProto flows. The unfiltered Persistence run ended without finalized results; browser/assistive-technology, latency and empirical outcome limits remain. This factual evidence update is not a new ethical assessment, an all-green release gate, or outcome certification.

## Scope

This single report combines the original consultancy with subsequent source-grounded planning and user corrections. It covers standalone single-binary Docker/SQLite hosting, community registries and public directories, multi-tenant private intranets, low-connectivity deployments, and supported Compose/Aspire topologies. Intended users include grassroots masajid, halaqat, solo organizers, volunteer communities, campuses, humanitarian coalitions, and privacy-conscious attendees.

### In Scope

- Optional outbound SMTP: usable directory browsing, event creation and permitted anonymous native registration without SMTP credentials, SPF/DKIM/DMARC setup, or a third-party delivery subscription.
- Instance/tenant visitor policies: account-based participation where public onboarding is available; anonymous-only or directory-only experiences where selected. Administrator access must remain distinct from visitor signup.
- Multi-tenant out-of-band provisioning: tenant creation and initial administrator credential handover without invitation email, with accountable verification and first-use rotation.
- Event participation experiences: authenticated native participation where eligible; minimal-data anonymous native capacity tracking; external ticketing redirection, such as Eventbrite or Luma; informational listings and offline organizer coordination.
- First-run administrator setup: existing headless identity selection, an authorized Local wizard, and optional Mailpit testing without external SMTP.
- Missing, failed, disabled and revoked delivery: truthful UI, explicit destructive-action confirmation, bounded queues, recovery, health and tenant BYO-SMTP isolation.
- Bookmarkable attendee status/cancellation access, calendar export, anti-hoarding measures, contact minimization, purpose-bound retention and safe operational telemetry.

### Out Of Scope

- Core payment processing and Stripe Connect payouts remain governed by [the paid-event consultation](i-vsd-paid-event-payments-consultation.md) and existing sovereign commerce authority.
- Deep ATProto/PDS cryptographic handshakes remain with the separate ATProto workstream. The original consultancy cited `i-vsd-database-backed-atproto-auth.md`; the 2026-09-05 inspection located it only in that workstream's isolated checkout, not main `develop`. This historical related-report locator is not a fresh inventory or claim of merged evidence; no broken main-repository link is introduced here.
- External identity-provider takeover, unrelated active work, and replacement of legal advice about consumer notifications are not authorized.
- Tenant-delegated Local credential administration and empirical no-show/abuse/usability/handover research are outside this revision. P12 assigns them separate backlog artifacts; this evidence update neither implements delegation nor conducts research.

### Settled User Decisions

The user resolved administrator identifier scope on 2026-09-05: retain email/password for Local and preserve the extensible multi-provider authentication model. Username-only identities and an additional authentication provider are excluded. Other providers retain their native identity semantics. Optional outbound delivery does not imply that an email-shaped identifier proves mailbox ownership.

The user additionally resolved authentication-email ownership: only Local Identity adapts its application-owned verification/reset email flows to Event SMTP availability. Keycloak owns its authentication emails and verification; ATProto leaves account email to the actual PDS/account provider. Event consumes trusted provider verification status where available without taking over that responsibility. Event-generated registration and event-notification emails remain governed by Event's own SMTP capability for all users.

The user subsequently specified mandatory verification for Local sign-in when SMTP is enabled, administrator-controlled Local account creation, and directly verified administrator-provisioned accounts. Preserving existing unverified sign-in was explicitly rejected. Where public attendee account creation is unavailable under this Local posture, authenticated-attendee-only event registration must not be configurable. External providers retain their own onboarding possibilities, including Keycloak configurations without email. The approved plan subsequently selected instance-administrator-only Local creation and supervised reset. Tenant administrators retain membership/role authority, not global credential takeover. The initial delegation question is preserved as history; an extension requires a separate user decision.

## Claim Boundary

This is provider-responsibility design reasoning about maintainer/operator choices: identity authority, defaults, data minimization, failure modes, transparency, credential stewardship and abuse safeguards. It is not a fatwa, Sharia certification, halal/haram ruling, legal opinion, security warranty, or assurance that unimplemented mitigations work. Religious-legal questions about commercial activities or contracts require qualified scholarly authority. This update combines source inspection with recorded parent-run native/HTTP/rendered validation. No external research or stakeholder study was performed. Bounded native-host proof remains distinct from pending full-provider, closure-review and empirical outcome evidence.

## Common Overlooked Failures And Outcomes

### Failures And Their Evidence Limits

- Anonymous attendees may assume that rescheduling, venue changes or cancellation notices will reach them and arrive at an empty venue when no communication channel exists.
- Unverified seat claims can enable capacity hoarding, fake rosters and high no-show rates, depriving genuine attendees of scarce places. The original consultancy described this through harm and appropriation concerns (*Darar* and *Ghasb*); the technical assessment does not issue a religious ruling.
- Missing/failed SMTP can create useless retry work, dead-letter growth and operational load. The original report described infinite retries as if observed; current source instead has bounded attempts and reconciliation. The residual risk is inappropriate handling and accumulated backlog, not a proven infinite loop.
- Requiring email verification before an initial administrator can configure SMTP can create a bootstrap cycle. The 2026-09-05 permissive Local source did not establish that cycle. P03/P04/P06 now implement authorized direct verification and restricted first-use replacement; complete deployment proof remains pending.
- Clearing working SMTP can disconnect expected notifications and recovery routes. It must not be described as disabling every authentication provider or necessarily breaking every in-flight registration.
- Shared initial credentials without forced replacement blur attribution between the provisioner and tenant administrator. Password loss without SMTP can also strand a tenant administrator unless supervised recovery exists.
- Treating SMTP absence as authentication absence can unnecessarily lock out existing users.
- Applying Event's SMTP switch to external-provider verification can either lock out valid users or incorrectly bypass an existing verification check. Provider authentication success does not imply a verified email assertion; current ATProto JIT accounts deliberately have no email and `EmailVerified=false`.
- Direct verification through administrator provisioning must record that trusted administrative origin; it must not claim an email-token exchange took place. The user explicitly permits this provisioning path. An SMTP switch alone is never such an authorization.
- Introducing a second event-registration mode enum can conflict with existing participation, approval, access and recovery policies.
- A checkout-hold capability may expire before an attendee needs to inspect event changes; a bookmarked URL alone does not establish a durable status lifecycle.
- Generic idempotency response storage can persist or replay a password or one-time capability response.
- A static calendar download and a local Mailpit inbox do not notify attendees of later changes.
- Disabling configuration in one replica while others retain credentials can violate operator expectations.
- Indiscriminate retry or automatic replay of uncertain SMTP handoffs can duplicate delivery; indiscriminate discard can conceal required communications.
- Name-only attendance is reduced PII, not zero PII. IP/subnet abuse controls also need bounded privacy-aware treatment.

### Negative Consequences

- Community grievance (*Niza'*), wasted journeys and loss of trust after uncommunicated event changes.
- Exclusion of nontechnical and under-resourced self-hosters through SMTP/DNS friction, increasing dependence on large hosted platforms and their data practices.
- Operational disorder from hoarding, false attendee rosters and unchecked no-shows.
- Accountability erosion when administrative activity cannot be attributed to the administrator rather than the credential provisioner.

### Intended Positive Outcomes

- Community sovereignty and empowerment (*Tamkin / Istiqlal*) without mandatory SaaS email or a separate directory binary. The original "60 seconds" setup ambition is a usability target, not a measured deployment result.
- Private/air-gapped multi-tenancy for campuses, organizations and humanitarian coalitions without external email connectivity.
- Privacy and data minimization (*Khususiya / Hifdh al-'Ird*) through not collecting attendee email for open gatherings.
- Truthful boundaries (*Sidq & Wafa'*): attendees understand when to self-monitor and organizers understand who cannot be notified.
- More accountable administrative handover and fairer capacity allocation. Forced rotation improves attribution but cannot establish non-repudiation against a malicious host operator; all practical outcomes need validation.

## Findings

IDs preserve the original seven findings and their one-to-one mitigation identities. **Lifecycle: open for IVSD-F001 through IVSD-F007.** Implemented mechanisms are not missing-design findings, but unclosed deployment/security verification and unmeasured stakeholder outcomes do not justify resolution. Each finding below distinguishes the 2026-09-05 baseline from source-current mechanisms; the implementation evidence matrix records saved validation and exact scenario/task mappings. No pass count is a new execution by this report update.

### IVSD-F001 - SMTP Dependency And Community Autonomy

- **Severity / claim:** High; provider-controlled adoption and technical access.
- **Principle/domain:** Justice (*'Adl*) and removing undue hardship (*Raf' al-Haraj*); strategic/technical.
- **Stakeholders / provider decision:** Grassroots operators, masajid and nontechnical organizers; whether SMTP is a prerequisite or progressive capability.
- **Evidence:** Historical 2026-09-05 locators: `SmtpEmailService.cs:48-52`, `InstanceSmtpSettingService.cs:24-30`; missing SMTP was not proof of a startup dependency. P01/P02 now use persisted `email.delivery_enabled=false`, coherent tenant-effective capability and optional-transport health. P12 source makes Compose Mailpit opt-in/private and separates public `branding.support_email` from SMTP; setup evidence and the bounded native host/restart scenarios now pass; default optional-worker topology and broader acceptance are not inferred.
- **Mitigation:** [IVSD-M001](#ivsd-m001---email-optional-architecture-with-zero-config-default).
- **Owner / validation:** Platform Architecture; demonstrate standalone startup, functional directory and eligible registration without SMTP variables.

### IVSD-F002 - Anonymous Attendee Communication Asymmetry

- **Severity / claim:** High; promise-keeping and operational transparency.
- **Principle/domain:** Trust (*Amanah*), promise-keeping (*Wafa' bil-'Uqud*) and non-harm (*La Darar*); UX/operations.
- **Stakeholders / provider decision:** Attendees and organizers; communicate no-email limitations and provide a way to inspect event changes.
- **Evidence:** Historical 2026-09-05 locators: `NativeRegistrationSubmissionCommands.cs:41-50`, `RegistrationOrderPii.cs:27-29`; the checkout guard alone did not establish durable status. P09 now has a separate PII-free `GuestRegistrationStatusAccessGuard`, persisted event-end-plus-30-day access promise, early fragment scrubbing, explicit private-link saving and public calendar export. P10 adds deliberate free-confirmed anonymous cancellation without relaxing checkout/payment authority.
- **Mitigation:** [IVSD-M002](#ivsd-m002---attendee-notice-status-access-and-calendar-export).
- **Owner / validation:** Registration and UX/Frontend; verify before/after notices, scoped bookmarkable status, change visibility and authorized cancellation.

### IVSD-F003 - Anonymous Capacity Hoarding And Sybil Abuse

- **Severity / claim:** High; harm prevention and distributive justice.
- **Principle/domain:** Non-harm (*La Darar wa-la Dirar*) and justice (*'Adl*); security/technical.
- **Stakeholders / provider decision:** Organizers, prospective attendees and venue hosts; abuse budgets, challenges and allocation controls.
- **Evidence:** Historical 2026-09-05 locators: `RegistrationOrder.cs:349`, `EmailDispatchEligibilityEvaluator.cs:528-538`; neither proved an attack or an implemented challenge. P08 now implements native Data Protection-bound SHA-256 proof-of-work, requester limiting, durable event/tenant issuance quotas and exact committed-allocation replay. Canonical body/key/scope validation precedes cached capability disclosure, with post-await deadlines; P10 preserves transactional capacity release. Native race/replay evidence is not proof of unique humans or field fairness.
- **Mitigation:** [IVSD-M003](#ivsd-m003---layered-anti-hoarding-controls).
- **Owner / validation:** Application Security and CQRS; verify client/event/tenant budgets, challenge replay prevention and concurrent capacity fairness. Rate limiting alone does not establish unique humans.

### IVSD-F004 - SMTP Disconnection And Operational State Mismatch

- **Severity / claim:** High; fail-safe operational state and honest delivery reporting.
- **Principle/domain:** Truthfulness (*Sidq*) and avoiding uncertainty (*Gharar*); operations/governance.
- **Stakeholders / provider decision:** Operators, users and tenant managers; distinguish intentional disable, configuration absence and unexpected failure.
- **Evidence:** Historical 2026-09-05 locators: `SmtpConfigResolver.cs:94-106`, `EmailDispatchDrainService.cs:293`; the five-minute resolved-credential cache belongs to that baseline, not current source. P01 removes that cache; P02 coordinates writes/admission through relational policy leases and durable revisions, confirms current disable impact, skips historical optional work, preserves required capability/operator parks and never automatically resends `Unknown`. Optional transport degradation is separated from required policy/database authority failure.
- **Mitigation:** [IVSD-M004](#ivsd-m004---graceful-degradation-and-deliberate-revocation).
- **Owner / validation:** Infrastructure, secrets and Blazor administration; verify confirmation, replica/cache convergence, tenant isolation, queue disposition and truthful health without automatic uncertain resend.

### IVSD-F005 - First-Run Administrative Bootstrap Integrity

- **Severity / claim:** Medium; privileged bootstrap and verification authority.
- **Principle/domain:** Trust (*Amanah*) and operational excellence (*Ihsan*); identity/governance.
- **Stakeholders / provider decision:** Initial operators; usable administrator establishment without external SMTP and without granting authority from an unproven selector.
- **Evidence:** Historical 2026-09-05 locators: `LocalIdentityAuthService.cs:93-102`, headless-onboarding report lines 51-60; permissive Local self-signup/session issuance is no longer current. P03 removes public Local signup and uses instance delivery intent for mandatory verified ordinary sign-in/session validation even during transport failure. P04/P06 establish authorized, directly verified administrators with Pending/ChangeRequired/Ready state and provider-proof-based external bootstrap. P07 guards public onboarding, publication/settings and new allocations using effective provider capability, not SMTP or email presence.
- **Mitigation:** [IVSD-M005](#ivsd-m005---provider-owned-verification-and-administrative-bootstrap).
- **Owner / validation:** Core Identity; verify mandatory SMTP-enabled Local sign-in confirmation, authorized directly verified provisioning and all selected bootstrap paths without an SMTP setup cycle.

### IVSD-F006 - Privacy Strength Of Minimal-Data Participation

- **Severity / claim:** Positive objective/system strength, with unresolved privacy risks; data minimization.
- **Principle/domain:** Modesty (*Haya*), avoiding spying (*Tajassus*), privacy and rights of people (*Huquq al-'Ibad*); strategic/design.
- **Stakeholders / provider decision:** Privacy-conscious attendees and political/religious minority communities; avoid unnecessary contact collection, tracking and retention.
- **Evidence:** Historical 2026-09-05 locators: `PublicExperienceSettingDefinitions.cs:10-15`, registration-data-collection report lines 54-58; category-only retention was the baseline. P11 now pins `AnonymousPiiRetentionUntilUtc` at allocation to finite event end plus seven days by default (0-30 configurable), without extension after edits/reschedule/claim/recreated rows. None collection creates no contact PII; expired/missing authority denies operational reads/writes and delayed/provider handoff. Legal holds preserve physical evidence, not disclosure. P09's extendable status promise is separate and PII-free.
- **Mitigation:** [IVSD-M006](#ivsd-m006---minimal-data-registration-and-retention).
- **Owner / validation:** Data Governance and Registration; verify no email/phone collection in the selected anonymous flow, justified names, deletion deadlines and tracking exclusion.

### IVSD-F007 - Credential Handover And Administrative Accountability

- **Severity / claim:** High; credential confidentiality, attribution and recovery boundaries.
- **Principle/domain:** Trust (*Amanah*), justice/accountability (*'Adl wa-Mas'uliyyah*) and truthfulness (*Sidq*); governance/technical.
- **Stakeholders / provider decision:** Instance administrators, tenant administrators and tenant users; one-time handover, private replacement and supervised recovery.
- **Evidence:** Historical 2026-09-05 locators: `EmailDispatchEligibilityEvaluator.cs:310-332`, `LocalIdentityAuthService.cs:93-102`; absent rotation/reset described the baseline. P04 implements instance-owned one-time credential issuance, operation receipts, restricted replacement authority and stamp-based session invalidation; P05 implements purpose-bound verification/recovery and SMTP-independent authenticated password change through `ILocalIdentityLifecycleStore`. Legacy unsupported methods still present on `LocalIdentityAuthService` are not those public lifecycle routes. P06 links an already-provisioned Local administrator by reference without passwords/invitation production in durable tenant requests.
- **Mitigation:** [IVSD-M007](#ivsd-m007---ephemeral-handover-first-use-rotation-and-supervised-reset).
- **Owner / validation:** Multi-Tenancy, Identity and Control Plane; verify one-time disclosure, complete pre-rotation access restriction, audited reset, secret-free durable requests and no cross-tenant/global identity takeover.

## Recommendations

### Onboarding And User Experience

- Make the SMTP setup step explicitly optional. The original suggested text was "Skip for now - Run in Zero-Email / Community Directory Mode (Email can be enabled anytime in Settings)." This remains sample copy, not an implemented string or a promise to silently change authentication providers.
- Display an anonymous-registration information notice before and after submission, with copyable status/ticket access and calendar download.
- Present newly issued tenant credentials once with a "Copy Credentials & Pass Securely Out-of-Band" affordance; dismissal must not allow plaintext redisplay.
- Distinguish visitor signup/login affordances from the operator entry point; use server-authored HAL and server-enforced policy, not client role checks.

### Visitor Policy And Event Experiences

The consultancy's proposed `VisitorAccessMode: FullRegistrationAndAuth | AnonymousOnly | DirectoryListingOnly` is now implemented under `GovernanceSettingKeys.PublicExperience` by P07. `VisitorAccessCapabilityResolver` excludes Local from public signup and requires usable provider-specific onboarding facts. Server-authored HAL and server mutation/publication/allocation gates agree; directory-only restricts new activity without orphaning existing private status/cancellation. Existing `DiscoveryCentric` and `OrganizationCentric` still describe discovery layout, not authentication admission. The event-experience labels below remain historical explanatory labels, not new enums.

| Original proposed experience | Functional behavior to retain | Existing authority to reuse |
| --- | --- | --- |
| `StandardAuthenticated` | Account-based participation only where effective visitor onboarding/policy allows it; verification follows the relevant account authority | Platform-managed participation, access policy and provider-owned authentication; the original "account or verified email" shorthand must not treat email as an account |
| `AnonymousNative` | Minimal-data, capacity-tracked native participation; name only when needed | Existing guest-capability order, workflow, capacity and recovery policies |
| `ExternalRedirect` | Link to the organizer's external ticketing/event platform | Existing external-managed participation; external clicks/returns are not native confirmation |
| `ListingOnly` | Informational listing and organizer contact for offline handling | Existing information-only participation; do not confuse it with native order creation |

Do not implement a competing event enum: current participation modes are InformationOnly, WalkIn, ExternalManaged and PlatformManaged; current registration modes Open, ApprovalRequired, InviteOnly and Closed govern a different axis. Keep WalkIn and published workflow lineage intact.

### Operational Integration

Reuse existing dispatch parking, tenant controls, bounded retries and reconciliation rather than creating another queue. Keep BYO-SMTP within the hierarchical settings/lock perimeter and credentials in the selected secrets authority. Optional Mailpit, delivery health, destructive disable confirmation and supervised Local recovery are specified by the corresponding mitigations below.

### Rejected Alternatives

- Central project-operated mandatory email relay: creates a central failure point, surveillance risk (*Tajassus*) and responsibility for third-party spam/abuse while undermining self-hosting independence (*Istiqlal*).
- Separate directory-only binary: bifurcates the codebase and CI/CD, adds maintenance work and obstructs enabling email later within the same deployment.
- Silent dropped-email success: misrepresents delivery and breaks attendee expectations.
- Stored redisplayable plaintext administrator credentials: violates credential confidentiality.
- Fake email addresses or bulk verification triggered by SMTP state: not the user's explicitly authorized administrator-provisioning verification path.
- Replacement registration enums or aggregates without demonstrated need: disregard existing participation/capacity authority.
- Username-only local administrators or a new authentication provider: explicitly rejected by the user in favor of existing email/password and extensible provider-native authentication.
- Preserving unverified Local sign-in when SMTP is enabled to avoid breaking development accounts: explicitly rejected by the user; the target admission policy governs.

## Mitigations

The normative requirements and original sample copy below remain acceptance constraints, not claims that every deployment/outcome has passed. Dated implementation paragraphs and the evidence matrix distinguish selected mechanisms from historical proposals.

### IVSD-M001 - Email-Optional Architecture With Zero-Config Default

Outbound email is an optional, tenant-effective progressive capability, not an inherent requirement for directory browsing, event management or eligible anonymous registration. The original `Operational (Zero-Email Mode)` copy and `EmailDeliveryHealthCheck` class name were proposals. Current `SmtpHealthCheck` reports healthy `smtp_disabled` only for deliberate disable; enabled incomplete configuration is degraded `smtp_configuration_unavailable`, and failed connection testing is degraded `smtp_unavailable`. Failed policy authority is unhealthy, not zero-email success.

Keep one binary/codebase and established composition roots. No mandatory relay, email SaaS, domain-verification DNS records or SMTP credentials are added to first use. Other required deployment/security configuration is not waived by "zero email." Verify whole-host startup and participation, not merely null SMTP resolution.

### IVSD-M002 - Attendee Notice, Status Access And Calendar Export

**Implemented, 2026-09-08 (P09/P10):** allocation pins `GuestStatusAccessUntilUtc` to finite last-session end plus 30 days. A live status read may extend that promise after a later reschedule, never shorten it or revive missing/expired authority. The limited projection survives checkout-hold expiry without granting checkout/payment access. The first-head fragment scrub precedes analytics/network; the BFF forwards capability only through the private boundary. Saving/copying is explicit, and calendar data remains public. Free confirmed cancellation uses POST, payment/attendance history and transaction-bound revocation/release fences; invalid authority is private 404, authorized ineligibility 409, success/duplicate 204. Native, HTTP, rendered and shipped-script evidence exists; live integrated browser/assistive-technology acceptance does not.

Before and after anonymous registration, explain that updates are not emailed and the attendee must bookmark/check status. The consultancy's sample wording was: "No email notifications are configured for this registration. Bookmark this page or save your ticket link for venue and schedule updates."

Provide an unguessable status capability for inspecting venue, schedule and cancellation updates and for an explicitly authorized cancellation action. Preserve UUIDv7 display/ticket identifiers, but never use an identifier alone as authorization. Existing guest capabilities use separate random secrets and persisted hashes; reuse that foundation rather than a second reservation authority.

Keep post-confirmation lifetime, expiry, revocation, loss and cancellation separate from a checkout hold. GET never cancels, revokes admission or releases capacity. The original blanket nonmutation wording is refined by the approved P09 monotonic status-promise write; it is not cancellation authority. Scope secrets to the intended tenant/order or admission purpose, keep responses private/no-store and prevent logging, referrer leakage and generic idempotency replay of one-time bearer responses.

Offer `.ics` download and copyable status access immediately. Calendar export is a snapshot, not a subscription or promise that future updates will be pushed. Do not expose bearer secrets through calendar/event metadata inadvertently.

### IVSD-M003 - Layered Anti-Hoarding Controls

**Implemented, 2026-09-08 (P08/P10):** native SHA-256 proof-of-work replaces the undecided challenge choice without a third-party CAPTCHA/tracking dependency. Data Protection binds tenant, event, operation, canonical body and idempotency key to stable order/capability material. Fresh allocation has a 120-second deadline; only exact committed recovery may use authenticated history until original expiry plus 24 hours. Difficulty defaults to 18 within 16-22. Private durable tenant/event minute quotas default to 600/120; these are starting budgets, not measured fairness guarantees. Pre-disclosure validation, post-await deadlines and original-order recovery close cached-response and response-store-failure bypasses. Existing approval/ticket/capacity authority is retained, not a new reservation subsystem.

- Enforce bounded client-IP/subnet budgets on anonymous writes, with trusted-proxy handling and additional event/tenant limits. The original finding suggested a token bucket while its mitigation specified a sliding window; these are candidate algorithms for one requirement, not two mandated implementations.
- Require a privacy-preserving bot-defense decision, such as lightweight cryptographic proof-of-work or CAPTCHA, with accessibility, low-powered/mobile devices, replay resistance, offline/self-hosted operation and outbound licensing considered.
- Allow organizers to cap anonymous allocations or require manual approval before scarce capacity is reserved, reusing current approval and inventory authority.
- Keep concurrent capacity checks transactional and duplicate attempts idempotent. Limit collection/retention of abuse identifiers; shared-network attendees must not be treated as one proven identity.

No challenge or limiter proves unique humans or eliminates Sybil abuse. Do not make a centralized tracking service mandatory. Field abuse velocity and false-rejection rates remain validation gaps.

### IVSD-M004 - Graceful Degradation And Deliberate Revocation

Distinguish deliberate disable, enabled incomplete configuration and configured transport failure. The original `EmailDegraded` label was illustrative. **Implemented, 2026-09-08 (P01/P02):** current-impact preview plus purpose-protected actor/scope/revision confirmation and exact `DISABLE EMAIL DELIVERY` acknowledgement gate disable. Ordered relational policy leases coordinate specialized/generic/batch/reset/lock/control-plane writes with admission. A send admitted before disable may finish; this is not retroactive recall. Durable source revisions prevent old optional work from reviving after enablement. Required capability parks may recover only while still authorized/valid; operator parks and uncertain handoffs do not auto-resume.

For an administrator removing working SMTP or disabling delivery, especially after events exist, show the actual consequences and require high-friction typed acknowledgement, for example `DISABLE EMAIL DELIVERY`. Explain paused notifications, changed Local auth-email/recovery availability and direct status/ticket access. Do not falsely state that Keycloak/ATProto login or every registration is disabled.

An involuntary outage must produce an administrative alert and truthful attendee communication posture automatically; it cannot wait for a confirmation dialog. Configured failure must not silently bypass Local verification or rewrite provider verification evidence.

Pause/park eligible pre-handoff work without hammering the transport. Nonessential notifications may be terminally skipped only with an explicit safe policy and observable reason, never reported as sent. Bound retries, parked backlog and retention; preserve current authorized-recipient reevaluation, lease/attempt fences and `Unknown` no-automatic-resend reconciliation after uncertain SMTP handoff.

The original circuit-breaker/safe-no-op proposal expresses suppression of repeated known-unavailable transport work. Retain that failure-suppression requirement without fabricating sent results, replacing durable reconciliation, or mandating a duplicate resilience framework.

Preserve tenant BYO-SMTP: an unlocked tenant can use its own permitted transport when instance fallback is absent, without changing another tenant. Resolve credentials through the approved secrets authority, not governance rows or persisted provisioning requests. P01's resolver no longer caches plaintext resolved transport credentials; it delegates hierarchical settings invalidation, while P02's admission/writer authority uses relational fences and revisions. Canonical SQLite evidence is not full multi-process/provider convergence proof.

The 2026-09-05 launch-critical SMTP/HTTP-503 guidance is historical. Current optional SMTP health is degraded without evicting healthy core readiness; missing required database/security/policy authority remains a failure. P02 has scoped health tests. Parent P12 native Staging HTTP checks now prove these outcomes and restart continuity with optional workers/webhooks explicitly disabled. This does not certify default-topology health or every optional integration.

### IVSD-M005 - Provider-Owned Verification And Administrative Bootstrap

**Implemented, 2026-09-08 (P03-P07):** public Local signup is removed. Ordinary Local login/session validation requires current Ready state/stamp and verified identity when instance delivery intent is enabled; tenant transport choice cannot bypass it. Direct administrative verification and replacement authority are separate. Selected Identity-store operations and explicit cross-store linkage keep Pending non-authenticating until convergence; P06 adds authorized Local wizard/headless bootstrap and reference-only managed enrollment. P05 routes lifecycle operations by the actual persisted account authority. P07 uses one fenced provider/visitor capability for configuration, publication and new allocations, retaining existing lawful status/cancellation access.

Retain Local email/password. Enabling SMTP makes verification mandatory for Local sign-in; an outage is not permission to waive it. Administrator-created Local accounts are directly verified through the explicitly authorized provisioning operation, with the administrator recorded as verification origin. Do not keep unverified development-account access for backward compatibility.

The described Local posture has no public attendee account creation. Enforce the resulting exclusion of authenticated-attendee-only event registration in configuration/publication and HAL as well as actual operations. Existing administrator accounts do not establish public signup capability. Evaluate effective available providers and policy rather than SMTP or the primary-provider name alone.

Only Local's application-owned auth-email flows depend on Event SMTP. Keycloak and ATProto/PDS own their verification, recovery and native onboarding. Event consumes trusted verification assertions when supplied, without inferring them from sign-in. Keycloak may be configured without email; ATProto JIT presently has no mailbox proof. Neither gets a new blanket email-presence prerequisite. Event-generated notifications remain a separate SMTP-dependent concern for users of all providers.

Preserve three bootstrap pathways from the consultancy:

1. **Headless:** deployment-injected Local bootstrap credentials where the authorized design permits, or configured external-provider identifiers such as an ATProto DID. Existing configured-provider selectors require actual provider authentication proof before privilege; a supplied DID is not itself authentication.
2. **First-run Local wizard:** explicitly authorized initial administrative establishment, directly verified under the user-approved policy, without an SMTP chicken-and-egg cycle. It is not an open public account-registration endpoint.
3. **Optional local Mailpit:** test verification without external mail infrastructure. P12 Compose source selects the `mail` profile, pinned v1.30.0 digest, internal SMTP `1025` with no host publication, and loopback-only `127.0.0.1:${MAILPIT_UI_PORT:-8025}:8025`. Capture is bounded to 500 messages with version checking disabled. The sidecar is not mandatory, a public relay or evidence that an attendee received email; native Compose configuration verification is not a running-mail capture test.

Secrets come from the selected environment/Infisical authority, or explicitly selected Development/Testing User Secrets, never inline source defaults. Reuse implemented headless bootstrap rather than redesigning external handshakes.

### IVSD-M006 - Minimal-Data Registration And Retention

In the selected anonymous registration flow, collect/store no email or telephone in `RegistrationOrderPii`. Collect a name only if entry management needs it; name-only is reduced PII, not zero PII. Preserve a truly no-PII experience where the event does not need names.

**Implemented, 2026-09-08 (P11):** `anonymous_registration.retention_days` defaults to seven, bounded 0-30 through existing instance/tenant settings and locks. Allocation pins immutable `AnonymousPiiRetentionUntilUtc` even when no PII exists. Disclosure requires time strictly before both the original order bound and any earlier row bound; anonymous history without its original bound fails closed. Edits, rescheduling, account claim and row recreation never extend the bound; cancellation/earlier rescheduling do not shorten it in this revision. Existing retention categories and bounded cleanup remain, with legal holds/evidentiary/authority-first erasure constraints intact.

This applies beyond row deletion: participant/answer/analytics/export/file metadata and streams, stored CSV content and presigned affordances, provider decryption/handoff and delayed admission/recovery contact delivery enforce disclosure authority. CSV stores `RegistrationContentRetentionUntilUtc`; held physical data is not operational read permission. Durable protected contact material authenticates the included deadline (or separately selected current verified account contact), survives source-row deletion, and rechecks after preparatory waits, including SMTP connect/auth before send. Pre-handoff expiry is `registration_data_retention_expired`, never sent/accepted; already-authorized provider handoff cannot be retracted. See the P11 retention and delayed-contact contracts in Evidence Reviewed. These are not blanket changes to nonanonymous commercial retention.

Do not associate anonymous attendees with tracking cookies or analytics fingerprints. Essential security/antiforgery controls are not permission to introduce behavioral tracking. Bound and minimize IP/subnet abuse data as well as contact fields. Keep answers, contact details and raw capabilities out of logs, traces, metrics, health, ProblemDetails and general public projections.

### IVSD-M007 - Ephemeral Handover, First-Use Rotation And Supervised Reset

**Implemented, 2026-09-08 (P04-P06):** instance-owned issuance/reset uses a selected Identity operation ledger and nonsecret recovery by operation ID. Temporary credentials are disclosed once, not recoverable by redisplay. Pending/link/ChangeRequired/Ready convergence separates credential creation from platform binding; private replacement uses a dedicated short-lived scheme, not a normal session. Actual API/BFF current-state/stamp checks revoke old authority after reset. P05's dedicated lifecycle store provides purpose-bound verification/email-change/recovery and current-password change without SMTP; the old unsupported authentication-service methods must not be mistaken for absence of these routes. P06 durable managed provisioning carries an existing Local administrator reference, not plaintext credential input, and the retired invitation producer cannot resume queued work after restoration.

For administrator-controlled Local provisioning, support request-scoped credential input or cryptographic generation, direct verified state and one-time display/copy for secure out-of-band handover. The consultancy suggested a strong 24-character passphrase; final entropy/policy must be justified rather than treating character count alone as a cryptographic guarantee. Never retain readable passwords for later redisplay, logs or support.

The original proposed `ScheduleManagedTenantProvisioning` variant and `MustChangePasswordOnFirstLogin=true` express the intended outcome, not a safe instruction to add a password to the current durable request DTO. That request is serialized: issue/handle credentials through a boundary that cannot persist their plaintext or replay it through generic idempotency response storage. The zero-email handover must not depend on `RecipientAddressSource.ManagedTenantAdministratorInvitation` delivery.

Force the recipient to establish a private password at first use before administrative access. Restrict actual API/BFF authority and sessions, not just a page redirect. Directly verified status does not bypass first-use rotation. One-time disclosure, expiry, concurrency/replay fencing and audit must be observable in tests.

Provide an audited instance-supervised reset for a locked-out Local tenant administrator, issuing a new temporary credential and reinstating mandatory replacement. Password change by an already authenticated user must not depend on SMTP. This revision selects instance-only credential authority. Tenant delegation remains a separate user-authorized extension; tenant scope cannot authorize takeover of a shared identity or another tenant.

Credential operations stay with their actual authority: do not create Local passwords for Keycloak/ATProto users or assume permission to administer their external accounts. Forced replacement improves accountability but cannot guarantee non-repudiation against the operator of the host.

## Stakeholders

| Stakeholder | Interests and vulnerabilities | Provider-controlled duty |
| --- | --- | --- |
| Grassroots self-hoster / solo instance operator | Technical friction, domain/mail SaaS dependencies, bootstrap and recovery | Accessible setup, deliberate SMTP disable, understandable health and recovery; removing hardship |
| Tenant administrator | Account autonomy, private credentials, recovery without email, private-network operation | Scoped provisioning, forced private replacement, supervised recovery and permitted independent SMTP |
| Anonymous attendee | Privacy, minority-community exposure, unwanted tracking, missing update notices | Minimal contact collection, usable status/cancellation, accessible challenge, truthful no-email expectation |
| Event organizer / venue host | Attendance counts, seat hoarding, false rosters and no-shows | Transactional capacity, abuse controls, roster minimization and honest reachability |
| Authenticated user / tenant | Continuity when mail changes and accurate provider verification | Fail-safe degradation without silent communication loss or cross-provider authentication changes |
| Platform maintainer | Maintainability, licensing, single-binary cohesion, operational responsibility | One authority per concern, reusable Clean Architecture, scoped verification/docs and no compulsory centralized relay |

## I-VSD Principles And Domains

| Principle | Primary domain | Provider-controlled application |
| --- | --- | --- |
| Justice / 'Adl | Strategic/technical | Reduce adoption barriers and unfair capacity capture |
| Trust / Amanah | Governance/operations | Protect bootstrap, credential custody, handover and recovery |
| Accountability / Mas'uliyyah | Governance/technical | Require private first-use replacement and attributable administrative actions, within the host-operator limitation |
| Truthfulness / Sidq | Design/UX | No silent dropped-email success or false verification/delivery claim |
| Non-harm / La Darar | Technical/security | Prevent avoidable hoarding, notification loss and unsafe retry behavior |
| Promise-keeping / Wafa' | Operations/UX | Explain how schedule/venue updates can actually reach an attendee |
| Modesty and privacy / Haya and Khususiya | Strategic/design | Minimize contact data, names, tracking and retained abuse identifiers |
| Avoiding Gharar | Governance/design | Make revocation consequences and uncertain delivery states explicit |

These principles apply across the whole provider responsibility boundary, not just UI wording. They do not certify ethical outcomes.

## Validation Gaps

- P01-P11 implementation is committed through `6b6da88047675fbd1bdddc2e51059272314a6192`; this is not all-green workstream acceptance. P12 setup has 22+20+19 passing checks, parent native Compose assertions pass, and the complete Standalone project passes 56/56 with all six new native host cases.
- Bounded P12 native proof now covers directory/admin/core-health HTTP, degraded SMTP diagnostics, required-authority failure, three-host credential/bootstrap/key/disable continuity, and support-contact update/clear/restart without SMTP mutation. Optional workers/webhooks are disabled in this fixture. Image runtime, default optional-worker topology, comprehensive backup/restore and eligible-registration integration beyond existing mapped proofs are not implied.
- Ring 3 full five-engine provider matrices, both-context migration apply/reverse and snapshot parity, final architecture/conventions and solution/review gates remain pending. Offline migration generation and scoped SQLite parity are not these gates.
- Inherited non-green dispositions remain: P01 architecture 574 pass/3 fail/1 existing skip; P03 full API 2,661 pass/43 fail/1 skip (exit 2); P05's prescribed broader LocalIdentity exit filter unverified despite passing scoped evidence. Context/tasks retain clean-base provenance and separate quarantines, including P11's pre-existing named-index assertion; none is silently repaired or called green here.
- Native/HTTP/loopback-SMTP/rendered/script evidence exists, but live integrated browser/Combined/keyboard/assistive-technology acceptance, multi-process/provider concurrency and unmet latency targets remain open. No empirical anti-hoarding, no-show, accessibility/usability, retention practice or operator-comprehension study is implied.
- Mailpit is now pinned with source-free provenance; no image pull/run or complete assembled-image/offline-conveyance license certification is established by that record. The native proof-of-work uses existing platform primitives, not a newly approved external challenge dependency.
- Measure anonymous signup-to-attendance conversion and no-show rates when neither email confirmation nor an emailed calendar invite exists.
- Assess real public-deployment abuse velocity and whether IP/subnet limits plus the selected challenge preserve fair access.
- Test whether operators distinguish anonymous-only, directory-only and full participation, and intentional email opt-out from degraded configured delivery. The consultancy also used `AnonymousDirectoryOnly` and `FullRegistration` as informal labels; these are not extra implemented modes.
- Assess actual out-of-band credential-sharing habits, including insecure unencrypted handover channels, rather than assuming secure operator behavior.

## Escalation Needed

The original pre-planning delegation escalation is historical: the approved plan selects instance-only creation/reset. `dev/backlog/tenant-delegated-local-account-provisioning.md` is the separately owned P12 graduation target, not authority granted here. Tenant credential delegation requires a new user decision and shared-identity/tenant isolation review before implementation. External-provider credential takeover or Local fallback accounts remain unauthorized.

Before release/workstream closure: record actual P12 durable-host and Ring 3 gate dispositions, close or explicitly retain inherited verification limitations, and complete the required independent technical/security/database/operations review. This report cannot substitute for CTO/PR readiness. `dev/backlog/anonymous-registration-field-evaluation.md` separately owns proposed no-show/abuse/false-rejection/mobile/operator/handover research; the peer-authored backlog is not evidence that research occurred.

Any later paid-event communication/legal-record scope belongs to the existing paid-event governance. No new religious-legal determination is needed for the present technical intake.

Technical rate limiting does not itself require a scholarly ruling. Organizer guidance may explain fair seat allocation (*'Adl fi al-Qismah*) without claiming one. If paid events are later combined with degraded communication, obtain jurisdiction-specific review of receipts, refund notices and statutory transaction records.

## Evidence Reviewed

- Original consultancy input: former `i-vsd-email-optional-self-hosting-consultancy-report.md`, SHA-256 `6213e3809f84ec4c6ec209be5bdca25a2beb30161e3568daa20833aa51e0c3e9`. Its standalone/current/ready-for-planning metadata, findings and lifecycle are incorporated here; the separate file is retired by the user's consolidation request.
- Planning input: this path before consolidation, SHA-256 `5f26939aeeb06fe931495e16e83bb9ee4c31ac627e81fe9721178f51d34c3303`. Its draft/changes-required state is retained as 2026-09-05 history, not the current evidence-mapped status.
- User clarification on 2026-09-05: retain email/password; preserve multiple current and future authentication providers. This is an explicit scope decision, not a source-code observation.
- Further user clarification on 2026-09-05: only Local adapts authentication-email behavior to Event SMTP; Keycloak/ATProto retain verification responsibility and Event must respect trusted provider-reported verification status. Current ATProto JIT source supplies no mailbox proof.
- Superseding Local policy correction on 2026-09-05: SMTP-enabled sign-in requires verification; Local account creation is administrator-controlled and accounts pass as verified directly; unavailable visitor signup prohibits authenticated-attendee-only event registration in that posture. Existing unverified development accounts are not a compatibility constraint.
- Shared [repository evidence and research packet](../dev/active/email-optional-self-hosting/email-optional-self-hosting-context.md), including verified source locators, initial intent classification, related workstreams and official source register.
- Git object `1ca0edeac1da90d29a135c5efa3f8c6269e2574c` is the historical 2026-09-05 product baseline. Current P01-P11 source is pinned separately below; no later implementation is backdated to that original revision.
- [OWASP password recovery guidance](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html), read 2026-09-05; functional security requirements only, no imported source or copied implementation.

### Current Evidence Identity - 2026-09-08

P01-P11 are committed through C11 `6b6da88047675fbd1bdddc2e51059272314a6192`, confirmed as HEAD for this inspection. C10 is `fd43b57f5f2e6ea8b49d1389e8f3639adc1bca90`. The [task ledger](../dev/active/email-optional-self-hosting/email-optional-self-hosting-tasks.md) owns execution/commit status; the [context](../dev/active/email-optional-self-hosting/email-optional-self-hosting-context.md) owns saved result provenance, corrections and inherited limits. Its earlier checkpoint instructions and source ledger are historical where later dated receipts supersede them. No test/build/host/commit was executed by this report update.

Because P12 and working-memory artifacts are not all committed, these are exact inspected input snapshots, not an assertion that C11 contains P12:

| Input | SHA-256 |
| --- | --- |
| [Plan, plan-r2](../dev/active/email-optional-self-hosting/email-optional-self-hosting-plan.md) | `1fc6bbd463b713ed4f7ef15a5f4f1522f30dfaaa79c7e17d621b4eb814e4e9df` |
| Task ledger linked above | `56345fb48a439d39281c3bba8bf185e36ed1bcc3daec4db2630bba654f31d72b` |
| Context linked above | `eac28dfc9eff3ae743c9472afff24b2729041972af64a9565f60a087476357ec` |
| [P12 branding contract](../dev/active/email-optional-self-hosting/p12-branding-contract.md) | `24ebd90a4a023fdc1cdf1f6a3a266d080ce4340a77580b9b150d4ef6191135e8` |
| P12 source snapshot defined below | `bbc04120b4cfe955fb2934308a29097b22da82773b63f09fd9447ebc5425b0f4` |

The P12 source digest hashes the concatenation of each repository-relative UTF-8 path, NUL, exact file bytes and NUL, in ordinal path order, for these nine files:

- `docker-compose.yml`
- `src/Explore.Domain/Constants/GovernanceSettingKeys.cs`
- `src/Explore.Domain/Settings/Definitions/BrandingSettingDefinitions.cs`
- `src/Explore.Application/Settings/Groups/BrandingSettingGroup.cs`
- `src/Explore.Application/DTOs/Instance/BrandingSettingsDto.cs`
- `src/Explore.Application/Services/InstanceGovernanceSettingService.cs`
- `src/Explore.Application/Features/InstanceOnboarding/Common/InstanceOnboardingProfileSettingHelpers.cs`
- `tests/Event.Setup.Core.Tests/EmailOptionalSetupTests.cs`
- `tests/Event.Standalone.IntegrationTests/EmailOptionalStandaloneTests.cs`

P12 source-current support-contact chain: [profile helper](../src/Explore.Application/Features/InstanceOnboarding/Common/InstanceOnboardingProfileSettingHelpers.cs) normalizes/persists public contact to canonical `branding.support_email`; the existing [branding group](../src/Explore.Application/Settings/Groups/BrandingSettingGroup.cs), [governance service](../src/Explore.Application/Services/InstanceGovernanceSettingService.cs) and [BrandingSettingsDto](../src/Explore.Application/DTOs/Instance/BrandingSettingsDto.cs) expose nullable `SupportEmail` / JSON `supportEmail` on the existing branding read. It does not write SMTP sender/delivery policy and adds no parallel profile store, environment authority, endpoint or migration. Native profile command and branding HTTP/restart checks pass; generated-client UI readback passes for update and clearing. No migration or transport authority is introduced.

Saved P12 setup logs were read at `$HOME/.cache/agent-tmp/p12-parent/`: `EmailOptionalSetupTests.log` (22/22, 306ms), `EnvironmentCatalogueInvariantTests.log` (20/20, 303ms), `DotenvContractTests.log` (19/19, 300ms), all zero failures/skips. [Setup tests](../tests/Event.Setup.Core.Tests/EmailOptionalSetupTests.cs) cover parsed Compose isolation/pinning, zero-email Standalone/Split projections, canonical keys and finite value-free validation. Parent native Compose default/profile assertions pass in `p12-parent/compose-{default,mail}-qa.log`. The complete Standalone project passes 56/56 in `p12-parent/standalone-final.log` (72.751s), including six new Staging native cases; the final generated-secret Split fixture correction passes 5/5 in `p12-parent/split-generated-secret-green.log`. The full Release build exits 0 with 1667 warnings and no errors. Native query 7/7 and generated-client UI 1/1 plus Local UI 5/5 provide the related regression evidence.

[Mailpit provenance](../dev/active/email-optional-self-hosting/mailpit-provenance.md) records official metadata and the separately operator-pulled MIT boundary for the pinned image. This update imports no research/source or runtime dependency. The [P11 retention contract](../dev/active/email-optional-self-hosting/p11-retention-contract.md) and [delayed-contact contract](../dev/active/email-optional-self-hosting/p11-delayed-contact-contract.md) provide exact deadline, hold, provider and SMTP-boundary ownership; corrected C11 evidence, not its rejected preliminary closure, governs.

### Implementation Evidence Matrix

Scenario/task IDs below preserve [plan Section 9](../dev/active/email-optional-self-hosting/email-optional-self-hosting-plan.md#9-i-vsd-mapping). Counts are saved bounded results, not cumulative unique workstream totals or new test executions. The shared pending gates in Validation Gaps apply to every row.

| Finding / mitigation | Approved scenarios / tasks | Implemented mechanism and source/test anchors | Saved evidence and remaining validation |
| --- | --- | --- | --- |
| IVSD-F001 / IVSD-M001 | S01-S03, S23-S24; 1.1-1.3, 2.1-2.3, 12.1-12.3 | [Capability resolver](../src/Explore.Infrastructure/Mail/EmailDeliveryCapabilityResolver.cs), [SMTP health](../src/Explore.API/HealthChecks/SmtpHealthCheck.cs), [Compose](../docker-compose.yml), P12 setup/branding chain above | P01 Domain 10/10 and real SQLite resolver/settings 26/26; P02 optional-health evidence and P12 22+20+19 setup passes. Bounded native directory/admin/health/restart proof passes in the 56/56 Standalone gate; optional default-topology and broader registration integration remain distinct; setup-time ease/60-second outcome is unmeasured. |
| IVSD-F002 / IVSD-M002 | S04, S06, S18-S20, S25-S26; 7.1-7.3, 9.1-9.3, 10.1-10.3 | [Limited status guard](../src/Explore.Application/Features/RegistrationOrders/Handlers/GuestRegistrationStatusAccessGuard.cs), [private status page](../src/Explore.Blazor.Client/Pages/Registration/GuestRegistrationStatus.razor), [status native tests](../tests/Event.Persistence.IntegrationTests/GuestRegistrationStatusTests.cs), [cancellation command](../src/Explore.Application/Features/RegistrationOrders/Commands/CancelConfirmedGuestRegistrationCommand.cs), [cancellation HTTP tests](../tests/Event.API.IntegrationTests/Features/GuestRegistrationCancellationHttpTests.cs) | P09 native 27/recovery 21/visitor 20, API 44 and browser/BFF/script 111 are recorded in context; P10 final native cancellation 32/32 and Domain 9/9 include tracked-issuance correction. Live integrated/assistive-technology usability, self-monitoring comprehension and no-show outcomes remain unproved. |
| IVSD-F003 / IVSD-M003 | S16-S17, S19, S22; 8.1-8.3, 10.1-10.3 | [Native challenge](../src/Explore.Infrastructure/Services/Registration/AnonymousRegistrationChallengeService.cs), [pre-disclosure boundary](../src/Explore.API/Middleware/AnonymousRegistrationChallengeBoundary.cs), [durable quota](../src/Explore.Persistence/Services/AnonymousRegistrationChallengeQuota.cs), [allocation replay tests](../tests/Event.Persistence.IntegrationTests/AnonymousRegistrationReplayTests.cs) | P08 core 27, quota 8, final native HTTP 8 and browser worker/client/BFF 12/34/4 saved passes; final C08 includes canonical-case binding and post-await recovery corrections. P10 cancellation evidence preserves capacity authority. Shared-key/database hosts in one process are not multiprocess/provider proof; real abuse velocity, shared-network false rejection and low-powered-device cost remain outside-revision research. |
| IVSD-F004 / IVSD-M004 | S02-S06, S24; 1.1-1.3, 2.1-2.3, 12.1-12.3 | [Guarded settings writer](../src/Explore.Persistence/Services/EmailDeliverySettingsWriter.cs), [disable command](../src/Explore.Application/Features/EmailDispatch/Handlers/Commands/DisableEmailDeliveryCommandHandler.cs), [dispatch drain](../src/Explore.Infrastructure/EmailDispatchDrainService.cs), [optional-health tests](../tests/Event.API.IntegrationTests/Features/EmailOptionalHealthTests.cs) | P02 canonical EmailDelivery 138/138 and non-Runtime Infrastructure 1798/1798; actual HTTP 6/6 plus corrected concurrent/native authority checks and approved closure. Recorded durations exceed target latency. Full provider/multiprocess disable/admission/restart, durable suppression/Unknown preservation and operator comprehension remain unclosed. |
| IVSD-F005 / IVSD-M005 | S04, S07-S09, S11-S15, S25-S26; 3.1-3.3, 4.1-4.3, 5.1-5.3, 6.1-6.3, 7.1-7.3 | [Local admission](../src/Explore.Persistence/Identity/LocalIdentityAuthService.cs), [bootstrap operation](../src/Explore.Application/Features/InstanceOnboarding/Services/LocalAdministratorBootstrapOperation.cs), [visitor resolver](../src/Explore.Application/Services/VisitorAccessCapabilityResolver.cs), [visitor settings tests](../tests/Event.Persistence.IntegrationTests/Repositories/VisitorAccessSettingsWriterTests.cs) | P03 scoped native/rendered/BFF and trusted-provider verification evidence is recorded; P06 committed bootstrap/linkage evidence is distinct from full deployment. P07 Domain 12/Application 38, native mutation 24/event 20, ATProto HTTP 16 and UI 141/accessibility checks 8 pass. P03 full API remains non-green; P12 durable setup and final provider/browser acceptance remain pending. No external mailbox proof is inferred from native provider login. |
| IVSD-F006 / IVSD-M006 | S18, S21-S22; 9.1-9.3, 11.1-11.3 | [Retention policy](../src/Explore.Domain/Services/Registration/AnonymousRegistrationRetentionPolicy.cs), [writer tests](../tests/Event.Persistence.IntegrationTests/AnonymousRetentionBoundaryTests.cs), [read-boundary tests](../tests/Event.Persistence.IntegrationTests/AnonymousRetentionReadBoundaryTests.cs), [delayed-contact tests](../tests/Event.Persistence.IntegrationTests/AnonymousRetentionContactDeliveryTests.cs), P11 contracts above | Corrected parent C11 native AnonymousRetention 90/90 and API Release zero warnings/errors; all four primary migrations generated, public generated contracts unchanged from C10. Real disclosure Red/Green covers sibling file GET/release and delayed queued/loopback-SMTP paths; original rejected closure is not acceptance. Provider matrix/migration roundtrips, deployment cleanup/restore and empirical privacy practice remain unclosed. |
| IVSD-F007 / IVSD-M007 | S09-S13, S24; 4.1-4.3, 5.1-5.3, 6.1-6.3 | [Current instance authority](../src/Explore.Application/Features/Authentication/Local/LocalCredentialAdministrator.cs), [lifecycle store](../src/Explore.Persistence/Identity/LocalIdentityLifecycleStore.cs), [SMTP-independent password handler](../src/Explore.Application/Features/Authentication/Local/Handlers/Commands/ChangeLocalPasswordCommandHandler.cs), [first-use tests](../tests/Event.Persistence.IntegrationTests/Identity/LocalCredentialFirstUseTests.cs), [reset tests](../tests/Event.Persistence.IntegrationTests/Identity/LocalCredentialResetTests.cs), [managed linkage tests](../tests/Event.Persistence.IntegrationTests/Identity/ManagedTenantLocalAdministratorLinkageTests.cs) | P04 native first-use/reset/current-session and restricted API/BFF evidence is recorded. P05 final native lifecycle 58/58 plus first-use regressions 68/68; HTTP 14/14 and loopback delivery 26/26; broader prescribed exit filter remains unverified. P06 reference-only linkage is committed. Actual durable-host/restart, full two-store/provider failure recovery and live handover usability remain pending; tenant delegation and field-sharing habits are outside this revision. |

### Factual Refresh Required After Host Proof

- Bind the final P12 source/generated-contract and context/tasks revisions or C12 commit, replacing the working snapshot only after the parent records exact evidence; do not backdate C12 into C11.
- Record which actual Standalone host scenarios passed or failed, environment/provider/topology and persisted key/database authority, including zero-email/degraded core responses, privileged first-use access, required-dependency failures and support-contact save/read/change/clear/restart without SMTP mutation. Merely authored tests or an InMemory fixture do not establish durability.
- State exactly which restart/restore authorities were exercised: delivery intent, revisions/suppression, uncertain handoffs, credential/bootstrap identity, selected secrets and Data Protection continuity. A partial host pass must not close unexercised S24 dispatch/provider cases.
- Update P12 closure/review and each Ring 3/inherited gate independently from saved output, preserving failures/skips/latency and browser limits. Host proof alone resolves neither all seven findings nor empirical autonomy, fair allocation, no-show or usability outcomes. Peer backlog/ADR graduation is documentation, not those outcomes.

### Repository And Related-Report Evidence

Historical locators in Findings bind to 2026-09-05; these portable links target current files. Interpret changed responsibilities as below, not as line-stable historical source.

| Source | Evidence retained and bounded interpretation |
| --- | --- |
| [SmtpConfigResolver](../src/Explore.Infrastructure/Mail/SmtpConfigResolver.cs) | Current capability-backed transport adapter and hierarchical invalidation; the baseline five-minute plaintext transport cache is removed |
| [SmtpEmailService](../src/Explore.Infrastructure/Mail/SmtpEmailService.cs) | Explicit unconfigured-send failure and connection-test behavior |
| [InstanceSmtpSettingService](../src/Explore.Application/Services/InstanceSmtpSettingService.cs) | Host/port/sender/TLS settings; the original claim that this stores credentials is corrected: credentials come from the secrets authority |
| [PublicExperienceSettingDefinitions](../src/Explore.Domain/Settings/Definitions/PublicExperienceSettingDefinitions.cs) | Existing discovery presentation remains distinct from the added visitor-access policy; P07 resolver/writers own effective onboarding enforcement |
| [RegistrationOrderPii](../src/Explore.Domain/RegistrationOrderPii.cs) | Separate nullable purchaser contact fields, verification and retention |
| [RegistrationOrder](../src/Explore.Domain/RegistrationOrder.cs) and [native submissions](../src/Explore.Application/Features/RegistrationSubmissions/NativeRegistrationSubmissionCommands.cs) | Existing order/lifecycle authority and published attempt lineage; original finding locators retained above |
| [LocalIdentityAuthService](../src/Explore.Persistence/Identity/LocalIdentityAuthService.cs) and [LocalIdentityUser](../src/Explore.Persistence/Identity/LocalIdentityUser.cs) | Current Ready/stamp/instance-intent verification admission and restricted replacement; P04/P05 state/lifecycle stores, not a new user boolean or the legacy unsupported methods, implement rotation/reset |
| [EmailDispatchEligibilityEvaluator](../src/Explore.Persistence/Services/EmailDispatchEligibilityEvaluator.cs) | Managed invitation destination rules and fenced rate/eligibility evaluation; no inference of a registration bot challenge |
| [EmailDispatchDrainService](../src/Explore.Infrastructure/EmailDispatchDrainService.cs) | Bounded retry, tenant context, parking and uncertain-handoff reconciliation |
| [RegistrationOrderAccessGuard](../src/Explore.Application/Features/RegistrationOrders/Handlers/RegistrationOrderAccessGuard.cs) | Tenant/event/order capability and expiry checks; not proof of long-lived status access |
| [UserController](../src/Explore.API/Controllers/UserController.cs) | Trusted principal-derived provider verification passed into synchronization |
| [ATProto JIT provisioning](../src/Explore.Application/Features/Authentication/Atproto/Services/AtprotoJitAccountProvisioningOperation.cs) | Verified DID-backed identity without email or mailbox verification |
| [Keycloak lifecycle delegation](../src/Explore.Infrastructure/Services/Keycloak/KeycloakAccountAuthorityLifecycleEmailService.cs) | Account-authority email API rather than Event SMTP transport |
| [Managed provisioning handlers](../src/Explore.Application/Features/Management/Handlers/ManagedTenantProvisioningHandlers.cs) | P06 existing Local administrator reference and idempotent tenant linkage; durable plaintext/invitation production is not the implemented handover authority |
| [Authentication policy](../docs/internal/AUTHENTICATION.md) and [operations](../docs/internal/OPERATIONS.md) | Operator/technical guidance; original missing-lifecycle and launch-critical SMTP statements are historical, superseded by current P03-P06 lifecycle and P02 optional-readiness behavior |
| [Registration data collection I-VSD](i-vsd-registration-data-collection.md) | Original related analysis separating orders from participant identity |
| [Headless onboarding I-VSD](i-vsd-headless-instance-onboarding.md) | Original related analysis of authenticated first-administrator bootstrap |

The linked context contains the fuller source ledger, test locations and sanitized official research register. Legacy line locators are historical anchors, not fresh runtime measurements.

## Missing Evidence

No production incident/spam telemetry, stakeholder interviews, formal security audit, field abuse metrics or usability study establishes the intended outcomes. Scoped implementation verification exists as recorded below; final deployment/workstream validation does not. Specifically absent are feedback from small masajid deploying on single-board computers/minimal VMs and mobile browser benchmarks for offline `.ics` generation. Future empirical observations must not be fabricated or replaced with architectural confidence.

## Context Inventory

The repository already has Clean Architecture, MediatR, EF Core, Local and external identity providers, BFF trust boundaries, scoped capability registration, participation policy, durable dispatch, hierarchical non-secret settings and a separate secrets authority. These are reusable foundations, not new features to invent.

The original inventory named `Explore.Application`, `Explore.Domain`, `Explore.Infrastructure`, `Explore.Persistence`, public/internal documentation, standalone SQLite, Compose and Aspire, and reported 35 I-VSD reports at its cutoff. That count is retained as historical context, not reasserted as a current inventory. Original user inputs required both graceful missing/failing-SMTP behavior with typed revocation confirmation and multi-tenant out-of-band administrative handover; subsequent user decisions refine, rather than erase, those goals.

Initial source guidance spans security/privacy and the sovereign registration intent even though payment-processing changes are excluded. Source-of-truth ownership and applicable verification obligations remain explicit in the approved plan mappings and outstanding implementation gates.

## Planning Handoff

- Workstream: email-optional-self-hosting
- Status: stale
- Reviewed input revision: P01-P11 Git object `6b6da88047675fbd1bdddc2e51059272314a6192` plus the exact `plan-r2`, tasks/context and P12 SHA-256 snapshots in Evidence Reviewed.
- Findings and mitigations: IVSD-F001 -> IVSD-M001 through IVSD-F007 -> IVSD-M007; all remain open.
- Required plan mappings: the implementation evidence matrix below reproduces all seven mappings from plan Section 9 and adds mechanisms, saved evidence and remaining validation. No S01-S26 behavior is silently deferred into empirical research or tenant delegation.
- Escalations required before: release/workstream closure for outstanding verification/review; before any separate tenant-delegation implementation for user authority approval.
- Refresh triggers: administrator identifier/provider scope; visitor defaults; communication guarantees; SMTP disable/tenant override behavior; capability lifecycle; abuse challenge; retention; credential recovery authority; changed mapped mitigation; P12 host proof or final gate disposition changing an evidence claim.
- Plan-aligned: historical plan-r2 mapping retained; the combined upstream integration awaits revalidation.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
| --- | --- | --- | --- | --- |
| 2026-09-05 | none | current (original consultancy) | Initial zero-email self-hosting and graceful-degradation consultation | Original working-tree assessment; original disposition ready-for-planning |
| 2026-09-05 | current | current (original consultancy) | Expanded consultation to multi-tenant out-of-band provisioning and credential handover | Original ManagedTenantProvisioning and EmailDispatchEligibilityEvaluator analysis |
| 2026-09-05 | none | draft | Integrated planning intake corrected consultancy assumptions and identified a foundational identity branch | Pinned source report and product revision above; shared context packet |
| 2026-09-05 | draft | draft | User retained email/password and rejected username-only identities to preserve multi-provider authentication | User clarification and context's resolved identity decision; remaining provisioning intake continues |
| 2026-09-05 | draft | draft | User separated provider-owned authentication email from Event SMTP and retained trusted verification checks | Context's authentication-email authority matrix; UserController trusted-principal mapping, Keycloak delegation adapter, ATProto JIT source |
| 2026-09-05 | draft | draft | Relocated planning artifacts to canonical main-repository paths under the updated planning workflow | Product revision and behavioral decisions unchanged; native artifact relocation only |
| 2026-09-05 | draft | draft | User mandated SMTP-enabled Local verification and directly verified administrative provisioning, rejecting permissive sign-in preservation | Corrected Local admission/event eligibility contract; instance versus tenant provisioning authority remains open |
| 2026-09-05 | two subject reports | draft (single canonical report) | User explicitly required a lossless consolidation instead of duplicate reports | Both input hashes, all seven stable findings/mitigations, source locators, corrected claims and decision history retained here |
| 2026-09-05 | draft | current / plan-aligned | Completed `plan-r1` with 12 phases, 36 implementation tasks and all seven I-VSD mappings | Canonical plan/task links, selected authority boundaries and bounded verification/commit contracts; implementation not started |
| 2026-09-08 | draft / stale implementation prose | current (source-current; findings open) | Approved P12 knowledge graduation reconciles the same report to plan-r2 and implemented P01-P11 mechanisms | C11 `6b6da88047675fbd1bdddc2e51059272314a6192`, SHA-256-bound working inputs and saved scoped evidence; P12 host/restart, Ring 3, inherited gates and empirical outcomes remain unclosed |
| 2026-09-08 | current (source-current; findings open) | stale / changes-required | Upstream integration changes the authentication/storage and migration basis | Preserve historical evidence and stable findings; revalidate the combined source and verification dispositions before closure |

## Consolidation Coverage And Corrections

| Original material | Canonical destination / treatment |
| --- | --- |
| Metadata and original two review transitions | Review Metadata, Evidence Reviewed and Review Lifecycle retain original draft/consolidation history; 2026-09-08 source-current/plan-aligned mapping does not imply closed verification |
| Target deployments, audiences, in/out scope | Scope and Context Inventory; payment/PDS/legal boundaries retained |
| Seven overlooked failures, four negative consequences and four positive outcomes | Common Overlooked Failures And Outcomes, including unmeasured 60-second ambition |
| IVSD-F001 through IVSD-F007, ownership, principles and validation duties | Findings retain IDs, original evidence locators and explicit provider decisions; source corrections are stated rather than silently dropped |
| UI copy, visitor posture and four proposed event experiences | Recommendations retain sample copy and candidate names, mapped onto existing orthogonal participation policies |
| Original technical proposals and all seven mitigations | Mitigations retain optional setup, status/cancellation/ICS, IP/subnet budgets, challenge choices, allocation/approval, typed disable, health, Mailpit ports, BYO-SMTP, retention and credential handover/reset |
| Four rejected alternatives and later rejected planning assumptions | Recommendations / Rejected Alternatives |
| Stakeholder interests and eight principle/domain mappings | Stakeholders and I-VSD Principles And Domains |
| No-show, abuse velocity, operator comprehension and credential-sharing gaps | Validation Gaps; no empirical confidence invented |
| Fair-seat education and possible paid-event consumer-law escalation | Escalation Needed |
| Original nine evidence sources and missing field/mobile evidence | Evidence Reviewed and Missing Evidence, with portable links and historical locators |
| Provider-owned authentication email, mandatory Local verification, direct administrative verification, account-only event eligibility | Settled User Decisions, IVSD-M005/IVSD-M007 and Planning Handoff |
| Added capability, cache/replica, privacy, replay, first-use and readiness constraints | Common failures and the corresponding mitigations |

Conflicting source claims are preserved through explicit correction: finite current retries replace the alleged observed infinite loop; administrator verification is an authorized origin rather than an SMTP side effect; Keycloak/ATProto auth is independent of Event SMTP; names are PII; SMTP credentials are not governance values; external-provider selectors are not authentication proof; a static calendar is not a pushed update; forced rotation does not defeat a malicious host. The original proposal to accept a password in durable managed provisioning is retained as an intended handover capability but rejected as a plaintext-persistence implementation.
