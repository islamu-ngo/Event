<!-- ABOUTME: Canonical I-VSD report for email-optional self-hosting, community directories, and administrative provisioning. -->
<!-- ABOUTME: Consolidates consultancy evidence, planning analysis, and user decisions with explicit correction and review history. -->

# Email-Optional Self-Hosting - I-VSD Consultancy And Planning Report

Last Updated: 2026-09-05

## Review Metadata

- Mode: planning
- Subject: email-optional self-hosting and mailbox-free participation
- Workstream: email-optional-self-hosting
- Report kind: consolidated consultancy and planning assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-05
- Reviewed input revision: original consultancy SHA-256 `6213e3809f84ec4c6ec209be5bdca25a2beb30161e3568daa20833aa51e0c3e9`; pre-consolidation planning assessment SHA-256 `5f26939aeeb06fe931495e16e83bb9ee4c31ac627e81fe9721178f51d34c3303`; repository Git object `1ca0edeac1da90d29a135c5efa3f8c6269e2574c`
- Supersedes: the two separate subject reports identified in Evidence Reviewed; this file is their single canonical successor, as explicitly requested by the user.
- Reviewed design: `plan-r1`; the canonical plan/task links and byte-level revision binding are recorded in Planning Handoff.
- Review boundary: alignment is to the proposed implementation design only. Findings remain open; implementation, empirical outcomes and security certification are not claimed.

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
- Deep ATProto/PDS cryptographic handshakes remain with the separate ATProto workstream. The original consultancy cited `i-vsd-database-backed-atproto-auth.md`; it currently exists only in that workstream's isolated checkout, not main `develop`, so no broken main-repository link or claim of merged evidence is introduced here.
- External identity-provider takeover, unrelated active work, and replacement of legal advice about consumer notifications are not authorized.

### Settled User Decisions

The user resolved administrator identifier scope on 2026-09-05: retain email/password for Local and preserve the extensible multi-provider authentication model. Username-only identities and an additional authentication provider are excluded. Other providers retain their native identity semantics. Optional outbound delivery does not imply that an email-shaped identifier proves mailbox ownership.

The user additionally resolved authentication-email ownership: only Local Identity adapts its application-owned verification/reset email flows to Event SMTP availability. Keycloak owns its authentication emails and verification; ATProto leaves account email to the actual PDS/account provider. Event consumes trusted provider verification status where available without taking over that responsibility. Event-generated registration and event-notification emails remain governed by Event's own SMTP capability for all users.

The user subsequently specified mandatory verification for Local sign-in when SMTP is enabled, administrator-controlled Local account creation, and directly verified administrator-provisioned accounts. Preserving existing unverified sign-in was explicitly rejected. Where public attendee account creation is unavailable under this Local posture, authenticated-attendee-only event registration must not be configurable. External providers retain their own onboarding possibilities, including Keycloak configurations without email.

### Planning Selections For The Complete Design

After the instruction to complete the plan, `plan-r1` selects instance-administrator-only Local credential creation/reset, consistent with global credential ownership. Tenant membership/role administration does not grant credential authority; tenant delegation is a separately graduated proposal, not a hidden unresolved branch or a claim of user approval.

The design uses explicit default-off delivery intent, coherent tenant transport ownership, a common disable/handoff fence and nonfatal email degradation for core readiness. Global Local verification reads instance intent; tenant mail cannot become a sign-in bypass. It specifies two-store credential operation convergence, mandatory private first-use replacement and current-stamp session checks, a native bounded challenge with stable-order replay recovery, limited-purpose post-confirmation access, free-only transactional cancellation and event-bounded PII expiry. Existing providers, modes, orders, outboxes, calendar serializer and cleanup remain authoritative.

## Claim Boundary

This is provider-responsibility design reasoning about maintainer/operator choices: identity authority, defaults, data minimization, failure modes, transparency, credential stewardship and abuse safeguards. It is not a fatwa, Sharia certification, halal/haram ruling, legal opinion, security warranty, or assurance that unimplemented mitigations work. Religious-legal questions about commercial activities or contracts require qualified scholarly authority. Source inspection establishes current implementation facts; no runtime validation or stakeholder study was performed.

## Common Overlooked Failures And Outcomes

### Failures And Their Evidence Limits

