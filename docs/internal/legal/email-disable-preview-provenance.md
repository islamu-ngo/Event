<!-- ABOUTME: Source-free functional handoff and provenance record for guarded SMTP disable previews. -->
<!-- ABOUTME: Records the research boundary, independent security requirements, and pending implementation review. -->

# Guarded Email Disable Preview: Clean-Room Handoff

Date: 2026-09-06, Europe/Brussels.
Workstream: `email-optional-self-hosting`, P02; approved plan sections 5.2 and 5.12, requirement S05.
Intent: apply `ip-clean-room-governance` to a task-owned documentation artifact. Only this file is owned by this review; no product, dependency, skill, rule, or generated-contract changes are authorized by this handoff task.

## Source Register And Access Boundary

- Microsoft Learn — Limited-lifetime payloads: https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/limited-lifetime-payloads?view=aspnetcore-10.0
- Microsoft Learn — Purpose strings: https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/purpose-strings?view=aspnetcore-10.0

These public-documentation references were supplied by the coordinating context on the date above. This reviewer did not open either page or receive its examples. No independent external access date is claimed. The only supplied interface facts used here are that time-limited protected payloads reject invalid or expired data, and distinct protection purposes isolate consumers. Source identity is retained; source expression is excluded.

Repository evidence inspected for this handoff:

