# Dedicated Keycloak Lifecycle Service Identity

- **Status:** Deferred; accepted direction, not implemented.
- **Owner:** implementing agent.
- **Delivery:** PR 4; D3 `KC-204`, `KC-205`, `KC-206`; `KG-101` graduation from `PC-CTO-r2`.
- **Decision:** [ADR-031](../../docs/internal/adr/ADR-031-provider-credential-lifetime-boundaries.md).
- **Review:** [I-VSD report](../../islamic-value-sensitive-design/workstreams/i-vsd-provider-credential-onboarding.md).

## Problem And Repository Evidence

At H (`1ea011a5353ec12a75340bec222c28526e9da9c8`), [KeycloakAccountAuthorityLifecycleEmailService](../../src/Explore.Infrastructure/Services/Keycloak/KeycloakAccountAuthorityLifecycleEmailService.cs) requests an admin token with `grant_type=password`, `AdminUsername` and `AdminPassword`, then delegates provider-owned required-action emails. [KeycloakLifecycleEmailOptions](../../src/Explore.Infrastructure/Services/Keycloak/KeycloakLifecycleEmailOptions.cs) is the owning configuration contract. Recurring lifecycle work is not a one-time human-admin interaction; H's HTTP changes do not replace this authority.

The exact minimum realm-management roles remain unverified. A successful token or email request does not prove absence of excess roles. Do not introduce broader introspection credentials to manufacture that proof.

## Prerequisites And Scope

[PR 2 runtime authority](provider-credential-runtime-authority.md) must supply the selected external authority contract. This packet is independent of PR 3's interactive UI after PR 2; the PC-CTO-r2 PR schedule supersedes original phase-number dependencies.

Provision a dedicated confidential client for the API only, with OAuth client credentials and the minimum real-provider-verified roles for the exact lifecycle operations. Use `/runtime/api/authentication/keycloak`; purpose-separate it from the BFF client, human administrator, bootstrap identity and provider database credentials. Missing, invalid, unauthorized or unavailable selected authority fails closed without fallback.

D3 inventory:

- `src/Explore.Infrastructure/Services/Keycloak/KeycloakLifecycleEmailOptions.cs`, `KeycloakAccountAuthorityLifecycleEmailService.cs`, and `src/Explore.Infrastructure/InfrastructureServicesRegistration.cs`.
- `src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs` and API runtime mappings resolved from PR 2.
- `tests/Explore.Infrastructure.Tests/Infrastructure/KeycloakAccountAuthorityLifecycleEmailServiceTests.cs`, `tests/Event.Persistence.IntegrationTests/Identity/KeycloakAccountAuthorityLifecycleEmailServiceTests.cs`, and focused configuration/secret tests.
- Internal authentication/secrets/security/operations contracts and public authentication/environment reference.

## Exclusions

No human password-grant fallback, BFF-client reuse, new browser credential path, generic credential store or broad Admin API account. Do not change lifecycle business authority or permit cross-tenant/provider-account delegation. Do not persist tokens or secret-bearing jobs/events. Existing domain-transition notifications keep their existing transactional outbox with value-free payloads; external provider calls remain outside DB transactions. Cerbos publication and remaining deployment isolation belong to other packets.

## Red Invariants And Acceptance

- `SCN-KC-003`: only client credentials are sent to the token endpoint; a dedicated API identity performs each permitted provider-owned required action.
- Missing/wrong/insufficient configuration yields truthful bounded failure and no human/BFF identity fallback. Test wrong realm, unrelated account/tenant and missing required role at the real provider boundary.
- Establish a real-provider minimum-role fixture: the documented role set succeeds, removal of required privilege fails, and an overprivileged deployment is rejected by deployment validation. Runtime token success alone is not the overprivilege validator.
- Generated client-secret/token canaries never appear in logs, traces, health, ProblemDetails, metrics, support evidence or returned contracts. Selected-authority failures disclose only safe reason codes.
- Test rotation and replica restart/convergence without reuse of retired human credentials. Coordinate async outcomes with subscribed signals and bounded timeout, never sleeps or polling.

Acceptance includes a version-bound minimum-role evidence record and operator provisioning/validation instructions before enabling delegation. No provider role claim is established by this brief.

## I-VSD Mapping And Revision Gate

Preserve `IVSD-F004` / `IVSD-M004` -> `SCN-KC-003`, D3 `KC-205`; retain the complete D3 task range `KC-204` through `KC-206`. `IVSD-F004` remains open; Cerbos portions (`SCN-CB-002` / `SCN-CB-003`, D4 `CB-302`, D5 `CB-305`) stay with [PR 5](provider-credential-cerbos-publication.md), not this packet. Zero-output testing supports the program constraint without transferring the report's `IVSD-F005` task ownership.

Before implementation, create a separate active triad with exact atomic paths/commands, bind current I-VSD/CTO review to it and resolve the provider-role evidence gate. ADR-031 records original PC-CTO-r2 hashes. Authority, role selection, delegation scope, runtime paths, recovery or mapping changes require fresh review. The old umbrella commit contracts are not executable.

## Dual Documentation And Recovery

Update [AUTHENTICATION](../../docs/internal/AUTHENTICATION.md), [SECRETS](../../docs/internal/SECRETS.md), [SECURITY-MODEL](../../docs/internal/SECURITY-MODEL.md), [OPERATIONS](../../docs/internal/OPERATIONS.md), public [authentication](../../docs/public/documentation/readme/security-and-identity/authentication.md) and [environment variables](../../docs/public/documentation/readme/configuration-and-operations/environment-variables.md) together. Document minimum roles, exact supported actions, deployment overprivilege validation, feature-disabled behavior, rotation and restart. Update owning machine configuration and governed release inputs for delivered breaking changes.

Recovery disables delegation or restores a still-valid prior service-client secret in the selected authority after validation; it never restores human administrator password-grant configuration. Revoke exposed/retired credentials after consumer convergence and retain value-free incident evidence.

## Verification Contract

Write Red invariants before the service change. Use focused in-memory owning class slices, then one Release build and at most one selected project per phase (D3 selects lifecycle behavior in `Explore.Infrastructure.Tests`). At PR exit execute the existing explicit real Keycloak integration lane including positive/minus-role tests, deployment overprivilege checks, rotation/configuration isolation, architecture gates and anonymized security/operations MAD. No skipped provider or unverified role set counts as acceptance. Capture value-free evidence and quarantine unrelated failures. No build or runtime test is claimed for this documentation-only graduation.
