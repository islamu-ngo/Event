---
name: tests
description: Apply when editing unit, integration, architecture, or end-to-end test source files.
paths:
  - "**/*Tests/*.cs"
  - "**/*UnitTests/*.cs"
  - "**/*IntegrationTests/*.cs"
  - "**/*.Tests/*.cs"
related_skills:
  - criticality-guardrail
  - implementation-plan
  - senior-cto-feedback
related_docs: [docs/internal/TESTING.md, AGENTS.md, docs/internal/QUICK_REFERENCE.md]
minimum_tests: [Event.Architecture.Tests]
related_intents: [add-get-endpoint, add-write-endpoint, add-cqrs-handler, add-ef-migration, update-repository-query, blazor-component-affordance, bff-auth-bug, openapi-contract-change]
---

<!-- ABOUTME: Apply when editing unit, integration, architecture, or end-to-end test source files. -->
<!-- ABOUTME: Twin copies live at .agents/rules/tests.md and .omo/rules/tests.md; update both paths. -->

# Test Rules

## Applies To
- All test projects and source files (`**/*Tests/*.cs`).

## Path-Specific Constraints
- **Clean-Architecture Sliced Execution & The 3-Ring Progressive Verification Model**:
  - **Ring 1 (Inner Loop, < 2s)**: Subtask iteration runs ONLY fast in-memory TUnit sliced tests (`--treenode-filter "/*/*/*<TestClass>/*"`) in `Event.Domain.UnitTests` or `Event.Application.UnitTests`. Zero Docker containers or network I/O in the inner loop. 90%+ of algorithmic, normalization, validation, and state-machine checks belong in `Event.Domain.UnitTests` (< 50ms).
  - **Ring 2 (Phase Exit Gate, < 15s)**: Intermediate phase exits run Release build + at most ONE selected project test against ONE canonical provider. Intermediate phase runs of multi-database matrices are strictly forbidden.
  - **Ring 3 (Plan Exit Gate)**: The full 5-database provider matrix, migrations, and architecture guardrails run once at workstream exit.
- **The Yak-Shaving Quarantine Rule**: Agents are strictly forbidden from absorbing, debugging, or fixing pre-existing test suite rot or broken fixtures outside the phase-owned scope. If an existing test fails outside the task path, verify if it reproduces on clean base branch, log in `*-context.md` (`Validation Baseline / Pre-Existing Technical Debt`), quarantine it, and proceed with the assigned deliverable.
- **Test-First Invariant Specification**: Author failing contract/invariant tests *before* implementing production code (Red Phase). Never write code first and generate post-hoc tests that merely mirror implementation assumptions ("The Ugly Mirror").
- **High-Leverage Behavioral Assertions**: Assert against public contracts (MediatR requests, HTTP routes, ProblemDetails RFC 7807, database state invariants) rather than private implementation details. Prioritize concurrency races, state transitions, and real DB integration tests over shallow getter/setter mocks.
- **Pre-Agreed Public Seams**: Tests verify behavior through public interfaces (MediatR commands/queries, API endpoints, aggregate root methods), never by reaching into private internals or mocking internal collaborators.
- **No Tautological Assertions**: Assertions must never recompute expected values the same way the production code does (e.g. `Assert.Equal(items.Sum(x => x.Price), result.Total)`). Expected values must come from an independent, known-good source of truth (a literal constant, worked domain scenario, or specification).
- **No Interface Bypassing**: Tests must verify operations through the public contract. Prohibit verifying a command's success by querying the raw database table directly if the aggregate or query handler exposes that state.
- **Mock Boundary Rule**: Mock ONLY external systems (third-party payment, email delivery, system clock, randomness). Never mock internal entities, repositories, or application handlers when test database fixtures or real handlers can be executed.
- **Suite Integrity**: Failing tests must be fixed or investigated; never deleted to bypass failures.
- **Pristine Output**: Test runs must have zero stray warnings, stack traces, or noisy logs.
- **Runtime Realism**: Keep in-process integration tests deterministic; use explicit runtime lanes when real provider infrastructure is the behavior under test.
- **Project Role Balance**: Assertions must live in the project matching the host profile (e.g., Domain logic in `Domain.UnitTests`, not API tests).
- **Invariant Disposition Before Deletion**: Every removed test or baseline cohort must map to a stronger retained public-seam test, a passing semantic replacement, or intentionally removed product behavior. Counts, runtime, coverage, and mutation score are never sufficient deletion evidence.
- **Executable Contracts, Not Source Inventories**: Reflection, runtime endpoint metadata, HTTP/HAL behavior, rendered semantics, and structured machine-consumed schemas are valid assurance seams. Raw C#, Razor, CSS, Markdown prose, class-name inventories, and historical allowlists are not product assurance.
- **No Governance Documents As Test Inputs**: Product tests must not read `AGENTS.md`, `.agents/**`, `docs/internal/**/*.md`, `dev/active/**`, plans, tasks, evidence, journals, or skill prose. Validate genuinely machine-consumed metadata through its production parser or an explicit `eng/` command; generators are tools, never `[Test]` methods.
- **Substitute the Principal, Not the Identity Service**: controller tests must set real claims on `ControllerContext.HttpContext.User` rather than mocking `IUserContext` through the container. Mocking the service means the test never exercises the claim chain it claims to cover.
- **Assert Across a Split Family**: after a controller is partitioned, contract tests that look actions up by name must search the whole family (see `EventFamilyAction`, `WebhookFamilyAction`) rather than one hardcoded class — that is what the assertion always meant.
- **Container Runtime**: Testcontainers-backed tests need a Docker-compatible endpoint. Under Podman, export `DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock`, `TESTCONTAINERS_RYUK_DISABLED=true`, and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE`. Without them the suite reports hundreds of `DockerUnavailableException` failures that look like a mass regression — check the endpoint before the code.
- **Zero Hard-Coded Test Secrets**: Tests must never define hard-coded passwords, tokens, API keys, or connection strings in test files or fixtures. Bind secrets via environment variables or mock secret providers (`ISecretResolver`) using keys documented in `.env.example`.
- **Record Contract Specifications**: Follow the [canonical record-selection policy](../../docs/internal/GOVERNANCE.md#canonical-record-selection-policy). Start Red with consumed equality, one-fact `with` variants, caller-mutation isolation, JSON construction, PATCH omitted/clear/replacement behavior, and trust-boundary attacks. Structured manifests may classify a complete machine-consumed contract surface; they must not pin source text, prose, or a historical debt inventory.

## Must Read
- [docs/internal/QUICK_REFERENCE.md#build-and-test-baseline](../../docs/internal/QUICK_REFERENCE.md#build-and-test-baseline)
- [docs/internal/TESTING.md](../../docs/internal/TESTING.md)

## Verification
- Build: `dotnet build --configuration Release --verbosity quiet`
- Tests: Run the specific test project using `--project` with `--configuration Release`.

## Related
- Intents: `add-get-endpoint`, `add-write-endpoint`, `add-cqrs-handler`, `add-ef-migration`, `update-repository-query`, `blazor-component-affordance`, `bff-auth-bug`, `openapi-contract-change`
- Agents: `quality-verifier-agent.md`
- Rules: `application-layer.md`, `api-controllers.md`, `blazor-client.md`
