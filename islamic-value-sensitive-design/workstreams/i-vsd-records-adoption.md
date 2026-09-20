<!-- ABOUTME: I-VSD technical consultation for adopting C# records across ISLAMU Event contracts. -->
<!-- ABOUTME: Traces immutability, identity authority, API compatibility, privacy, and maintainability duties. -->

# I-VSD Provider-Responsibility Consultation: C# Records Adoption

Last Updated: 2026-08-25

## Scope

This report reviews the provider-controlled architectural decision to adopt C# `record` types for suitable handwritten DTOs, MediatR requests, serialized event payloads, value objects, and presentation snapshots across the ISLAMU Event platform.

The review covers the supplied enterprise architecture report, current repository conventions, representative Application/API/Domain/Blazor contracts, identity and tenant boundaries, System.Text.Json and ASP.NET Core binding constraints, generated NSwag ownership, EF Core exclusions, and relevant test seams. It does not authorize a repository-wide positional-record codemod, change runtime code, or claim that implementation has started.

The requested “no backward compatibility” development posture removes the need for compatibility shims, but it does not remove the provider duty to make each intentional wire-contract or schema break explicit, generate governed artifacts, preserve stored data, document release impact, and keep tenant/authentication behavior fail-closed.

On 2026-08-25 the user approved bringing every previously deferred area into this workstream. The provider-responsibility scope now also covers immutable command-result factories, repository-wide published collection immutability, evidenced money/coordinate/temporal-range values, generated multi-provider EF migrations, and generator-owned NSwag record DTOs.

## Claim Boundary

This report is I-VSD provider-responsibility design reasoning and implementation traceability. It is **not a fatwa, Sharia certification, legal opinion, security certification, product certification, or empirical proof of ethical outcomes**. Record syntax alone does not prove immutability, thread safety, authorization safety, correctness, performance, or maintainability.

## Phase 9 High-Criticality Value Boundary

- `Money` represents normalized currency-qualified integer minor units only. It does not encode interest, financing, exchange, payout, refund, or religious-legal permissibility; those remain separate product/domain and qualified scholarly decisions.
- `GeoCoordinate` is exact location PII owned by `LocationPii` and governed through the `Location` erasure/anti-resurrection authority. Invalid, partial, non-finite, or out-of-range pairs fail closed, while diagnostic formatting redacts exact values.
- Local calendar ranges and UTC instant ranges remain distinct concepts. Local ranges are inclusive dates without timezone conversion; UTC schedule ranges normalize instants and use half-open overlap semantics so adjacent schedules do not conflict.
- Phase 9 keeps scalar persistence seams and avoids providers, PostGIS, migrations, or generated-client changes. Phase 10 owns generated data-preserving persistence migration evidence.
- Phase 10 intentionally retains those scalar leaves as the relational representation. This avoids duplicate currency authority, preserves indexed/queryable time and amount columns, and keeps open-ended schedules representable. Generated provider migrations add only bounded checks for nonnegative ticket money, atomic/bounded coordinates, and ordered local ranges; malformed legacy rows fail installation for explicit correction rather than silent monetary or PII rewriting.

These boundaries strengthen privacy, stewardship, predictability, and maintainability without making a Sharia compliance claim.

## Findings

