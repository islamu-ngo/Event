<!-- ABOUTME: Graduates durable email-optional self-hosting pitfalls from the P01-P12 implementation ledger. -->
<!-- ABOUTME: Distinguishes historical root causes, implemented repairs, and evidence that remains pending. -->

# Email-Optional Self-Hosting Knowledge Ledger

The architectural decisions live in [ADR-029](../../../docs/internal/adr/ADR-029-email-optional-self-hosting.md). These findings explain why the owning boundaries matter; they are not a replacement execution ledger or a claim of full workstream acceptance.

Navigation: [journal index](../journal.md), [domain index](../README.md), [finding template](../FINDING_TEMPLATE.md).

## [2026-09-08 Europe/Brussels] - Logged invalidation is not send revocation

**Context**: P01/P02 separated delivery intent from transport configuration and added deliberate disable against concurrent dispatch.

**Symptom / Observation**: The pre-implementation SMTP resolver's global invalidation path only logged. Its five-minute configuration/credential cache could not prove that another replica or an already-waiting sender observed disable.

**Root Cause**: A local cache operation was being treated as global authorization. Neither a log nor a cached transport snapshot serializes with the final send decision.

**Resolution**: The current `SmtpConfigResolver` delegates to the capability resolver, invalidates hierarchical settings, and does not cache plaintext transport credentials. More importantly, disable and final handoff share the persisted policy/control and ordered mutation fences. `EmailDispatchEligibilityEvaluator` records handoff before transport I/O. An admitted send may finish after disable; unknown acceptance is fenced against automatic resend. This is implemented in C02 `fe0bbfeb74293b6982e296b83c7fa499c09fdf79`, not an exactly-once SMTP guarantee.

**Why This Matters for Future Work**: Cache freshness is an optimization; revocation requires durable admission authority. Repair/materialization must preserve the original suppression history rather than manufacture a new occurrence after re-enable.

**References**:

- [SmtpConfigResolver](../../../src/Explore.Infrastructure/Mail/SmtpConfigResolver.cs), `InvalidateCache`, line 21.
- [DisableEmailDeliveryCommandHandler](../../../src/Explore.Application/Features/EmailDispatch/Handlers/Commands/DisableEmailDeliveryCommandHandler.cs), lease before serializable transaction, line 37.
- [EmailDispatchEligibilityEvaluator](../../../src/Explore.Persistence/Services/EmailDispatchEligibilityEvaluator.cs), `EvaluateAndBeginProviderHandoffAsync`, line 38.
- [EmailDispatchDrainService](../../../src/Explore.Infrastructure/EmailDispatchDrainService.cs), handoff/Unknown reconciliation.

**Promotion Consideration**: Promoted to ADR-029's disable/handoff decision; retain this historical root cause in the journal.

## [2026-09-08 Europe/Brussels] - Hold expiry is not durable status authority

**Context**: P09 made saved guest status useful after confirmation and checkout expiry without expanding guest mutation authority.

**Symptom / Observation**: Confirmation did not clear the checkout expiry, and the general access guard rejected later private status. A scope-only browser capability store also did not survive a lost page/session as a durable bookmark.

**Root Cause**: Checkout work and post-confirmation access shared one expiry assumption. Removing that expiry from the general guard would have extended forms, checkout, and other capabilities as well as status.

**Resolution**: A separate status guard checks the exact tenant/event/order/hash and persisted event-end-plus-30-day promise. Only a still-live promise can extend for a later event end; historical missing or expired promises cannot be renewed. Status remains PII-free. Explicit fragment-based save/copy preserves the capability without placing it in a server query string; calendar output stays public-purpose. C09 is `a3b4121d7c7b58a99018072b4c0c1754ded521a0`.

**Why This Matters for Future Work**: A token may identify the same order while granting different purposes with different deadlines. Extend only the intended purpose, and recheck authority after blocking waits.

**References**:

- [GuestRegistrationStatusAccessGuard](../../../src/Explore.Application/Features/RegistrationOrders/Handlers/GuestRegistrationStatusAccessGuard.cs), `GetAsync`, line 19.
- [Guest status page](../../../src/Explore.Blazor.Client/Pages/Registration/GuestRegistrationStatus.razor).
- [Public participation guidance](../../../docs/public/documentation/readme/events-and-ticketing/email-optional-participation.md).

**Promotion Consideration**: Promoted to ADR-029's status/cancellation/retention decision; do not generalize the limited guard into ordinary checkout access.

