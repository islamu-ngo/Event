ABOUTME: Documents implemented event and session template synchronization workflows.
ABOUTME: Separates UI/API sync behavior from unimplemented history and full diff-bucket UI coverage.

# Template Synchronization

> **Audience:** Admins | Contributors
> **Status:** Mixed
> **Owner:** Product/Admin
> **Last Verified:** 2026-05-12
> **Source Anchors:** `Explore.Blazor.Client/Pages/Admin/EventTemplateSync/`, `Explore.Blazor.Client/Pages/Admin/EventSessionTemplateSync/`, `Explore.API/Controllers/EventTemplateSyncController.cs`, `Explore.API/Controllers/EventSessionTemplateSyncController.cs`, `Explore.Application/Services/EventTemplateSyncService.cs`, `Explore.Application/Services/EventSessionTemplateSyncService.cs`

Template synchronization lets administrators compare an event or event session against its source template and apply selected custom-property changes. Treat this as a high-impact admin workflow because apply operations mutate live event or session custom-property definitions and options.

## Admin Entry Points

| Target | Admin Route | Primary Component |
|---|---|---|
| Event | `/admin/events/{eventId:guid}/template-sync` | `EventTemplateSyncPage.razor` |
| Event session | `/admin/event-sessions/{sessionId:guid}/template-sync` | `EventSessionTemplateSyncPage.razor` |

The inspected pages load a diff on initialization, show a local-change warning when untouched local definitions exist, and expose apply only when the HAL `sync-apply` affordance is present and at least one row is selected.

## API Surface

| Target | Endpoints | Controller |
|---|---|---|
| Event template sync | `/api/events/{eventId:guid}/template-sync/diff`, `/api/events/{eventId:guid}/template-sync/apply`, `/api/events/{eventId:guid}/template-sync/history` | `EventTemplateSyncController` |
| Event session template sync | `/api/event-sessions/{sessionId:guid}/template-sync/diff`, `/api/event-sessions/{sessionId:guid}/template-sync/apply`, `/api/event-sessions/{sessionId:guid}/template-sync/history` | `EventSessionTemplateSyncController` |

The API recomputes the diff server-side before apply. The Blazor pages post an explicit sync plan instead of applying every detected difference automatically.

## Authorization Boundary

- Sync controllers require the `template_admin` policy.
- In current source, `template_admin` is defined as an authenticated-user policy.
- Application requests add resource authorization with `ResourceKinds.CustomPropertyTemplate` and `AuthorizationActions.CustomPropertyTemplates.SyncDiff` or `SyncApply`.
- Client affordances should continue to rely on HAL links such as `sync-apply`, not local role checks.

Do not document broader role semantics unless the source policy changes.

## Sync Plan And Apply Behavior

`TemplateSyncPlanDto` requires:

- A positive target template version.
- A non-negative base provenance version.
- A bounded total change count.
- Explicit selected definition and option keys for added, modified, or retired changes.

The services run apply work transactionally, refresh projections, and write audit logs when changes are applied. Retired definitions and options are deactivated rather than physically removed, and default-option links are cleared when needed.

## Recovery And Conflict Behavior

| Situation | Behavior |
|---|---|
| Event or session template provenance changed after diff load | Stale base conflict; API returns a conflict response and the UI asks the admin to reload. |
| Definition or option changed concurrently | Concurrent-update conflict is returned for the affected item. |
| Template provenance missing | Sync reports `missing_template_provenance`. |
| Quota exceeded | Apply is rejected before mutating data and returns structured `quota_exceeded` ProblemDetails (`422`). |
| Transaction failure | Apply reports `apply_failed`; reload before retrying. |

The API cookbook treats template-sync `409 Conflict` as a reload-and-retry case. Operators should not manually edit persisted template provenance to bypass these checks.

## Dangerous Operations

- Applying retired definitions or options deactivates live event/session custom-property data.
- Applying modified options can change selectable values visible to users and admins.
- Apply plans should be reviewed as selected subsets; do not assume the diff page applies all server-detected changes.
- If conflicts appear, reload the diff and review the changed plan before applying again.

## Implemented Caveats

- The inspected Blazor pages render the modified-definitions tab; the service and DTO model also include added and retired buckets.
- History endpoints and services exist, but the inspected page source does not render a history view.
- The inspected pages use placeholder confirmation slugs in source comments; do not treat those literal values as production entity slugs.

## Related Documentation

- [ADMIN_GUIDE.md](ADMIN_GUIDE.md) - admin workflow overview.
- [API.md](API.md) - authoritative API route and error conventions.
- [API_COOKBOOK.md](API_COOKBOOK.md) - template-sync conflict handling for integrators.
- [AUTHORIZATION_PATTERNS.md](AUTHORIZATION_PATTERNS.md) - resource authorization patterns.
