<!-- ABOUTME: Records Local Identity, Keycloak, and AT Protocol as first-class primary authorities. -->
<!-- ABOUTME: Defines passwordless JIT identity, sole-provider admission, and administrator safety boundaries. -->

# ADR-027: First-Class Authentication Provider Matrix

- **Status:** Accepted
- **Date:** 2026-09-04
- **Deciders:** Project Steward, Architecture, Security, and Platform Engineering
- **Supersedes:** [ADR-021](ADR-021-keycloak-authentication-standard.md)

## Context

Mandatory Keycloak made small standalone deployments depend on a second
identity service, extra persistence, and OIDC administration. Embedded ASP.NET
Core Identity now provides a secure low-overhead default, while AT Protocol can
delegate password authentication to a user's personal data server.

The platform still needs one unambiguous new-login authority, provider-neutral
authorization, safe administrator bootstrap, and old-session continuity.

## Decision

1. Local Identity (`4`), Keycloak (`1`), and AT Protocol (`2`) are first-class
   primary authentication providers.
2. Local Identity or Keycloak primary may independently enable AT Protocol
   login. AT Protocol primary forces its own axis on and disables all other
   new-login providers.
3. Deployment configuration overrides persisted provider selection. Invalid,
   unavailable, or unsupported provider state fails closed.
4. A verified unlinked DID may JIT-create one passwordless `User`, personal
   `Actor`, and global `UserExternalLogin` only while AT Protocol is primary.
5. Provider bindings are instance-global. Tenant participation and authority
   exist only through `TenantUser` and role grants.
6. Email is profile data, never the external identity merge key. Passwordless
   accounts may have no email.
7. JIT identity, AT Protocol identity state, and encrypted OAuth session
   converge transactionally before token issuance.
8. OAuth success never grants administrator authority. Interactive root
   assignment remains setup-secret-bound; configured root assignment remains
   exact-provider, exact-DID, generation, and fingerprint bound.
9. Existing cookies and validation schemes remain usable until normal expiry
   after a provider switch. New-login discovery follows the new primary
   authority immediately after cache invalidation.
10. Offline recovery may grant only `platform.admin` to an existing exact
    linked DID after migration-current and authoritative-role checks. It may not
    create identities or grant tenant authority.

## Consequences

- Standalone self-hosting defaults to embedded Local Identity and requires no
  external identity service.
- AT Protocol-only hosting is passwordless for the application operator but
  requires a publicly reachable HTTPS origin; localhost cannot complete the
  decentralized OAuth callback flow.
- Keycloak remains the most capable option for professional and SaaS operators
  needing centralized SSO, federation, 2FA, and mature identity lifecycle
  administration.
- Global external-login bindings remove the false coupling between identity
  authority and a tenant selected at sign-in.
- User profile email uniqueness is no longer a persistence identity invariant;
  provider account uniqueness remains authoritative.
- All primary database providers regenerate their unshipped development
  migrations from the corrected model.

## Verification

- `AtprotoSoleProviderInvariantTests`
- `RuntimeAuthenticationProviderDispatcherTests`
- `AuthProviderConfigurationDtoValidatorTests`
- `BffAuthEndpointValidationTests`
- `AuthProviderConfigurationTests`
- `AuthRedirectPagesTests`
- `InstanceAuthProviderSectionTests`

## Related

- [Authentication](../AUTHENTICATION.md)
- [Authentication Providers](../AUTHENTICATION_PROVIDERS.md)
- [Security Model](../SECURITY-MODEL.md)
- [Operations](../OPERATIONS.md)
