<!-- ABOUTME: Defines a future authority proposal for tenant-delegated creation of instance-global Local accounts. -->
<!-- ABOUTME: Keeps current instance-only credentials intact and specifies approval, isolation, revocation, and audit gates. -->

# Tenant-Delegated Local Account Provisioning

> **Audience:** Contributors | Admins | AI agents
> **Status:** Planned
> **Owner:** Security
> **Last Verified:** 2026-09-08
> **Source Anchors:** `src/Explore.API/Controllers/LocalIdentityAdministrationController.cs`, `src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.cs`

## Trigger and current boundary

Open this authority proposal only after an explicit Project Steward/user decision to extend the instance-administrator-only model. It is outside the email-optional revision, not an unfinished implementation task. **Tenant credential delegation is not implemented or approved by this backlog.**

Current Local identities, provider bindings, passwords, verification provenance, and credential operations are instance-global. Tenant administrators manage membership and roles. Managed provisioning can reference an already-provisioned Local identity without gaining credential authority. Public Local signup remains closed.

Owner is the Security category, with Product/Admin owning the delegated workflow and Platform/Ops reviewing recovery. Assign a named accountable owner before proposal intake; none is invented here.

## Problem and recommended starting point

Some operators may want tenant staff to help enroll new attendees without routing every request through an instance administrator. Treat that as a new authority decision, not an extra role check on the existing global reset endpoint.

Start with an instance-approved, narrowly scoped **creation-only** delegation proposal. Keep reset, reverification, account adoption, and credential recovery instance-only unless separately authorized. If tenant staff need only membership assignment, use existing membership authority rather than introduce credential delegation.

## Required proposal

- Define who grants delegation, its tenant/action scope, lifetime, quotas, and immediate revocation authority. Check current grants at the mutation boundary, not cached claims or HAL alone.
- Define consent and out-of-band identity/handover responsibilities without treating a tenant administrator's statement as proof of mailbox possession.
- Resolve shared-email/global-identity behavior explicitly. Matching email must not reveal, adopt, merge, reset, or reverify an existing global Local or external-provider account. Existing-account collision responses must avoid cross-tenant enumeration.
- Preserve provider-native ownership. No Keycloak/PDS credential mutation or email-based external account linking enters this flow.
- Retain selected-Identity `ProvisioningPending -> ChangeRequired -> Ready` convergence, exact application binding, one-time handover, and operation-ID-only recovery. Never persist temporary plaintext in a tenant request, outbox, audit, or replay response.
- Define revocation during pending creation and after membership linkage. Remove only operation-owned/delegated authority; do not delete or reset a shared identity because one tenant revokes membership.
- Define audit ownership across stores: initiating instance authority, delegated actor, tenant scope, grant/operation IDs, target, decision, stage, time, and bounded reason. Keep credential values and unnecessary contact data out of audit/support exports.
- Specify tenant suspension/deletion, grant expiry, concurrent instance reset, lost handover acknowledgement, cross-store failure, and replay after revocation.
- Supply public operator/admin guidance, internal authority documentation, and HAL affordances from the same enforced policy. No local-role-only UI gate or hidden bypass.

## Acceptance before implementation can ship

- [ ] Explicit authority decision and threat model approved by the Project Steward/user and Security owner.
- [ ] Shared-email, exact-provider, cross-tenant nonenumeration, existing-account denial, and consent policies are unambiguous.
- [ ] Revocation/expiry wins against waiting and replayed requests using current durable authority.
- [ ] Deterministic race/fault tests prove one issuance winner, safe cross-store convergence, no automatic password replacement on retry, and no shared-identity deletion.
- [ ] One-time handover, current-session revocation, restricted first-use replacement, and value-free recovery remain enforced through actual API/BFF surfaces.
- [ ] Existing instance-only administration and external-provider behavior retain negative controls.
- [ ] Security/database/operations review, relevant native provider verification, audit retention, and public/internal documentation are complete.

No endpoint, migration, grant schema, deadline, or delivery date is allocated by this item. Those become an exact implementation packet only after the authority decision.

## References

- [ADR-029](../../docs/internal/adr/ADR-029-email-optional-self-hosting.md)
- [Durable credential/session lessons](../_journal/domains/email-optional-self-hosting.md)
- [Local administration controller](../../src/Explore.API/Controllers/LocalIdentityAdministrationController.cs)
- [Identity credential state store](../../src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.cs)
- [Authentication](../../docs/internal/AUTHENTICATION.md)
- [Authorization](../../docs/internal/AUTHORIZATION.md)
