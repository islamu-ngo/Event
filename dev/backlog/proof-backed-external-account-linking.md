# Proof-Backed External Account Linking

- **Status:** Deferred; security/privacy policy and endpoint design remain future work.
- **Owner:** Future identity workstream, with explicit product and security approval.
- **Decision:** [ADR-036](../../docs/internal/adr/ADR-036-verified-identity-correlation-authority.md).
- **Provenance:** Approved profile/recovery decision D3 and knowledge-graduation task T7.3.

## Problem And Current Boundary

Current sync preserves exact bindings and offers bounded recovery through the
original sign-in provider or operator support when correlation is unsafe.
It does not implement a general self-service link/merge endpoint. A contact
address, factual `EmailVerified`, accepted issuer or authenticated session alone
does not prove ownership of two accounts.

The embedded
[IdentityAccountResolver](../../src/Explore.Application/Services/IdentityAccountResolver.cs)
is operational today without a remote service or additional deployment
dependency. This backlog adds no selectable stub. Controlled trusted-issuer
correlation continues under ADR-036; explicit linking needs its own policy.

## Activation And Scope Gates

Approve the threat model, provider-specific proof capabilities, reauthentication
freshness/expiry limits, consent text, support recovery and unlink policy before
shipping a route. Identify exact native authentication/BFF safeguards, rate
limits, operation idempotency and non-disclosing error behavior. Update public
account/recovery instructions and internal identity/privacy/security anchors
together with the eventual behavior.

The initial scope is adding an unbound provider account to a proved existing
canonical Event user. Preserve `User.Id`, personal `Actor.Id`, existing
`UserExternalLogin.Id` values, histories and memberships. Allocate a new binding
ID only for a genuinely new binding. Moving a binding already owned by another
user, combining two canonical users, transferring roles/history or bypassing a
privacy fence is not this operation. Such collisions require a separately
approved recovery policy; do not silently expand linking into account merging.

## Two Authoritative Proofs

Require both proofs for the same server-created linking intent:

1. **Destination account proof:** fresh reauthentication through an existing
   exact binding/native credential authority for the canonical Event user.
   A stale cookie, body-supplied user UUID, email match or tenant administrator
   assertion is insufficient. Local proof uses its native current credential,
   security-stamp and lifecycle rules, not a synthetic issuer or verification
   flag. Specify assurance/MFA requirements and provider support explicitly.
2. **Additional account proof:** fresh provider authentication for the exact
   additional authority-qualified subject. Validate issuer, audience, signature,
   redirect/state/nonce and protocol-specific authority; for AT Protocol use the
   existing proved DID flow. An email challenge alone cannot substitute for
   provider-account control, including when addresses are equal and verified.

Bind both proofs to destination user, exact proposed provider account,
operation purpose, linking intent ID, expiry, authenticated browser/session and
authority generation. Establish the intended destination before starting the
second login so its callback cannot replace the original session identity.
Prevent login CSRF, session fixation, wrong-tab/intent substitution, issuer
mix-up and confused-deputy linking.

Proofs must expire, be one-use and become invalid after relevant credential
revocation, account erasure or binding generation changes. Recheck freshness
and authority at commit. Never persist raw passwords or OAuth tokens as linking
evidence. Store only minimized, purpose-bound proof metadata needed for replay
rejection and audit, with an approved retention period.

## Consent, Disclosure And Audit

Present the authenticated user with the exact provider being added, what future
sign-in access it grants, which profile/verification fields may change, and how
to revoke it. Require explicit confirmation after both proofs, not an automatic
callback mutation. State that linking does not grant roles or transfer event
history. Disclose only the user's own supported binding information; collisions
must not reveal another user's name, UUID, email, memberships or existence.

Notify through already supported safe destinations where policy permits.
An unsupported/conflicting new email must not receive a notification merely
because its provider asserted verification. Record bounded outcome, proof kind,
intent, authorized actor and binding IDs with restricted audit access; omit raw
addresses, credentials, tokens and unnecessary durable hashes. Include linking
proof/intent/notification records in the erasure and retention inventory.

## Transaction, Replay And Collision Rules

