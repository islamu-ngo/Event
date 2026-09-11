<!-- ABOUTME: ISLAMU Event-specific planning guardrails for Senior CTO review of implementation plans. -->
<!-- ABOUTME: Encodes repository invariants, implementation-plan expectations, and platform rules that plan feedback must preserve or challenge explicitly. -->
# ISLAMU Event Guardrails For CTO Plan Review

Use these rules when improving implementation plans for ISLAMU Event.

These are planning guardrails. They help review and rewrite `plan.md`, `context.md`, and `tasks.md`. They do not authorize direct code implementation unless the user separately asks for implementation.

## Product Posture

ISLAMU Event is:

- open-source,
- AGPL-3.0,
- self-hostable,
- white-label,
- general-purpose beyond the public Islamic-focused instance,
- capable of single-tenant and multi-tenant operation,
- pre-v1, so breaking changes are acceptable.

CTO interpretation:

- **Backward compatibility in this phase is completely counterproductive**: With 0 users and 0 external adopters, breaking changes are first-class, encouraged, and prioritized to achieve the cleanest architecture.
- Reject adapter layers, legacy route aliases, backward-compatibility shims, and obsolete ratchets.
- Do not keep bad routes, DTOs, workflows, or UI flows just because they exist. Break and replace cleanly.
- Still protect data integrity, tenant isolation, security fail-closed paths, and operator clarity.
- For self-hosters, document configuration resets and upgrade actions clearly.

## Implementation-Plan Baseline

Implementation plans in this repository are expected to follow `.agents/skills/implementation-plan/SKILL.md` and its resources.

That means a serious plan should usually provide:

- `dev/active/[task-name]/[task-name]-plan.md`
- `dev/active/[task-name]/[task-name]-context.md`
- `dev/active/[task-name]/[task-name]-tasks.md`

CTO review implication:

- do not review only the narrative architecture;
- review whether the workstream is resumable and executable by another agent;
- challenge missing current-state evidence, stale context, or vague task breakdown as real planning defects.

## Intake And Adversarial Review Gate

Before approval:

- verify that `plan.md`, `context.md`, and `tasks.md` link the same dated `islamic-value-sensitive-design/workstreams/i-vsd-*.md`;
- block approval when provider-controlled moral risks, evidence limits, mitigations, or scholarly escalation boundaries are missing;
- apply the `grill-me` mindset to rollback safety, tenant boundaries, query-performance thresholds, operator clarity, dependency failures, and edge cases;
- resolve challenges from repository evidence first, then require an explicit decision for every remaining material fork instead of accepting an assumption.

Trigger `robin-neutral` when a plan treats one vendor, library, or architectural pattern as inevitable without examining a credible alternative. Require a technical trade-off matrix and rejected-approach rationale. Keep this steelmanning strictly separate from I-VSD moral/provider analysis.

## Architectural Baseline

The repository uses:

- .NET,
- Clean Architecture,
- CQRS with MediatR,
- PostgreSQL with EF Core,
- Blazor BFF + Blazor client,
- Keycloak for OIDC/OAuth2,
- Cerbos or local fallback authorization,
- HAL/HATEOAS API responses,
- OpenAPI/NSwag client generation,
- Docker Compose and Aspire,
- structured logs and OpenTelemetry.

Planning implications:

- Domain stays pure.
- Application owns business orchestration.
- Persistence implements repositories and EF configuration.
- API is transport/composition boundary.
- Blazor BFF owns server-side auth/session/proxy behavior.
- Blazor client renders UI and calls service layer, not raw generated clients from pages.

## Contribution Contract

Every implementation plan should tell future agents:

1. what kind of change this is,
2. which authoritative docs/internal/rules apply,
3. which files must be read first,
4. which files are expected to change,
5. which tests must run,
6. which docs must update,
7. which checklist applies,
8. what is forbidden without explicit approval.

When rewriting the plan, include these answers directly or make them easy to infer.

## Critical Codebase Rules

Plans must preserve these defaults unless explicitly proposing a rule change:

- repositories return entities, not DTOs;
- mapping happens in handlers;
- validators are manually instantiated in handlers/services, not injected through DI;
- navigation properties are readonly for writes;
- use `Guid` for core aggregates;
- use `int` for lookup IDs;
- use `long` only for large size/cursor fields;
- commands generally return `BaseCommandResponse<TId>`;
- GET endpoints are usually `[AllowAnonymous]`;
- write endpoints are `[Authorize]`;
- privileged operations require policies/roles/resource authorization;
- new C# files need two `ABOUTME:` comments at the top;
- use file-scoped namespaces;
- no empty catch blocks;
- no type suppression shortcuts;
- comments explain what/why, not change history.

## HAL/HATEOAS UI Rule

HAL links are the source of truth for UI action affordances.

Plans must not introduce:

- standalone DTO permission flags such as `CanEdit`,
- client-side role checks for per-resource mutation buttons,
- duplicated authorization logic in Razor components.

Correct planning language:

- API policies decide which links are emitted.
- Blazor components check `_links` through helper methods.
- If a link is missing, the user cannot perform that action.
- Client-side role inspection is acceptable only for broad navigation/menu/route-guard cases, not per-resource affordance decisions.

## BFF Trust-Boundary Rules

Plans touching Blazor BFF must preserve:

