# SecretKit Event Ownership And Preparation Boundary

> **Audience:** Contributors | Architects | Maintainers
> **Status:** Accepted; integration assurance in progress
> **Owner:** Security
> **Last Verified:** 2026-10-27
> **Source Anchors:** `dev/active/secretkit-adoption/secretkit-adoption-plan.md` Sections 5.1-5.3, `dev/active/secretkit-adoption/secretkit-adoption-tasks.md` task 1.5, `docs/internal/SECRETKIT_LOCAL_PACKAGES.md`

## Decision status

The architecture is accepted and the serving composition uses the selected
native owners described below. Source and dependency retirement are implemented
in the task workspace; final integration assurance and delivery remain open.
Dated verification receipts qualify their recorded snapshots, not every later
source change or a deployed provider.

## Context and alternatives

API, BFF and tooling need a shared acquisition boundary without importing
backend business layers into the BFF. Use the existing
`Explore.ServiceDefaults/Secrets/` boundary instead of creating a replacement
project, retaining a slim provider-mirroring project, or duplicating acquisition
in each host. The October 27, 2026 native cutover removes `Explore.Secrets` and its
test project after rehoming retained invariants; final runtime/provider/image
qualification remains separate from that source and dependency retirement.

## Responsibility and dependency direction

ServiceDefaults owns process role, immutable authority selection, exact
process-root admission, explicitly injected bootstrap identity, SDK resource
lifetime and configuration export. It stays independent of Domain,
Application, EF and database drivers. Capture role, environment, selector and
authority identity once before authentication; provider content cannot replace
them.

Domain retains registry/binding scope and canonical-reference policy.
Infrastructure applies that policy before tenant cache/provider access in
its native adapter. Do not move the Domain-dependent
acquisition policy wholesale into shared hosting.

SecretKit belongs only at outer implementation boundaries, never in Domain,
Application, wire DTOs or WASM. The BFF reaches shared acquisition through
ServiceDefaults, not a backend project link. Infrastructure's typed native
read/write adapters directly reference Core/Infisical.
Persistence references ServiceDefaults for explicit migration/tooling acquisition:
the ASP.NET-capable assembly dependency is acknowledged, but ordinary queries
must not depend on HTTP hosting. Database construction and provider semantics
remain Persistence responsibilities.

`MigrationDatabaseAcquisition` opens the migration consumer through ServiceDefaults,
prepares only declared database groups and overlays explicit null masks rather
than reviving lower-priority credential aliases. MigrationService retains that
owner through `RunAsync`; the synchronous EF contract bridges acquisition once
with a two-minute deadline and disposes it after context construction.
`DesignTimeModelConfiguration` selects the explicit Development/Testing model-only
lane before acquisition, so offline scaffolding requires no secret authority.

## Async required-group preparation

The approved composition contract awaits complete preparation before host
`Build`, EF pools, OIDC clients, workers or traffic. No temporary DI container,
remote work in a DI factory, or network I/O in synchronous
`IConfigurationProvider.Load` is permitted. Build-time API generation and
model-only tooling retain explicit no-I/O lanes; controlled tests use an
admitted fake authority, not production credentials.

Use `SecretSnapshotLoader` for explicit selections. Every member of one loader
is required: missing, denied, invalid or unavailable required material prevents
publication. Optional capabilities use separate groups included only when
enabled, with bounded non-success outcomes rather than fabricated absence.
Validate returned coordinates and case-insensitive mapped-key collisions before
publishing any group.

Environment, Infisical and Development/Testing-only UserSecrets are explicitly
selected authorities, not a fallback chain. Selection or provider failure does
not authorize another principal, hidden login, retry-based recovery or a lower
source. AppHost projects approved inputs; it does not become the secret reader.
API, BFF, Combined, migration and specialized Data Protection inputs remain
distinct profiles. Combined prepares its admitted union once, not two logins.

Publication is atomic **locally**: an accepted generation is complete for its
local candidate. Sequential or concurrent remote reads do not thereby acquire
provider-wide snapshot isolation. Any cross-secret remote consistency guarantee
needs independent provider/version evidence.

## Ownership, export and retirement

The preparing owner holds the session, generation and owned transport until
successful composition transfers ownership exactly once to the host.
Preparation failure, cancellation or host construction failure disposes the
lease exactly once; caller-owned dependencies remain caller-owned. Held reads
are independently disposable. Drain accepted work before asynchronous
retirement outside locks; do not leave orphan sessions or double-dispose them.

`SecretConfigurationExport.CreateAcknowledgingManagedStrings` is a deliberate
boundary for legacy configuration/Options consumers, not a claim of zero-copy
or erasable managed strings. Disposing protected SDK material does not erase
exported strings or downstream client copies. Bound retention and export only
what the consumer needs. Controlled keys use an authority-only configuration
view that later JSON/environment/command-line providers cannot override;
non-secret configuration retains normal host precedence. Synchronous Build/Load
reads prepared memory only.

Finite/periodic policy must agree with evidence bound to the actual login.
Zero maximum TTL alone proves neither periodic nor unlimited lifetime.
Reject expired or uncertain preparation; no persisted token, automatic relogin
or renewal replay. One caller's cancellation must not cancel another owner's
accepted shared work.

## Native runtime read ownership

