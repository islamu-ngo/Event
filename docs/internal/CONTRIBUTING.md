ABOUTME: Contribution workflow covering prerequisites, validation, code standards, and PR process.
ABOUTME: Includes architecture test requirements, CSS rules, DTO sync flow, and release checklist.

# Contributing

> **Audience:** Contributors | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-07-03
> **Source Anchors:** `docs/FIRST_CONTRIBUTION.md`, `docs/TESTING.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/ISSUE_TEMPLATE/`, `.agents/CONTEXT_ENGINEERING.md`

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 10.0+ (pinned by `global.json`) | Build and test |
| Docker Desktop | Latest | Infrastructure services |
| .NET Aspire CLI/workload | Latest | Service orchestration |

See [GETTING_STARTED.md](GETTING_STARTED.md) for complete setup instructions.

Install Aspire CLI before using `aspire run`:

```bash
curl -sSL https://aspire.dev/install.sh | bash
```

## Starting Point

For high-level expectations, project scope, discussion requirements, and the AI contribution policy, see the repository root [CONTRIBUTING.md](../../CONTRIBUTING.md).

If this is your first contribution, start with [FIRST_CONTRIBUTION.md](FIRST_CONTRIBUTION.md). It gives the shortest safe path for docs-only and small-bug PRs without duplicating this full workflow.

For code changes, first prove the local stack can start from a clean checkout:

```bash
cp .env.example .env
aspire run --apphost Explore.AppHost/Explore.AppHost.csproj
```

This starts the full local Aspire topology by default. Contributors should use this path unless they intentionally need the Docker-only stack or a maintainer-specific external-infrastructure profile.

Use the GitHub templates to keep requests and reviews actionable:

