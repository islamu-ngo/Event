> **Scope:** Native operations and remaining MediatR requests; shared authorization facts and provider failure semantics.

# Authorization Patterns

`Explore.Application.Authorization.RequestAuthorization<TRequest>` owns evaluation for native operation decorators and the remaining `AuthorizationBehavior<TRequest,TResponse>` integration.

## Enforcement Point

- Requests are checked before handlers execute.
- Denials throw `AuthorizationException`.
- `Explore.API.ExceptionHandling.GlobalExceptionHandler` maps `AuthorizationException` to HTTP `403 Forbidden`.
- Native order is authorization -> performance -> handler; denial never enters timing.
- Remaining MediatR order is `PerformanceBehavior` -> `AuthorizationBehavior`.
- Provider-unavailable decisions throw `AuthorizationProviderUnavailableException`, distinct from ordinary denial.
- There is no global validation pipeline behavior in current registration; validators are used from handlers/services.

## Request Patterns

1. `[AuthorizeResource]`: fixed catalog resource/action, with request type name as the default resource ID.
2. `[AuthorizeResource]` + `ISecureRequest`: optional resource ID and typed `AuthorizationFacts` supplied by the request.
3. Optional `IAuthorizationContextEnricher<TRequest>`: resolves typed context before `AuthorizationResourceContextResolver` replaces it with persisted facts wherever supported. Caller or enricher facts do not override persisted authority.

There is no `IAuthorizedRequest` or caller-authored policy dictionary. Unannotated requests retain their exact reviewed public, capability-token, worker or handler-owned enforcement from [AUTHORIZATION.md](AUTHORIZATION.md#reviewed-handler-and-worker-authorities); do not add blanket bypass flags or arbitrary exception inventories.

## How To Choose

1. Fixed resource kind and no instance-specific context: `[AuthorizeResource]`
2. Fixed kind but policy depends on entity ID/attributes: `[AuthorizeResource]` + `ISecureRequest`
3. Persisted resource or feature-owned context: add a typed enricher while retaining the authoritative persisted resolver and fixed catalog capability.

## Provider Resolution (Runtime)

`RuntimeAuthorizationProvider` routes checks in this order:

1. Tenant BYO Cerbos config (if configured through `ICerbosConfigResolver`).
2. Handler-owned local check bypasses (`GetHandlerOwnedLocalCheckIndexes`): self-service `user:update`, pre-create `event:create`, `organization:create`, `event_session:create`, `ai_conversation` route directly to `FallbackAuthorizationService` to ensure stale PDP policy packages cannot block self-service or pre-create handlers.
3. Instance-level mode from `SystemSetting` key `AuthorizationProvider` (cached for 1 minute):
   - `"cerbos"` -> `CerbosAuthorizationService`
   - any other value / `"local"` -> `FallbackAuthorizationService`
4. If the instance provider setting cannot be read, the runtime uses the Cerbos fail-closed path and logs safe `FailureType` metadata only.

Instance Cerbos failures are fail-closed. Network, timeout, or PDP-unavailable failures deny rather than falling back to `FallbackAuthorizationService`; switching back to local RBAC requires an explicit provider configuration change.

BYO Cerbos failure handling:

- Any BYO PDP failure activates fallback provider `SafeMode` (one-way latch: non-instance-admin traffic denied).
- There is no fail-open configuration. The `cerbos.failure_mode` setting was deleted; runtime authorization always treats a BYO outage as fail-closed and does not run standard local RBAC.

BYO config resolver failures activate provider-instance `SafeMode` instead of silently using local RBAC. A tenant configured with `cerbos.mode=custom_endpoint` but no custom PDP endpoint remains in BYO mode: runtime authorization activates safe mode, while explicit BYO Admin API configuration remains available for package sync/status operations.

## Fallback RBAC Facts

`FallbackAuthorizationService` is a 4-part partial class (`.cs`, `.Evaluators.cs`, `.Batch.cs`, `.MachineCaller.cs`) that is deny-by-default for unknown resource kinds and includes explicit rules across all 40 domain resource kinds (`tenant_setting`, `organization`, `event`, `event_registration`, `storage_object`, `user`, `webhook`, `support_access_session`, etc.).

Notable behavior:

- Instance admins bypass normal checks except for direct event authority rules (`event:manage-tickets` requires explicit event authority).
- Tenant-setting updates are denied when `isLockedByInstance=true` (unless document is `tenant.branding`).
- `user` resource supports self-service `view`/`update` when `targetUserId == current user`.
- Machine callers (API keys) evaluate via `EvaluateMachineCallerAccessAsync`: barred from registration workflows, gated by `MachineScopeMapping` scope ceilings, and scoped by `ExternalApiKeyOwnerType`.
- Batch evaluation (`IsAllowedBatchAsync`) pre-resolves an `AuthorityProfile` and fetches active event role snapshots via `IEventAuthoritySnapshotService` in **a single pass**, executing batch link checks in $O(1)$ database queries.

## Related

- [SECURITY_OVERVIEW.md](SECURITY_OVERVIEW.md)
- [AUTHORIZATION.md](AUTHORIZATION.md)
- [adr/ADR-001-authorization-provider-architecture.md](adr/ADR-001-authorization-provider-architecture.md)