- browser never sees bearer tokens;
- auth tokens remain server-side;
- browser uses HttpOnly cookies;
- unsafe BFF endpoints require antiforgery;
- YARP transforms strip untrusted inbound privileged headers;
- setup-secret and tenant forwarding must come from trusted BFF/server-side state;
- outbound BFF `HttpClient` handlers should avoid pooled cookie leakage;
- client route guards are UX only, never security.

Red flag:

- any plan that forwards `X-Setup-Secret`, tenant slug, API keys, or authorization state from raw browser input as trusted downstream state.

## Multi-Tenancy Rules

Plans must preserve fail-closed tenant isolation.

Required considerations:

- tenant resolution is API-authoritative,
- tenant context must be available before tenant-scoped persistence access,
- EF named query filters protect tenant-scoped data,
- `IgnoreQueryFilters()` is dangerous because it can drop tenant filters,
- filter bypass must be named, scoped, and justified,
- background jobs need explicit tenant handling,
- cache keys must include tenant context where tenant-specific,
- single-tenant mode should not accidentally expose multi-tenant controls.

Plan language should distinguish:

- instance administrator,
- tenant administrator,
- organization administrator,
- group administrator,
- standard user.

## Authorization Rules

Plans must include authorization behavior when touching writes, admin flows, settings, tenants, organizations, registrations, API keys, storage, or policies.

Required considerations:

- endpoint-level attributes,
- MediatR authorization behavior,
- `IAuthorizedRequest`,
- `[AuthorizeResource]`,
- `ISecureRequest` where dynamic resource context is needed,
- Cerbos and local provider parity,
- fail-closed behavior,
- 401/403/ProblemDetails API tests.

If Cerbos is selected by configuration, do not silently fall back to a more permissive local path for instance-level failures.

## API and OpenAPI Rules

Plans touching API contracts must address:

- route names,
- stable operation IDs,
- endpoint classification,
- `ProducesResponseType`,
- ProblemDetails,
- HAL response shape,
- OpenAPI export,
- NSwag client regeneration,
- generated client method drift,
- API changelog.

Preferred direction for pre-v1:

- remove duplicate URL-segment aliases from client-facing OpenAPI,
- keep one canonical contract,
- delete obsolete compatibility tests,
- regenerate clients only after contract stabilization.

## Persistence and Data Rules

Plans touching persistence must address:

- EF configuration,
- migrations,
- tenant-scoped indexes,
- uniqueness constraints,
- soft delete behavior,
- concurrency behavior,
- transaction boundaries,
- outbox for async side effects,
- data migration for existing rows,
- rollback/reset notes for self-hosters.

Do not plan external HTTP/email/broker calls inside DB transactions.

For async side effects, prefer transactional outbox and idempotent consumers.

## Testing Rules & Invariant Mandate (Strict Quality Over Quantity)

Implementation plans must enforce **Quality-Over-Quantity Invariant Verification**:
- Slices touching **Core Domain Invariants, Concurrency Races, Money/State Transitions, or Security Boundaries** sequence failing invariant tests (Red Phase) *before* production code.
- Standard feature orchestration, CQRS commands/queries, API endpoints, and UI components implement directly and verify against public contracts.
- **Prohibit Mock-Mirroring & Test Bloat**: Reject unit tests that mock internal dependencies (`NSubstitute.Received(1)` on repositories/caches), framework-testing boilerplate (EF Core cancellation), and raw source/CSS string scrapers.
- **Stryker Mutation Testing**: Stryker threshold gating is disabled during active greenfield development. Do not block implementation approval on Stryker mutation scores.
- Prioritize high-leverage adversarial tests (concurrency races, state machines, row locking, zero-PII log sinks, tenant isolation) over shallow mock-heavy tests.

At each phase end, run one Release build and at most one project-level `dotnet test` command. Do not add per-task checks, test-only phases, E2E/browser projects, Playwright, Chrome DevTools MCP, app startup, Aspire/Docker startup, live-service smoke, or manual runtime walkthroughs.

Repository-mandated projects remain contract requirements, but distribute them across existing phases without repeating commands or creating extra phases solely for verification. Architecture tests remain mandatory when agent context or architecture contracts change.

Testing posture for pre-v1:

- delete obsolete backward-compatibility tests and ratchets when behavior is intentionally removed,
- do not preserve deprecated tests when refactoring,
- do not skip tests permanently; delete obsolete ones outright.

## Documentation Rules

Plans should include docs updates when behavior changes.

Likely docs:

- `docs/internal/API.md`
- `docs/internal/API_CHANGELOG.md`
- `docs/internal/CONFIGURATION.md`
- `docs/internal/SELF_HOSTING.md`
- `docs/internal/OPERATIONS.md`
- `docs/internal/SECURITY_OVERVIEW.md`
- `docs/internal/MULTI_TENANCY.md`
- `docs/internal/BLAZOR.md`
- `docs/internal/TESTING.md`
- `docs/internal/TROUBLESHOOTING.md`

Docs should focus on non-inferable facts:

- fallback order,
- env var names,
- operator choices,
- failure modes,
- defaults,
- upgrade steps,
- what changed and why.

## CTO Rewrite Doctrine

When improving a plan:

- be more decisive,
- remove weak compromise language,
- convert concerns into tasks,
- convert vague tasks into file-level work,
- require tests for every high-risk boundary,
- make self-hosting consequences explicit,
- keep breakage acceptable but controlled,
- do not let “enterprise-grade” become empty wording.
