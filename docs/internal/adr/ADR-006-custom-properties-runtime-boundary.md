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

## Bounded Pattern and Candidate Contract

`Explore.Domain.CustomProperties.CustomPropertyPatternPolicy` owns definition
syntax admission and value matching. Patterns have a maximum length of 2,048
UTF-16 code units and use .NET `RegexOptions.NonBacktracking` with a 100 ms
match timeout, without a compiled per-definition cache. Malformed syntax,
unsupported constructs (including backreferences and lookarounds), excessive
pattern length and match timeout fail closed. Null or empty patterns remove the
pattern constraint; whitespace is a literal pattern, not an absent constraint.
The non-backtracking engine's own supported syntax/complexity limits also apply.

The general, event and session create DTO validators invoke this policy. They
reject required `Options = null` and null option items before any count,
default-option selection or entity construction can dereference those items.
Create omission retains the DTO's empty collection: it is legal for non-option
definitions, while option definitions require at least one valid option.
Length bounds must be nonnegative and ordered; text defaults must meet length
and pattern constraints, and number/date defaults must meet their bounds.

All three update command handlers reconstruct a detached complete create DTO
from stored state and the presence-aware patch, then run the same create
validator before mutating the tracked definition. This preserves tenant
authorization, `ExpectedConcurrencyStamp`, governance, quotas and
repository-owned option identity/reference lifecycle. Invalid candidates leave
description, bounds, pattern, option relationships and concurrency stamp intact.
An invalid historical pattern must be corrected or deliberately cleared in that
candidate, even when another metadata field is the intended edit.

The wire representation of nullable patch fields is `OptionalUpdate<T>`, not
a scalar replacement. Omission, a bare JSON null, or `hasValue: false` preserves
the saved field. `{"hasValue":true,"value":null}` clears a nullable field,
`{"hasValue":true,"value":""}` saves an empty string, and the same envelope with
a nonempty value replaces it. Description clearing remains legal. Supplied
`options` requires non-null `items`; an empty array is validated against the
merged property type, and null entries are rejected rather than coerced.
The existing PATCH route requires `If-Match` with the current definition stamp.

`CustomPropertyRuntimeValueValidator` is shared by both event/session single
and multi-value command handlers. It checks trimmed input length before matching
through the Domain policy; an invalid historical pattern returns validation
instead of throwing or using the backtracking engine. Typed shape, active-option
membership, ordinal, duplicate-value and parent-scope checks remain in place.
Pattern/value content and parsed URL schemes are not reflected in validation
messages. HTTP mapping retains `validation_failed` (400) and the owning
definition/value error key; there is no new pattern-specific HTTP code.

Source regressions live in `CustomPropertyCandidateValidationTests` and the
`PatternCandidates` partials of `CustomPropertyDefinitionPrerequisiteTests`,
`EventCustomPropertyNativeTests` and `SessionCustomPropertyNativeTests`.
They exercise real JSON, actual authorized native operations, unchanged tracked
state after rejection, and fresh repository reloads from each existing SQLite
HTTP fixture for legal clears and rejected mutations. Event/session tests also
cover all four value commands with supported and historical unsafe patterns.
These sources are authored but execution is deferred until the entire security
programme implementation is complete; they are not passing-test evidence.
Generated client PATCH/If-Match companions must be regenerated and exercised in
that final assurance gate, never hand-edited.

## Related

- [CUSTOM_PROPERTIES.md](../CUSTOM_PROPERTIES.md) — Layer 3 governance and lifecycle rules.
- [ARCHITECTURE.md](../ARCHITECTURE.md) — Layer 1 / Layer 2 / Layer 3 architecture.
- [LEXICONS.md](../LEXICONS.md) — authoritative and extension publication contracts.