- Anonymous attendees may assume that rescheduling, venue changes or cancellation notices will reach them and arrive at an empty venue when no communication channel exists.
- Unverified seat claims can enable capacity hoarding, fake rosters and high no-show rates, depriving genuine attendees of scarce places. The original consultancy described this through harm and appropriation concerns (*Darar* and *Ghasb*); the technical assessment does not issue a religious ruling.
- Missing/failed SMTP can create useless retry work, dead-letter growth and operational load. The original report described infinite retries as if observed; current source instead has bounded attempts and reconciliation. The residual risk is inappropriate handling and accumulated backlog, not a proven infinite loop.
- Requiring email verification before an initial administrator can configure SMTP can create a bootstrap cycle. Existing Local behavior does not prove that cycle today; the requested verified administrative provisioning must prevent it in the target design.
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

IDs preserve the original seven findings and their one-to-one mitigation identities. Lifecycle is **open** for all seven; none is resolved by consolidation. Validation level is inspected source and named scout evidence, not executed target behavior.

### IVSD-F001 - SMTP Dependency And Community Autonomy

- **Severity / claim:** High; provider-controlled adoption and technical access.
- **Principle/domain:** Justice (*'Adl*) and removing undue hardship (*Raf' al-Haraj*); strategic/technical.
- **Stakeholders / provider decision:** Grassroots operators, masajid and nontechnical organizers; whether SMTP is a prerequisite or progressive capability.
- **Evidence:** Original locators were `SmtpEmailService.cs:48-52` and `InstanceSmtpSettingService.cs:24-30`. Current `SmtpConfigResolver` already returns null without host/from address and `SmtpEmailService` explicitly fails a send. These facts do not prove whole-host startup requires SMTP.
- **Mitigation:** [IVSD-M001](#ivsd-m001---email-optional-architecture-with-zero-config-default).
- **Owner / validation:** Platform Architecture; demonstrate standalone startup, functional directory and eligible registration without SMTP variables.

### IVSD-F002 - Anonymous Attendee Communication Asymmetry

- **Severity / claim:** High; promise-keeping and operational transparency.
- **Principle/domain:** Trust (*Amanah*), promise-keeping (*Wafa' bil-'Uqud*) and non-harm (*La Darar*); UX/operations.
- **Stakeholders / provider decision:** Attendees and organizers; communicate no-email limitations and provide a way to inspect event changes.
- **Evidence:** Original locators were `NativeRegistrationSubmissionCommands.cs:41-50` and `RegistrationOrderPii.cs:27-29`. Current guest start/free-finalize paths omit email; `RegistrationOrderAccessGuard` checks order expiry. That does not establish durable post-confirmation status or sufficient attendee notices.
- **Mitigation:** [IVSD-M002](#ivsd-m002---attendee-notice-status-access-and-calendar-export).
- **Owner / validation:** Registration and UX/Frontend; verify before/after notices, scoped bookmarkable status, change visibility and authorized cancellation.

### IVSD-F003 - Anonymous Capacity Hoarding And Sybil Abuse

- **Severity / claim:** High; harm prevention and distributive justice.
- **Principle/domain:** Non-harm (*La Darar wa-la Dirar*) and justice (*'Adl*); security/technical.
- **Stakeholders / provider decision:** Organizers, prospective attendees and venue hosts; abuse budgets, challenges and allocation controls.
- **Evidence:** Original locators were `RegistrationOrder.cs:349` and `EmailDispatchEligibilityEvaluator.cs:528-538`; these are cited context, not proof of an attack. Current guest endpoints already have PublicTransactional controls and capability checks. No registration-specific bot challenge was established by this investigation.
- **Mitigation:** [IVSD-M003](#ivsd-m003---layered-anti-hoarding-controls).
- **Owner / validation:** Application Security and CQRS; verify client/event/tenant budgets, challenge replay prevention and concurrent capacity fairness. Rate limiting alone does not establish unique humans.

### IVSD-F004 - SMTP Disconnection And Operational State Mismatch

- **Severity / claim:** High; fail-safe operational state and honest delivery reporting.
- **Principle/domain:** Truthfulness (*Sidq*) and avoiding uncertainty (*Gharar*); operations/governance.
- **Stakeholders / provider decision:** Operators, users and tenant managers; distinguish intentional disable, configuration absence and unexpected failure.
- **Evidence:** Original locators were `SmtpConfigResolver.cs:94-106` and `EmailDispatchDrainService.cs:293`. Current dispatch has bounded attempts, tenant pauses, lease fencing and uncertain-handoff reconciliation; resolved SMTP configuration is cached for five minutes.
- **Mitigation:** [IVSD-M004](#ivsd-m004---graceful-degradation-and-deliberate-revocation).
- **Owner / validation:** Infrastructure, secrets and Blazor administration; verify confirmation, replica/cache convergence, tenant isolation, queue disposition and truthful health without automatic uncertain resend.

### IVSD-F005 - First-Run Administrative Bootstrap Integrity

- **Severity / claim:** Medium; privileged bootstrap and verification authority.
- **Principle/domain:** Trust (*Amanah*) and operational excellence (*Ihsan*); identity/governance.
- **Stakeholders / provider decision:** Initial operators; usable administrator establishment without external SMTP and without granting authority from an unproven selector.
- **Evidence:** Original locators were `LocalIdentityAuthService.cs:93-102` and the headless-onboarding report at lines 51-60. Current Local registration issues unconfirmed sessions; configured headless bootstrap requires provider proof. Those are current facts, not permission to preserve behavior the user rejected.
- **Mitigation:** [IVSD-M005](#ivsd-m005---provider-owned-verification-and-administrative-bootstrap).
- **Owner / validation:** Core Identity; verify mandatory SMTP-enabled Local sign-in confirmation, authorized directly verified provisioning and all selected bootstrap paths without an SMTP setup cycle.

### IVSD-F006 - Privacy Strength Of Minimal-Data Participation

- **Severity / claim:** Positive objective/system strength, with unresolved privacy risks; data minimization.
- **Principle/domain:** Modesty (*Haya*), avoiding spying (*Tajassus*), privacy and rights of people (*Huquq al-'Ibad*); strategic/design.
- **Stakeholders / provider decision:** Privacy-conscious attendees and political/religious minority communities; avoid unnecessary contact collection, tracking and retention.
- **Evidence:** Original locators were `PublicExperienceSettingDefinitions.cs:10-15` and the registration-data-collection report at lines 54-58. Current recovery policy includes EmailOptional/CapabilityLinkOnly; order PII is separate. Names remain PII, and current retention is category-based rather than automatically event-end.
- **Mitigation:** [IVSD-M006](#ivsd-m006---minimal-data-registration-and-retention).
- **Owner / validation:** Data Governance and Registration; verify no email/phone collection in the selected anonymous flow, justified names, deletion deadlines and tracking exclusion.

### IVSD-F007 - Credential Handover And Administrative Accountability

- **Severity / claim:** High; credential confidentiality, attribution and recovery boundaries.
- **Principle/domain:** Trust (*Amanah*), justice/accountability (*'Adl wa-Mas'uliyyah*) and truthfulness (*Sidq*); governance/technical.
- **Stakeholders / provider decision:** Instance administrators, tenant administrators and tenant users; one-time handover, private replacement and supervised recovery.
- **Evidence:** Original locators were `EmailDispatchEligibilityEvaluator.cs:310-332` and `LocalIdentityAuthService.cs:93-102`. Current Local credential storage has no first-use rotation state; password change/reset are unsupported. Managed tenant requests are durably serialized, and tenant onboarding of an existing actor is not authentication-account creation.
- **Mitigation:** [IVSD-M007](#ivsd-m007---ephemeral-handover-first-use-rotation-and-supervised-reset).
- **Owner / validation:** Multi-Tenancy, Identity and Control Plane; verify one-time disclosure, complete pre-rotation access restriction, audited reset, secret-free durable requests and no cross-tenant/global identity takeover.

## Recommendations

### Onboarding And User Experience

- Make the SMTP setup step explicitly optional. The original suggested text was "Skip for now - Run in Zero-Email / Community Directory Mode (Email can be enabled anytime in Settings)." This remains sample copy, not an implemented string or a promise to silently change authentication providers.
- Display an anonymous-registration information notice before and after submission, with copyable status/ticket access and calendar download.
- Present newly issued tenant credentials once with a "Copy Credentials & Pass Securely Out-of-Band" affordance; dismissal must not allow plaintext redisplay.
- Distinguish visitor signup/login affordances from the operator entry point; use server-authored HAL and server-enforced policy, not client role checks.

### Visitor Policy And Event Experiences

The consultancy proposed `VisitorAccessMode: FullRegistrationAndAuth | AnonymousOnly | DirectoryListingOnly` under `GovernanceSettingKeys.PublicExperience`, including hiding visitor login/signup buttons in the latter modes. Preserve these functional choices while choosing final names through existing settings conventions; the values are proposed, not implemented. Existing `DiscoveryCentric` and `OrganizationCentric` describe discovery layout, not authentication admission.

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

### IVSD-M001 - Email-Optional Architecture With Zero-Config Default

Outbound email is an optional, tenant-effective progressive capability, not an inherent requirement for directory browsing, event management or eligible anonymous registration. Unconfigured/disabled delivery should be reported truthfully as `Operational (Zero-Email Mode)` rather than a failed mail service. The original suggested `EmailDeliveryHealthCheck` is a proposed responsibility, not a verified existing class.

Keep one binary/codebase and established composition roots. No mandatory relay, email SaaS, domain-verification DNS records or SMTP credentials are added to first use. Other required deployment/security configuration is not waived by "zero email." Verify whole-host startup and participation, not merely null SMTP resolution.

### IVSD-M002 - Attendee Notice, Status Access And Calendar Export

Before and after anonymous registration, explain that updates are not emailed and the attendee must bookmark/check status. The consultancy's sample wording was: "No email notifications are configured for this registration. Bookmark this page or save your ticket link for venue and schedule updates."

Provide an unguessable status capability for inspecting venue, schedule and cancellation updates and for an explicitly authorized cancellation action. Preserve UUIDv7 display/ticket identifiers, but never use an identifier alone as authorization. Existing guest capabilities use separate random secrets and persisted hashes; reuse that foundation rather than a second reservation authority.

Define post-confirmation lifetime, expiry, revocation, loss and cancellation separately from a checkout hold. GET must remain nonmutating. Scope secrets to the intended tenant/order or admission purpose, keep responses private/no-store and prevent logging, referrer leakage and generic idempotency replay of one-time bearer responses.

Offer `.ics` download and copyable status access immediately. Calendar export is a snapshot, not a subscription or promise that future updates will be pushed. Do not expose bearer secrets through calendar/event metadata inadvertently.

### IVSD-M003 - Layered Anti-Hoarding Controls

- Enforce bounded client-IP/subnet budgets on anonymous writes, with trusted-proxy handling and additional event/tenant limits. The original finding suggested a token bucket while its mitigation specified a sliding window; these are candidate algorithms for one requirement, not two mandated implementations.
- Require a privacy-preserving bot-defense decision, such as lightweight cryptographic proof-of-work or CAPTCHA, with accessibility, low-powered/mobile devices, replay resistance, offline/self-hosted operation and outbound licensing considered.
- Allow organizers to cap anonymous allocations or require manual approval before scarce capacity is reserved, reusing current approval and inventory authority.
- Keep concurrent capacity checks transactional and duplicate attempts idempotent. Limit collection/retention of abuse identifiers; shared-network attendees must not be treated as one proven identity.

No challenge or limiter proves unique humans or eliminates Sybil abuse. Do not make a centralized tracking service mandatory. Field abuse velocity and false-rejection rates remain validation gaps.

### IVSD-M004 - Graceful Degradation And Deliberate Revocation

Distinguish deliberately disabled/unconfigured delivery from a configured transport that unexpectedly fails. The original `EmailDegraded` label is a proposed observable state, not proof that a matching state machine exists.

For an administrator removing working SMTP or disabling delivery, especially after events exist, show the actual consequences and require high-friction typed acknowledgement, for example `DISABLE EMAIL DELIVERY`. Explain paused notifications, changed Local auth-email/recovery availability and direct status/ticket access. Do not falsely state that Keycloak/ATProto login or every registration is disabled.

An involuntary outage must produce an administrative alert and truthful attendee communication posture automatically; it cannot wait for a confirmation dialog. Configured failure must not silently bypass Local verification or rewrite provider verification evidence.

Pause/park eligible pre-handoff work without hammering the transport. Nonessential notifications may be terminally skipped only with an explicit safe policy and observable reason, never reported as sent. Bound retries, parked backlog and retention; preserve current authorized-recipient reevaluation, lease/attempt fences and `Unknown` no-automatic-resend reconciliation after uncertain SMTP handoff.

The original circuit-breaker/safe-no-op proposal expresses suppression of repeated known-unavailable transport work. Retain that failure-suppression requirement without fabricating sent results, replacing durable reconciliation, or mandating a duplicate resilience framework.

Preserve tenant BYO-SMTP: an unlocked tenant can use its own permitted transport when instance fallback is absent, without changing another tenant. Resolve credentials through the approved secrets authority, not governance rows or persisted provisioning requests. Define instance lock/override consequences and cache/replica convergence explicitly; current resolved configuration is cached for five minutes.

Health must distinguish healthy intentional zero-email mode from failure. Current operations guidance makes enabled SMTP launch-critical and documents `/health` HTTP 503 on failure. Final graceful-degradation design must deliberately reconcile this readiness/load-balancer behavior rather than assuming a UI alert keeps the instance reachable.

### IVSD-M005 - Provider-Owned Verification And Administrative Bootstrap

Retain Local email/password. Enabling SMTP makes verification mandatory for Local sign-in; an outage is not permission to waive it. Administrator-created Local accounts are directly verified through the explicitly authorized provisioning operation, with the administrator recorded as verification origin. Do not keep unverified development-account access for backward compatibility.

The described Local posture has no public attendee account creation. Enforce the resulting exclusion of authenticated-attendee-only event registration in configuration/publication and HAL as well as actual operations. Existing administrator accounts do not establish public signup capability. Evaluate effective available providers and policy rather than SMTP or the primary-provider name alone.

Only Local's application-owned auth-email flows depend on Event SMTP. Keycloak and ATProto/PDS own their verification, recovery and native onboarding. Event consumes trusted verification assertions when supplied, without inferring them from sign-in. Keycloak may be configured without email; ATProto JIT presently has no mailbox proof. Neither gets a new blanket email-presence prerequisite. Event-generated notifications remain a separate SMTP-dependent concern for users of all providers.

Preserve three bootstrap pathways from the consultancy:

1. **Headless:** deployment-injected Local bootstrap credentials where the authorized design permits, or configured external-provider identifiers such as an ATProto DID. Existing configured-provider selectors require actual provider authentication proof before privilege; a supplied DID is not itself authentication.
2. **First-run Local wizard:** explicitly authorized initial administrative establishment, directly verified under the user-approved policy, without an SMTP chicken-and-egg cycle. It is not an open public account-registration endpoint.
3. **Optional local Mailpit:** test verification without external mail infrastructure. Compose/local topology may expose SMTP on `1025` and inbox UI on `8025`; exposure must be bounded appropriately. The sidecar is not mandatory, a public relay or evidence that an attendee received email.

Secrets come from the selected environment/Infisical authority, or explicitly selected Development/Testing User Secrets, never inline source defaults. Reuse implemented headless bootstrap rather than redesigning external handshakes.

### IVSD-M006 - Minimal-Data Registration And Retention

In the selected anonymous registration flow, collect/store no email or telephone in `RegistrationOrderPii`. Collect a name only if entry management needs it; name-only is reduced PII, not zero PII. Preserve a truly no-PII experience where the event does not need names.

Purge or anonymize names after the event according to justified configured retention. Current retention is category-based and row-specific; the requested event-linked lifecycle needs an explicit policy rather than assuming it already exists. Preserve legal holds, necessary evidentiary records, authority-first erasure and anti-resurrection controls.

Do not associate anonymous attendees with tracking cookies or analytics fingerprints. Essential security/antiforgery controls are not permission to introduce behavioral tracking. Bound and minimize IP/subnet abuse data as well as contact fields. Keep answers, contact details and raw capabilities out of logs, traces, metrics, health, ProblemDetails and general public projections.

### IVSD-M007 - Ephemeral Handover, First-Use Rotation And Supervised Reset

For administrator-controlled Local provisioning, support request-scoped credential input or cryptographic generation, direct verified state and one-time display/copy for secure out-of-band handover. The consultancy suggested a strong 24-character passphrase; final entropy/policy must be justified rather than treating character count alone as a cryptographic guarantee. Never retain readable passwords for later redisplay, logs or support.

The original proposed `ScheduleManagedTenantProvisioning` variant and `MustChangePasswordOnFirstLogin=true` express the intended outcome, not a safe instruction to add a password to the current durable request DTO. That request is serialized: issue/handle credentials through a boundary that cannot persist their plaintext or replay it through generic idempotency response storage. The zero-email handover must not depend on `RecipientAddressSource.ManagedTenantAdministratorInvitation` delivery.

Force the recipient to establish a private password at first use before administrative access. Restrict actual API/BFF authority and sessions, not just a page redirect. Directly verified status does not bypass first-use rotation. One-time disclosure, expiry, concurrency/replay fencing and audit must be observable in tests.

Provide an audited instance-supervised reset for a locked-out Local tenant administrator, issuing a new temporary credential and reinstating mandatory replacement. Password change by an already authenticated user must not depend on SMTP. The completed plan selects instance-only credential authority; a future tenant-delegation proposal must not authorize takeover of a shared identity or another tenant.

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

- Administrator identifier, verification, provisioning authority and account-only event eligibility are explicit in the completed plan. External-provider credential administration is excluded.
- Managed-request serialization, recipient/dispatch ownership, identity topology, session caches, capability expiry and consumed allocation were traced; the plan owns their concrete changes and regression seams.
- No live host, no-email registration, mail transport, concurrency, token replay, provider migration or browser scenario was exercised.
- No empirical anti-hoarding, mobile accessibility, no-show, retention or operator-comprehension evidence exists in this investigation.
- The plan reuses existing Mailpit/Ical.Net/framework infrastructure and selects native proof-of-work without a new third-party dependency. Actual deployment pin/provenance checks and measured challenge tuning remain implementation/release evidence, not claimed completed research.
- Measure anonymous signup-to-attendance conversion and no-show rates when neither email confirmation nor an emailed calendar invite exists.
- Assess real public-deployment abuse velocity and whether IP/subnet limits plus the selected challenge preserve fair access.
- Test whether operators distinguish anonymous-only, directory-only and full participation, and intentional email opt-out from degraded configured delivery. The consultancy also used `AnonymousDirectoryOnly` and `FullRegistration` as informal labels; these are not extra implemented modes.
- Assess actual out-of-band credential-sharing habits, including insecure unencrypted handover channels, rather than assuming secure operator behavior.

## Escalation Needed

No unresolved authority branch blocks `plan-r1`. Instance-only Local provisioning is the conservative planning selection for user review; tenant delegation is explicitly outside this revision. Local keeps email/password and owns its application-SMTP-dependent authentication email; Keycloak and ATProto retain their own authority. No external credential administration or Local fallback identity is authorized.

Any later paid-event communication/legal-record scope belongs to the existing paid-event governance. No new religious-legal determination is needed for the present technical intake.

Technical rate limiting does not itself require a scholarly ruling. Organizer guidance may explain fair seat allocation (*'Adl fi al-Qismah*) without claiming one. If paid events are later combined with degraded communication, obtain jurisdiction-specific review of receipts, refund notices and statutory transaction records.

## Evidence Reviewed

- Original consultancy input: former `i-vsd-email-optional-self-hosting-consultancy-report.md`, SHA-256 `6213e3809f84ec4c6ec209be5bdca25a2beb30161e3568daa20833aa51e0c3e9`. Its standalone/current/ready-for-planning metadata, findings and lifecycle are incorporated here; the separate file is retired by the user's consolidation request.
- Planning input: this path before consolidation, SHA-256 `5f26939aeeb06fe931495e16e83bb9ee4c31ac627e81fe9721178f51d34c3303`. Its historical draft/changes-required state is preserved in Review Lifecycle; the completed design is now assessed separately below.
- User clarification on 2026-09-05: retain email/password; preserve multiple current and future authentication providers. This is an explicit scope decision, not a source-code observation.
- Further user clarification on 2026-09-05: only Local adapts authentication-email behavior to Event SMTP; Keycloak/ATProto retain verification responsibility and Event must respect trusted provider-reported verification status. Current ATProto JIT source supplies no mailbox proof.
- Superseding Local policy correction on 2026-09-05: SMTP-enabled sign-in requires verification; Local account creation is administrator-controlled and accounts pass as verified directly; unavailable visitor signup prohibits authenticated-attendee-only event registration in that posture. Existing unverified development accounts are not a compatibility constraint.
- Shared [repository evidence and research packet](../dev/active/email-optional-self-hosting/email-optional-self-hosting-context.md), including verified source locators, initial intent classification, related workstreams and official source register.
- Git object `1ca0edeac1da90d29a135c5efa3f8c6269e2574c` for product-source evidence. No unmerged ATProto implementation is attributed to this revision.
- [OWASP password recovery guidance](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html), read 2026-09-05; functional security requirements only, no imported source or copied implementation.

### Repository And Related-Report Evidence

| Source | Evidence retained and bounded interpretation |
| --- | --- |
| [SmtpConfigResolver](../src/Explore.Infrastructure/Mail/SmtpConfigResolver.cs) | Hierarchical non-secret settings, tenant overrides, null configuration and credential resolution/cache |
| [SmtpEmailService](../src/Explore.Infrastructure/Mail/SmtpEmailService.cs) | Explicit unconfigured-send failure and connection-test behavior |
| [InstanceSmtpSettingService](../src/Explore.Application/Services/InstanceSmtpSettingService.cs) | Host/port/sender/TLS settings; the original claim that this stores credentials is corrected: credentials come from the secrets authority |
| [PublicExperienceSettingDefinitions](../src/Explore.Domain/Settings/Definitions/PublicExperienceSettingDefinitions.cs) | DiscoveryCentric/OrganizationCentric presentation settings, not a visitor-auth policy |
| [RegistrationOrderPii](../src/Explore.Domain/RegistrationOrderPii.cs) | Separate nullable purchaser contact fields, verification and retention |
| [RegistrationOrder](../src/Explore.Domain/RegistrationOrder.cs) and [native submissions](../src/Explore.Application/Features/RegistrationSubmissions/NativeRegistrationSubmissionCommands.cs) | Existing order/lifecycle authority and published attempt lineage; original finding locators retained above |
| [LocalIdentityAuthService](../src/Explore.Persistence/Identity/LocalIdentityAuthService.cs) and [LocalIdentityUser](../src/Explore.Persistence/Identity/LocalIdentityUser.cs) | Email/password identity, current unverified token issuance and missing rotation/reset capabilities |
| [EmailDispatchEligibilityEvaluator](../src/Explore.Persistence/Services/EmailDispatchEligibilityEvaluator.cs) | Managed invitation destination rules and fenced rate/eligibility evaluation; no inference of a registration bot challenge |
| [EmailDispatchDrainService](../src/Explore.Infrastructure/EmailDispatchDrainService.cs) | Bounded retry, tenant context, parking and uncertain-handoff reconciliation |
| [RegistrationOrderAccessGuard](../src/Explore.Application/Features/RegistrationOrders/Handlers/RegistrationOrderAccessGuard.cs) | Tenant/event/order capability and expiry checks; not proof of long-lived status access |
| [UserController](../src/Explore.API/Controllers/UserController.cs) | Trusted principal-derived provider verification passed into synchronization |
| [ATProto JIT provisioning](../src/Explore.Application/Features/Authentication/Atproto/Services/AtprotoJitAccountProvisioningOperation.cs) | Verified DID-backed identity without email or mailbox verification |
| [Keycloak lifecycle delegation](../src/Explore.Infrastructure/Services/Keycloak/KeycloakAccountAuthorityLifecycleEmailService.cs) | Account-authority email API rather than Event SMTP transport |
| [Managed provisioning handlers](../src/Explore.Application/Features/Management/Handlers/ManagedTenantProvisioningHandlers.cs) | Persisted request snapshots and external identity/invitation alternatives; plaintext password addition is unsafe |
| [Authentication policy](../docs/internal/AUTHENTICATION.md) and [operations](../docs/internal/OPERATIONS.md) | Deferred Local credential workflows, existing readiness and bounded retention behavior |
| [Registration data collection I-VSD](i-vsd-registration-data-collection.md) | Original related analysis separating orders from participant identity |
| [Headless onboarding I-VSD](i-vsd-headless-instance-onboarding.md) | Original related analysis of authenticated first-administrator bootstrap |

The linked context contains the fuller source ledger, test locations and sanitized official research register. Legacy line locators are historical anchors, not fresh runtime measurements.

## Missing Evidence

No production incident/spam telemetry, stakeholder interviews, formal security audit, field abuse metrics, usability study, or implementation verification. Specifically absent are feedback from small masajid deploying on single-board computers/minimal VMs and mobile browser benchmarks for offline `.ics` generation. Future empirical observations must not be fabricated or replaced with architectural confidence.

## Context Inventory

The repository already has Clean Architecture, MediatR, EF Core, Local and external identity providers, BFF trust boundaries, scoped capability registration, participation policy, durable dispatch, hierarchical non-secret settings and a separate secrets authority. These are reusable foundations, not new features to invent.

The original inventory named `Explore.Application`, `Explore.Domain`, `Explore.Infrastructure`, `Explore.Persistence`, public/internal documentation, standalone SQLite, Compose and Aspire, and reported 35 I-VSD reports at its cutoff. That count is retained as historical context, not reasserted as a current inventory. Original user inputs required both graceful missing/failing-SMTP behavior with typed revocation confirmation and multi-tenant out-of-band administrative handover; subsequent user decisions refine, rather than erase, those goals.

Initial source guidance spans security/privacy and the sovereign registration intent even though payment-processing changes are excluded. Source-of-truth ownership and applicable verification obligations must remain explicit in final planning.

## Planning Handoff

- Workstream: email-optional-self-hosting
- Status: current / plan-aligned to proposed `plan-r1`
- Reviewed input revision: both pre-consolidation SHA-256 inputs in Review Metadata, product Git object `1ca0edeac1da90d29a135c5efa3f8c6269e2574c`, the dated user decisions in Scope, and the completed plan/task artifacts linked below.
- Plan: [email-optional-self-hosting-plan.md](../dev/active/email-optional-self-hosting/email-optional-self-hosting-plan.md).
- Tasks: [email-optional-self-hosting-tasks.md](../dev/active/email-optional-self-hosting/email-optional-self-hosting-tasks.md).
- Plan SHA-256 at design handoff: `8ba76db055c77389abc56cb3239e576e0d7f7b06450021fa175bfa2c46f646e6`.
- Tasks SHA-256 at design handoff: `80de3df82a29006e481427275b96a24c9367f210827ab43a8371b36cd3addfc4`.
- These hashes bind the reviewed design/ledger revision. Later checkbox-only progress does not constitute a behavioral rewrite; material design changes require I-VSD revalidation.
- Planning verification: 19 numbered plan sections, 26 behavioral scenarios, 36 implementation tasks, 12 phase gates, 12 matching commit packets, all seven I-VSD mappings, 16 syntax-valid Bash blocks and 25 existing local link targets. No product test or build was run for this documentation task.
- Review limitation: the separate plan-review child could not run because its provider usage quota was exhausted. Lead source/contract/structure review is recorded; no independent CTO or implementation-security approval is claimed.
- Findings and mitigations: IVSD-F001 -> IVSD-M001 through IVSD-F007 -> IVSD-M007.
- Implementation/user approval: not granted by this report. CTO review: Not reviewed.
- Escalations required before implementation: user approval of the complete plan; no further intake branch is hidden in its tasks.
- Refresh triggers: administrator identifier/provider scope; visitor defaults; communication guarantees; SMTP disable and tenant override behavior; capability lifecycle; abuse challenge; retention; credential recovery authority; any changed mapped mitigation.
- Plan-aligned: Yes, as a documentation/design assessment only. No finding is marked implemented or resolved.

### Scenario And Task Mapping

| Finding / mitigation | Scenarios in plan Section 3 | Tasks in the execution ledger | Disposition |
| --- | --- | --- | --- |
| IVSD-F001 / IVSD-M001 | S01-S03, S23-S24 | 1.1-1.3, 2.1-2.3, 12.1-12.3 | Implement explicit optional delivery, coherent scope and actual deployment |
| IVSD-F002 / IVSD-M002 | S04, S06, S18-S20, S25-S26 | 7.1-7.3, 9.1-9.3, 10.1-10.3 | Implement honest notice, durable private status, calendar and safe cancellation |
| IVSD-F003 / IVSD-M003 | S16-S17, S19, S22 | 8.1-8.3, 10.1-10.3 | Implement native challenge, stable allocation replay, approval/capacity and cancellation safety |
| IVSD-F004 / IVSD-M004 | S02-S06, S24 | 1.1-1.3, 2.1-2.3, 12.1-12.3 | Implement typed revocation, locks, bounded park/skip and Unknown protection |
| IVSD-F005 / IVSD-M005 | S04, S07-S09, S11-S15, S25-S26 | 3.1-3.3, 4.1-4.3, 5.1-5.3, 6.1-6.3, 7.1-7.3 | Implement Local administrator enrollment/verification and preserve external authority |
| IVSD-F006 / IVSD-M006 | S18, S21-S22 | 9.1-9.3, 11.1-11.3 | Implement minimum collection, read-time expiry and bounded cleanup |
| IVSD-F007 / IVSD-M007 | S09-S13, S24 | 4.1-4.3, 5.1-5.3, 6.1-6.3 | Implement instance-owned handover/reset, private replacement and cross-store convergence |

P12 graduates durable decisions and field-evaluation gaps. Tenant credential delegation is a separate scoped backlog proposal; the required instance-supervised recovery is fully included. Revalidation confirms every material finding/mitigation has a concrete scenario/task mapping and retains its source evidence and uncertainty.

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

## Consolidation Coverage And Corrections

| Original material | Canonical destination / treatment |
| --- | --- |
| Metadata and original two review transitions | Review Metadata, Evidence Reviewed and Review Lifecycle retain source identities and historical state; later plan-alignment is explicitly bound to the completed proposed design, not implementation |
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
