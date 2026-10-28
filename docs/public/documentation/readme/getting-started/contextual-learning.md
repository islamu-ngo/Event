---
description: Private authored learning storage, simulation boundaries, and operator responsibilities.
---

# Private learning backend

The private learning backend stores optional learning preferences, tour progress
and authored practice independently of the rendered onboarding experience.
It does not deliver tour overlays, practice pages or a
simulation mode in the live AI interface; those require the ordered presentation
and guidance successors.

## Learning Does Not Grant Production Authority

An active tenant member can learn without publishing or administration rights.
Organization/group context still requires current membership in the appropriate
tenant placement. Completing a lesson does not configure payments, make a
provider available, publish an event or complete tenant setup.

Each learner's state is private to their resolved internal account and tenant.
Another learner, publisher or administrator does not gain a progress/history
reader merely through their role. Switching tenant or account requires a fresh
private context. Do not embed learning state in public page prerendering,
browser transfer state or shared caches.

## What Practice Represents

The shipped `learning-core` pack contains six deterministic exercises:

* Event details: choose in-person or online presentation.
* Sessions/programme: choose one or two authored sessions.
* Ticket capacity: choose a synthetic capacity of 12 or 24.
* Attendees/team: choose an attendee or helper example.
* Publication lifecycle: keep the example draft or preview publication.
* AI example review: accept or reject an authored example.

These choices are simulations. They create no production events, tickets,
orders, attendees, payments, emails, webhooks, conversations or federation
effects. A simulated confirmation is not evidence that a live action succeeded.
The exercises require no live AI provider and accept no arbitrary prompt.

## State, Removal And Privacy

The shared tenant learning source is created only by an explicit authorized
start, not by reading a page or enabling a deployment flag. Intentional removal
leaves a tombstone so it stays removed until explicitly recreated. Tenant-wide
source management is distinct from changing a learner's hide/pin/suppression
preferences.

The backend retains at most the current and previous material versions per
tour family. A single claim coordinates concurrent tabs; it expires after
120 seconds without advancing explicit progress. It is not kept alive by
background polling. Manual replay preserves the earlier outcome.

Practice is bounded to 20 self/actor contexts and six recipes per context,
1 KiB per record and 128 KiB per learner. Learners may clear their own practice
in a context they can no longer preview. This is not permission to inspect
another person's state.

Internal-account erasure must remove learning across all tenants inside the
existing protected erasure operation, including covered replay and native
PostgreSQL execution. The tenant's shared pack is not another learner's private
history. Physical tenant removal also removes that tenant's learning roots and
children. Learning relationships introduce no deletion path to production users
or actors; existing tenant-retirement rules still govern production data.

## Operator Rollout Boundary

The backend source defines seven `/api/learning` operations: private context and
practice reads; tour claim/outcome, preference, authored-practice and shared-source
actions. Even reads require the current authenticated tenant member. Responses
and errors are private/no-store. Use the returned HAL links for actions rather
than inferring permission from a role or an earlier response.

Clients send the current scope stamp and expected revisions. Claims, outcomes and
practice also carry one stable intent ID for a bounded retry of the same payload.
A conflict requires refreshing the private context. Learning does not cache a
private response to bypass current authorization, and an uncertain commit must
not be automatically repeated as a new action. No existing endpoint or
compatibility adapter is replaced.

This backend adds learning-owned schema through the normal generated provider
migrations. It adds no tour package, configuration flag, secret, external
provider account or compatibility route. Apply the normal deployment migration
procedure; never hand-edit generated migrations or reset a shared database.
For PostgreSQL, run the existing declared-purpose migration composition as well:
it installs Runtime tenant-scoped learning rights and the existing protected
Global purpose's limited learner-root erasure rights. Do not grant broad child
table access or production administrator authority to make a learning request
succeed.

API exposure and generated-client rollout remain gated on real private
authorization, concurrency, erasure and provider verification. The later UI
rollout also requires actual keyboard, focus, screen-reader, RTL and
representative-comprehension evidence. Do not describe backend tests as proof
that the complete onboarding experience is ready.

This increment is not yet release-certified across the provider matrix.
Existing schema failures block MySQL/MariaDB learning assurance, and existing
historical migration failures block the complete SQLite/SQL Server gates.
Native PostgreSQL tenant retirement is also blocked by an existing discovery
permission-contract conflict before learning deletion.
Passing isolated learning checks do not make those deployments ready. Require
the complete migration and privacy gates for the selected provider before rollout.

See [Privacy Erasure](../security-and-identity/privacy-erasure.md) and
[Authorization](../security-and-identity/authorization.md) for the existing
account and authority boundaries.
