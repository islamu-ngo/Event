# ADR-029: Email-Optional Self-Hosting

> **Audience:** Contributors | Operators | AI agents
> **Status:** Mixed
> **Owner:** Security
> **Last Verified:** 2026-09-08
> **Source Anchors:** `src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.cs`, `src/Explore.Persistence/Services/EmailDispatchEligibilityEvaluator.cs`, `src/Explore.Application/Services/Registration/AnonymousCancellationService.cs`, `docker-compose.yml`

- **Decision:** Accepted architecture; operational acceptance remains incomplete.
- **Date:** 2026-09-08
- **Extends:** [ADR-027](ADR-027-first-class-authentication-provider-matrix.md), [ADR-008](ADR-008-email-dispatch-state-machine.md), and [ADR-023](ADR-023-admission-credential-check-in-transfer-recovery.md).
- **Numbering:** ADR-029 avoids the two unrelated ADR-028 files already present on upstream `develop`; neither is replaced by this decision.

## Context

A self-hosted directory must not require outbound mail to browse events, establish an administrator, or support eligible anonymous participation. Removing SMTP must not remove identity verification policy, tenant isolation, capacity accounting, or credential revocation. Email delivery, authentication authority, public onboarding, and registration are separate capabilities.

P01-P11 are implemented and committed. P12 adds deployment/setup projections and this knowledge packet. The evidence boundary below is part of the decision: writing an ADR does not close the native zero-email host or workstream exit gates.

## Decisions

### Global identity and tenant delivery have different authorities

`email.delivery_enabled` is persisted nonsecret intent, defaults to `false`, and supports permitted tenant delivery overrides. Effective capability distinguishes disabled, unconfigured, misconfigured, available, and degraded transport. A disabled transport does not connect. Required database, authorization, privacy, signing, and secret-authority failures remain failures; enabled SMTP failure degrades email rather than declaring otherwise healthy core services unavailable.

An unlocked tenant may enable its own complete transport while instance fallback is disabled. Tenant-selected transport must not inherit instance credentials. Existing setting locks and `governance.lock_tenant_smtp` remain authoritative across specialized, generic, batch, and reset writers.

Local credentials and provider bindings are instance-global. Only an instance administrator can create or supervise reset of Local accounts; tenant administrators manage membership and role grants, not the shared password or verification state. Managed tenant provisioning references an already-provisioned Local identity without persisting temporary credentials or creating an email invitation dependency.

Sources: [SMTP resolver](../../../src/Explore.Infrastructure/Mail/SmtpConfigResolver.cs), [capability resolver](../../../src/Explore.Infrastructure/Mail/EmailDeliveryCapabilityResolver.cs), and [Local administration](../../../src/Explore.API/Controllers/LocalIdentityAdministrationController.cs).

### Local verification remains mandatory when instance delivery is enabled

Local remains email/password, with no public Local signup. Administrative enrollment directly verifies the account with recorded administrator provenance; it is not evidence of mailbox possession. When **instance** delivery intent is enabled, ordinary unverified Local sign-in and session validation fail even if SMTP is down or the selected tenant has no transport. Toggling delivery does not verify accounts.

Keycloak and AT Protocol/PDS retain their own verification, recovery, and onboarding authority. Event SMTP does not take over provider-native security mail or impose a new email requirement on a provider that permits no email. Equal email text is not permission to merge or adopt an external identity.

Sources: [Local authentication and session validation](../../../src/Explore.Persistence/Identity/LocalIdentityAuthService.cs), [authentication providers](../AUTHENTICATION_PROVIDERS.md), and [authentication](../AUTHENTICATION.md).

### Two stores converge through restricted credential states

Colocated and external Identity topologies use the same explicit convergence protocol, not a distributed database transaction:

1. The selected ASP.NET Identity store commits an operation receipt, credential, and `ProvisioningPending` state.
2. Application reconciliation establishes the exact Local subject, Domain user, and personal actor binding with stable identities in its own transaction.
3. After that binding commits, Identity activates `ChangeRequired`.
4. A temporary-password proof grants only a purpose/audience-isolated replacement challenge, valid for at most five minutes. Private replacement atomically changes password, state, stamps, and receipt to `Ready`; ordinary login must follow separately.

