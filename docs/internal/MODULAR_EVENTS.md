ABOUTME: Event modularity model implemented in the current codebase.
ABOUTME: Documents concrete aspect entities, endpoints, filters, and module guard behavior.

# Modular Events

## What Exists Today

The platform supports two optional event aspect families:

1. Islamic aspect (`EventIslamicAspect`)
2. Tech aspect (`EventTechAspect`)

There is also session-level Islamic extension data (`EventSessionIslamicAspect`).

This means the repo already treats `EventSession` as its own typed semantic scope, not just as a disguised peer event.

Each aspect is stored in its own table and linked 1:1 to the base record through a shared key.

## Aspect Fields (High-Signal)

Islamic event aspect includes:
- `MadhabId`
- `ReferencePrayer`
- `PrayerTimeOffset`
- `GenderMode`
- `IncludesQuranRecitation`
- `PrimaryLanguageId`

Tech event aspect includes:
- `GithubRepoUrl`
- `HackathonTrack`
- `SkillLevel`
- `TechStackTags`
- `RequiresLaptop`
- `IsCodingCompetition`
- `MaxTeamSize`
- `PrizePool` / `PrizeCurrencyCode`

Session Islamic aspect includes prayer-relative scheduling fields and ritual flags:
- `StartTimeType`
- `ReferencePrayer`
- `OffsetMinutes`
- `RequiresWudu`
- `RitualRequirementsJson`

## API Surface

Aspect-specific endpoints live under `EventAspectController` (route `api/event`):

- `GET /api/event/{id}/aspects/islamic`
- `GET /api/event/{id}/management-aspects/islamic`
- `POST /api/event/{id}/aspects/islamic`
- `PATCH /api/event/{id}/aspects/islamic`
- `DELETE /api/event/{id}/aspects/islamic`
- `GET /api/event/{id}/aspects/tech`
- `GET /api/event/{id}/management-aspects/tech`
- `POST /api/event/{id}/aspects/tech`
- `PATCH /api/event/{id}/aspects/tech`
- `DELETE /api/event/{id}/aspects/tech`

Aspect updates require authorization; read endpoints are anonymous.

## Layer Boundary

These aspect families are Layer 2 typed schema, not flexible Layer 3 metadata.

- they are first-class relational tables
- they participate directly in filtering and module-guarded query behavior
- they are the correct home for sector-standard semantics

Layer 3 custom properties must not redefine these meanings later through `Namespace + Key` extension fields.

Parent/child aggregate rule:

- `Event` remains the parent program/container aggregate
- `EventSession` remains the scheduled child aggregate
- sessions may appear like first-class items in UI/search, but authoritative persistence stays parent/child

## List Filtering And Module Guards

`GET /api/event` supports aspect filters (Islamic and Tech), but they are guarded by module enablement:

- Islamic filters are applied only when `Mod_Islamic` is enabled.
- Tech filters are applied only when `Mod_Tech` is enabled.
- If disabled, related filters are ignored instead of failing the request.

This keeps one stable query API across tenants with different module sets.

## Module Enablement Path

Module availability is controlled by:

1. `ModuleDefinition` (global module catalog)
2. `TenantCapability` (tenant enable/disable state)
3. `IModuleService` checks at runtime

Current seeded module keys:
- `Mod_Core`
- `Mod_Islamic`
- `Mod_Tech`

## Non-Obvious Reality

1. Aspect data model and API are fully implemented.
2. Strategy abstractions for module-aware business logic exist, but event create/update handlers do not currently invoke `IStrategyResolver`.
3. Tech strategy is not active for create applicability (`TechEventStrategy.IsApplicable` currently returns `false`).
4. Session-level Layer 2 already exists, so the next enterprise-grade step is session-level Layer 3 plus aggregate event-with-sessions read views, not collapsing sessions into peer events.

## Related

- [EXTENSIBILITY.md](EXTENSIBILITY.md)
- [CUSTOM_PROPERTIES.md](CUSTOM_PROPERTIES.md)
- [API.md](API.md)
- [DOMAIN.md](DOMAIN.md)
