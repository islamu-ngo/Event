<!-- ABOUTME: Technical provider matrix for Local Identity, Keycloak, and AT Protocol authentication. -->
<!-- ABOUTME: Defines runtime admission, JIT provisioning, switching, and fail-closed operator contracts. -->

# Authentication Providers

This page is the provider-policy companion to
[Authentication](AUTHENTICATION.md). The architecture and data flow live there;
this page owns the closed runtime matrix used by configuration, BFF, API, and
operator tooling.

## Closed Provider Matrix

| Primary | AT Protocol axis | New-login admission |
|---|---:|---|
| Local Identity (`4`) | off | Local Identity |
| Local Identity (`4`) | on | Local Identity and AT Protocol |
| Keycloak (`1`) | off | Keycloak |
| Keycloak (`1`) | on | Keycloak and AT Protocol |
| AT Protocol (`2`) | on | AT Protocol only |

Primary AT Protocol with its axis off is invalid. Google SSO is forced off in
AT Protocol-only mode. Persisted values use `AuthenticationProviderKind`
integers; `local`, `keycloak`, and `atproto` are boundary codes only.

## Resolution and Cache Contract

`RuntimeAuthenticationProviderDispatcher` resolves deployment configuration
first, then `auth.primary_provider_id`, then Local Identity. Unsupported or
malformed values fail closed. Successful database resolution has a one-minute
cache; a committed provider update invalidates it before JWT authority refresh.

The BFF applies the same resolved primary authority:

- Local forms exist only for Local Identity;
- retained Keycloak schemes may validate old sessions but are not advertised
  outside Keycloak primary mode;
- AT Protocol-only discovery returns exactly one ready `handle_input` provider;
- browser code receives provider metadata, never bearer tokens.

## Correlation Trust Is Not Login Admission

Selecting Keycloak or accepting a Google token does not authorize email-based
account adoption. Deployment `IdentityCorrelation:TrustedIssuers` explicitly
lists exact normalized OIDC issuers and defaults to empty. Existing exact
bindings and otherwise permitted safe signup continue to work without that
allowlist. Removing trust blocks future email correlation, not existing bindings.
Local-owned accounts remain excluded from automatic adoption, and ATProto
remains a DID authority rather than an email authority. See
[embedded account correlation](AUTHENTICATION.md#embedded-account-correlation)
for the principal evidence and transactional resolver contract.

## AT Protocol Provisioning Authority

An independently verified DID is the account key. A mutable handle is challenge
input and display metadata only.

When AT Protocol is primary, the bootstrap command may JIT-create:

1. one passwordless `User` plus its PII extension;
2. one personal `Actor` plus its PII extension;
3. one global `UserExternalLogin` for the exact DID.

The provider/key unique index and bootstrap-convergence transaction make
repeated and concurrent first login idempotent. `TenantUser` is the sole tenant
participation authority and is added only after the target tenant exists.

Local login and registration handlers independently check the runtime
dispatcher. UI suppression is not the security boundary.

## Administrator Safety

Provider changes are server-authorized and fail when the current administrator
would lose every enabled linked sign-in path. AT Protocol primary cannot be
disabled through its optional-login toggle.

Interactive first-run administration remains bound to the active setup-secret
session. Configured administration remains bound to exact provider account,
generation, and fingerprint evidence. Neither flow trusts email matching.

If interactive access is lost, the break-glass tool grants only the global
`platform.admin` role to an already-linked exact DID. See
[Operations](OPERATIONS.md#instance-administrator-direct-database-recovery).

## Verification Owners

- `AtprotoSoleProviderInvariantTests`
- `RuntimeAuthenticationProviderDispatcherTests`
- `AuthProviderConfigurationDtoValidatorTests`
- `AuthProviderConfigurationTests`
- `AuthRedirectPagesTests`
- `InstanceAuthProviderSectionTests`
- `BffAuthEndpointValidationTests`