Pending or ambiguous work cannot authenticate. Reconciliation uses the original operation ID and never rotates a password as a retry side effect. Temporary plaintext is disclosed only to the successful issuance winner, with private/no-store responses and no generic replay storage. A lost response is recovered as nonsecret operation status, not by retrieving the old password; a fresh supervised reset is a distinct audited operation.

Reset fences the expected predecessor and current credential version. API bearer validation checks current Local readiness, stamp, and verification policy; BFF cookie/circuit validation cannot substitute cached administrator enrichment or a redirect for that authority. This narrows ADR-027's session-continuity statement: provider switching is not permission to retain a reset or otherwise invalid Local credential session. External schemes do not enter the Local credential gate.

Sources: [Identity state store](../../../src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.cs), [reconciliation handler](../../../src/Explore.Application/Features/Authentication/Local/Handlers/Commands/ReconcileLocalCredentialOperationCommandHandler.cs), [API authentication](../../../src/Explore.API/Extensions/AuthenticationExtensions.cs), and [shared cookie validation](../../../src/Event.Web.BffHosting/Authentication/EventBffTokenRefreshCookieEvents.cs).

Local verification, email change, and password recovery use separate expiring, one-use authority. Verification/email change lasts 30 minutes and recovery 15 minutes. Public recovery is nonenumerating; delivery does not create a full session. Authenticated password change requires current credentials, not SMTP. Durable notification work carries protected intent/operation references, not reusable plaintext credentials or recovery tokens. See [authentication](../AUTHENTICATION.md) for the implemented lifecycle surface.

### Disable and SMTP handoff share durable fences

Disable requires current scope authority, a fresh impact preview/revision, and the exact acknowledgement `DISABLE EMAIL DELIVERY`. The mutation lease precedes the serializable setting transaction; authority is rechecked inside it. Configuration, notification graph/suppression, and final send admission coordinate through the existing ordered mutation/control locks, with global-before-tenant ordering where both scopes are needed.

Final admission reads authoritative policy and commits the provider-handoff fence before transport I/O. A handoff already admitted before disable may finish: SMTP is not part of the database transaction. A cache invalidation message is not revocation proof.

The existing durable drain owns bounded retry. Only proven pre-acceptance transient failures are automatically retryable. Configuration failures park work; ambiguous acceptance becomes `Unknown` and is not automatically resent. Optional notifications suppressed during disable stay skipped, including later graph repair. Re-enabling resumes only eligible capability-parked work within its validity bound, not operator parks, expired required work, suppressed history, or uncertain sends. There is no exactly-once SMTP claim.

Sources: [disable handler](../../../src/Explore.Application/Features/EmailDispatch/Handlers/Commands/DisableEmailDeliveryCommandHandler.cs), [handoff eligibility](../../../src/Explore.Persistence/Services/EmailDispatchEligibilityEvaluator.cs), and [durable drain](../../../src/Explore.Infrastructure/EmailDispatchDrainService.cs).

### Anonymous recovery reuses the committed order

Anonymous intake uses existing guest orders, relational capacity authority, rate/concurrency limits, and durable tenant/event issuance quotas. It does not introduce another RSVP aggregate or CAPTCHA service. ASP.NET Data Protection binds the tenant, event, operation, canonical request digest, idempotency key, stable server-generated order ID, and random capability material. Browser Web Crypto performs bounded SHA-256 work; the default is 18 bits within 16-22, not a measured mobile-performance guarantee.

Fresh allocation authority lasts 120 seconds. Historical proof validation until original expiry plus 24 hours permits only exact committed-order recovery, not a late new allocation. Validation precedes idempotency response disclosure. A response-store failure after business commit must return the same order, holds, and capability without allocating or scheduling again; changed scope, body, key, or envelope cannot disclose another result.

Public signup capability comes from actual enabled provider policy, not SMTP reachability. Local-only administration offers no public signup. Visitor/publication policy rejects account-required configurations without eligible onboarding; directory-only stops new allocation without orphaning lawful existing status/cancellation access.

