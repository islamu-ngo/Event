ABOUTME: Defines the repository documentation architecture, ownership model, and quality gates.
ABOUTME: Keeps docs source-grounded, audience-oriented, and safe to evolve without a hosted docs site.

# Documentation Architecture

> **Audience:** Contributors | Operators | Admins | Integrators | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-09-02
> **Source Anchors:** `README.md`, `docs/index.md`, `docs/DOCUMENTATION_STYLE_GUIDE.md`

This repository uses Markdown-first documentation as the canonical operator, contributor, and agent knowledge base, complemented by our [official hosted public documentation portal](https://islamu.gitbook.io/islamu-event).

## Intent Model

Use Diátaxis intent categories to prevent one page from becoming a mixed manual:

| Intent | Reader Question | Use For | Example Docs |
|---|---|---|---|
| Tutorial | How do I learn the path once? | Guided first-run or first-contribution flows | `GETTING_STARTED.md`, `FIRST_CONTRIBUTION.md`, `DEVELOPER_GUIDE.md` |
| How-to | How do I complete this task safely? | Developer blueprints, operator/admin procedures and runbooks | `CONTRIBUTOR_RECIPES.md`, `SELF_HOSTING.md`, `BACKUP_RESTORE_UPGRADE.md` |
| Reference | What are the exact keys/contracts? | Stable facts, settings, APIs, commands | `CONFIGURATION.md`, `SECRETS.md`, `API.md` |
| Explanation | Why is the system designed this way? | Architecture, request flows, tradeoffs, governance | `ARCHITECTURE_OVERVIEW.md`, `REQUEST_FLOWS.md`, `ARCHITECTURE.md`, ADRs, `GOVERNANCE.md` |

If a page needs two intents, split the task steps from the reference table and link between them.

## Audience Paths

Use `README.md` as the public entry point. It should route new readers by task and audience without forcing them into the full documentation inventory. Use `docs/index.md` as the complete documentation map for readers who already know their task or need a specific reference.

| Audience | Start Here | Then Read |
|---|---|---|
| Evaluators | `README.md`, `PROJECT.md` | `ARCHITECTURE_OVERVIEW.md`, `SECURITY-MODEL.md`, `SELF_HOSTING.md` |
| Local developers | `GETTING_STARTED.md` | `DEVELOPER_GUIDE.md`, `TESTING.md`, `TROUBLESHOOTING.md` |
| Operators | `SELF_HOSTING.md` | `CONFIGURATION.md`, `SECRETS.md`, `OPERATIONS.md`, `BACKUP_RESTORE_UPGRADE.md`, `RELEASE_CHECKLIST.md` |
| Instance and tenant admins | `ADMIN_GUIDE.md` | `ADMIN_HIERARCHY.md`, `DEPLOYMENT_MODES.md`, `AUTHORIZATION_PATTERNS.md`, product feature docs |
| Integrators | `API_COOKBOOK.md` | `API.md`, `API_CHANGELOG.md`, `SECURITY-MODEL.md`, `CONFIGURATION.md` |
| Contributors | `DEVELOPER_GUIDE.md` | `ARCHITECTURE_OVERVIEW.md`, `REQUEST_FLOWS.md`, `CONTRIBUTOR_RECIPES.md`, `CONTRIBUTING.md`, `QUICK_REFERENCE.md` |
| AI agents | `AGENTS.md` | `.agents/contract/README.md`, `.agents/contract/intents.yaml`, `dev/_journal/README.md` |

## Canonical Docs And Owners

Each canonical doc has an owner category. Ownership means the category is responsible for accuracy, not that only that team may edit it.

| Owner | Canonical Docs |
|---|---|
| Platform/Ops | `SELF_HOSTING.md`, `BACKUP_RESTORE_UPGRADE.md`, `OPERATIONS.md`, `CONFIGURATION.md`, `RELEASE_CHECKLIST.md` |
| Security | `SECURITY_OVERVIEW.md`, `SECURITY-MODEL.md`, `SECRETS.md`, `AUTHORIZATION_PATTERNS.md`, `DEPLOYMENT_TIERS.md` |
| API | `API.md`, `API_CHANGELOG.md`, OpenAPI/client-generation guidance in `GOVERNANCE.md` |
| Frontend | `BLAZOR.md`, `DESIGN_SYSTEM.md`, `ACCESSIBILITY.md`, `RENDER_POLICIES.md` |
| Product/Admin | `ADMIN_HIERARCHY.md`, `ADMISSION_AND_REGISTRATION.md`, feature/admin workflow docs |
| Contributor Experience | `DEVELOPER_GUIDE.md`, `ARCHITECTURE_OVERVIEW.md`, `REQUEST_FLOWS.md`, `RECORD_CONTRACTS.md`, `CONTRIBUTOR_RECIPES.md`, `CONTRIBUTING.md`, `TESTING.md`, `DOCUMENTATION_STYLE_GUIDE.md`, this document |
| Agent Context | `AGENTS.md`, `AGENTIC_CONTEXT_ENGINEERING.md`, `.agents/contract/`, `.agents/skills/`, `dev/_journal/` |

## Metadata Policy

New canonical docs and operator-critical docs must include this block immediately below the title:

```markdown
> **Audience:** Operators | Contributors | Admins | Integrators | AI agents
> **Status:** Implemented | Draft | Planned | Mixed
> **Owner:** Platform/Ops | Security | API | Frontend | Product/Admin | Contributor Experience | Agent Context
> **Last Verified:** YYYY-MM-DD
> **Source Anchors:** `path/one`, `path/two`
```

Rules:

- `Audience` can list multiple audience labels separated by `|`.
- `Status` must describe the page as a whole; sections that are not implemented must also be labeled in the section text.
- `Owner` must use one of the owner categories in this page.
- `Last Verified` is the date source anchors were checked, not the edit date for grammar-only changes.
- `Source Anchors` must point to real files or directories that prove the behavior.

Legacy docs can migrate gradually. Do not add metadata mechanically to low-value pages without checking their source anchors.

## Source-Anchor Policy

Documentation must prefer source-grounded claims over inferred behavior:

- Runtime/service facts anchor to code or infrastructure files such as `docker-compose.yml`, `Explore.AppHost/`, or `Explore.API/Program.cs`.
- Configuration tables anchor to binding or compatibility code such as `Explore.API/Extensions/ConfigurationExtensions.cs`.
- Testing commands anchor to `docs/TESTING.md`, `.github/workflows/`, and the relevant test project files.
- Roadmap or future behavior must be explicitly marked `Planned` or `Draft` and must not be presented as implemented.

When a source anchor and a doc disagree, update the doc or create a task to reconcile the source. Do not preserve stale examples for narrative continuity.

## Docs Impact Contract

Every non-trivial change must record one of these outcomes in the PR or dev handoff:

| Outcome | Meaning |
|---|---|
| Updated | Docs changed in the same PR because behavior, commands, config, or operations changed. |
| Not needed | The change is internal and does not affect documented behavior. |
| Deferred | Docs impact exists but is intentionally split; include the follow-up path and reason. |

API contract, operator, security, onboarding, and release changes should default to `Updated` unless proven otherwise.

## Dual-Documentation Architecture (`docs/public/` vs `docs/internal/`)

To preserve clarity for all audiences, the documentation is strictly partitioned into two parallel tracks:

```text
docs/
├── README.md               <-- The Grand Router
├── public/                 <-- GitBook Hosted Portal (Adopters, Operators & Integrators)
└── internal/               <-- Engineering Brain (Contributors & AI Coding Agents)
```

1. **`docs/public/` (Public GitBook Portal)**: Curated, task-oriented guides for community admins, operators deploying with Docker/Coolify, and external API consumers. Avoids internal CQRS or EF Core plumbing.
2. **`docs/internal/` (Engineering Brain)**: Source of technical truth, Clean Architecture rules, invariants, MediatR request shapes, database locks, tenant filters, and context engineering contracts.

### Documentation Twin Parity Matrix & Separation of Concerns

Every public guide in `docs/public/` corresponds to an architectural anchor in `docs/internal/`. However, **the twins maintain strict Single Responsibility and do NOT duplicate content**:

| Domain | Public Adopter Guide (`docs/public/`)<br>*(Operator / Adopter / Admin Focus)* | Technical Source Anchor (`docs/internal/`)<br>*(Engineer / Contributor / Agent Focus)* | Boundary of Separation |
|---|---|---|---|
| **Self-Hosting** | `self-hosting/docker-compose.md`<br>`self-hosting/docker-standalone.md`<br>`self-hosting/coolify-cerbos-traefik.md`<br>`self-hosting/deployment-tiers.md` | `HOSTING_ARCHITECTURE.md`<br>`SELF_HOSTING.md`<br>`ARCHITECTURE.md` | Public docs owns 100% of Docker/Compose/Coolify runbooks, ports, and reverse-proxy recipes. Internal docs owns C# composition roots, startup lifecycle phases, and DB providers. |
| **Configuration** | `configuration-and-operations/environment-variables.md`<br>`configuration-and-operations/secrets.md` | `CONFIGURATION.md`<br>`SECRETS.md` | Public docs owns the complete, categorized Environment Variable Reference Catalogue. Internal docs owns C# Options classes (`IOptions<T>`), validation, and secret resolution mechanics. |
| **Operations & DR** | `configuration-and-operations/backup-restore-upgrade.md`<br>`configuration-and-operations/troubleshooting-and-health.md` | `OPERATIONS.md`<br>`BACKUP_RESTORE_UPGRADE.md`<br>`TROUBLESHOOTING.md` | Public docs owns step-by-step database backup/restore scripts (`pg_dump`) and operator symptom/cause/repair tables. Internal docs owns disaster recovery invariants, replay gates, and test reliability. |
| **Security & Auth** | `security-and-identity/authentication.md`<br>`security-and-identity/authorization.md`<br>`security-and-identity/multi-tenancy.md`<br>`security-and-identity/privacy-erasure.md` | `AUTHORIZATION.md`<br>`SECURITY-MODEL.md`<br>`MULTI_TENANCY.md`<br>`PRIVACY_ERASURE.md` | Public docs explains Keycloak realm configuration, Cerbos PDP connection, multi-tenant subdomains, and erasure topologies. Internal docs explains MediatR authorization, EF query filters, and anti-resurrection fences. |
| **Administration** | `administration-and-branding/admin-guide.md`<br>`administration-and-branding/admin-hierarchy.md`<br>`administration-and-branding/white-labeling.md` | `ADMIN_GUIDE.md`<br>`ADMIN_HIERARCHY.md`<br>`FOOTER_MANAGEMENT.md` | Public docs walks through Blazor admin UI screens (`/admin/instance`, monetization, branding). Internal docs specifies authority boundaries, role permissions, and governance locks. |
| **Events & Commerce** | `events-and-ticketing/modular-event-aspects.md`<br>`events-and-ticketing/custom-properties.md`<br>`events-and-ticketing/paid-events-and-payouts.md` | `MODULAR_EVENTS.md`<br>`CUSTOM_PROPERTIES.md`<br>`PAYMENTS.md`<br>`ADMISSION_AND_REGISTRATION.md` | Public docs guides organizers on modular aspects, custom questions, and Stripe Connect. Internal docs specifies DDD aggregates, serializable concurrency locks, and HMAC ticket digests. |
| **API Reference** | `api-reference/readme/hal-rest.md`<br>`api-reference/readme/api-cookbook.md`<br>`api-reference/readme/interactive-endpoints.md` | `API.md`<br>`API_CONTRACT_INVENTORY.md` | Public docs provides task-first curl integration recipes, HAL conventions, and Swagger/Scalar endpoints. Internal docs specifies middleware pipeline order, HATEOAS assembler classes, and caching. |

### Dual-Documentation Parity Protocol

1. **GitBook Freshness Gate (Pull-Before-Edit)**: Because GitBook pushes web-edited documentation commits directly to `develop` via the GitHub App bypass list, agents and developers MUST execute `git checkout develop && git pull --ff-only` before authoring local edits to either `docs/public/` or `docs/internal/` twins. This prevents stale base drift and merge conflicts.
2. **Adopter Projection Rule (Public Docs)**: When updating a public doc in `docs/public/`, write instructions strictly from the perspective of an operator, adopter, or API integrator. Provide copy-pasteable configurations, bash commands, and UI walkthroughs. **Never mention internal C# classes, MediatR handlers, EF Core entity configurations, or internal TUnit test commands.**
3. **Technical Depth Rule (Internal Docs)**: When updating an internal doc in `docs/internal/`, document the full architectural reality: C# class names, DDD invariants, concurrency behaviors, tenant query filters, state machines, and rollback mechanics. **Never duplicate 1000-line Docker Compose configs or reverse-proxy manuals in internal docs.**
4. **Intent Enforcement**: Every intent in `.agents/contract/intents.yaml` affecting external contracts declares both internal and public twins in `docs_to_update`.
5. **Subset Parity Rule (`.env.example` vs Public Docs)**: To preserve an approachable onboarding experience for everyday self-hosters (following Convention over Configuration), `.env.example` is an intentionally curated baseline subset of `docs/public/.../environment-variables.md`. Every variable present in `.env.example` MUST exist in the public reference, while the public reference serves as the exhaustive superset containing all advanced dials and auxiliary profile configurations.
6. **GitBook Pure Markdown & Mermaid Rule**: GitBook does not reliably support raw HTML tags (e.g. `<details>`, `<summary>`, `<div>`, `<accordion>`). All documentation in `docs/public/` must use pure standard Markdown tables, GitHub alert syntax (`> [!NOTE]`, `> [!TIP]`, `> [!WARNING]`, `> [!IMPORTANT]`, `> [!CAUTION]`), and native Mermaid diagrams.
7. **Metadata & Header Boundary**: Public documentation in `docs/public/` is written for external humans and GitBook synchronization; it uses native GitBook YAML frontmatter `description:` for summaries and must never contain synthetic AI comment headers. High-level navigation and agent discovery are anchored centrally in `docs/README.md`, `docs/public/documentation/SUMMARY.md`, and the Twin Parity Matrix in this document. Internal documents in `docs/internal/` use standard Markdown metadata blocks.
