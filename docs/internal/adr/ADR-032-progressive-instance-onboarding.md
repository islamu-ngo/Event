# ADR-032: Progressive Instance Onboarding

- Status: Accepted for the safety foundation; completion/UI slices remain separate.
- Date: 2026-09-20

## Context

Private directory preparation must not imply publication. A completed setup
administrator needs a management entry point without relying on public tenant
lookup. Identity readiness and tenant lifecycle are distinct authorities.

The existing lifecycle transition already shares a tenant identity mutation lock
with document edits, compares the expected old tenant status, and records history.
However, a node-local typed-document cache could return an obsolete ready identity
after another writer committed an incomplete revision.

## Decision

Reuse the existing control-plane detail and activation endpoints. SingleTenant
accepts only `PlatformDefaults.DefaultTenantId`; other targets fail closed.
MultiTenant retains exact-target instance-authorized lifecycle operations. Fleet
creation/listing and the remaining lifecycle controller routes retain their mode
gates. SingleTenant detail HAL removes fleet-only relations after normal server
permission evaluation. Clients do not reconstruct these actions from claims.

Keep authorization unchanged. Control-plane access requires instance authority;
tenant identity/branding writes require the existing exact tenant authority.
Completed setup administrators have both persisted grants. Machines and unrelated
memberships do not substitute for them.

Resolve tenant directory identity documents directly from persistence instead of
the typed-document cache. Activation evaluates the fresh document inside the
existing mutation lock and transaction; it does not introduce a second activation
command, readiness alias, or publication Boolean. Concurrent patches retain their
revision check. Identity completion alone never changes lifecycle state.

## Consequences And Evidence

- PostgreSQL races use explicit task-completion gates around real relational
  mutation locks, separate DbContexts and separate reader/writer caches.
- Identity-first mutation leaves an incomplete tenant Provisioning; activation-first
  rejects an incomplete patch; same-revision writers cannot silently overwrite.
- HTTP exercises persisted platform and exact tenant grants, private document
  edits, explicit activation, same-state retry, history, arbitrary targets, wrong
  tenant administrators, ordinary members, machines and Single/MultiTenant HAL.
- Identity reads trade node-local caching for current authority. Other typed
  document caching is unchanged.
- No migrations, dependencies, compatibility aliases, or payment/history changes.
- Setup completion, authentication handoff and the browser getting-started journey
  are not delivered by this safety slice. Do not infer their readiness from these
  API tests.

Implementation and tests use repository-native contracts only; no third-party
implementation or dependency was introduced. Naming, flow and test organization
were independently selected for these existing ownership boundaries.