Sources: [challenge service](../../../src/Explore.Infrastructure/Services/Registration/AnonymousRegistrationChallengeService.cs), [registration implementation](../../../src/Explore.Application/Features/RegistrationOrders/), and [participation guide](../../public/documentation/readme/events-and-ticketing/email-optional-participation.md).

### Status, cancellation, and PII have separate purposes and deadlines

Checkout hold expiry is not post-confirmation status authority. A separate limited guard accepts the scoped guest capability for PII-free status and eligible cancellation, not checkout, payment, form edits, or admission scanning. Confirmation persists an event-end-plus-30-day access promise. A live promise can extend for a later event end, but an expired or missing historical promise cannot be renewed by reading status.

The private bookmark uses a URL fragment that browser interop removes; requests use the established capability header with private/no-store and no-referrer protections. Calendar export remains public event data, never attendee data or the private capability.

Confirmed remains terminal to generic cancellation. The new operation is limited to pinned free anonymous individual orders with no paid acceptance/payment history or attendance. One serializable transaction checks the exact lineage, cancels the order, revokes admission/credentials, and releases consumed holds once. Duplicate cancellation is idempotent; check-in and cancellation serialize. Paid/refund/staff correction authority is unchanged, and GET never performs cancellation.

Anonymous PII has a separate original, non-increasing order deadline: event end plus seven days by default, configurable 0-30 days. Existing row deadlines and stored-export content deadlines constrain disclosure too. Missing original authority fails closed. Editing, claim, rescheduling, or deleting/recreating PII rows cannot acquire a fresh window. This revision does not automatically shorten the original deadline on cancellation or earlier rescheduling.

Read/export/file and delayed-delivery boundaries enforce expiry before eventual Quartz deletion. Protected staged contacts retain their purpose deadline; queued dispatch rechecks after waits and at actual SMTP handoff. Legal holds may preserve physical evidence, not renew operational disclosure. PII-free status may therefore remain available after names and answers are unreadable.

Sources: [status guard](../../../src/Explore.Application/Features/RegistrationOrders/Handlers/GuestRegistrationStatusAccessGuard.cs), [cancellation rules](../../../src/Explore.Domain/Services/Registration/AnonymousCancellationRules.cs), [cancellation transaction](../../../src/Explore.Application/Services/Registration/AnonymousCancellationService.cs), and [retention policy](../../../src/Explore.Domain/Services/Registration/AnonymousRegistrationRetentionPolicy.cs).

### Public support contact is not SMTP configuration

`branding.support_email` stores public support contact independently of SMTP sender/transport and delivery intent. Onboarding writes this branding key; the existing branding DTO exposes nullable `SupportEmail` (`supportEmail` on the wire). No new endpoint, environment override, parallel profile store, or migration is introduced.

The six server owners are:

- [GovernanceSettingKeys](../../../src/Explore.Domain/Constants/GovernanceSettingKeys.cs)
- [BrandingSettingDefinitions](../../../src/Explore.Domain/Settings/Definitions/BrandingSettingDefinitions.cs)
- [BrandingSettingGroup](../../../src/Explore.Application/Settings/Groups/BrandingSettingGroup.cs)
- [BrandingSettingsDto](../../../src/Explore.Application/DTOs/Instance/BrandingSettingsDto.cs)
- [InstanceGovernanceSettingService](../../../src/Explore.Application/Services/InstanceGovernanceSettingService.cs)
- [InstanceOnboardingProfileSettingHelpers](../../../src/Explore.Application/Features/InstanceOnboarding/Common/InstanceOnboardingProfileSettingHelpers.cs)

The API schema build passes. This is not, by itself, proof that every rendered consumer or the native host has passed.

### Mail capture remains an optional operator tool

The curated environment baseline does not enable SMTP. Persisted delivery intent remains authoritative; a restart or setup projection must not restore delivery over an operator's disable. The default Compose RabbitMQ dispatch projection is off because its base topology has no broker.

[Compose](../../../docker-compose.yml) puts Mailpit behind the optional `mail` profile, without a mandatory API dependency or published host SMTP port. Inbox publication is loopback-only, capture has a 500-message limit and a persistent volume, and update checks are disabled. Starting the profile does not enable delivery or configure relay/forwarding.

