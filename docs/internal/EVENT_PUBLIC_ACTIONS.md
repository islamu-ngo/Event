# Native Event Public Actions

> **Audience:** Contributors
> **Status:** Implemented
> **Source anchors:** `src/Explore.Application/Features/EventPublicActions/`, `src/Explore.API/Controllers/EventPublicActionController.cs`

## Operation boundary

The feature owns two `IQuery<TResult>` requests, three
`ICommand<BaseCommandResponse<Guid>>` writes, and the non-generic
`RecordEventPublicActionEngagementCommand : ICommand`. Its metrics-only handler
returns `Task`, not `Unit`. There is no MediatR compatibility contract.

`EventPublicActionController` injects the six closed handler interfaces and its
HAL assembler. Its seven invocations comprise list, detail, create, update,
delete, and redirect's detail-then-engagement sequence. Application native
discovery supplies scoped authorization/performance decorators automatically;
no additional registration or generic dispatcher is needed. Reporting's nested
moderation workflow is not part of this cohort.

## Authority and state

Both public queries retain the published/public parent checks, participation
configuration, and canonical `IsPubliclyEligibleAsync` check (including publisher
eligibility). Only Active actions allowed by the parent's participation mode
are exposed. Detail requires the persisted action's EventId to match the route.
Redirect resolves this same detail before recording any engagement and uses only
its stored destination, never a caller-supplied redirect URL.

Create/update/delete retain the protected `manage-public-actions` capability.
Current identity and tenant are service-owned. Update/delete independently check
persisted event/action association and tenant before mutation. The existing
manual validator enforces HTTPS, no URL userinfo/fragment, supported kind, length,
sort-order and update-stamp constraints. Create and update yield PendingReview;
primary-action checks retain serializable transaction ownership. Updates and
soft-deletes retain concurrency stamps and EF optimistic concurrency protection.

## Transport, metrics and cancellation

Routes, operationIds, DTOs, HAL links and `DetailData` output-cache metadata are
unchanged. Redirect remains 302 with no-store. No new cache invalidation behavior
is introduced. Validation and missing detail retain structured ProblemDetails
bodies; the controller's existing JSON `Produces` declaration can serialize these
as `application/json`, rather than `application/problem+json`.

The engagement counter retains only `action_kind`, `surface` and `outcome` tags.
Unknown surfaces normalize to `other`; no event/user/tenant/action identifiers or
URLs are metric dimensions. `redirect_issued` records the server's redirect
decision, not proof that the browser reached the destination.

Controllers forward cancellation unchanged. Token-aware authorization, validation
and repository methods observe it. Legacy repository methods without token
parameters remain unchanged. The metrics handler is synchronous and still records
when called directly with an already-cancelled token; the void contract does not
invent cancellation or rollback of an emitted measurement.

## Verification seams

`NativeEventPublicActionsOperationTests` checks native request/handler shapes and
scoped discovery. `RecordEventPublicActionEngagementCommandHandlerTests` captures
real synchronous MeterListener measurements without polling.
`NativeEventPublicActionsHttpTests` uses the existing SQLite-backed
`NativeEventSeriesFactory`, real repositories and local authorization: it covers
public/hidden parents, health and mode filtering, redirect metrics, owner and
outsider authority, forged/foreign/mismatched targets, pending-review writes,
validation, primary uniqueness, stale writes, two loaded writers, cancellation,
controller closure and shipped OpenAPI parity. Hidden/pending write state is
inspected through the entity repository because this cohort has no management
read query. Public state is read through native queries and HTTP.

See the [adopter guide](../public/features/event-public-actions.md).