| ID | Severity | Finding | Principle / domain | Stakeholders | Provider-controlled decision | Evidence | Required mitigation |
|---|---|---|---|---|---|---|---|
| F1 | Critical gate | A blanket conversion to positional records would break valid repository patterns: generated DTO ownership, partial-update absent-versus-null semantics, validation/inheritance-based model binding, mutable UI edit state, and stateful entities. | Trust (`Amanah`), Non-harm (`La Darar`), Excellence (`Ihsan`); Technical, Operational | API consumers, tenant administrators, developers, operators | Whether adoption is semantic classification or a syntax-wide codemod | `PaginatedQueryRequests.cs`; `OptionalUpdateJsonConverterFactory.cs`; `EventApiClient.g.cs`; mutable Blazor edit models; official ASP.NET Core record-binding constraints | Inventory and classify every candidate. Convert only types whose equality, construction, binding, serialization, and mutation semantics fit records. Maintain explicit exclusion categories. |
| F2 | Critical gate | The supplied controller pattern correctly rejects body-owned identity, but its direct `User.GetUserId()` / tenant-claim example is not repository authority. User identity must flow through `PlatformIdentityPrincipalExtensions` / `IUserContext`, and tenant identity through the centrally resolved `ITenantContext`; route identifiers remain route-owned. | Trust, Justice (`Adl`), Rights of People; Technical, Governance | Every tenant and authenticated user | Which boundary is trusted to introduce user, tenant, and resource identity | `docs/QUICK_REFERENCE.md`; `ApiHostServiceCollectionExtensions.cs`; `TrustedAuthorizationFacts.cs`; `EventManagementMcpTools.cs`; API identity tests | Define request-body DTOs as client-owned fields only where route/context authority exists. Bind trusted facts at the established API/MCP/BFF boundary without re-parsing raw claims or trusting client tenant identifiers. Add wrong-tenant, missing-identity, and body-tampering invariant tests first. |
| F3 | High | Record immutability is shallow. Lists, arrays, dictionaries, nested mutable DTOs, and referenced objects can still change after construction, so records do not by themselves create thread-safe cache entries or “zero mutation bugs.” | Truthfulness (`Sidq`), Trust, Excellence; Technical, Evaluation | Maintainers and consumers relying on cache/message stability | Whether architectural claims distinguish syntax from deep immutability | Official C# record documentation; existing collection-bearing commands and DTOs | Use immutable or read-only collection contracts where mutation must be prevented, copy mutable inputs at boundaries when required, and test the actual invariant instead of claiming it from the `record` keyword. |
| F4 | High | Converting API-facing DTOs can alter constructor requirements, validation metadata location, requiredness, OpenAPI schemas, source-generated JSON metadata, and generated NSwag client behavior even when JSON property names appear unchanged. | Promise-keeping, Truthfulness, Non-harm; Technical, Operational | Browser clients, API integrators, self-hosters | Whether conversion is treated as a governed public-contract change | `nswag.json`; `AppJsonSerializerContext.cs`; checked-in OpenAPI schema; `openapi-contract.yml`; official ASP.NET Core and System.Text.Json documentation | Lock request/response JSON and validation behavior with failing tests before each contract slice; regenerate OpenAPI and NSwag artifacts; run drift and serialization tests; record intentional breaking changes. |
| F5 | High | Record value equality changes observable semantics in assertions, dictionaries, sets, deduplication, cache keys, and comparison code. Equality over mutable reference members remains reference-based for those members. | Justice, Truthfulness, Excellence; Technical, Evaluation | Developers, users affected by deduplication or caching | Whether equal-by-value is genuinely part of each type’s domain meaning | Existing mixed class/record contracts; official C# record equality documentation | Require an equality-semantics decision per category. Do not convert merely to shorten declarations. Add focused equality tests only where equality is machine-consumed behavior. |
| F6 | High | Compiler-generated record `ToString()` can expose every positional member. Commands and DTOs may carry personal data, tokens, provider identifiers, reasons, URLs, or other sensitive values that become easier to log accidentally. | Privacy, Avoiding Spying (`Tajassus`), Trust; Technical, Operational | Registrants, actors, organizers, tenants | Which fields can enter logs, traces, snapshots, and exception text | Repository zero-PII telemetry rules; commands and DTOs with identity/provider fields | Classify sensitive contracts before conversion, preserve consumer-routed structured logging, prohibit whole-request interpolation/destructuring, and add zero-PII telemetry checks for security/privacy slices. |
| F7 | High | The mutable `OutboxMessage` is a lifecycle entity and must remain a class. Immutable event payload snapshots can be records only when their schema, versioning, deserialization, privacy, idempotency, and replay behavior are explicit. | Trust, Promise-keeping, Non-harm; Technical, Operational | Event recipients, operators, downstream consumers | Whether outbox transport payloads are confused with outbox persistence state | `OutboxMessage.cs`; outbox factories and processors; existing positional payload records | Exclude outbox entities and processors. Plan payload-record adoption as separate bounded slices with serialization/replay tests and no schema migration unless persistence evidence requires one. |
| F8 | Medium | `readonly record struct` is appropriate only for small, self-contained values with intentional copy/value semantics. The phrase “stack-allocated value equality” is not a sufficient design justification and can mislead about boxing, copying, or where a value is stored. | Truthfulness, Excellence; Technical | Domain maintainers and performance-sensitive consumers | Whether value types are chosen by semantics and evidence | Existing `Explore.Domain/ValueObjects`; official C# record-class versus record-struct guidance | Keep entity and large/reference-rich types as classes or record classes. Introduce record structs only through a separate value-object review with invariant tests and measured performance need where performance is claimed. |
| F9 | High | A big-bang migration across thousands of declarations would make failures hard to diagnose and review, especially across API generation, authorization, mapping, validation, and UI consumers. | Trust, Excellence, Promise-keeping; Operational, Governance | Maintainers, reviewers, downstream consumers | Migration sequencing and rollback granularity | Repository inventory: mixed record/class conventions across Application, API, Domain, and Blazor; governed OpenAPI/client drift workflow | Use reviewable vertical slices: inventory and policy, trusted-boundary pilot, read DTO waves, CQRS waves, payload/value-object waves, then presentation-only candidates. One Release build and at most one fastest relevant non-browser test project per phase. |