Use Domain invariants, Application native CQS handlers and entity-returning
Persistence ports. Transport/protocol verification belongs at the API/BFF or
Infrastructure boundary; clients use generated contracts and HAL link presence
for action affordances. Do not introduce a mediator, compatibility endpoint,
parallel profile service or shared remote database.

Perform external authentication before opening the local write transaction.
At commit, recheck both proofs, destination binding/user/Actor state, erasure
fences, exact proposed binding ownership and expected concurrency/generation.
Consume the linking intent and create the new binding atomically. Unique binding
constraints and compare-and-swap intent consumption must arbitrate concurrent
attempts, not an earlier optimistic read.

- Repeating a completed intent returns the same safe outcome only to its
  authorized context; it cannot create a second binding or consume a different
  intent's proofs.
- Expired, replayed, wrong-purpose, wrong-subject, wrong-session or revoked
  proofs fail without mutation.
- Concurrent links to different users allow at most one winner; the loser
  receives a bounded conflict without owner disclosure.
- An existing binding on the same user can converge idempotently after proof
  revalidation. An existing binding on another user cannot be reassigned,
  deleted or merged by this operation.
- Email equality never authorizes a merge. A link can be proved without email;
  any later canonical claim acquisition must separately satisfy the authority
  policy and uniqueness rules. A proved link must not silently steal another
  user's claim or fabricate verification.

Policy must explicitly decide whether email-claim collision blocks a new link
or allows it with unsupported email. It must also define unlinking the last
usable sign-in authority and heightened administrator-account assurance. Those
choices are pending; this backlog does not imply an implemented resolution.

## Erasure, Failures And Rollback

Serialize link commit against retained identity erasure authority. A late
callback or restored pending intent must not bind an erased subject, create a
replacement UUID or restore readable profile data. Preserve the retained fence
needed for no-resurrection; remove/minimize proof payloads and user-visible
binding data according to erasure/hold policy.

Before commit, cancellation or failure leaves no binding and consumes/invalidates
pending proof according to the approved intent policy. After commit, callback
acknowledgement failure does not mean the link rolled back: exact-intent replay
must report the actual stored result. Post-commit notifications use durable
delivery where required, never an external send inside the account transaction.

Undoing a completed link is a separately authenticated, authorized unlink
operation, not restoration of an older database snapshot. Revoke sessions/access
derived from the removed binding, reconcile only that binding's email evidence,
preserve independent proof and canonical IDs/history, and prevent last-authority
lockout. Rollback cannot transfer the binding to another user, erase audit facts
or resurrect an erased identity. Disabling the future feature fences/drains
pending intents; committed bindings still need an explicit safe lifecycle.

## Observable Acceptance And Exit

- Two independent fresh exact-account proofs plus explicit consent add one
  unbound provider account to the intended unchanged canonical user.
- Destination and added-provider sessions cannot swap identities or bypass
  native Local receipt/credential validation.
- Missing, expired, revoked, replayed or substituted proof and rejected consent
  leave user, Actor, binding, claim and role state unchanged.
- Barrier-controlled concurrent link/link and link/erasure races produce at
  most one lawful binding, no reassignment and no resurrection.
- Equal verified email alone cannot link/merge users; existing cross-user
  binding and email collisions produce the approved bounded result without
  revealing the other owner.
- User profile edits, independent address proof and tenant/administrator roles
  survive linking unchanged unless a separately disclosed policy owns a change.
- Lost acknowledgement/restart converges by exact intent; rollback/unlink removes
  only the authorized binding and its dependent access, without last-authority
  lockout or restoration of PII.
- Notifications and audit demonstrate the approved consent/retention contract
  without raw proof, token or unsupported-recipient disclosure.

Exit requires the approved policy, deterministic concurrency and expiry tests
using controlled time/events, native HTTP/BFF antiforgery and authentication
acceptance, provider-specific proof validation and database uniqueness/erasure
verification. No sleeps, source scraping or mock-call mirrors prove these
invariants. This backlog claims no implementation or runtime test completion.

## Related Work

[ISLAMU Identity integration](islamu-identity-control-plane-integration.md) must
preserve this proof/consent boundary if linking authority later moves remotely;
remote email matching cannot replace the two proofs.
