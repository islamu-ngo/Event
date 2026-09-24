<!-- ABOUTME: Domain journal for reviewed Keycloak operator authority and recovery. -->
<!-- ABOUTME: Captures durable provider-identity, credential, receipt, UI, and startup invariants. -->

# Keycloak Operator Safety Knowledge Ledger

> **Scope**: Keycloak runtime binding, Admin REST inspection/mutation,
> operation receipts, BFF recovery, managed-local hosting, and operator UX.

---

## Architectural Decision

Keycloak writes require an expiring, credential-free, server-authored receipt.
Runtime authentication credentials remain deployment-owned. Existing provider
resources are preserved except for exact approved subject/audience mapper
repair; realm/client provisioning is create-only after fresh absence proof.
Startup imports no sample realm and performs no reconciliation. Canonical
decision:
[`ADR-033`](../../../docs/internal/adr/ADR-033-keycloak-operator-mutation-boundary.md).

## Durable Invariants

1. Connection reads use the selected secret authority and public OIDC metadata;
   they never require an administrator credential.
2. Advanced credentials are fresh request input. They never come from Event
   deployment configuration and never enter receipts, cookies, caches, logs,
   generated clients, support artifacts, or database settings.
3. A provider write begins only after durable local intent, exact current
   actor/setup authority, unexpired approval, target/digest equality, and
   precondition revalidation.
4. Mapper repair binds provider ID, mapper name, protocol/type, claim semantic,
   token flags, client target, API audience, expected fingerprint and desired
   fingerprint. Same-name or same-semantic substitution is a conflict.
5. Realm creation plans and submits an immutable UUID. Client creation captures
   the immutable ID from the provider response. Reconciliation never adopts by
   name.
6. Provider id/version metadata binds create receipts to runtime credential
   provenance. The revision is value-free; no secret-derived digest is stored.
7. Timeout, disconnect and ambiguous 5xx after send become `OutcomeUnknown`.
   Later steps stop. No automatic retry, rollback, compensation or resume is
   permitted.
8. Browser controls are HAL-driven. Apply requires receipt review and explicit
   confirmation. Any ambiguous Apply response suppresses replay until an
   authoritative receipt refresh/reconcile.
9. A valid access token does not prove successful account synchronization. A
   rejected sync returns a safe full-sign-in action without reporting refresh
   success or silently signing out.
10. AppHost and Compose preserve Keycloak database volumes but mount no sample
    realm and launch no import/reconciliation hook.

## Non-Obvious Lessons

- Resolution timestamps are not credential versions: they change on every read
  and make unchanged receipts stale. Provider-owned id/version metadata is the
  correct nonsecret binding input.
- Capturing a realm ID in a GET after create still permits delete/recreate
  adoption. The immutable UUID must be approved and submitted in the create
  payload.
- A mapper can match desired values while representing the wrong provider
  identity/type. Desired-state equality never replaces update identity
  preconditions.
- A lost HTTP response is not evidence that a provider rejected a write.
  Preserve the receipt, hide Apply, and reconcile.
- Initial realm templates are not a safe runtime authority. Automatic import
  hides ownership and cannot preserve existing provider state.

## Operator Responsibilities

- Establish the native Keycloak administrator and first provider user outside
  Event.
- Configure endpoint, realm, BFF client ID/secret and `PublicBaseUrl` in the
  selected authority before reviewed provisioning.
- Back up the application and Keycloak databases together before Apply.
- Rotate an existing BFF secret in Keycloak and the deployment authority
  together, restart affected replicas, reinspect, then verify a fresh sign-in.
- Resolve incompatible existing realm/client settings manually. Do not delete
  or reimport a realm as an Event upgrade mechanism.

## Explicitly Prohibited, Not Deferred

- Whole-realm desired-state synchronization.
- Existing-client replacement or secret rotation by Event.
- Background privileged workers or startup mutation.
- Generic Admin REST proxying.
- Automatic retry/rollback of uncertain writes.
- Name-based adoption after timeout.
- Callback or SMTP environment ratchets that recreate the retired initializer.

## Verification Anchors

- `KeycloakOperationTests`
- `KeycloakOperationExecutionTests`
- `KeycloakOperationPersistenceTests`
- `KeycloakOperationRepairTests`
- `KeycloakProvisioningOperationTests`
- `KeycloakOperationHttpTests`
- `ProviderCredentialHttpBoundaryTests`
- `KeycloakOperatorPanelTests`
- `BffSessionRefreshServiceTests`
- `KeycloakRealmOwnershipTests`
