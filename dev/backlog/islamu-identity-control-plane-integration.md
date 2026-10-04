# ISLAMU Identity Control Plane Integration

- **Status:** Deferred; integration requirements, not a shipped remote adapter or deployment option.
- **Owner:** Future identity/platform workstream, with security and privacy review.
- **Decision:** [ADR-036](../../docs/internal/adr/ADR-036-verified-identity-correlation-authority.md).
- **Provenance:** Approved pre-release contract foundations decisions D1-D3 and knowledge-graduation task T7.3.

## Starting Point

Event has a working embedded
[IdentityAccountResolver](../../src/Explore.Application/Services/IdentityAccountResolver.cs)
and instance-global claim/evidence ownership. Local native identity, exact OIDC
bindings and AT Protocol bootstrap work without ISLAMU Identity. No remote
service stub, protocol adapter, selectable remote mode or new deployment
dependency is implemented by the current foundations.

The future service may own cross-product correlation and deliberately selected
profile fields. Event must consume its authoritative binding rather than also
matching email locally. This is an authority migration, not a second lookup
source behind the existing resolver.

## Activation Prerequisites

Before implementation, establish a separately approved scope and threat model:

1. Define the service's immutable subject/binding model, protocol version,
   operator trust root, environment isolation and authoritative capabilities.
   Specify which profile fields transfer ownership, their revision semantics
   and user-edit rules; do not infer ownership from a login claim.
2. Inventory every enrollment, Local confirmation/recovery/mirror repair,
   configured administrator, AT Protocol bootstrap, account lookup, delivery
   and erasure consumer. Identify local writes to be fenced at handover.
3. Reconcile canonical ownership and active evidence, including collision,
   Local-owned, erased and retained-fence cases. Unresolved rows block activation;
   never guess a mapping from contact email or raw `EmailVerified`.
4. Approve service availability, timeout and recovery policy, operator rollout
   and exit procedures, privacy retention, backup/restore convergence and
   deployment licensing. Standalone embedded hosting remains a real supported
   choice, not a remote-outage fallback that silently changes authority.
5. Update internal architecture/security/privacy anchors and public operator
   configuration, handover, recovery and backup documentation together when
   the integration actually changes runtime behavior.

Protocol and authentication choices are pending design decisions. This backlog
does not select SCIM, mandate a transport, or authorize a shared database.

## Immutable IDs And Ownership

Preserve existing Event `User.Id`, personal `Actor.Id` and
`UserExternalLogin.Id`. Map remote immutable subject and binding IDs explicitly;
email, display name, tenant slug and issuer presentation are not identifiers.
Require uniqueness for each remote authority/subject-to-local-user mapping and
binding, and reject reassignment without the separately approved recovery
process. Retain original Event histories and references.

The ownership contract must name exactly one correlation authority per
activation generation. Event continues to own tenant memberships, roles,
administrator freshness, local authorization and domain history. A remote
identity assertion grants no tenant or instance administrator authority.
Local credentials remain native unless a separate credential migration is
approved; native proof/receipt validation cannot be replaced by a claimed
remote Boolean. Decide whether each verified-address claim is remotely owned
or a local projection and specify evidence provenance and revocation.

Transfer of profile ownership requires an explicit field-level handover.
Existing user edits cannot be replaced by stale identity snapshots. Credential
verification facts, correlation eligibility and delivery support remain
separate fields/decisions through the protocol.

## Adapter, Protocol And Threat Model

Keep Domain invariants and immutable IDs independent of HTTP/service SDKs.
Application owns native CQS request/result contracts and identity ports;
Infrastructure owns an actual protocol adapter; Persistence owns mappings,
inbox/outbox records and local transactions. Controllers map transport outcomes,
and clients consume generated API contracts and HAL affordances. Do not add a
mediator dependency, compatibility alias or foreign implementation.

Before activation, specify and test:

- Authenticated encrypted service communication; exact peer, issuer and
  audience validation; service credential scope/rotation through approved secret
  authority; separate browser and service credentials.