- Approved email-optional self-hosting implementation plan, sections 5.2 and 5.12:
  deliberate disable confirmation, stale-preview conflicts and the four HTTP
  operations. The durable runtime contract is documented in
  [Operations](../OPERATIONS.md#delivery-policy-revocation).
- [EmailUnsubscribeTokenService](../../../src/Explore.Infrastructure/Mail/Unsubscribe/EmailUnsubscribeTokenService.cs): existing project use of native purpose-specific, expiring protected data and bounded validation failures.
- [RelationalSettingMutationLock](../../../src/Explore.Persistence/RelationalSettingMutationLock.cs): shared policy ownership is acquired before caller-owned transactions; nested SMTP mutations must already own their keys.
- [EmailDeliveryPolicyReader](../../../src/Explore.Persistence/Services/EmailDeliveryPolicyReader.cs): authoritative non-secret policy uses database reads and the shared settings merge, including exact tenant qualification.
- [EmailDeliveryPolicySnapshot](../../../src/Explore.Application/Models/EmailDeliveryPolicySnapshot.cs): effective intent and transport ownership are existing project concepts.

## Incident And Separation Record

The coordinating context reported that a documentation query unexpectedly returned a source-bearing example despite requesting interface facts. It reported stopping product implementation immediately, providing none of that material to implementation workers, and producing no product output after exposure. It also reported that the current impact contract was created before the incident. Those chronology and exposure statements are coordinator attestations, not independently reconstructed tool-history evidence.

This reviewer has not received or inspected the example. This file was independently written from the approved plan, repository-owned material, and the two supplied interface facts. It contains no reproduction, paraphrase, attachment, or hash of the example. No product output was created by this reviewer, and no other worktree changes were discarded or modified.

The coordinator reports starting separate token and command workers with no inherited conversation (`fork_turns=none`) and without the source-bearing response. They must use this handoff and repository-owned context, record their own source-exclusion attestations, and independently choose their implementation. The exposed context must not produce further feature implementation or source-derived design instructions. If later evidence reveals post-exposure output or wider exposure, stop and apply the contamination response to that affected output; do not treat this record as clearance for uncertain material.

## Functional Specification

### Actors, Authority, And Operations

An instance administrator can preview and deliberately disable instance delivery intent. A tenant administrator can do the same for the current tenant only when current governance and individual setting locks permit it. Preview authority never grants commit authority: resolve the authenticated application actor and recheck authorization for each operation. Do not accept an actor identifier supplied as authority by the request body.

Use the approved operation contracts:

| Operation | Route | Observable result |
| --- | --- | --- |
| `PreviewInstanceSmtpDisable` | POST `/api/instance/settings/smtp/disable-preview` | Safe current instance revision and actual impact, plus a bounded expiring token. |
| `DisableInstanceSmtp` | POST `/api/instance/settings/smtp/disable` | Deliberate current-authority commit, or a bounded failure without policy mutation. |
| `PreviewTenantSmtpDisable` | POST `/api/settings/email-delivery/disable-preview` | The equivalent current-tenant preview; locked delegation is denied. |
| `DisableTenantSmtp` | POST `/api/settings/email-delivery/disable` | The equivalent guarded tenant commit, with no generic-settings bypass. |

Sensitive responses are private and not cacheable. Use named routes, the existing ProblemDetails failure path, current authentication/authorization, and applicable BFF antiforgery protection. UI actions are offered through server-authored `disable-preview` and `disable` HAL relations; the client does not infer authority from local roles or boolean capability fields.

### Preview

Read current persisted policy, revisions, and the scopes affected by the proposed disable under a coherent policy view. A preview must neither change database state nor reserve a send, resolve SMTP credentials, contact SMTP, or mutate external secrets. Cancellation closes the confirmation UI without changing policy.

Compute impact from the effective policy before and after the proposed change using the existing settings semantics. Include the selected instance or tenant target and the actual affected delivery scopes. Do not label every tenant as affected merely because the request is instance-scoped: independently enabled tenant-owned transport may remain effective. Do not leak unrelated tenant metadata to a tenant administrator. Safe scope identifiers and counts must agree with the impact bound to the token.

The preview is information, not a reservation or authorization grant. Disclose that a send already admitted before disable commits may finish. The response must not contain SMTP hostnames, credentials, secret-binding locations, message contents, recipients, or other contact data.

### Protected Impact And Commit

The token must be opaque, integrity-protected, purpose-isolated, and short-lived. An independently selected five-minute maximum lifetime is appropriate for this confirmation interaction. Reject absent, malformed, oversized, tampered, expired, and wrong-purpose inputs through bounded validation outcomes. Apply an explicit input length limit before decoding or unprotecting; do not allocate from untrusted declared lengths.

Bind the token to the action, target scope and tenant where applicable, authenticated actor, expected policy revision, and a deterministic representation of the actual impact. The same token cannot confirm another actor's request, another tenant, a different scope, or a changed impact. Token size must remain bounded as tenant count grows: a compact protected impact fingerprint can bind the complete scope/revision set without placing an unbounded tenant list inside the token. Use existing framework/library facilities; no custom cryptography or token persistence is needed.

Commit requires the exact ordinal acknowledgment `DISABLE EMAIL DELIVERY`. Case changes, leading/trailing whitespace, missing acknowledgment, and alternate text do not satisfy it. The client supplies the preview token and expected revision; neither value replaces current server-side authorization or governance checks.

Under the existing policy lock, acquired before the mutation transaction, recompute authoritative revision and impact and compare them with the protected preview. A stale revision or changed affected-scope evidence returns HTTP 409 and leaves policy unchanged. A tenant created or reconfigured between preview and commit must not silently expand or alter the confirmed impact. Recheck current locks before changing tenant policy. Missing authority or newly locked delegation must deny the action even if the token is otherwise valid.

Only a fully valid commit sets the target delivery intent to disabled through the established mutation path, advancing the durable revision/suppression state atomically with that policy change. Preserve operator pause/rate state and SMTP configuration; credentials remain deployment-owned. Invalidate relevant non-secret caches only after successful commit. Concurrent confirms must not duplicate policy effects: after a state-changing winner, the old preview is stale. A later replay must not disable delivery again after a subsequent re-enable.

Single-setting, batch, and reset operations must not bypass the same deliberate-disable rule. In particular, removing a tenant override can disable effective delivery by restoring an inherited false value. Preserve lawful configuration editing and apply current governance checks through the existing common mutation boundary.

### Privacy And Failure Semantics

Never log or persist raw preview tokens, protected payloads, acknowledgments combined with sensitive request content, credentials, or contact data. Diagnostic events use bounded reason codes; generic request logging and replay storage must not capture token bodies. An invalid token must not trigger secret-provider access or SMTP work. A failed transaction must not leave a policy update without the matching revision and suppression update.

This handoff does not redesign dispatch, introduce a queue or policy cascade, change authentication-provider authority, or implement automatic recovery. The existing rule remains that uncertain SMTP acceptance cannot be automatically resent.

## Independent Design And AFC/SSO Assessment

Abstraction: the requested behavior is a read-only impact preview followed by a separately authorized state transition. Layer ownership follows existing ISLAMU patterns: Application carries typed contracts and command behavior, Persistence owns transactional authoritative policy, Infrastructure supplies native payload protection, and API/HAL/Blazor expose the interaction.

Filtration: opaque integrity protection, expiry, purpose isolation, actor/tenant binding, exact acknowledgment, and authoritative conflict checks follow security or explicit approved-plan requirements. Public framework identifiers are necessary interface references. No external module decomposition, schema, algorithm sequence, test arrangement, UI layout, comments, or prose was supplied to this reviewer.

Independent decision: bind the impact to current revision and a compact representation of affected scopes while keeping preview read-only. Reuse the existing relational policy lock and revision/suppression transaction rather than adding a preview database record or independent coordination mechanism. The five-minute maximum is a project interaction decision, not an extracted external example value.

Comparison disposition: **handoff pass; implementation AFC/SSO review pending**. This reviewer found no third-party expression in the handoff. Comparison did not inspect prohibited material and does not establish legal certification. Actual naming, decomposition, operation ordering, data relationships, tests, and UI must be reviewed after the fresh-context implementation exists. This document cannot certify code that has not been inspected.

Reviewer: unexposed independent handoff reviewer; date as above. Each implementer must separately attest that restricted source was neither accessed nor supplied. The coordinator's report of fresh worker contexts is recorded above; it is not a substitute for those attestations.

## Acceptance And Audit Evidence Still Required

- Successful instance and tenant preview/commit, including independently enabled tenant transport excluded from an instance disable's actual impact.
- Wrong actor, tenant, scope, purpose, acknowledgment, signature, expiry, and excessive token length fail without mutation.
- Revoked administration or newly locked settings deny commit after a valid preview.
- Revision change, affected-scope change, and concurrent confirmation produce stale conflicts without partial effects; a pre-re-enable token cannot disable the new state.
- Preview leaves policy, revisions, suppression, outbox, and secret authority unchanged and makes no SMTP connection.
- Generic single/batch/reset changes cannot bypass deliberate effective disable.
- Durable policy/revision/suppression atomicity, existing operator state preservation, and the admitted-send boundary are demonstrated with relevant runtime evidence.
- HTTP/HAL/generated-contract parity, private/no-store behavior, BFF antiforgery, accessible confirmation/cancellation, and public/internal documentation parity are verified at their owning layers.
- Logs and traces contain no raw tokens, protected payloads, secrets, or contact data.

Dependency decision: no new dependency, package version, generated asset, or licensing mode is introduced. Use the already present ASP.NET Core Data Protection facilities; this is not a new or blanket approval of third-party licensing terms. Existing repository dependency gates remain applicable to the implementation phase.

This documentation-only task uses diff/format and local-link checks; it does not run product builds, architecture suites, or dependency scans. The broader intent registry's implementation gates remain pending with the feature; AGENTS.md section 8 scopes this artifact's verification to documentation. No intent registry, skill schema, canonical governance document, or twin rule changes are required for this handoff.

Evidence location: this task-owned handoff. Commit, PR, durable journal entry, implementation attestations, final AFC/SSO disposition, and feature runtime results must be linked when available; none is claimed by this document. The coordinator owns those follow-ups outside this reviewer's file ownership.

## Command Worker Source-Exclusion And Design Attestation

The command worker received a fresh bounded task and repository instructions, without the coordinator's external response or examples. It accessed no external source, documentation page, snippet, or implementation example. Its implementation context consists of this source-free handoff, repository-owned settings handlers/repositories, the Application response/authority/lease contracts, and the peer-owned impact/token ports. The first files authored were Application request/result contracts and behavioral tests; this handoff was read before authoring the handlers.

The independently selected design uses two MediatR handlers. Both resolve identity through IAdminContext and query the existing uncached role-repository ports before acquiring the complete SMTP/LockSmtp lease; they repeat those queries inside the lease-owned serializable transaction and consume the non-secret impact reader. Cached administration flags do not authorize or reject this operation. The tenant grant query requires the matching active tenant filter. Empty tenant targets receive bounded validation. Commit validates the exact acknowledgement and current preview binding before setting only DeliveryEnabled=false through existing repositories. The existing post-commit SettingChangedNotification provides cache invalidation and audit behavior. No additional coordinator, token persistence, crypto implementation, dependency, HTTP endpoint, or client affordance is introduced by this worker. Stateful Application tests use real setting entities and enforce operation ownership; authority and opaque-token behavior are substituted at their ports.

Parent-coordinated runtime evidence for this pre-writer-migration slice: `p02-disable-commands-green.log` records 29/29 focused Application cases passing with zero skips. The independent read-only worker found no new blocker in the six-file slice, using current native sources after stale-graph fallback. The parent subsequently reported `p02-disable-commit-green.log`, 12/12 composed real SQLite cases using actual roles, token protection, impact reader, settings repositories, lease and unit of work. These results establish the bounded preview/commit increment, not generic-route bypass closure, HTTP/HAL/UI, all-provider concurrency, or the later dedicated-writer migration.

## Impact-Reader Worker Attestation

Worker `smtp_disable_impact`, 2026-09-06: I received the bounded reader assignment before the coordinator reported the documentation incident. I have not accessed external implementation examples or received the source-bearing response. My implementation context consists of repository-owned source and instructions, the assigned behavioral requirements, and this sanitized handoff. I did not open the external references listed above.

The reader independently extends the existing `EmailDeliveryPolicyReader` with a detached-value counterfactual, then uses its unchanged `HierarchicalSettingMerge.Resolve` and `EmailDeliveryPolicySnapshot.Evaluate` calls. Both policies are evaluated from one materialized raw-settings and tenant-membership view. This design preserves repository ownership and governance semantics without adding another cascade or persisted preview model. The independent implementation choice is to compare `Enabled` before and after the hypothetical setting change, including misconfigured enabled scopes, and read existing control revisions in sorted 500-tenant chunks. Transaction ownership, tenant qualification, lock preservation, and deterministic ordering follow the task's security and correctness requirements.

The focused SQLite tests reuse `EmailDispatchSqliteFixture`, exercise independent and inherited transport ownership, and put the instance preview connection into query-only mode. The initial concrete-stub Red compiled and failed all five initial cases with no skips; evidence is `/home/amir/.cache/agent-tmp/email-optional-self-hosting/p02-disable-impact-red.log`. Eight Green cases are source-ready; runtime Green and the coordinating independent AFC/SSO review remain pending. The membership case proves fresh evidence after separately committed tenant creation/removal, not concurrent ReadCommitted isolation; real provider race evidence remains a P02 gate. No dependency or migration was introduced. This is a source-exclusion attestation and design rationale, not a legal certification or a full-feature acceptance claim.

## Implementer Attestations

### Protected Preview Token Service

Implementer: `smtp_disable_token`; date: 2026-09-06. This implementation context received only source-free functional requirements, the public interface facts identified above, and repository-owned material. It did not fetch either external page or receive any external source, example, snippet, source-derived structure, or other restricted expression. This handoff was read fully before the final test refinements.

Repository-native anchors are `EmailUnsubscribeTokenService` for purpose-specific expiring protection, `GuestCapabilityTokenService` for SHA-256 and constant-time comparison, and `WebhookSignatureService` for an internal clock constructor. The independently selected design hashes a deterministic JSON representation of the actor, selected scope/revision/lock state, and sorted affected scope revisions. Only the fixed-size digest enters the protected token. Empty actors, invalid identifiers or revisions, duplicate scope identifiers, and non-disableable snapshots are rejected before issuance or validation. The lifetime is fixed at five minutes; the clock seam is internal and expiry validation remains the native protector's responsibility.

AFC/SSO self-assessment: Application owns the two-operation interface and immutable output record; Infrastructure owns hashing and Data Protection. Scope sorting and a bounded digest follow the required stable-impact binding and size constraints. Framework identifiers and cryptographic primitives are interface or security constraints. Naming, field arrangement, validation flow, and adversarial test cases were independently chosen from repository requirements without an external implementation reference. No dependency was added. Independent final implementation review and runtime verification remain pending; authored tests are not claimed as passing evidence.

### Independent Token Implementation Review

Reviewer: unexposed independent handoff reviewer; date: 2026-09-06. Reviewed the actual `IEmailDeliveryDisableTokenService`, `EmailDeliveryDisableTokenService`, and `EmailDeliveryDisableTokenServiceTests`, together with the impact snapshot contract and the three repository-native anchors named in the implementer attestation. No external page, example, or source-derived representation was accessed or compared.

AFC/SSO disposition: **pass for the token implementation only**. The compact digest, deterministic scope ordering, purpose-specific expiry, constant-time comparison, and internal clock seam are traceable to the approved requirements and independently inspected project patterns. The digest covers actor, selected tenant/instance scope, selected revision, lock state, and all affected scope identifiers/revisions. The output contains a fixed-size protected digest rather than the affected-scope list. No new persistence model, cryptographic algorithm, package, or external structural reference was introduced. This is an engineering provenance assessment, not legal certification.

Bounded security source review found no actionable defect. Invalid actors, negative revisions, empty/duplicate scope identifiers, locked or empty snapshots, and token inputs above the 2,048-character limit are rejected before protection or comparison. Native unprotection enforces expiration and purpose/key isolation; comparison failures expose only a boolean result. No logging, secret-provider access, SMTP operation, or database mutation occurs in the token service. The five authored tests exercise exact field binding, tenant/instance separation, order independence and large impacts, malformed/tampered/foreign tokens, and native expired-token rejection without sleeping.

Runtime evidence remains pending at this review point; no test pass is inferred from authored assertions. Current-administrator revalidation, trusted impact acquisition, acknowledgment validation, transactional revision checks, HTTP errors, generic-settings bypass prevention, and commit/replay behavior belong to command/API integration and were not reviewed here. The token proves binding to a server-supplied snapshot; it does not itself authorize a caller or establish that the snapshot is authoritative. The complete feature's final AFC/SSO and security review remain open.

### Independent Application Handler Review

Reviewer: unexposed independent handoff reviewer; date: 2026-09-06. Reviewed the actual disable command and preview query handlers, their requests and preview DTO, and `EmailDeliveryDisableCommandHandlerTests`. Inspected the existing platform/tenant authority repositories, setting mutation entry points, setting-cache notification handler, and request performance logging to resolve the specific trust and side-effect questions. No external source or example was accessed.

AFC/SSO disposition: **pass for the bounded Application handler design**. Request contracts carry the target and confirmation evidence without a client-supplied actor. Separate preview and commit handlers follow the approved operation split; both use existing repository authority and policy coordination. Repository-owned writes and a deferred existing setting notification preserve the project's established ownership boundaries without introducing another policy engine or token store. No dependency was added. This assessment does not replace the command implementer's source-exclusion attestation.

Security source disposition: no actionable finding in this slice. `IAdminContext` supplies identity only; database-backed platform authority or strict current-tenant unrevoked administrator authority is checked before obtaining the global SMTP lease and again inside the serializable transaction. Empty targets and denied callers cannot reach the shared lease. Exact ordinal acknowledgment, current actionable impact, expected revision, and token matching precede any write. Tenant writes select the requested tenant; instance writes preserve existing setting metadata and locks. The returned setting notification is published after transaction completion and contains only the fixed setting key/value transition and normal audit metadata, never confirmation-token material. Existing performance logging records request type and duration rather than request contents.

The authored Application tests cover authority changes during lease acquisition, stale cached administration disagreeing with current grants, exact acknowledgment, stale/unusable tokens and impact, missing/locked/no-op scopes, permitted instance and tenant writes, replay rejection, and post-commit notification timing. Their repositories, lock/transaction owner, and token service are controlled substitutes; they demonstrate handler decisions and ordering, not real-provider concurrency or actual cryptographic validation. Runtime Green was pending at this review point. No HTTP route has been exposed by these handlers alone. Generic single/batch/reset bypass closure, authorization-revocation linearizability, real-provider transaction races, HTTP request/replay/logging controls, HAL, BFF antiforgery, and rendered confirmation UI remain outside this acceptance.

### SQLite Preview-To-Commit Test Worker Attestation

Worker: `smtp_invariant_tests`; date: 2026-09-06. This worker received the bounded integration-test assignment and this sanitized handoff, not the external response or its example. It accessed no external documentation page, source, example, or source-derived representation. Its implementation inputs were the functional requirements above and repository-owned handlers, policy readers, token service, repositories, and SQLite fixtures. It did not open the source-register links.

The independently authored `EmailDeliveryDisableCommitTests` composes the existing `InstanceSettingsCommandFixture` principal and administrator context with real platform and tenant role repositories, an explicitly bound tenant context, both Application handlers, `EmailDeliveryDisableImpactReader`, `EmailDeliveryDisableTokenService`, the existing relational mutation lock, and `EfCoreUnitOfWork`. Native ephemeral Data Protection supplies runtime protection without embedded credentials. Existing SQLite schema creation and lookup seeding are reused without a shared-fixture, dependency, model, or migration change.

The independent test-design choice is to compare freshly persisted policy and detached metadata before and after the complete preview/commit interaction, while keeping an independently enabled tenant and an inherited tenant in the same fixture. Separately committed membership and role changes exercise current evidence. A SQLite abort trigger rejects the revision update to test rollback of the setting and suppression state together. The existing notification observer rejects publication inside a transaction. These choices derive from the repository's tenant-isolation, transaction, and post-commit invariants; no third-party test arrangement was supplied or imitated.

Twelve cases cover successful instance and tenant commits, revision or affected-membership changes, incorrect acknowledgment, another authorized actor, revoked tenant authority, mismatched current tenant, replay after re-enable in both scopes, and rollback in both scopes. The parent-coordinated Release run completed with **12 passed, 0 failed, 0 skipped** on its first execution; evidence is `/home/amir/.cache/agent-tmp/email-optional-self-hosting/p02-disable-commit-green.log`. No failing product behavior was observed in this composed slice, so this result is integration Green rather than a new Red/Green cycle. This scope does not prove HTTP/HAL exposure, generic-setting bypass closure, cross-provider concurrency, or full P02 completion. Independent AFC/SSO review and the broader feature acceptance remain separate gates.
