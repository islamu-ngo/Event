<!-- ABOUTME: Records the global Actor and concrete tenant-participation architecture decision. -->
<!-- ABOUTME: Defines identity, authority, profile, subscription, evidence, and URL ownership boundaries. -->

# ADR-020: Global Actor And Concrete Tenant Participation

- Status: Accepted
- Date: 2026-07-29

## Context

The same real User, Organization, or Group may appear in several tenants and through AT Protocol federation. Tenant-scoped Actors or duplicated tenant Organizations/Groups would fragment exact DID identity and make cross-tenant moderation ambiguous. A generic `ActorTenantPresence` would combine participation, observation, import policy, and membership lifecycles that have different authority and retention rules.

## Decision

`Actor`, `AtprotoIdentity`, `User`, `Organization`, `Group`, and unclassified external subjects are global. `Actor` owns exactly one concrete subject through one authoritative foreign-key direction, and exact DID/handle authority belongs to `AtprotoIdentity`, not `ActorPii`.

Tenant authority is concrete:

- `TenantUser` owns local User participation.
- `OrganizationTenant` owns Organization approval, visibility, organizer eligibility, moderation, members, settings, local profile/media overrides, and legitimacy evidence.
- `GroupTenant` owns equivalent Group state plus tenant hierarchy.
- Imported Events and record presentations may prove local discoverability without creating participation.

Events keep a simple global `ActorId`; tenant write authority is resolved from the concrete participation and current user. `ActorSubscription` remains tenant-local and targets a global Actor. Create/read/fanout require local discoverability; unsubscribe may retain durable-row access after the target becomes hidden.

Canonical `/actors/{actorId}` is the only Actor profile page and contains only
safe global data. Tenant-aware API consumers may request
`GET /api/actor/by-tenant/{tenantId}/{id}` to compose approved public local
overrides under the endpoint's fail-closed discoverability rules; this does not
create a tenant-contextual browser page. Clients render subscription, evidence,
document, and review actions only from HAL.

Organization legitimacy evidence targets `OrganizationTenant` and a private tenant-owned Document. Submission and review are separate, retained, audited workflows; evidence approval never approves participation automatically.

Generic browser Actor create/update/delete routes are absent. Verified onboarding, federation materialization, explicit consolidation, and moderation own identity lifecycle changes.

## Consequences

- No tenant Actor, tenant Organization/Group duplicate, `ActorTenantPresence`, composite Event-Actor foreign key, name/email/handle merge inference, or global-follow semantic is allowed.
- Global profile DTOs cannot expose participation, private User identity, tenant storage IDs, or reviewer identity.
- Tenant administrators cannot mutate global Actor or exact credential moderation.
- Tenant-owned profile media stays on concrete participation until a separately approved global storage scope exists.
- OpenAPI, generated clients, Blazor routes, localization, DBML, privacy inventory, and architecture tests must preserve these boundaries.