## Recommendations

### Decision

Proceed only with a **classification-first, contract-governed adoption standard**, not the report’s blanket positional-record rule.

The default target for a proven immutable handwritten data contract should be `public sealed record`, but the declaration form is selected per contract:

- use a positional record when constructor order, validation metadata, serializer binding, source generation, and call-site ergonomics are all stable;
- use a nominal record with `required` / `init` properties when named construction and serializer/framework compatibility are safer;
- retain a class when identity, lifecycle mutation, inheritance, framework binding, generated ownership, or mutable editing behavior is intentional;
- use `readonly record struct` only for a small domain value with deliberate value/copy semantics.

### Trusted Identity And Tenant Authority

Body DTOs should contain only client-owned fields. This does **not** imply that every command must have the same `UserId` and `TenantId` constructor shape.

For each write boundary:

1. identify route-owned identifiers;
2. resolve user identity through the repository’s single principal authority or `IUserContext`;
3. use centrally resolved `ITenantContext`, not a body field or ad hoc tenant claim;
4. construct or enrich the MediatR request using the existing authorization pattern;
5. deny missing, empty, conflicting, and wrong-tenant facts;
6. preserve `ISecureRequest` authorization facts and ProblemDetails behavior.

### Contract Classification Matrix

| Category | Default | Mandatory review |
|---|---|---|
| Handwritten read/projection DTO | Sealed nominal or positional record | Mapping/projection construction, JSON shape, collection mutability, HAL/ETag behavior |
| HTTP body request DTO | Sealed record only after boundary tests | Body-owned fields, validation metadata, absent/null semantics, OpenAPI/NSwag impact |
| MediatR command/query | Sealed record when intent is immutable | Trusted identity source, `ISecureRequest`, handler/validator construction, logging/PII |
| Serialized outbox/domain-event payload | Sealed record | Schema/version/replay/idempotency/privacy; exclude lifecycle entities |
| Domain value object | Existing class/record or reviewed record struct | Invariants, EF mapping, copy cost, equality meaning |
| Blazor result/filter snapshot | Sealed record when genuinely immutable | Generated-client isolation, rerender/equality effects, collection safety |
| Blazor edit/component state | Class by default | Required mutation, binding, validation, lifecycle |
| EF entity, outbox entity, service, handler, validator, controller | Class | No conversion in this workstream |
| Generated NSwag code | Generator-owned partial record where framework behavior permits | Never hand-edit; prove native support or use an independently designed deterministic repository-owned extension |

### Test-First Migration

Every behavioral slice must first establish failing tests for the public seam it changes. The most important invariant breakers are:

- client-supplied user/tenant IDs cannot override trusted context;
- missing or conflicting identity fails closed;
- JSON request/response shape, nullability, requiredness, and validation errors remain intentional;
- partial updates preserve omitted versus explicit-clear behavior;
- AutoMapper/projection construction remains valid;
- OpenAPI and generated clients are deterministic and synchronized;
- sensitive request values are absent from logs and traces;
- equality changes affect only contracts for which value equality is intended.

## Scope Re-Baseline — 2026-08-25

The expanded plan preserves the original moral boundaries and adds these provider-controlled duties:

1. **Immutable command results:** valid-state factories must replace setter mutation without hiding failure codes, quota facts, validation errors, or RFC 7807 behavior.
2. **Published collections:** read-only typing is insufficient by itself; caller-owned mutable inputs require defensive snapshots, while aggregate/service internal mutation remains private and intentional.
3. **Domain values:** `Money` must retain checked integer minor-unit and normalized currency semantics; `GeoCoordinate` must stay inside the location-PII ownership boundary; local calendar ranges and UTC instant ranges must not be conflated.
4. **Persistence migration:** value-object adoption must preserve existing rows, tenant filters, privacy erasure, nullability, constraints, rollback/reapply, and shipped provider models through generated artifacts only.
5. **Generated client records:** the repository must not copy NSwag/NJsonSchema templates or hand-edit generated declarations. Record output must be produced through public generator capabilities or independently designed repository-owned generation logic, with JSON/HAL/PATCH behavior proven first.

