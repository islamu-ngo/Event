# ADR-033: Reviewed Keycloak Operator Operations

> **Status:** Accepted and implemented
> **Date:** 2026-09-22
> **Owner:** Identity and Onboarding
> **Scope:** Keycloak inspection, repair, provisioning, credential custody, and startup

## Context

The previous Keycloak integration mixed runtime authentication with broad
administrative synchronization. Setup, startup, administrator pages, and local
infrastructure could patch existing realms and clients, rotate secrets, or
reconcile wide desired-state documents. A browser request carried mutable
provider shape, while provider timeouts had no durable identity-bound recovery
record. This made operator ownership, replay safety, credential custody, and
post-crash truth ambiguous.

The platform is pre-release, so preserving those routes, DTOs, startup hooks,
and environment knobs would add compatibility debt without protecting an
external consumer.

## Decision

1. **Runtime authority is deployment-owned.** Endpoint, realm, BFF client ID
   and BFF client secret resolve from one selected secret authority. Event
   stores no runtime client secret. Provider id/version metadata, never a
   secret-derived digest, binds a create receipt to the reviewed credential.
2. **Inspection is separate and read-only.** Public OIDC connection checks use
   no administrator credential. Advanced inspection accepts a fresh
   administrator username/password for one foreground request and returns only
   allowlisted normalized facts and reason codes.
3. **Every write starts from a durable receipt.** A credential-free operation
   receipt persists the exact actor/setup generation, target, expiry, ordered
   steps, preconditions, desired fingerprints, immutable provider identities,
   and outcomes before provider I/O.
4. **Mutation authority is closed.** Existing realm/client settings, users,
   roles, sessions, secrets and shared scopes are outside application
   authority. Event may repair only an exact approved subject/audience mapper.
   Realm/client provisioning is create-only after fresh absence proof.
5. **Provider uncertainty is explicit.** A timeout, disconnect or ambiguous 5xx
   after send becomes `OutcomeUnknown`; later steps stop. Reconciliation reads
   only the captured immutable provider ID. Event never retries, adopts by
   name, resumes, compensates, or rolls back a provider write.
6. **UI affordances are server-authored.** Setup and administration share one
   operator panel. Inspect, plan, apply, reconcile, cancel and refresh controls
   render only from HAL links. Apply requires receipt review and explicit
   confirmation. Credentials and confirmation are cleared after every attempt.
7. **Startup has no administrative mutation authority.** Managed-local
   Keycloak mounts no sample realm and runs no import/reconciliation hook.
   Fresh installations reach setup before realm discovery and use the same
   explicit operation workflow.

## Consequences

- Legacy bootstrap, realm-sync, rotation, desired-state and startup-init
  contracts are deleted without aliases.
- Operators must coordinate client-secret rotation directly in Keycloak and
  the selected deployment authority, restart affected replicas, reinspect, and
  verify a fresh sign-in.
- Existing incompatible resources require explicit manual correction; Event
  does not adopt them.
- A partially applied or unknown operation may require human reconciliation
  and a new plan for remaining work. This is deliberate safety, not reduced
  availability.
- Application and Keycloak backups must be captured together before Apply and
  retained while any outcome is unresolved.
- Managed-local launch requires setup-time provisioning or manual provider
  preparation; this makes the authority boundary explicit and preserves
  persistent Keycloak state.

## Rejected Alternatives

- Broad desired-state synchronization or generic Admin REST proxying.
- PUT replacement of existing clients or realms.
- Application-managed client-secret rotation or secret persistence.
- Binding receipts with a secret hash/digest.
- Automatic provider retries, rollback, or name-based adoption after timeout.
- Local role/claim checks for browser action visibility.
- Startup callback/SMTP/client reconciliation workers or replacement
  environment ratchets.

## Verification Anchors

- Domain lifecycle: `Explore.Domain.Keycloak.KeycloakOperation`
- Application orchestration:
  `Explore.Application.Features.InstanceOnboarding.Services.KeycloakOperationService`
- Provider boundary:
  `Explore.Infrastructure.Services.Keycloak.KeycloakAdminClient`
- HTTP/HAL boundary: `InstanceKeycloakOperationsController` and
  `KeycloakOperationLinkPolicy`
- Operator surface: `KeycloakOperatorPanel`
- Startup guardrail: `KeycloakRealmOwnershipTests`

## Related Decisions

- [ADR-021: Keycloak Authentication Standard](ADR-021-keycloak-authentication-standard.md)
- [ADR-027: First-Class Authentication Provider Matrix](ADR-027-first-class-authentication-provider-matrix.md)
- [ADR-031: Provider Credential Lifetime Boundaries](ADR-031-provider-credential-lifetime-boundaries.md)
- [ADR-032: Progressive Instance Onboarding](ADR-032-progressive-instance-onboarding.md)
