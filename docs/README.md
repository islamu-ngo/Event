<!-- ABOUTME: Grand Router for ISLAMU Event documentation separating Public and Internal knowledge bases. -->
<!-- ABOUTME: Directs adopters to GitBook public docs and developers/agents to engineering specifications. -->

# Documentation Hub

Welcome to the ISLAMU Event documentation hub. To provide the best possible experience for both operators and engineers, our documentation is cleanly separated into two distinct tracks:

```text
docs/
├── README.md               <-- You are here (The Documentation Router)
├── public/                 <-- Public Documentation (Synced with GitBook for Adopters & Operators)
└── internal/               <-- Engineering Brain (For Contributors, Architects & AI Coding Agents)
```

---

## 🧭 Choose Your Path

| I am a... | Goal | Go To |
|---|---|---|
| **Adopter / Self-Hoster / Operator** | Deploying, operating, or configuring an instance (Docker, Coolify, Traefik) | 📖 **[Public Documentation](public/)**<br>*(Also available online at [islamu.gitbook.io/islamu-event](https://islamu.gitbook.io/islamu-event))* |
| **Community Admin / Organizer** | Managing events, ticketing, white-labeling, or tenant branding via UI | 🏢 **[Administration Guides](public/documentation/readme/administration-and-branding/README.md)** |
| **API Integrator** | Integrating third-party apps or building clients against the REST API | 🔌 **[API Reference & Cookbook](public/api-reference/README.md)** |
| **Developer Contributor** | Contributing C# backend, Blazor frontend, or architecture improvements | 💻 **[Internal Developer Docs](internal/)** (Start with [`internal/DEVELOPER_GUIDE.md`](internal/DEVELOPER_GUIDE.md)) |
| **AI Coding Agent** | Pair programming, verifying invariants, running tests, or planning tasks | 🤖 **[`AGENTS.md`](../AGENTS.md)** & **[`internal/index.md`](internal/index.md)** |

---

## 1. Public Documentation (`docs/public/`)

> **Hosted Portal:** [https://islamu.gitbook.io/islamu-event](https://islamu.gitbook.io/islamu-event)

This directory is the source of truth for the public GitBook site. It is curated for clarity, actionable step-by-step guidance, and ease of adoption without exposing internal framework mechanics.

- **[Getting Started](public/documentation/readme/getting-started/README.md)**: 5-minute evaluation, architectural concepts, and platform advantages.
- **[Self-Hosting](public/documentation/readme/self-hosting/README.md)**: Production topologies using Docker Compose, Coolify, Traefik, or standalone containers.
- **[Configuration & Operations](public/documentation/readme/configuration-and-operations/README.md)**: Environment variable matrices, secrets injection, backup, restore, and health checks.
- **[Security & Identity](public/documentation/readme/security-and-identity/README.md)**: Authentication with Keycloak, tenant isolation, and privacy erasure.
- **[Events & Ticketing](public/documentation/readme/events-and-ticketing/README.md)**: Modular aspects, custom registration properties, admission credentials, and payouts.
- **[API Reference](public/api-reference/README.md)**: HAL-REST concepts, interactive endpoints, and task-first recipes.
- **[Changelog](public/changelog/README.md)**: Adopter-facing release notes, upgrade instructions, and breaking change announcements.

---

## 2. Internal Engineering Documentation (`docs/internal/`)

This directory is the engineering brain for core maintainers and AI coding agents. It contains exhaustive technical truth, Clean Architecture rules, CQRS patterns, database policies, and repository constraints.

- **[Documentation Index](internal/index.md)**: Comprehensive inventory of all 90+ internal technical guides.
- **[Quick Reference](internal/QUICK_REFERENCE.md)**: Non-negotiable repository invariants, critical rules, and constraints.
- **[Architecture & Layer Rules](internal/ARCHITECTURE.md)**: Domain, Application, Infrastructure, API, and Blazor layer boundaries.
- **[Domain Model](internal/DOMAIN.md)**: Aggregate roots, entities, invariants, and state machines.
- **[API Contract & HAL Rules](internal/API.md)**: RFC 9457 ProblemDetails, HAL link affordances, output caching, and versioning.
- **[Configuration & Manifests](internal/CONFIGURATION.md)**: Complete binding paths, schema types, fallback hierarchy, and manifest specifications.
- **[Hosting Architecture](internal/HOSTING_ARCHITECTURE.md)**: Composition roots (Explore.API, Explore.Blazor, Event.Standalone), startup lifecycle phases, and provider abstraction.
- **[Operations & Verification](internal/OPERATIONS.md)**: Build, test, CI/CD runbooks, Aspire local hosting, and diagnostic tooling.
- **[Architecture Decision Records (ADRs)](internal/adr/)**: Permanent record of architectural choices and tradeoffs.
- **[Plane Work-Item Taxonomy & Granularity Standard](internal/project-management/work-item-taxonomy-and-granularity-report.md)**: How `EVENT` work is sized and structured (Epics, vertical work items, Surface Labels, Version Modules, priority vs urgency), bound by the [Plane project management ADR](internal/adr/ADR-plane-project-management-standardization.md).

---

## 🔄 Dual-Documentation Parity & Separation Protocol

To prevent public docs and internal technical truth from drifting apart, this repository enforces a **Dual-Documentation Parity & Separation Rule**:

> **Rule:** Any change impacting external behavior (environment variables, docker-compose services, public API endpoints, authentication flows, or self-hosting runbooks) **MUST update both tracks in the same pull request**:
> 1. Update the **Public Guide** in `docs/public/` (adopter-friendly operational guide, copy-pasteable configurations, no internal C# classes).
> 2. Update the **Technical Anchor** in `docs/internal/` (exhaustive architectural specification, C# code bindings, DDD invariants).
> Both tracks fulfill distinct responsibilities without duplicating content or cluttering each other's audience.

See [`docs/internal/DOCUMENTATION_ARCHITECTURE.md`](internal/DOCUMENTATION_ARCHITECTURE.md) for the complete Documentation Twin Parity Matrix and verification instructions.