## [2026-09-08 Europe/Brussels] - Confirmed cancellation must release consumed holds

**Context**: P10 introduced cancellation for free, unattended anonymous registrations while preserving paid and staff authority.

**Symptom / Observation**: Generic cancellation treated Confirmed as terminal. Even if the order status were changed, confirmed orders had already consumed capacity holds; changing status alone could neither free seats nor revoke issued admission.

**Root Cause**: Order lifecycle, capacity consumption, ticket issuance, and attendance are separate persisted facts. A broad Confirmed-to-Cancelled transition does not establish that all entitlement effects happened once.

**Resolution**: `AnonymousCancellationRules` requires pinned free pricing and absence of payment/attendance history, not merely a current zero total. `AnonymousCancellationService` owns one serializable transaction over current limited capability, exact order/ticket/hold lineage, revocation, and consumed-hold release. Duplicate completed cancellation returns the committed outcome without releasing twice. Check-in and cancellation share serialization authority. C10 is `fd43b57f5f2e6ea8b49d1389e8f3639adc1bca90`; the saved focused evidence records Domain 9/9 and native SQLite 32/32, not multiprocess/five-provider acceptance.

**Why This Matters for Future Work**: A cancellation result must account for every consumed entitlement. Do not weaken generic lifecycle or treat a mocked revocation call as proof of the integrated transaction.

**References**:

- [AnonymousCancellationRules](../../../src/Explore.Domain/Services/Registration/AnonymousCancellationRules.cs), `IsFreeAnonymous`, line 21.
- [AnonymousCancellationService](../../../src/Explore.Application/Services/Registration/AnonymousCancellationService.cs), `ExecuteAsync`, line 27.
- [AnonymousCancellationRepository](../../../src/Explore.Persistence/Repositories/AnonymousCancellationRepository.cs).

**Promotion Consideration**: Promoted to ADR-029; retain the narrower free-only authority instead of broadening the generic transition.

## [2026-09-08 Europe/Brussels] - BFF redirects cannot revoke global credentials

**Context**: P04 implemented temporary credentials, supervised reset, and current Local session enforcement across colocated and external Identity stores.

**Symptom / Observation**: A password-change redirect could hide an admin page while an old API bearer or cached BFF/circuit principal still represented usable authority. Re-reading credential state after password validation could also mistake a concurrently replaced credential's `Ready` state for proof of the old password.

**Root Cause**: UI routing and previously issued claims do not prove current global credential state. Refreshing state without retaining the version checked by the original password proof changes the meaning of that proof.

**Resolution**: Capture the security stamp immediately after password validation and require that same version in fresh state reads. `ProvisioningPending` cannot authenticate; `ChangeRequired` gets restricted replacement authority only; ordinary API sessions require `Ready`, a current stamp, and instance verification policy. Cookie acceptance revalidates current authority before cached enrichment/refresh, and rejected Local authority clears browser-session state. Exact cross-store binding must commit before activation. C04 is `ca77595261255f7fd655147f5f3072683edea35d`.

**Why This Matters for Future Work**: Enforce credential state at every actual authentication boundary. Tenant context, an administrator-looking claim, and a frontend redirect cannot grant or revoke an instance-global credential on their own.

**References**:

- [LocalIdentityAuthService](../../../src/Explore.Persistence/Identity/LocalIdentityAuthService.cs), checked stamp at line 80 and current session read at line 156.
- [LocalIdentityCredentialStateStore](../../../src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.cs), `ReadReadySessionAsync`, line 945.
- [API authentication](../../../src/Explore.API/Extensions/AuthenticationExtensions.cs), current Local validation near line 237.
- [Shared BFF cookie validation](../../../src/Event.Web.BffHosting/Authentication/EventBffTokenRefreshCookieEvents.cs), `ValidatePrincipal`, line 96.

**Promotion Consideration**: Promoted to ADR-029's Local convergence/session decision. Tenant delegation remains an [unimplemented authority proposal](../../backlog/tenant-delegated-local-account-provisioning.md).

## [2026-09-08 Europe/Brussels] - Replay recovery needs business identity, not a new attempt

**Context**: P08 exercised allocation commit followed by failure to persist the HTTP idempotency response.

**Symptom / Observation**: Response storage could remain in-progress after the order and holds had committed. Retrying as a new allocation could consume another seat; checking proof only in the controller allowed middleware replay to disclose a result before validation.

**Root Cause**: HTTP response replay and the business transaction are not one atomic store. An idempotency key alone also does not bind a cached capability to the same protected challenge envelope.