| Template | Use For |
|---|---|
| [Bug report](../../.github/ISSUE_TEMPLATE/01_BUG_REPORT.yaml) | Reproducible defects, validation gaps, and regressions. |
| [Feature request](https://github.com/islamu-ngo/Event/discussions/categories/feature-requests) | New behavior with explicit problem, proposal, and non-goals (via GitHub Discussions). |
| [Documentation issue](../../.github/ISSUE_TEMPLATE/documentation.yml) | Stale, missing, confusing, or incorrect docs with source anchors. |
| [AI agent contract](../../AGENTS.md) | Work packages that need context, scoped files, validation, and handoff expectations. |
| [Pull request template](../../.github/PULL_REQUEST_TEMPLATE.md) | PR summary, changes, AI disclosure, validation evidence, release notes, and contributor agreement. |

### How to Contribute

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/foobar`)
3. Commit your changes (`git commit -m "Add feature"`)
4. Push to the branch (`git push origin feature/foobar`)
5. Create a new Pull Request

Before opening a PR:
- build the solution in Release mode
- run the affected test projects individually
- update docs when behavior, configuration, or operations change


## Contributor Legal Status
 
Every non-bot contributor must sign the [ISLAMU Event Contributor License Agreement](../../legal/CLA.md) by posting the exact CLA signature comment on the pull request. The CLA gives the ISLAMU project steward broad inbound rights to maintain, provide, and relicense ISLAMU Event under alternative terms when sustainability, enterprise internal-use on-premises compliance, nonprofit, humanitarian, public-sector, or procurement-restricted needs require it.

**Anti-SaaS Governance Invariant:** The Project Steward is bound never to license ISLAMU Event under terms that allow a third-party closed-source SaaS. Any entity operating a public SaaS must do so under `AGPL-3.0-or-later`, preserving universal community parity. See [I-VSD Strategy Review](../../islamic-value-sensitive-design/i-vsd-licensing-and-commercial-strategy.md).

The `Contributor License Agreement` workflow records v1.0 signatures in `signatures/v1.0/cla.json` on the dedicated `cla-signatures` branch. A pre-flight script short-circuits the full action when all commit authors are already signed or allowlisted. It uses `pull_request_target` and `issue_comment` metadata only; it must not checkout, build, test, cache, restore packages, or execute pull-request head code.

## Branch And Commit

### Branch Prefixes

| Prefix | Usage |
|--------|-------|
| `feat/` | New features |
| `fix/` | Bug fixes |
| `refactor/` | Code restructuring without behavior change |
| `docs/` | Documentation only |
| `test/` | Test additions or fixes |
| `chore/` | Build, CI, dependency updates |

### Commit Messages

Use conventional commit format. The release engine treats commit text as
untrusted input and validates it against `eng/release/policy/release-policy.yaml`
and `eng/release/policy/scope-registry.yaml`:

```
type(scope): description

feat(registration): let attendees correct registration details
fix(events): keep draft events private until organizers publish them
ci(release): verify promoted release tooling
docs(documentation): clarify operator release checks
```

Use public product scopes such as `events`, `registration`, `ticketing`,
`discovery`, `notifications`, `privacy`, `access`, `storage`, `onboarding`,
`federation`, `webhooks`, `localization`, `accessibility`, and `self-hosting`
for release-visible outcomes. Use engineering scopes such as `ci`,
`dependencies`, `architecture`, `database`, `observability`, `documentation`,
`release`, `testing`, and `build` for valid internal commits that are omitted
from public notes by default.

Breaking commits must include both the `!` marker and a non-empty
`BREAKING CHANGE:` footer. Nonbreaking commits may be omitted explicitly only
with both `Changelog: skip` and a non-empty `Changelog-Reason:`; breaking
changes cannot be skipped.

High-impact, breaking, migration, security, and deterministically grouped work also
needs a governed change fragment under `docs/internal/releases/changes/<change-id>.yaml` and a
matching `Change-Id:` footer. A backport records the original commit in that fragment's
`Backport-Of` field as a full object ID.

Canonical release notes deliberately carry **no** author or committer identity, email,
raw commit body, or provider handle. Release artifacts are signed, mirrored, and
permanent, so contributor recognition is kept out of them on purpose; see
[i-vsd-release-governance.md](../../islamic-value-sensitive-design/i-vsd-release-governance.md)
for why that trade was made.

The release preparation commit is:

```text
chore(release): prepare v1.1.0

Changelog: skip
Changelog-Reason: release metadata commit
```

## Required Validation Before PR

Every PR must pass this checklist. Run from solution root:

### Step 1: Build

```bash
dotnet build --configuration Release --verbosity quiet
```

### Step 2: Run All Test Projects

Run each standard project individually — never use solution-level `dotnet test`:

```bash
dotnet test --project Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet
dotnet test --project Explore.Secrets.UnitTests/Explore.Secrets.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category!=Runtime]" --minimum-expected-tests 1
dotnet test --project Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet test --project Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet test --project Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet test --project Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --verbosity quiet
```

When a change touches SMTP, EmailDispatch, or optional RabbitMQ transport, also run the focused runtime category that matches the change:

```bash
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=Email]" --minimum-expected-tests 1
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=RabbitMQ]" --minimum-expected-tests 1
```

### Step 3: Verify Architecture Tests

Architecture tests are CI gates — not optional. They enforce:

- **Layer dependencies** — Domain has no upstream references
- **Naming conventions** — handlers, validators, specifications follow suffixes
- **Accessibility** — routable pages have `<h1>`, MainLayout has landmarks
- **Authorization parity** — every `ResourceKinds` constant has a Cerbos policy, a `FallbackAuthorizationService` case, and a JSON schema
- **Descriptor coverage** — every `ResourceDescriptors` kind is a valid `ResourceKinds` constant
- **Schema coverage** — every Cerbos policy YAML references both principal and resource schemas
- **ABOUTME headers** — all C# files start with `ABOUTME:` comments

See [TESTING.md](TESTING.md) for the full list of architecture convention tests.

### Step 4: Record Documentation Impact

Every non-trivial PR must record one documentation impact outcome:

| Outcome | Use When |
|---|---|
| Updated | Behavior, commands, configuration, API contracts, operator flows, or release notes changed and docs were updated in the same PR. |
| Not needed | The change is internal and does not alter documented behavior. |
| Deferred | Docs impact exists but is intentionally split; include owner, follow-up path, and reason. |

## Code Standards

### File Conventions

- Every file starts with a two-line `ABOUTME:` header
- File-scoped namespaces for all new C# files
- No `as any`, `@ts-ignore`, or type error suppression equivalents
- No empty catch blocks
- Comments explain **what/why**, not change history

### Architecture Rules

- Repositories return entities, not DTOs — mapping happens in handlers
- Validators are manually instantiated in handlers (not injected via DI)
- Navigation properties are readonly; writes go through repositories
- Use `Guid` for core aggregates, `int` for lookups
- Commands return `BaseCommandResponse<TId>`
- `GET` endpoints: `[AllowAnonymous]`; write endpoints: `[Authorize]`

See [QUICK_REFERENCE.md](QUICK_REFERENCE.md) for the complete constraint list.

### CSS Layer Rules

When modifying or adding CSS:

1. **Never add bare `.mud-*` selectors** — MudBlazor overrides go only in `mudblazor-overrides.css`
2. **Use design tokens** — `var(--isl-*)` instead of hardcoded values
3. **Respect layer order**: reset → base → tokens → mudblazor-overrides → components → utilities
4. **Scoped CSS** — use `component.razor.css` with BEM naming and `::deep` for child components
5. **No physical direction properties** in scoped CSS — use logical properties (`margin-inline-start` not `margin-left`)

See [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md) for full CSS architecture details.

### Clean Architecture Compliance

Changes must respect layer boundaries:

| Layer | May Reference |
|-------|--------------|
| Domain | Nothing (self-contained) |
| Application | Domain only |
| Persistence | Domain, Application |
| Infrastructure | Domain, Application |
| API | All layers (composition root) |
| Blazor | Own services + generated API client |

Architecture tests enforce these boundaries automatically.

## DTO Change Workflow (API → Blazor Client)

When DTO contracts change, sequence matters to avoid false compile failures.

1. Update DTOs, validators, mappings, and handlers in API/Application layers
2. Build API: `dotnet build --project Explore.API/Explore.API.csproj --configuration Release --verbosity quiet`
3. Confirm the API build refreshed `schemas/openapi_islamu-event.json` through build-time OpenAPI generation
4. Build Blazor client: `dotnet build --project Explore.Blazor.Client/Explore.Blazor.Client.csproj --configuration Release --verbosity quiet`
5. Update Blazor services/components that use the generated client types
6. Rebuild and rerun all tests

### Why This Sequence

The Blazor client auto-generates API types from `schemas/openapi_islamu-event.json` via NSwag. If UI code is changed before client regeneration, you get false compile failures because generated types still reflect the old API contract.

## TDD Workflow

TDD is the default unless explicitly allowed to skip.

1. Write a failing test
2. Run to confirm failure
3. Write minimal code to pass
4. Run tests — all must pass
5. Refactor with tests green

## Pull Request Checklist

Before submitting:

- [ ] Scope is focused and independently testable (target ≤ 4 hours of work)
- [ ] Pull request template is completed with summary, linked context, docs impact, validation, and risk notes
- [ ] Build succeeds: `dotnet build --configuration Release --verbosity quiet`
- [ ] All 9 standard PR test projects pass individually
- [ ] Architecture tests pass (layer deps, naming, accessibility, auth parity)
- [ ] Documentation impact is recorded: `Updated`, `Not needed`, or `Deferred` with reason
- [ ] New C# files have `ABOUTME:` headers
- [ ] New CSS follows layer architecture and uses design tokens
- [ ] API contract changes include docs updates (`docs/API.md`, `docs/API_CHANGELOG.md`)
- [ ] Configuration portability changes regenerate both v1alpha2 JSON Schemas,
      OpenAPI, API inventory, and the NSwag client; the second generation is
      byte-stable and no generated file was hand-edited
- [ ] Operator/release changes update `docs/SELF_HOSTING.md`, `docs/BACKUP_RESTORE_UPGRADE.md`, or `docs/RELEASE_CHECKLIST.md` when applicable
- [ ] Multi-session work records bounded resume and handoff state in its task-owned `*-context.md` following [Context Engineering](../../.agents/CONTEXT_ENGINEERING.md#workstream-and-handoff-state)
- [ ] Breaking changes are explicitly documented
- [ ] No type error suppression (`as any`, `@ts-ignore`)
- [ ] No empty catch blocks or deleted failing tests

## Review Process

1. PR author ensures all checklist items pass
2. Reviewer verifies architecture compliance and test coverage
3. CI runs full build + all test projects
4. Merge requires passing CI and reviewer approval

## Related

- [GETTING_STARTED.md](GETTING_STARTED.md) — setup and first run
- [FIRST_CONTRIBUTION.md](FIRST_CONTRIBUTION.md) — shortest safe path for first-time contributors
- [TESTING.md](TESTING.md) — test framework and project roles
- [DOCUMENTATION_ARCHITECTURE.md](DOCUMENTATION_ARCHITECTURE.md) — docs metadata, source anchors, and docs impact contract
- [ARCHITECTURE.md](ARCHITECTURE.md) — layer boundaries
- [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md) — CSS architecture and component wrappers
- [QUICK_REFERENCE.md](QUICK_REFERENCE.md) — hard constraints
