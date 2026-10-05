---
applyTo: "**/*Tests.cs,**/*Test.cs,**/*.Tests/**/*.cs,**/tests/**/*.cs,**/test/**/*.cs"
---
# Test Instructions — ISLAMU Event

> **Audience:** Contributors | AI Agents (Copilot, Claude, Gemini, Cursor)  
> **Status:** Authoritative  
> **Source Anchors:** [`AGENTS.md`](../../AGENTS.md), [`PROJECTS.md`](../../PROJECTS.md), [`AI_REVIEW.md`](../../AI_REVIEW.md), [`docs/internal/QUICK_REFERENCE.md`](../../docs/internal/QUICK_REFERENCE.md)

Use these rules for all test work across the repository. Keep tests small, deterministic, isolated, and strictly behavior-focused.

---

## 1. Do / Don't (Read First)

### Do
- **Default to TDD when behavior changes**: Author failing specification/invariant tests before implementing production code (Red $\rightarrow$ Green $\rightarrow$ Refactor).
- **Default to TUnit / xUnit** for unit and integration test execution.
- **Default to bUnit** for Blazor component and page tests.
- **Default to AwesomeAssertions** for assertion ergonomics; keep assertions consistent with existing suites.
- **Target verification by default**: Run the specific test class or affected test project defined in [`PROJECTS.md`](../../PROJECTS.md).
- **Follow the 3-Ring Progressive Verification Model**:
  - **Ring 1 (< 2s)**: In-memory TUnit slicing (`--treenode-filter "/*/*/*<TestClassName>/*"`). Pure domain/validation logic belongs in `Event.Domain.UnitTests`.
  - **Ring 2 (< 15s)**: Single modified project test with Release configuration.
  - **Ring 3**: Multi-database matrix and architecture tests run strictly at plan exit.
- **Test observable behavior through public seams**: native CQS operations, HTTP routes, ProblemDetails RFC 7807 payloads, and aggregate root operations.
- **bUnit: Re-query DOM elements after interactions**: State changes re-render DOM nodes; always re-query elements after interactions to avoid stale references.
- **bUnit: Use async interaction helpers**: Prefer `ClickAsync`, `InputAsync`, `ChangeAsync`, and `BlurAsync`.
- **bUnit: Use `InvokeAsync()`**: Wrap parameter changes and component method calls in `InvokeAsync()`.
- **Use `TimeProvider` / `FakeTimeProvider`**: Avoid `Task.Delay` and fragile timing; use deterministic, time-controlled assertions.
- **Enforce the Yak-Shaving Quarantine Rule**: If a test failure reproduces on clean `develop` outside your task scope, log it in `*-context.md` / `dev/backlog/` and quarantine it; never derail feature work to fix pre-existing suite rot.
- **Ensure test names describe the behavior**: State the condition and expected outcome clearly.

### Don't
- **Do not suppress warnings to get a test or build to pass**: Never use `#pragma warning disable` or `[SuppressMessage]`. Fix the underlying issue.
- **Do not end test names with `Test` or `Async`**.
- **Do not include `Test_` in test names** and **do not end test names with `_`**.
- **Do not mock internal domain entities, aggregate roots, repositories, or native CQS handlers**: Mock ONLY external third-party infrastructure (payment gateways, external email delivery, system clock).
- **Do not author tautological assertions**: Never recompute expected values using the same formula as production code (`Assert.Equal(items.Sum(x => x.Price), result.Total)`). Expected values must come from an independent, known-good specification or constant.
- **Do not bypass public interfaces**: Do not verify a command's outcome by querying the raw database table if the domain aggregate or query handler exposes that state.
- **Do not commit secrets, tokens, passwords, or real connection strings in test fixtures or sample data**: Bind secrets dynamically via environment variables or mock secret providers (`ISecretResolver`).
- **Do not test third-party library or framework internals directly**.

---

## 2. Test Project Organization

Tests are organized strictly by architectural layer and purpose (see [`PROJECTS.md`](../../PROJECTS.md)):
- `tests/Event.Domain.UnitTests/` — Pure domain invariants, entities, value objects (< 50ms, zero Docker).
- `tests/Event.Application.UnitTests/` — CQRS handlers, command responses, validator rules, mapping (< 2s).
- `tests/Explore.Blazor.Client.Tests/` — bUnit component tests, parameter updates, HAL affordance gating.
- `tests/Event.API.IntegrationTests/` — HTTP endpoints, RFC 7807 error responses, auth, rate limiting.
- `tests/Event.Persistence.IntegrationTests/` — EF Core multi-provider queries, tenant RLS, concurrency.
- `tests/Event.Architecture.Tests/` — Architectural rules and Clean Architecture boundary enforcement.

---

## 3. Regression Testing Protocol

For every bug or regression fix:
1. **Red Test First**: Author a failing test first that reproduces the exact defect scenario before editing production code.
2. **Targeted Assertion**: Keep at least one targeted assertion explicitly tied to the reported symptom.
3. **Preserve Nearby Checks**: Retain existing behavior checks to ensure no collateral regression occurs.
4. **Issue Traceability**: Reference the issue or ticket slug in the test documentation or method summary.

---

## 4. Verification Commands

Refer to [`PROJECTS.md#targeted-verification-commands`](../../PROJECTS.md#targeted-verification-commands) for the exact, copy-pasteable verification commands for each test project.