**Resolution**: A Data Protection envelope binds canonical request/key/scope to stable server-issued order and capability material. Validation precedes cached disclosure. Recovery looks up exact committed allocation and preserves its IDs, deadline, and capability. Fresh allocation expires after 120 seconds; the additional 24-hour historical-validation window permits recovery only. C08 is `da88aa6113807ece4c78ba0d134a11b05d3a82d9`; independent-host/shared-store tests recorded response-update faults and live-owner barriers, not a multiprocess deployment proof.

**Why This Matters for Future Work**: Reconcile from durable business identity after an acknowledgement loss; never infer that a missing response means no business commit occurred.

**References**:

- [AnonymousRegistrationChallengeService](../../../src/Explore.Infrastructure/Services/Registration/AnonymousRegistrationChallengeService.cs), `Issue` and `Validate`, lines 24 and 37.
- [Registration-order implementation](../../../src/Explore.Application/Features/RegistrationOrders/).

**Promotion Consideration**: Promoted to ADR-029's stable-order recovery decision. Default work difficulty is not a substitute for the [pending field evaluation](../../backlog/anonymous-registration-field-evaluation.md).

## [2026-09-08 Europe/Brussels] - Deletion and queue staging do not end disclosure authority

**Context**: P11 traced anonymous names/answers through read APIs, files, stored CSV, and delayed notifications.

**Symptom / Observation**: Deleting a PII row removed its original deadline while later edits could recreate it. Stored exports and staged encrypted contacts could outlive source-answer deletion. Initial closure also missed sibling file-name responses and queued delivery after expiry.

**Root Cause**: Data copies had lost their original purpose bound, and checks before an asynchronous wait were being treated as authority at later disclosure.

**Resolution**: Persist the non-increasing anonymous deadline on the order and the included-content deadline on stored exports. Fail closed when the original bound is missing. Read/export/file projections and protected queued contacts retain/check their deadline; actual SMTP checks follow configuration/connect/authentication waits. Legal hold preserves storage, not disclosure. The corrected C11 gate records 90/90 native retention checks after genuine HTTP/queued-delivery/loopback-SMTP Red/Green. Commit: `6b6da88047675fbd1bdddc2e51059272314a6192`.

**Why This Matters for Future Work**: Erasure of the source does not erase already-copied content. Propagate the purpose deadline with the data and recheck at the last actual disclosure boundary; never extend PII from a longer status promise.

**References**:

- [AnonymousRegistrationRetentionPolicy](../../../src/Explore.Domain/Services/Registration/AnonymousRegistrationRetentionPolicy.cs), `CanDisclose`, line 32.
- [Retention setting definitions](../../../src/Explore.Domain/Settings/Definitions/AnonymousRegistrationRetentionSettingDefinitions.cs).
- [ADR-029](../../../docs/internal/adr/ADR-029-email-optional-self-hosting.md).

**Promotion Consideration**: Promoted to ADR-029's retention decision; no new legal-hold or evidence-retention category is implied.

## [2026-09-08 Europe/Brussels] - Provider composition is part of the test boundary

**Context**: P01's real SQLite settings tests exercised transaction-owned mutation leases; P12 requires native host durability evidence.

**Symptom / Observation**: Calling `UseSqlite` alone omitted the application's `SqliteNamedLockTransactionInterceptor`, so mutation leases were not released on commit. Separately, an InMemory Standalone host cannot establish restart persistence merely by passing startup checks.

**Root Cause**: A fixture can use the same database brand while omitting the production transaction lifecycle, or replace the persistence behavior it claims to prove.

**Resolution**: Use the actual `PrimaryDatabaseProviderComposition.ConfigureApplication` seam for application settings tests. Keep P12 setup parsing separate from native durable host proof. Read parent setup logs pass 22/22, 20/20, and 19/19; native Compose parsing and the successful API schema build do not close the still-pending zero-email host/restart proof.

**Why This Matters for Future Work**: Preserve the composition mechanism being asserted. A fixture setup failure is not behavioral Red, and generated migrations or an in-memory restart are not real storage evidence.

**References**:

- [Database composition](../../../src/Explore.Persistence/Database/).
- [EmailOptionalSetupTests](../../../tests/Event.Setup.Core.Tests/EmailOptionalSetupTests.cs).
- [Internal self-hosting](../../../docs/internal/SELF_HOSTING.md).

**Promotion Consideration**: Stays in journal as a reusable fixture/composition lesson; no new runtime or test changes are made by this packet.
