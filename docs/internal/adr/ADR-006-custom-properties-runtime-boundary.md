ABOUTME: Decision record for keeping Layer 3 custom properties bounded to existing resources.
ABOUTME: Rejects using EAV custom properties as a generic runtime schema engine.

# ADR-006: Custom Properties Runtime Boundary

- **Status:** Accepted
- **Date:** 2026-05
- **Deciders:** Core team

## Context

The custom-property system gives tenants governed extension fields for existing platform resources such as events and event sessions. The implementation now supports namespaced machine identity, typed values, options, projections, template instantiation, explicit sync, governance flags, quotas, tenant isolation, and lifecycle controls.

That capability creates a product boundary risk: EAV can be stretched from “custom fields on known aggregates” into a no-code runtime schema engine with user-defined entity types, arbitrary relationships, uniqueness rules, formulas, workflow policies, and schema migrations. Letting Layer 3 drift into that role would make custom properties a hidden parallel domain model and would undermine the repository’s Layer 1 / Layer 2 / Layer 3 architecture.

## Decision

Keep EAV custom properties bounded to custom fields on existing first-class resources.

Layer 3 may extend existing aggregates with tenant-local fields, but it must not define new authoritative product entities or replace typed domain schema.

### Allowed Layer 3 Use

- Tenant-specific or organizer-specific fields on existing resources.
- Option/text/number/boolean/date/url values validated by governed metadata.
- Search, filter, export, moderation, and analytics participation only when the definition’s exposure ceiling and purpose-specific flags allow it.
- Template-driven instantiation and explicit sync for event and event-session custom fields.
- Governance reporting to identify custom fields that are candidates for promotion.

### Not Allowed In Layer 3

- User-defined entity types.
- Arbitrary relationships between custom objects.
- Reference fields that become authoritative domain relationships.
- Cross-field rules, formulas, computed fields, or workflow/policy logic.
- Custom uniqueness constraints beyond the local `Namespace + Key` identity rules.
- Tenant-authored schema migrations.
- Authorization, moderation, ranking, or policy-critical semantics that bypass typed Layer 1 or Layer 2 models.

### Promotion Rule

If a custom property becomes sector-standard, discovery-critical, policy-critical, moderation-critical, or interoperability-critical, it must be promoted to typed Layer 2 schema or a governed first-class projection. It must not remain only as Layer 3 EAV data.

### Future Runtime Schema Engine

If the product needs user-defined entities, relationships, formulas, or schema migrations, that work requires a separate Runtime Schema Engine plan and architecture decision. It must not mutate the custom-property layer into a generic relational engine.

## Consequences

1. The current custom-property closure plan can finish as a bounded hardening effort rather than an open-ended no-code database platform.
2. Clean Architecture remains stable because Domain entities keep explicit ownership and custom properties stay an extension layer.
3. Query, export, moderation, and analytics pipelines can rely on typed promotion for standard semantics instead of inferring hidden schema from tenant fields.
4. Governance reports become the path for surfacing promotion candidates.
5. Some tenant requests for arbitrary data modeling will be rejected or deferred to a separate product initiative.

## Related

- [CUSTOM_PROPERTIES.md](../CUSTOM_PROPERTIES.md) — Layer 3 governance and lifecycle rules.
- [ARCHITECTURE.md](../ARCHITECTURE.md) — Layer 1 / Layer 2 / Layer 3 architecture.
- [LEXICONS.md](../LEXICONS.md) — authoritative and extension publication contracts.