Native sources and `SecretResolverCacheState` are shared across scopes, while
binding repositories and `ISecretResolver` remain scoped. Resolve
`IRetainedSecretResolver` through that same scoped resolver instance. Registration
does not create or take ownership of the selected authority supplied by the
composition root.

Admit binding coordinates and consumer roots before provider access. Keep native
revision comparison inside its selected owner; exported comparison tokens are
not portable provider versions. Fence publication by read-start order and reject
superseded responses or evidence lost through compaction. Binding changes, owner
replacement and inventory eviction invalidate comparison evidence even when
secret material has not changed.

Environment and User Secrets validation uses the selected frozen generation,
not a promise to reread ambient configuration. Provider access and cache
publication tests qualify these local boundaries, not remote actor permissions
or convergence across replicas. Native read registration alone does not activate
a serving entrypoint.

Commitment support resolves its instance key through the selected resolver and
binds tenant, actor, enrollment, generation, operation, purpose and exact secret
bytes with SHA-256 HMAC. It does not dispatch a provider mutation. Its immediate
barrier is a cancellation checkpoint, not a concurrency lock; the application
coordinator retains operation ownership through dispatch and settlement.

## Foundation versus successor execution

All four immutable packages at `[0.1.0-local.20261024.1]` support this foundation;
the Refit family aligns at 16.3.0. Their availability is not runtime activation.
The Hosting helper may expose explicit lifecycle/cancellation seams, but this
foundation registers no serving host, maintenance service or scheduler rule.
Short-lived migration/design-time operations do not start background
maintenance merely because Hosting is transitively available.

The successor must qualify and wire real host entrypoints, tenant adapters,
Data Protection, Persistence tooling and client activation/retirement. It must
close the preparation-to-host-start token-maintenance gap under its approved
contract, then remove old projects only after consumers migrate. Foundation
tests cannot establish fleet convergence, remote atomicity, native actor/ACL
enforcement, image readiness or trimming/Native AOT support.

## Evidence and distribution

The [dependency admission](../legal/dependencies/secretkit.md) records retained
MIT notices, provenance and outbound-license obligations.
The [package guide](../SECRETKIT_LOCAL_PACKAGES.md) separates historical transfer
receipts, verified foundation checks, quarantined baseline failures and
unexecuted successor qualification. This ADR does not infer image or
live-provider assurance from offline checks.

## Serving composition cutover

API and split BFF open one selected authority before composing services, start
its token-only maintenance when applicable, and await their preparation methods
before `Build`. Combined passes the same Combined owner to API preparation and
then BFF preparation; the frontend checks the already admitted Combined key
partition and owner identity instead of opening a second session. The outer
program retains that owner through host shutdown and disposes it after the app.

Preparation admits configuration groups and Data Protection before synchronous
consumers can construct. The API selects `AddNativeSecretResolution`, not the
legacy provider pipeline. Readiness reacquires the same key authority and fences
cached protectors on rejected admission; it cannot install a new ring or clear
the rejection without restart. Testing and build-time OpenAPI retain explicit
no-login composition lanes. Their injected authorities are not production
fallbacks.

Aspire projects value-free consumer policy and only that consumer's machine
identity. Compose projects API, BFF and migration policies independently. Direct
launches use native fields; neither path borrows a shared lifetime policy.
Token renewal is not application-secret rotation, provider write permission or
fleet readiness. Native writer activation still does not establish durable
setup cancellation correctness without the separate Domain/Application effect
settlement and its generated model change.

## Admission and retirement verification

Native mutation adapters retain a request-owned `SecretMutationAttempt` before
provider dispatch. Its live projection preserves `NotDispatched`,
`PossiblyDispatched`, `Applied`, `Pending`, `Rejected` and `Conflict` separately
from local delivery. Authentication and setup adapters update only an existing
canonical instance binding through the selected owner, never create missing
material or replay uncertain work. Applied evidence requires exact-instance
cache invalidation before delivery is reported; later invalidation failure does
not erase the provider effect. These additive adapters and opt-in registration
do not activate serving roots or strengthen persisted setup settlement by
themselves; that cutover must replace selection and its handler/model together.

Preparation reserves the owner before enumerating caller-supplied selections.
Retirement closes admission, cancels, drains reservations, and disposes native
resources outside the ownership gate. Publication checks the same gate.
Protected configuration destinations reject both underscore and hierarchical
namespace spellings. Native login binding preserves original timing and finite
ceilings; Event does not manufacture another absolute lifetime from a response.

Independent security and ownership reviews produced two concrete exploit
schedules. Both were reproduced before correction and are covered by the
25-case passing authority slice. The review record contains no reviewer
identities:

```json
{
  "review": "independent asymmetric boundary review",
  "aggregation": {"primary": 0.6, "secondary": 0.4},
  "findings": [
    {
      "id": "authority-hierarchical-destination",
      "severity": "medium",
      "disposition": "fixed",
      "reproducer": "HierarchicalProtectedDestinationsCannotBeExported",
      "red": "three hierarchical destination cases admitted unexpectedly",
      "green": "all three rejected before provider IO"
    },
    {
      "id": "authority-enumeration-retirement",
      "severity": "medium",
      "disposition": "fixed",
      "reproducer": "RetirementReservesPreparationBeforeCallerEnumeration",
      "red": "retirement completed while accepted enumeration was blocked",
      "green": "retirement drains the reserved operation without disposed-source access"
    }
  ]
}
```
