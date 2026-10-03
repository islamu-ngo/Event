# ADR-036: Verified Identity Correlation Authority

- **Status:** Accepted; embedded identity and profile behavior implemented.
- **Date:** 2026-10-02
- **Decision scope:** Approved identity decisions D1-D3; future service activation and explicit account linking remain deferred.
- **Behavior anchors:** `706cc5a06` (issuer trust), `322a30c61` (verified ownership), `6cfe55611` (profile ownership).
- **Refines:** [ADR-027](ADR-027-first-class-authentication-provider-matrix.md), specifically its email/profile boundary; profile contact email is not a merge key, but separately proved canonical identity email may authorize controlled correlation.

## Context

Authentication establishes control of an exact provider account. It does not
grant every accepted issuer permission to associate that account with an
existing Event user. A provider's factual `EmailVerified` observation, a
canonical address's current proof, and permission to correlate are separate
facts. Shared contact addresses must not merge users, Actors or their histories.

Event must run independently today while leaving a deliberate future identity
authority handover possible. The embedded
[IdentityAccountResolver](../../../src/Explore.Application/Services/IdentityAccountResolver.cs)
is a real implementation of
[IIdentityAccountResolver](../../../src/Explore.Application/Contracts/Identity/IIdentityAccountResolver.cs),
not a remote-service stub. This decision introduces no deployment dependency on
ISLAMU Identity.

## Decisions

### 1. Exact authority precedes email correlation

Validated principal evidence supplies the provider account key and issuer;
request-body profile data cannot declare trust. OIDC account keys qualify the
subject by issuer. Existing exact bindings win over email lookup, subject to
binding consistency and privacy fences. Read-side identity resolution remains
binding-only; it does not perform enrollment or speculative email adoption.

[IdentityCorrelationOptions](../../../src/Explore.Application/Configuration/IdentityCorrelationOptions.cs)
contains a deployment-owned, instance-global exact normalized issuer allowlist,
empty by default. Malformed, wildcard and duplicate normalized entries are
invalid. Tenant administrators cannot widen this trust. Accepting an issuer's
authentication token and enabling signup are distinct from placing that issuer
on the correlation allowlist.

The [Domain policy](../../../src/Explore.Domain/Services/Identity/IdentityCorrelationPolicy.cs)
permits verified-email correlation for Keycloak or Google only when the exact
issuer is trusted. An unlinked identity may adopt exactly one supported owner
only if that owner is eligible and not Local-owned. Missing users, mismatched
evidence, unsupported ownership and conflicting bindings require recovery,
without disclosing another owner's identity. Permitted separate enrollment is
not permission to create a second eligible verified owner.

Local authority is intrinsic to the native credential lifecycle, not an OIDC
allowlist entry or a browser-provided flag. Local sync requires the exact
UUID subject, Local binding, personal Actor and, where applicable, current
lifecycle receipt. Email coincidence cannot take over a Local-owned account.
AT Protocol DID authentication creates no verified-email authority from profile
claims; its supported email-free admission belongs to the dedicated bootstrap
flow, not an arbitrary unbound `/api/User/sync` call.

### 2. Canonical ownership and proof are separate records

`UserIdentityEmailClaim` assigns one normalized identity address to one Event
user across the entire instance. A user may own multiple supported aliases.
Normalization trims and lowercases invariantly; it does not fold dots, plus
suffixes or provider-specific aliases. General organization, tenant and profile
contact fields remain shareable.

`UserIdentityEmailEvidence` supports a claim through one exact
`UserExternalLogin`. The database enforces unique normalized claim values and
one evidence row per binding. Composite foreign keys require both the claim
and binding to belong to the evidence's user. These are ordinary required-key
constraints, not tenant-local or nullable filtered-index approximations.
See [claim configuration](../../../src/Explore.Persistence/Configurations/Entities/UserIdentityEmailClaimConfiguration.cs)
and [evidence configuration](../../../src/Explore.Persistence/Configurations/Entities/UserIdentityEmailEvidenceConfiguration.cs).

Correlation and delivery require active supporting evidence, not merely a
reservation row or `UserPii.Email`. Removing one binding's obsolete evidence
preserves a claim supported by another binding; removing its last support
removes the claim in the account transaction.

### 3. Synchronization owns credential observations, not profile edits

[SyncUserCommandHandler](../../../src/Explore.Application/Features/Users/Handlers/Commands/SyncUserCommandHandler.cs)
initializes names and personal Actor display on creation. Subsequent sign-in
does not overwrite chosen names or Actor display. Existing native profile
commands own those edits and their concurrency checks; profile input cannot
manufacture email authority.

[IdentityEmailSynchronizationOperation](../../../src/Explore.Application/Services/IdentityEmailSynchronizationOperation.cs)
reconciles proof separately. A free changed trusted verified address may become
a claim for the same exact user. If it belongs to another user, the exact
authenticated subject remains unchanged, that binding's old proof is removed,
and the conflicting address is not adopted into the email mirror. Sync returns
success for the same user with a bounded recovery/operator-support message;
it does not merge accounts or expose the other owner.

`User.EmailVerified` continues to reflect the authenticated provider/native
verification observation. An allowlist change must not fabricate a change to
that fact. An untrusted observation cannot create a new eligible claim.
The implemented reconciliation preserves an already active proof when the
same binding still presents the same verified address, even after issuer
trust removal. Removing trust stops future email-based correlation and new
claim acquisition; it is not an implicit revocation of unchanged exact-binding
proof. A changed or unverified observation supersedes that binding's old proof.

