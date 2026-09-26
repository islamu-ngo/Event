# Update-Tenant Slug Uniqueness Check

- **Status:** Deferred; correctness gap not fixed by the routing workstream.
- **Owner:** Tenant application and persistence maintainers.
- **Evidence:** `CreateTenantCommandHandler` checks and locks slug allocation;
  `UpdateTenantCommandHandler` validates format/reservation but writes a changed
  slug without an application-level uniqueness check.

## Problem

`Tenant.Slug` has a unique database index, but the update handler does not return
the same bounded validation outcome as tenant creation when another tenant owns
the requested slug. Relying on the provider exception leaks persistence behavior
and leaves concurrent slug changes without the create flow's explicit slug lock.

## Required Change

When an update actually changes the normalized slug, serialize against the same
canonical slug mutation lock used by creation. Inside that boundary, query the
slug owner, exclude the tenant being updated, and return a validation failure when
the slug is already allocated. Retain the database unique index as final race
defense and refresh the tenant-slug cache only after a successful commit.

## Acceptance Boundary

- Updating to another tenant's slug returns the established tenant-validation
  response and does not mutate either tenant.
- A case-only/no-op update does not reject itself or refresh the cache needlessly.
- Concurrent create/update and update/update claims for one slug produce at most
  one owner across every supported database provider.
- Tests coordinate real competing operations with deterministic barriers; no
  sleeps or polling.
- Reserved-slug and format validation still occurs before persistence work, and
  authorization remains unchanged.

## Non-Goals

Do not remove the unique index, weaken reserved-slug validation, add compatibility
aliases, or repair unrelated tenant lifecycle behavior.