The immutable operator-pulled reference is `axllent/mailpit:v1.30.0@sha256:0059ef81e492a7192af3816281eed6859eb078bd7bdc58b76757c13e10e53a7d`. Source-free image evidence dated 2026-09-08 records verified registry-index metadata and runtime manifests for linux/amd64, linux/arm64, and linux/386. The tag license is reported as MIT; third-party material retains its own terms. This is not assembled-image, SBOM, vulnerability, attestation, or redistribution certification. Mirroring, bundling, preloading, and air-gapped conveyance require separate complete evidence and approval under [IP governance](../legal/IP_GOVERNANCE.md).

This packet used only repository source and the sanitized `mailpit-provenance.md` handoff, not external implementation source or copied external prose. Its decomposition follows Event's existing authority, outbox, registration, and retention owners. Mailpit capture is not external attendee delivery, and no image execution is established by this evidence.

## Evidence and remaining acceptance

At this graduation checkpoint:

- P01-P11 implementation/commit evidence is recorded in the task-owned context; C11 is `6b6da88047675fbd1bdddc2e51059272314a6192`. Its corrected parent native retention gate records 90/90 passing checks, not an all-provider proof.
- Read parent P12 logs report `EmailOptionalSetupTests` 22/22, `EnvironmentCatalogueInvariantTests` 20/20, and `DotenvContractTests` 19/19, with zero failures/skips. Parent handoff also records native Compose parsing. These establish bounded setup/configuration evidence, not running-service or restart acceptance.
- The API schema build succeeds with 0 errors and 300 warnings in the recorded build; it is not a warning-free solution gate.
- Parent native Standalone verification now passes 56/56, including directory/admin/health HTTP, three-host credential/bootstrap/key/disable continuity, and support-contact update/clear/restart without SMTP mutation. SQLite temporal regression coverage passes 7/7; generated-client UI readback and adjacent Local UI checks pass 1/1 and 5/5.
- The native fixture explicitly disables optional workers/webhooks and substitutes only external SMTP diagnostics. It does not certify default-topology health, published-image runtime or comprehensive backup/restore. The existing InMemory fixture remains unsuitable as persistence evidence.
- The complete Release build exits 0 with 1667 warnings and no errors. Parent evidence is recorded under `$HOME/.cache/agent-tmp/p12-parent/`, including `standalone-final.log`; final generated-secret Split fixture verification passes 5/5.
- **Still open:** inherited full-suite failures and explicit quarantine dispositions, the prescribed broader P05 exit filter, Ring 3 five-provider/migration-roundtrip/architecture checks, integrated browser/Combined/keyboard/assistive-technology acceptance, latency targets, and final security/database/operations and I-VSD acceptance. This packet closes none by inference.
- No field study establishes no-show reduction, abuse resistance in practice, false-rejection rates, mobile work cost, or operator comprehension.

## Consequences and excluded alternatives

This design accepts operation-ID reconciliation and honest `Pending`/`Unknown` outcomes rather than inventing cross-database atomicity or exactly-once email. Operators need durable database/key backups, private credential handover, and explicit handling of uncertain sends. Attendees must save their private access link; SMTP absence does not create identity-based recovery for a lost capability.

Rejected alternatives are SMTP-derived global authentication mode, username-only migration, public Local signup, tenant control of shared credentials, email-based identity adoption, perpetual environment precedence over persisted disable, blanket confirmed cancellation, and status access that implicitly extends PII retention.

Only separate future authority/research work is graduated to backlog; no S01-S26 behavior is silently deferred:

- [Tenant-delegated Local account provisioning](../../../dev/backlog/tenant-delegated-local-account-provisioning.md)
- [Anonymous registration field evaluation](../../../dev/backlog/anonymous-registration-field-evaluation.md)

## Related guidance

- [Durable implementation pitfalls](../../../dev/_journal/domains/email-optional-self-hosting.md)
- [Internal self-hosting](../SELF_HOSTING.md)
- [Public Standalone guide](../../public/documentation/readme/self-hosting/docker-standalone.md)
- [Public Compose guide](../../public/documentation/readme/self-hosting/docker-compose.md)
- [Single I-VSD report](../../../islamic-value-sensitive-design/i-vsd-email-optional-self-hosting.md)