### 4. Consumers use supported ownership, not raw verification

[RecipientEmailAddressResolver](../../../src/Explore.Application/Notifications/RecipientEmailAddressResolver.cs)
requires a live exact recipient and an active claim/proof belonging to that
user. It prefers the supported address matching the contact mirror, then uses
stable claim-ID ordering among supported alternatives. No proof means no
verified operational recipient, even when raw `EmailVerified` is true.
Another binding's valid proof can still authorize delivery after a collision;
the unsupported new address cannot.

Notification factories use this resolved state, as does
[authenticated guest-order claiming](../../../src/Explore.Application/Features/RegistrationOrders/Handlers/Commands/AuthenticatedRegistrationOrderAccessCommandHandlers.cs).
The current-user DTO still exposes factual `EmailVerified` through
[UserMapper](../../../src/Explore.Application/Mappings/UserMapper.cs); it is
not a public proof registry or an instruction to re-infer delivery eligibility.
Conflict is currently conveyed by the sync message, not a newly implemented
self-service recovery/link endpoint.

### 5. Account writes and privacy fences delimit authority

Application uses native `ICommandHandler` CQS and entity-returning persistence
ports. The resolver chooses accounts; Persistence enforces ownership.
[UserIdentityEmailRepository](../../../src/Explore.Persistence/Repositories/UserIdentityEmailRepository.cs)
requires an account transaction for claim/evidence mutation.
External sync re-resolves binding and ownership inside
[bootstrap convergence](../../../src/Explore.Persistence/EfCoreUnitOfWork.cs).
User/Actor creation, exact binding and claim assignment form one local commit;
uniqueness/write conflicts trigger bounded authoritative convergence or a
bounded failure, never a guessed owner or an orphan enrollment graph.

Local lifecycle synchronization is receipt-bound and serialized by the native
user security/concurrency-stamp compare-and-swap in
[LocalIdentityLifecycleStore](../../../src/Explore.Persistence/Identity/LocalIdentityLifecycleStore.cs).
It holds that credential write lock through the Application mirror commit.
In a separate identity database topology, the Application commit precedes the
native synchronized acknowledgement; these are not a distributed atomic
transaction. Idempotent receipt replay repairs a missing acknowledgement
without reopening email-based account selection.

Claims and evidence are PII.
[UserLocationPrivacyErasureRepository](../../../src/Explore.Persistence/Repositories/UserLocationPrivacyErasureRepository.cs)
removes evidence before claims and binding cleanup.
[PrivacyIdentityFenceOperation](../../../src/Explore.Application/Services/PrivacyIdentityFenceOperation.cs)
uses retained erasure authority, subject fences and keyed external-binding
fingerprints to serialize enrollment against erasure and fence restored
bindings. The retained authority prevents late sign-in/sync or restore from
resurrecting erased readable identity/profile data; deleting the local email
registry is not permission to enroll the erased external identity again.
Secret resolution precedes the authority write transaction.

## Alternatives

- **Trust every accepted provider or every realm:** rejected; authentication
  admission would become permission to adopt unrelated accounts.
- **Match profile/contact email or one Boolean:** rejected; neither records
  canonical ownership, exact proof provenance or independent surviving proof.
- **Always enroll and never correlate:** rejected for the approved controlled
  correlation policy; safe enrollment remains available where admission allows.
- **Email-only merge or silent Local takeover:** rejected; recovery/linking
  requires a separate proof-backed policy.
- **Remote resolver option now:** rejected; a dormant adapter or shared database
  would add coupling without an implemented protocol or authority handover.
- **Refresh profile names at every login:** rejected; credential observation
  cannot take ownership of user-authored presentation.

## Consequences And Deferred Boundaries

Operators must explicitly configure correlation trust; contact sharing remains
possible without identity sharing. Exact subjects and durable User/Actor/binding
IDs remain stable. Unsupported or obsolete proof may suppress operational mail
without changing a provider's verification fact. Local mirrors and external
authority fencing have explicit transaction/acknowledgement boundaries.

[Future ISLAMU Identity integration](../../../dev/backlog/islamu-identity-control-plane-integration.md)
must hand over correlation and selected profile authority without concurrent
local email matching.
[Dual-proof account linking](../../../dev/backlog/proof-backed-external-account-linking.md)
must define fresh proof, consent and collision recovery before exposing an
endpoint. Neither backlog is an implemented feature or activation switch.

## Regression Anchors And Evidence Limits

The committed behavior is covered by
[Domain correlation policy tests](../../../tests/Event.Domain.UnitTests/IdentityCorrelationPolicyTests.cs),
[ownership persistence tests](../../../tests/Event.Persistence.IntegrationTests/Identity/UserIdentityEmailOwnershipTests.cs)
(duplicate-owner rollback, cross-account proof rejection and independent-proof survival),
[resolved recipient tests](../../../tests/Event.Application.UnitTests/Features/Users/IdentityEmailResolutionTests.cs),
[profile ownership tests](../../../tests/Event.Application.UnitTests/Features/Users/UserProfileOwnershipTests.cs),
[external subject HTTP tests](../../../tests/Event.API.IntegrationTests/Features/ExternalProviderSubjectHttpTests.cs),
and [verification HTTP tests](../../../tests/Event.API.IntegrationTests/Features/ExternalProviderEmailVerificationHttpTests.cs).

These are concrete source anchors for the committed increments, not a claim
that the ongoing combined workstream's provider matrix, browser acceptance,
storage gates or final knowledge review have completed. This ADR contains no
transient run receipts; documentation graduation checks links and formatting.
