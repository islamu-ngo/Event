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

## Announcement Dismissal And Browser Privacy

Anonymous announcement dismissal stays in the browser and is separated by
tenant. Signing in does not copy that marker into your account; signed-in
preferences use the account's server-backed settings. Old browser markers that
did not identify a tenant are ignored, so an announcement may appear again after
this change. If the current tenant cannot be established, no anonymous preference
is read or saved.

The close action keeps the announcement visible and reports a save failure when
storage or the server does not confirm the dismissal. You can explicitly try
again. Switching account, tenant or announcement settings invalidates pending
results so an older save cannot hide the replacement announcement.

This repair keeps the existing integer announcement revision. It does not add
multiple publications, scheduling, acknowledgements or the later material-edition
contract.

## What Practice Represents

The backend-defined version-1 `learning-core` practice pack contains six deterministic exercises:

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

Tour material is version 2, independent of the version-1 practice pack. The backend
retains at most the current and previous tour material versions per family,
with up to 32 semantic steps per family and 256 retained step-evidence rows per
learner. A single claim coordinates concurrent tabs; it expires after
120 seconds without advancing explicit progress. It is not kept alive by
background polling. Manual replay preserves the earlier outcome.

## Current Plans, Progress And Replay

The private named-family plan at `GET /api/learning/tours/{family}/plan` describes
only guidance applicable to the learner's current workspace and, for event
management, the selected event. Families are `Shell`, `Studio`, `EventManagement`
and `NormalAi`. The response includes the captured context, source/plan stamps,
scope and learner revision, with current HAL links indicating claim eligibility.
An earlier response is not permission to act after switching account, tenant or
context.

Steps are identified by a stable key and version, not their screen position.
Compatible completed evidence survives a temporarily unavailable step or a
reordered tour. Historical completion or skip remains distinct from coverage of
the steps applicable now; updated guidance may introduce new work without
rewriting that history. Completion requires coverage of every applicable step.
These backend semantics do not mean a tour renderer has shipped.

Replay must be a deliberate learner choice. Skip and snooze can dismiss an
unclaimed invitation; snooze offers one hour, one day or one week rather than an
arbitrary deadline. An owned claim can be released after its source is removed
without restoring access to that source. No tour starts again automatically,
and lessons never become a hard gate on production work.

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

The backend source defines eight `/api/learning` operations: private context,
named-family tour-plan and practice reads; tour claim/action, preference, authored-practice and shared-source
actions. Even reads require the current authenticated tenant member. Responses
and errors are private/no-store. Use the returned HAL links for actions rather
than inferring permission from a role or an earlier response.

Integrations must use the semantic tour contract: tour version 2, expected plan
and policy-source stamps, captured context and explicit `Replay` on claims.
The existing tour-outcome route now accepts `Action` (`Progress`, `Complete`,
`Skip`, `Interrupt` or `Snooze`), not ordinal `StepIndex` or `Outcome` input.
Only progress supplies a step key/version; only snooze supplies a closed duration.
There is no compatibility adapter for old ordinal inputs.

Clients also send the current scope stamp and expected revisions. Claims, actions
and practice carry one stable intent ID for a bounded retry of the same payload.
A conflict requires refreshing the private context and applicable plan. Learning does not cache a
private response to bypass current authorization, and an uncertain commit must
not be automatically repeated as a new action. Source stamps detect stale input;
they grant no access. Observed AI availability is not a guarantee that a provider
credential remains valid or that a later AI request will succeed.

This backend adds learning-owned schema through the normal generated provider
migrations. It adds no tour package, configuration flag, secret, external
provider account or compatibility route. All four semantic migration cohorts
were regenerated and checked against the current model. The original SQL Server
metadata defect was corrected separately; the semantic cohort no longer carries
that unrelated foreign-key change. These checks are not proof of deployment.
No database has been upgraded or reset as evidence for this increment. Require
the normal deployment migration procedure and selected-provider acceptance before
rollout; never hand-edit generated migrations or reset a shared database.
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
No hosted or final migration/provider assurance is complete, and no learning UI
has shipped. Earlier isolated checks do not certify the current semantic contract
or renderer. Targeted actual-role checks now cover original-user source
retention, rejection after a concurrent guidance insertion, and live routine
drift detection after a warmed read. API/client regeneration and compilation also
passed. These results do not certify every authority topology or deployment.
Existing schema failures block MySQL/MariaDB learning assurance, and existing
historical migration failures block the complete SQLite/SQL Server gates.
Native PostgreSQL tenant retirement is also blocked by an existing discovery
permission-contract conflict before learning deletion.
Passing isolated learning checks do not make those deployments ready. Require
the complete migration and privacy gates for the selected provider before rollout.

See [Privacy Erasure](../security-and-identity/privacy-erasure.md) and
[Authorization](../security-and-identity/authorization.md) for the existing
account and authority boundaries.
