ABOUTME: Extensibility model implemented in this codebase (modules, typed sector schema, and Layer 3 custom properties).
ABOUTME: Focuses on what exists today and documents the boundary between typed sector semantics and governed custom extensions.

# Extensibility

## Implemented Building Blocks

Unlike rigid commercial event tools that force every event into a single static model, ISLAMU Event provides a 3-layer architecture designed for **white-label adaptability**. Any community, enterprise, or platform operator can define custom domain attributes and sector aspect models without modifying core code.

1. Layer 2 typed schema for events and sessions:
   - `EventIslamicAspect` (1:1 with `Event` via shared PK `Id`)
   - `EventTechAspect` (1:1 with `Event` via shared PK `Id`)
   - `EventSessionIslamicAspect` (1:1 with `EventSession` via shared key `EventSessionId`)
2. Module governance entities:
   - `ModuleDefinition` (global catalog)
   - `TenantCapability` (tenant-level enable/disable state)
3. Layer 3 governed custom-property entities:
   - `CustomPropertyDefinition`, `CustomPropertyOption`, `CustomPropertyValue` for shared Organization / Group extension catalogs
   - `EventTemplate`, `EventTemplateCustomPropertyDefinition`, `EventTemplateCustomPropertyOption` for versioned event blueprints
   - `EventCustomPropertyDefinition`, `EventCustomPropertyOption`, `EventCustomPropertyValue` for event-local runtime extension state
   - `EventCustomPropertyProjection` for derived read/query optimization
   - planned next: session template/runtime/projection families mirroring the event-local Layer 3 architecture

## Module Keys And Defaults

Seeded module keys:
- `Mod_Core`
- `Mod_Islamic`
- `Mod_Tech`

Module definitions are seeded by `LookupTableSeeder`; tenant capabilities are seeded in `DatabaseSeeder` (development data path).

## Runtime Module Operations

`ModuleController` exposes:
- `GET /api/module/available`
- `GET /api/module/enabled`
- `GET /api/module/{moduleKey}/enabled`
- `GET /api/module/{moduleKey}/schema`
- `POST /api/module/{moduleKey}/enable`
- `POST /api/module/{moduleKey}/disable`

Important behavior:
- disabling a module marks capability `IsEnabled=false`; data rows are preserved.
- `ModuleService` caches module lookups for 5 minutes (`Modules_All`, `Modules_Tenant_{tenantId}`).

## How Modules Affect Event Queries

`GetEventListRequestHandler` applies aspect filters only when the related module is enabled for the current tenant:
- Islamic filters require `Mod_Islamic`.
- Tech filters require `Mod_Tech`.

If module-specific filters are sent while the module is disabled, they are ignored (endpoint still succeeds).

## UI-Facing Module Flags

`PublicExperienceSettingsDto` includes:
- `IsIslamicModuleEnabled`
- `IsTechModuleEnabled`
- `EnabledModules`

Blazor event list uses these flags to control filter/UI exposure.

## Current Limits (Non-Obvious)

1. Strategy infrastructure exists (`IEventStrategy`, `StrategyResolver`), but event create/update handlers do not currently call it.
2. `TechEventStrategy.IsApplicable` currently returns `false`, so strategy-based tech logic is not active in create flow.
3. `WizardSchemaUrl` and `/api/module/{moduleKey}/schema` exist, but there is no full schema-driven dynamic form pipeline wired in the current Blazor app.
4. Modules are compile-time application modules, not runtime plugin loading.
5. Layer 3 custom properties are now modeled strongly enough that they need explicit governance: reserved namespaces, collision rules, projection lifecycle, and provenance/version support are part of the architecture.

## Layer Boundary

The platform uses three layers for event and session semantics:

1. Layer 1 universal core fields on `Event` and `EventSession`.
2. Layer 2 typed sector schema (`EventIslamicAspect`, `EventTechAspect`, `EventSessionIslamicAspect`).
3. Layer 3 custom properties for local long-tail extension at event scope or session scope.

Layer 3 must not redefine Layer 2 meaning. If a field is standard across a sector or is required for filtering, moderation, policy, ranking, publication, or export, it must not live only in Layer 3.