These are engineering/provider-responsibility decisions, not religious-legal rulings. No new scholarly escalation is required; security, privacy, data-loss, and outbound-license gates remain mandatory.

## Common Overlooked Failures And Outcomes

| Overlooked failure | Possible outcome | Responsible outcome |
|---|---|---|
| Positional parameter order changes during maintenance | Call sites compile with semantically swapped same-typed arguments | Prefer nominal records for long/same-typed contracts or group body data into a reviewed request record |
| Validation attributes remain on properties after positional conversion | ASP.NET Core ignores expected validation metadata | Put binding/validation metadata on record constructor parameters and prove error responses |
| `required` / nullability changes OpenAPI | Generated browser clients drift or fail to deserialize | Regenerate schema/client in the same slice and run contract tests |
| A record contains mutable collections | Cached/message data changes after publication | Use read-only/immutable collections or defensive copies where the invariant requires it |
| Whole-record structured logging is introduced | PII, tokens, reasons, or provider data enters telemetry | Log bounded event names and approved scalar fields only |
| An outbox entity is converted with its payload | EF tracking or retry lifecycle breaks | Keep persistence entities as classes; convert only versioned payload snapshots |
| Generated client DTOs are manually converted | Regeneration overwrites changes and CI detects drift | Change source contracts, then regenerate checked-in artifacts |
| Big-bang codemod mixes semantic and mechanical failures | Review becomes non-auditable and rollback unsafe | Deliver vertical slices with explicit candidate manifests and phase-end gates |

## Stakeholders

- Event attendees, actors, organizers, and tenant administrators whose identity and data boundaries must remain correct.
- Browser, mobile, MCP, BFF, and third-party API consumers affected by request/response contracts.
- Self-hosters and operators responsible for upgrades, generated clients, and incident diagnosis.
- Maintainers and reviewers responsible for equality semantics, serialization safety, and sustainable conventions.
- Downstream message consumers affected by serialized event-payload changes.

## I-VSD Principles And Domains

- **Trust / Amanah:** preserve tenant and identity authority, reliable serialization, and reviewable migration evidence.
- **Truthfulness / Sidq:** describe records as shallowly immutable data types, not proof of thread safety or zero mutation.
- **Justice / Adl and Rights of People:** prevent client-controlled identity from crossing tenant or user boundaries.
- **Non-Harm / La Darar:** avoid contract drift, silent validation loss, PII leakage, and undiagnosable big-bang failure.
- **Avoiding Spying / Tajassus:** ensure generated `ToString()` and structured logging do not expand data exposure.
- **Promise-Keeping:** update OpenAPI, generated clients, release fragments, and operational guidance when contracts intentionally break.
- **Excellence / Ihsan:** use test-first semantic classification, clean architecture ownership, and bounded vertical slices.
- **Technical domain:** record form, equality, model binding, serialization, tenant isolation, generated ownership, and EF boundaries.
- **Operational domain:** release sequencing, generated-client drift, rollback, observability, and support burden.
- **Governance/Evaluation domains:** explicit exclusions, candidate manifests, invariant-breaker tests, review gates, and evidence-backed claims.

## Validation Gaps

- Phase 7 closed the complete `BaseCommandResponse<T>` descendant and direct-construction inventory with immutable valid-state factories and exhaustive RFC 7807 mapping evidence.
- Phase 8 classified every published collection-bearing record and removed mutable exposure without claiming deep collection equality.
- Phases 9–10 implemented the approved money, coordinate, local-date, and UTC-instant values, then generated four reversible constraints for all five providers with pre-DDL malformed-row protection.
- Phase 11 proved pinned NSwag has no native record mode and added a repository-owned SDK-Roslyn transform with exact eligibility, inheritance, protocol-input, HAL, file, exception, mutable-state, AOT, and privacy-safe diagnostic ratchets.
- No implementation validation gap remains inside Phases 7–11. Unrelated shared-worktree gates remain classified in the active workstream context.

