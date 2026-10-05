# AI_REVIEW.md — Code Change & Pull Request Review Checklist

> **Audience:** Contributors | Reviewers | AI Agents  
> **Status:** Authoritative  
> **Source Anchors:** [`AGENTS.md`](AGENTS.md), [`PROJECTS.md`](PROJECTS.md), [`docs/internal/QUICK_REFERENCE.md`](docs/internal/QUICK_REFERENCE.md), [`docs/internal/GOVERNANCE.md`](docs/internal/GOVERNANCE.md)

Use this checklist before proposing, authoring, or accepting code changes. It provides a structured, high-signal self-review to catch regressions and architectural rot before submission.

---

## 1. Scope & Layer Boundaries

- [ ] **Focused Scope**: Change remains strictly within the requested task; no unrelated refactoring, cosmetic touchups, or opportunistic cleanup in unmodified files.
- [ ] **Protected Paths Untouched**: Migrations (`Explore.Persistence.Migrations.*`), generated clients (`Clients/EventApiClient.g.cs`), and CI scripts (`.ci/**`) were not modified unless explicitly authorized by the task.
- [ ] **Dependency Direction**: Clean Architecture dependencies flow strictly inward: `Explore.Domain` $\leftarrow$ `Explore.Application` $\leftarrow$ `Explore.Persistence` / `Explore.Infrastructure` $\leftarrow$ `Explore.API`.
- [ ] **Blazor Isolation**: `Explore.Blazor.Client` and `Explore.Blazor` do not reference Domain, Application, or Persistence projects; all backend communication flows through generated API client contracts.

---

## 2. Security & Data Protection

- [ ] **Zero Hard-Coded Secrets**: No passwords, API keys, private tokens, certificates, or real connection strings are committed in source code, appsettings, test fixtures, or docs.
- [ ] **Secrets Source of Truth**: All configuration secrets bind dynamically through **Infisical**, environment variables documented in `.env.example`, or User Secrets in Development.
- [ ] **Fail-Closed Authorization**: `GET` endpoints are explicitly `[AllowAnonymous]`; write/mutation endpoints are protected by `[Authorize]`. Provider identity resolves via `PlatformIdentityPrincipalExtensions`.
- [ ] **Tenant Isolation**: Database queries respect global query filters and PostgreSQL Row-Level Security (`FORCE ROW LEVEL SECURITY`). `IgnoreQueryFilters()` is never called without naming specific non-tenant filters (e.g. `SoftDelete`).

---

## 3. Blazor & Presentation Layer

- [ ] **Auto-Property Parameters**: Component parameters (`[Parameter]`) are declared as auto-properties only (`public string Value { get; set; } = string.Empty;`).
- [ ] **No Parameter Mutation via `@ref`**: Component parameters are never set or modified imperatively through component references (`@ref`).
- [ ] **Parameter State Management**: Parameter updates use `ParameterState<T>` where applicable to manage reactive state transitions cleanly.
- [ ] **CSS Isolation & Theming**: Styling uses scoped `.razor.css` files, BEM naming, MudBlazor v9 tokens, and `CssBuilder`. Hard-coded style attributes or colors are prohibited.
- [ ] **HAL-Driven Action Affordances**: Client buttons, menus, and actions (Edit, Delete, Transfer) are gated strictly by checking the presence of HAL `_links` in the received DTO, never by local role/claim checks.
- [ ] **Accessibility (a11y)**: Interactive elements maintain keyboard navigation, semantic headings, proper focus management, and accessible ARIA attributes.

---

## 4. CQRS, Handlers & Persistence

- [ ] **Entity Return Rule**: Repositories return Domain entities only, never DTOs. DTO mapping happens strictly inside native CQS handlers.
- [ ] **Manual Validator Instantiation**: FluentValidation validators are manually instantiated inside handlers/services (`new CreateEventCommandValidator()`), never injected via DI.
- [ ] **RFC 7807 Error Responses**: Handler errors map to ProblemDetails via `CommandFailurePolicy` or `MapCommandResponse`. Raw command objects or custom error payloads are never returned as failure bodies.
- [ ] **Scheduler Primacy**: Periodic tasks use Quartz.NET `IJob` sweeps registered with `AddSweepJob<TJob>`. Hand-rolled `while (!ct.IsCancellationRequested)` loops in `BackgroundService` are banned.
- [ ] **No Generic CRUD Controllers**: Controllers inherit `EventControllerBase` and remain concrete presentation adapters without generic CRUD base classes.

---

## 5. Testing & Verification

- [ ] **Behavioral Coverage**: Focused tests were added or updated for every observable behavior change (RFC 2119 requirements).
- [ ] **Invariant-First Sequencing**: Tests were authored in the Red phase before production code, asserting against pre-agreed public seams (native CQS requests, HTTP routes, aggregate methods).
- [ ] **No Tautological Tests**: Expected assertion values derive from independent literal specifications, not by repeating the production calculation (`items.Sum(x => x.Price)`).
- [ ] **No Internal Mocking**: Internal domain entities, aggregates, repositories, and CQS handlers are NOT mocked. Mock ONLY external infrastructure (payment processors, third-party email, system clock).
- [ ] **bUnit Interaction Best Practices**: Component tests re-query DOM elements after actions, use async helpers (`ClickAsync`, `InputAsync`), and leverage `TimeProvider`/`FakeTimeProvider` rather than fragile `Task.Delay`.
- [ ] **Test Naming Conventions**: Test method names describe the scenario and expected outcome clearly without ending in `Test` or `Async`, and without `Test_` prefixes or trailing underscores.

---

## 6. Code Quality & Compiler Standards

- [ ] **Minimal & Focused Diff**: Diffs are concise, readable, and free of extraneous line shifts or whitespace churn.
- [ ] **No Warning Suppressions**: No compiler warnings or analyzer diagnostics were suppressed via `#pragma warning disable` or `[SuppressMessage]` to force a build to pass. Root causes must be resolved.
- [ ] **XML Documentation**: Public types, interfaces, methods, and properties in Domain and Application layers include clear XML `<summary>` doc comments.
- [ ] **Code Formatting**: Code complies with the repository `.editorconfig` rules (file-scoped namespaces, brace styles, member order).
- [ ] **No Ad-Hoc Scripts**: No ad-hoc Python or Node.js scratch scripts were used or committed. Tooling lives in `eng/` as C# or POSIX Bash.

---

## 7. Verification Evidence Reporting

- [ ] **Commands Run & Exit Codes**: Final summary explicitly records every command run (build, test, format) with exact outcomes.
- [ ] **Checks Not Run with Reasons**: Relevant checks omitted (e.g. Ring 3 persistence matrix for a UI-only change) are noted with technical justifications.
- [ ] **Zero Fictitious Claims**: No test or build is claimed as passed unless physically executed and verified in the session.