Parent/child rule:

- `Event` stays the program/container aggregate
- `EventSession` stays the scheduled child aggregate
- merged event-with-sessions views are read models, not authoritative write models

## Projection Lifecycle

`EventCustomPropertyProjection` and future `EventSessionCustomPropertyProjection` rows exist only as derived read models.

- source of truth remains event-local and session-local custom-property rows plus Layer 1/Layer 2 typed schema
- projection rows are atomic per projected value, not one merged row per property
- only projection-relevant properties are copied into projection rows
- projection rows are rebuildable and invalidated when source definitions, flags, options, or values change
- projections optimize discovery/filter/export/moderation reads; they do not replace typed policy truth

Aggregate event-with-sessions views may embed session summaries and selected session projections for UX/discovery.

## Related

- [MODULAR_EVENTS.md](MODULAR_EVENTS.md)
- [CUSTOM_PROPERTIES.md](CUSTOM_PROPERTIES.md)
- [DOMAIN.md](DOMAIN.md)
- [MULTI_TENANCY.md](MULTI_TENANCY.md)

## Translation Management System (TMS) Provider Abstraction

**Status:** Implemented.

The localization system uses the same pluggable provider pattern as analytics:

```
ITranslationManagementProvider
  ├── TolgeeTranslationProvider
  ├── WeblateTranslationProvider
  ├── OfflineTranslationProvider (default — reads bundled .json files)
  └── NullTranslationProvider (safe no-op)
```

`RuntimeTranslationProvider` wraps all concrete providers and delegates based on the `localization.tms_provider` governance setting. Falls back to `OfflineTranslationProvider` on errors.

Adding a new TMS provider:
1. Create `{Provider}TranslationProvider : ITranslationManagementProvider` in `Explore.Infrastructure/Localization/`
2. Add enum value to `TranslationManagementProviderEnum`
3. Register in `InfrastructureServicesRegistration.cs` with named HttpClient
4. Add routing case in `RuntimeTranslationProvider.ResolveProviderAsync()`

See [LOCALIZATION.md](LOCALIZATION.md) for full details.

## API Keys / Service Accounts — Planned

**Status:** Not yet implemented. Strategy documented for post-v1.0.

**Current auth model:** User-centric via OIDC (Keycloak) through BFF pattern. All API access requires a user session.

**Why machine-to-machine (M2M) is needed:**
- Enterprise consumers integrating event data into CRM, LMS, or campus systems.
- Automated event publishing from CI/CD or content pipelines.
- Partner organizations syncing events bidirectionally.

**Planned entities:**
- `ApiKey`: id (uuid v7), tenant_id, name, hashed_key (sha256), prefix (first 8 chars for identification), scopes (jsonb — list of permitted operations), expires_at, created_by, is_revoked, last_used_at.
- `ApiKeyAuditLog`: id, api_key_id, action, ip_address, timestamp.

**Design decisions:**
- Keys are tenant-scoped (no cross-tenant API keys).
- Scopes map to existing permission codes (read:events, write:events, etc.).
- Keys are hashed at rest (only shown once on creation).
- Rate limiting per key via middleware.
- Admin UI for key management (create, revoke, rotate, view usage).

## Tenant Quotas / Usage Tracking — Planned

**Status:** Not yet implemented. Strategy documented for post-v1.0.

**Why quotas matter for multi-tenant SaaS:**
- Prevent noisy-neighbor resource exhaustion.
- Enable tiered pricing (free/pro/enterprise).
- Provide usage visibility for tenant admins.

**Planned entities:**
- `TenantQuota`: id, tenant_id, resource_type (enum: events, storage_bytes, members, organizations, api_calls_per_day), max_allowed, current_usage, reset_period (monthly/none).
- `TenantUsageLog`: id, tenant_id, resource_type, delta, recorded_at (for historical tracking and billing).

**Enforcement approach:**
- Command handler decorator checks quotas before write commands.
- Soft limits: warn at 80%, block at 100%.
- Storage quota enforced at upload handler level.
- Usage counters updated via domain events (eventually consistent).
- Admin override capability for emergency situations.