## Escalation Needed

- Security review is mandatory for slices that change user identity, tenant context, `ISecureRequest`, authorization facts, or failure behavior.
- Privacy review is mandatory for contracts carrying PII, tokens, provider identifiers, free-form text, or sensitive URLs.
- API-owner review is mandatory for intentional OpenAPI or generated-client breaking changes.
- No religious-legal question is raised by record syntax itself. Any later request for a halal/haram or Sharia-compliance ruling is outside this report and must go to qualified scholarly authority.

## Evidence Reviewed

- User-provided “ISLAMU Event Platform: Enterprise Architecture Report.”
- `AGENTS.md` and `docs/QUICK_REFERENCE.md`.
- `.agents/contract/intents.yaml` — `openapi-contract-change` and clean-room research constraints.
- `.agents/rules/application-layer.md`, `api-controllers.md`, `domain.md`, `blazor-client.md`, `tests.md`, `auth-trust-boundaries.md`, and `work-criticality-matrix.md`.
- `docs/API.md`, `docs/DUAL_VERSIONING.md`, and `docs/legal/IP_GOVERNANCE.md`.
- `src/Explore.Application/DTOs/Category/CategoryDto.cs`.
- `src/Explore.Application/DTOs/Category/CreateCategoryDto.cs`.
- `src/Explore.Application/DTOs/Category/UpdateCategoryDto.cs`.
- `src/Explore.Application/Responses/BaseCommandResponse.cs`.
- `src/Explore.API/ExceptionHandling/CommandResponseResultMapper.cs`.
- `src/Explore.Application/Responses/PaginatedResult.cs`.
- `src/Explore.Application/Features/Promotions/Requests/Commands/PromotionManagementCommands.cs`.
- `src/Explore.Application/Features/RegistrationProviders/RegistrationProviderManagementRequests.cs`.
- `src/Explore.Application/Authorization/TrustedAuthorizationFacts.cs`.
- `src/Explore.Domain/OutboxMessage.cs` and representative outbox factories.
- `src/Explore.Domain/LocationPii.cs`, `EventTicketType.cs`, `PaymentAttempt.cs`, `Event.cs`, `EventSeries.cs`, `EventSession.cs`, and `EventAgendaItem.cs`.
- Representative EF entity configurations, application/provider migration snapshots, and migration integration tests.
- `src/Explore.API/Serialization/OptionalUpdateJsonConverterFactory.cs`.
- `src/Explore.API/Hosting/ApiHostServiceCollectionExtensions.cs`.
- `src/Explore.Blazor.Client/nswag.json`.
- `src/Explore.Blazor.Client/Explore.Blazor.Client.csproj` generation target and pinned NSwag tool manifest.
- `src/Explore.Blazor.Client/Clients/EventApiClient.g.cs` ownership and generated shape.
- `src/Explore.Blazor.Client/Serialization/AppJsonSerializerContext.cs`.
- `eng/tools/Explore.GeneratedContracts/**` and its direct synthetic policy tests.
- `dev/active/records-adoption/phase11-clean-room-provenance.md`.
- `.github/workflows/openapi-contract.yml` and `schemas/openapi_islamu-event.json`.
- Representative API identity, OpenAPI, Application, Domain, and Blazor serialization tests identified during repository exploration.
- Microsoft Learn: C# record types, immutable System.Text.Json types, ASP.NET Core constructor/record model binding, and EF Core entity constructors.

## Missing Evidence

- AnySearch MCP and Context7 MCP were requested but are not registered in this session.
- The configured general web-search provider failed; official documentation was fetched directly from Microsoft Learn URLs.
- No external issue tracker, roadmap, customer-support system, or production incident evidence was needed or reviewed for this internal refactor plan.
- No external implementation source, template, snippet, AST, migration, test, comment, or asset informed Phase 11. The clean-room disposition and independent SSO/dependency review are recorded in the linked provenance artifact.

## Context Inventory

- Repository/workspace documentation, source, configuration, tests, generated OpenAPI/NSwag artifacts, and existing I-VSD reports were available.
- Five bounded read-only repository scouts across the original and expanded planning passes inventoried type categories, immutable result/collection seams, Domain/Persistence candidates, generated-client ownership, and tests.
- Official Microsoft Learn documentation was reviewed through direct URL retrieval under the repository clean-room policy.
- No relevant external project-context MCP, AnySearch, Context7, or hosted planning integration was visible.