- Versioned immutable subjects/bindings, bounded error codes, request intent,
  idempotency keys, monotonic revisions or equivalent stale-write prevention,
  expiry and authenticated provenance. Never trust caller-supplied correlation
  flags, tenant claims or email as proof of ownership.
- Replay, issuer substitution, malicious accepted issuer, compromised service
  credential, wrong deployment/environment, confused deputy, tenant-admin
  escalation, subject reassignment, stale sync, concurrent activation and
  enrollment-versus-erasure threats.
- Data-minimized requests/responses and access-controlled lookup. Logs, traces,
  metrics and audit record bounded outcomes, not raw email, tokens or unnecessary
  stable identity hashes. Prevent account-existence disclosure.

External calls must occur outside local database transactions. Use durable,
idempotent handover/synchronization operations with explicit partial completion,
not a pretend distributed transaction. Where reliable post-commit delivery is
required, use the project's transactional outbox and deduplicating inbox
patterns rather than a dual write.

## Synchronization, Privacy And Failure Boundaries

Fence/drain embedded correlation writers before activating remote authority.
Activation must compare the expected authority generation and mapping revision
and commit local state atomically. A stale embedded or remote writer cannot
reintroduce local email matching or overwrite a newer binding/profile decision.
Do not hide unresolved convergence behind successful readiness.

Define failures separately: remote unavailable, authentication rejected,
mapping collision, stale revision, local commit failure after remote mutation,
and acknowledgement loss. Reconcile by exact immutable operation/subject ID;
do not fall back to contact lookup or enroll a replacement user. Existing
binding reads may proceed only under an explicitly approved freshness policy;
new correlation must fail closed when authoritative proof is unavailable.

Extend the erasure inventory to service mappings, transferred profile data,
inbox/outbox payloads and backups. Specify retained erasure/fence authority,
remote acknowledgements, lawful holds, retryable deletion and restoration
replay. Fence both local and remote admission before reporting erasure complete.
A delayed remote snapshot, imported binding or restored backup cannot recreate
readable erased identity/profile data under a new UUID. Minimize retained proof
without deleting the authority needed to prevent resurrection.

## Exit And Rollback Gates

Define exit before enabling remote writes. Fence/drain the outgoing authority,
export and verify complete immutable mappings and current eligible evidence,
apply retained erasure facts, then activate the incoming authority under a new
generation. Only after reconciliation may embedded correlation resume.
Preserve User/Actor/binding IDs and user-authored fields throughout.

Rollback is not a feature-flag flip: after remote mutations, an older local
snapshot cannot be reinstated as authority. An interrupted migration remains
fenced with truthful partial-completion status and an operator repair path.
If evidence or mapping completeness cannot be proved, stop correlation rather
than invent support, recreate credentials or resurrect erased state.
Rotate/revoke retired service access after successful exit and drain caches;
never restore erased PII or retired secrets from a rollback artifact.

## Observable Acceptance

- Standalone embedded enrollment/sync remains operational without the service.
- A full handover and exit preserve all existing User/Actor/binding IDs and
  histories; each generation has exactly one writer for correlation.
- Concurrent old/new admissions, conflicting mappings and replayed operations
  commit at most one canonical owner and no orphan graph.
- Remote authority cannot grant tenant roles, override Local credential proof
  or replace a user profile edit with a stale observation.
- A trusted verification fact without eligible canonical evidence cannot
  authorize correlation or operational mail.
- Auth failure, timeout and local-after-remote failure produce bounded truthful
  outcomes; exact-operation replay converges without duplicate users.
- Erasure races, delayed sync and restore/import remain fenced across both
  authorities; no ready state precedes required acknowledgement/reconciliation.
- Failure injection at each handover/exit boundary proves rollback requirements,
  with no email-only mapping, secret/PII telemetry or silent fallback.

Future verification must exercise deterministic concurrency, native HTTP/BFF
flows, supported database ownership/migration boundaries and the actual remote
adapter with protocol authentication/failure injection. No runtime integration
or acceptance result is claimed by this prose-only backlog.

## Related Work

[Dual-proof account linking](proof-backed-external-account-linking.md) owns
explicit additional-binding consent and recovery policy. Integration cannot
silently replace that policy with remote email matching.
