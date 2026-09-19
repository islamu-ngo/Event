ABOUTME: Entry point for project documentation with high-signal navigation only.
ABOUTME: Prioritizes non-inferable rules and operational docs over generated or repetitive detail.

# Documentation Index

> **Audience:** Operators | Contributors | Admins | Integrators | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-08-29
> **Source Anchors:** `../../README.md`, `DOCUMENTATION_ARCHITECTURE.md`, `DOCUMENTATION_STYLE_GUIDE.md`

Start with [Repository README](../../README.md) or the [Documentation Hub](../README.md) if you are new to the project. Use this page when you already know your task or need the complete internal engineering documentation inventory.

> 📖 **Official Hosted Documentation:** Looking for online guides, tutorials, and hosted manuals? Visit the **[Official Documentation Portal](https://islamu.gitbook.io/islamu-event)** or browse [`docs/public/`](../public/).

## Task Paths

| I need to... | Start Here | Then Read |
|---|---|---|
| Browse hosted platform guides & tutorials | [Official Documentation](https://islamu.gitbook.io/islamu-event) | [Docs Hub](../README.md), [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) |
| Understand the whole codebase & mental model | [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) | [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md), [REQUEST_FLOWS.md](REQUEST_FLOWS.md) |
| Follow step-by-step feature recipes | [CONTRIBUTOR_RECIPES.md](CONTRIBUTOR_RECIPES.md) | [QUICK_REFERENCE.md](QUICK_REFERENCE.md), [TESTING.md](TESTING.md) |
| Understand the product and current scope | [PROJECT.md](PROJECT.md) | [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md), [SECURITY-MODEL.md](SECURITY-MODEL.md) |
| Run the app locally | [GETTING_STARTED.md](GETTING_STARTED.md) | [TESTING.md](TESTING.md), [TROUBLESHOOTING.md](TROUBLESHOOTING.md) |
| Self-host or operate a deployment | [SELF_HOSTING.md](SELF_HOSTING.md) | [CONFIGURATION.md](CONFIGURATION.md), [SECRETS.md](SECRETS.md), [OPERATIONS.md](OPERATIONS.md), [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) |
| Bootstrap instance and tenant configuration | [CONFIGURATION_MANIFEST.md](CONFIGURATION_MANIFEST.md) | [CONFIGURATION.md](CONFIGURATION.md), [SELF_HOSTING.md](SELF_HOSTING.md), [SECRETS.md](SECRETS.md), [TROUBLESHOOTING.md](TROUBLESHOOTING.md) |
| Troubleshoot a failure | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | [OPERATIONS.md](OPERATIONS.md), [CONFIGURATION.md](CONFIGURATION.md), [TEST_RELIABILITY.md](TEST_RELIABILITY.md) |
| Prepare a governed release | [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) | [RELEASE_POLICY.md](RELEASE_POLICY.md), [RELEASE_RUNBOOK.md](RELEASE_RUNBOOK.md), [ADR-025](adr/ADR-025-provider-neutral-release-governance.md) |
| Configure tenants, admins, or branding | [ADMIN_GUIDE.md](ADMIN_GUIDE.md) | [ADMIN_HIERARCHY.md](ADMIN_HIERARCHY.md), [DEPLOYMENT_MODES.md](DEPLOYMENT_MODES.md), [MULTI_TENANCY.md](MULTI_TENANCY.md) |
| Configure payments or add a payment provider | [PAYMENTS.md](PAYMENTS.md) | [CONFIGURATION.md](CONFIGURATION.md), [SECRETS.md](SECRETS.md), [ADR-022](adr/ADR-022-paid-event-commerce-and-stripe-connect.md) |
| Understand registration, admission credentials, or recovery | [ADMISSION_AND_REGISTRATION.md](ADMISSION_AND_REGISTRATION.md) | [API.md](API.md), [SECURITY-MODEL.md](SECURITY-MODEL.md), [ADR-023](adr/ADR-023-admission-credential-check-in-transfer-recovery.md) |
| Integrate with the API | [API_COOKBOOK.md](API_COOKBOOK.md) | [API.md](API.md), [WEBHOOKS.md](WEBHOOKS.md), [INTEGRATIONS.md](INTEGRATIONS.md), [API_CHANGELOG.md](API_CHANGELOG.md), [SECURITY-MODEL.md](SECURITY-MODEL.md) |
| Change authorization or Cerbos behavior | [AUTHORIZATION.md](AUTHORIZATION.md) | [AUTHORIZATION_PATTERNS.md](AUTHORIZATION_PATTERNS.md), [SECURITY-MODEL.md](SECURITY-MODEL.md), [OPERATIONS.md](OPERATIONS.md) |
| Contribute code or docs | [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) | [CONTRIBUTOR_RECIPES.md](CONTRIBUTOR_RECIPES.md), [CONTRIBUTING.md](CONTRIBUTING.md), [QUICK_REFERENCE.md](QUICK_REFERENCE.md) |
| Choose records, classes, or generated contract shapes | [RECORD_CONTRACTS.md](RECORD_CONTRACTS.md) | [GOVERNANCE.md](GOVERNANCE.md), [ARCHITECTURE.md](ARCHITECTURE.md), [TESTING.md](TESTING.md) |
| Research external behavior or assess a dependency license | [IP Governance](legal/IP_GOVERNANCE.md) | [CLA](../../legal/CLA.md), [Mapperly provenance](legal/dependencies/mapperly.md), [CI/CD Governance](CI_CD_GOVERNANCE.md) |
| Work on Blazor UI | [BLAZOR.md](BLAZOR.md) | [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md), [ACCESSIBILITY.md](ACCESSIBILITY.md), [RENDER_POLICIES.md](RENDER_POLICIES.md) |
| Use an AI coding agent or understand context engineering | [AGENTIC_CONTEXT_ENGINEERING.md](AGENTIC_CONTEXT_ENGINEERING.md) | [../../AGENTS.md](../../AGENTS.md), [../../.agents/contract/intents.yaml](../../.agents/contract/intents.yaml), [QUICK_REFERENCE.md](QUICK_REFERENCE.md) |

## How To Use This Index

- Prefer the task path first, then use the full inventory below only when you need a specific reference.
- Treat how-to pages as workflows and reference pages as the exact source for keys, contracts, commands, and runtime behavior.
- If two docs disagree, prefer the page with source anchors to code, infrastructure, tests, or workflows, then fix the stale page.

## Read First

- [Official Documentation](https://islamu.gitbook.io/islamu-event) - official hosted documentation portal for operators, evaluators, and platform users.
- [Docs Hub](../README.md) - router separating public GitBook docs and internal engineering specs.
- [Repository README](../../README.md) - product overview and repository entrance.
- [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) - orientation, 5-minute mental model, and invariant survival guide.
- [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md) - high-level visual architecture, C4 container map, and component interactions.
- [REQUEST_FLOWS.md](REQUEST_FLOWS.md) - end-to-end execution flows with detailed sequence diagrams.
- [CONTRIBUTOR_RECIPES.md](CONTRIBUTOR_RECIPES.md) - practical blueprints for adding entities, CQRS slices, APIs, and pages.
- [GETTING_STARTED.md](GETTING_STARTED.md) - shortest local build, run, and first-contribution path.
- [QUICK_REFERENCE.md](QUICK_REFERENCE.md) - implementation constraints that are easy to violate.
- [AGENTS.md](../../AGENTS.md) - contribution contract used by contributors and AI agents.

## Core Architecture
- [ARCHITECTURE_OVERVIEW.md](ARCHITECTURE_OVERVIEW.md) - high-level visual architecture, C4 diagrams, and component interactions.
- [REQUEST_FLOWS.md](REQUEST_FLOWS.md) - end-to-end request lifecycles with sequence diagrams.
- [ARCHITECTURE.md](ARCHITECTURE.md) - layer boundaries, BFF model, CQRS flow.
- [RECORD_CONTRACTS.md](RECORD_CONTRACTS.md) - record/class selection, immutable results and collections, semantic values, persistence checks, and generated C# contracts.
- [DOMAIN.md](DOMAIN.md) - domain entities and important relationships.
- [ADMISSION_AND_REGISTRATION.md](ADMISSION_AND_REGISTRATION.md) - registration order lifecycle, entitlement materialization, and zero-knowledge admission credentials.
- [API.md](API.md) - API conventions, HAL behavior, filters, caching, error shape.
- [API_CONTRACT_INVENTORY.md](API_CONTRACT_INVENTORY.md) - generated inventory of every OpenAPI operation.
- [API_CONTRACT_TEST_DEBT.md](API_CONTRACT_TEST_DEBT.md) - governed inventory of intentionally skipped API contract tests and removal criteria.
- [API_COOKBOOK.md](API_COOKBOOK.md) - task-first API integration guidance.
- [BLAZOR.md](BLAZOR.md) - client architecture, render policy, service patterns.
- [DOCK_LAYOUT.md](DOCK_LAYOUT.md) - dock engine, responsive behavior, shell/workspace stacking, inspector overlays, and persistence.
- [OUTBOX_PATTERN.md](OUTBOX_PATTERN.md) - transactional outbox, retry, dead-letter.
- [FOOTER_MANAGEMENT.md](FOOTER_MANAGEMENT.md) - footer data model, templates, governance.
- [CUSTOM_PROPERTIES.md](CUSTOM_PROPERTIES.md) - Layer 3 custom-property governance, projections, templates, purge, export/moderation boundaries.
- [PAYMENTS.md](PAYMENTS.md) - provider-neutral payment architecture, OrganizerDirect, and checkout flows.
- [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md) - CSS layers, tokens, wrapper components.

## Governance And Rules
- [GOVERNANCE.md](GOVERNANCE.md) - design and code governance.
- [CI_CD_GOVERNANCE.md](CI_CD_GOVERNANCE.md) - GitHub Actions gates, branch protection, deployment environments, and CI/CD evidence policy.
- [CI_CD_RUNBOOKS.md](CI_CD_RUNBOOKS.md) - maintainer runbooks for rerunning CI/CD gates without bypassing controls.
- [RELEASE_POLICY.md](RELEASE_POLICY.md) - normative provider-neutral release invariants; prospective until the release engine is activated.
- [RELEASE_RUNBOOK.md](RELEASE_RUNBOOK.md) - current manual and future governed-release operator procedure.
- [adr/ADR-025-provider-neutral-release-governance.md](adr/ADR-025-provider-neutral-release-governance.md) - trusted-bundle, exact-commit, and renderer-only release architecture.
- [legal/CLA.md](../../legal/CLA.md) - active ISLAMU Event contributor license agreement.
- [legal/CONTRIBUTION_GOVERNANCE.md](legal/CONTRIBUTION_GOVERNANCE.md) - CLA legal contribution decision record and automation threat model.
- [legal/IP_GOVERNANCE.md](legal/IP_GOVERNANCE.md) - clean-room research, source isolation, dependency-license compatibility, and audit evidence policy.
- [NAMING_CONVENTIONS.md](NAMING_CONVENTIONS.md) - naming rules for entities, CQRS, services, tests.
- [TEMPLATE_GLOSSARY.md](TEMPLATE_GLOSSARY.md) - placeholder syntax used in generic templates.
- [FIRST_CONTRIBUTION.md](FIRST_CONTRIBUTION.md) - shortest safe path for docs-only and small-bug PRs.
- [CONTRIBUTING.md](CONTRIBUTING.md) - contribution workflow and validation steps.
- [DOCUMENTATION_ARCHITECTURE.md](DOCUMENTATION_ARCHITECTURE.md) - docs architecture, ownership, metadata, source-anchor, and quality-gate policy.
- [DOCUMENTATION_STYLE_GUIDE.md](DOCUMENTATION_STYLE_GUIDE.md) - documentation writing rules.
- [PUBLIC_DOCS_ROADMAP.md](PUBLIC_DOCS_ROADMAP.md) - future hosted public-docs boundary and candidate pages.
- [BLAZOR_DEV_WORKFLOW.md](BLAZOR_DEV_WORKFLOW.md) - stop → build → run → wait → inspect loop for Blazor visual verification.

## AI Agent Contribution Context
- [../../AGENTS.md](../../AGENTS.md) - tool-neutral entrypoint, Contribution Contract, rule authority, cold-start flow.
- [../../.agents/contract/README.md](../../.agents/contract/README.md) - Contribution Contract (eight questions every change must answer).
- [../../.agents/contract/intents.yaml](../../.agents/contract/intents.yaml) - intent → context map (paths, rules, skills, tests, docs).
- [../../.agents/rules/README.md](../../.agents/rules/README.md) - path-scoped rule files auto-loaded by editing scope.
- [../../.agents/skills/_SKILL_SCHEMA.md](../../.agents/skills/_SKILL_SCHEMA.md) - schema every migrated `SKILL.md` follows.
- [../../.agents/agents/_AGENT_SCHEMA.md](../../.agents/agents/_AGENT_SCHEMA.md) - schema every subagent file follows.
- [../../.agents/benchmarks/README.md](../../.agents/benchmarks/README.md) - cold-start agent benchmark scenarios.
- [../../dev/_journal/README.md](../../dev/_journal/README.md) - durable findings log + promotion rules.

## Runtime And Operations
- [CONFIGURATION_MANIFEST.md](CONFIGURATION_MANIFEST.md) - strict instance-and-tenant bootstrap contract, operator workflow, rerun semantics, export, recovery, and contributor extension guide.
- [CONFIGURATION.md](CONFIGURATION.md) - environment/config settings and secret providers.
- [SECRETS.md](SECRETS.md) - multi-provider secret management (Explore.Secrets library).
- [STORAGE.md](STORAGE.md) - local-first object storage, optional S3-compatible configuration, reconciliation, API boundaries, and backup impact.
- [EMAIL_NOTIFICATIONS.md](EMAIL_NOTIFICATIONS.md) - SMTP email delivery and its boundary from in-app notifications.
- [NOTIFICATIONS.md](NOTIFICATIONS.md) - in-app notification lifecycle, inbox UI, and API boundaries.
- [WEBHOOKS.md](WEBHOOKS.md) - outgoing webhook providers, LocalProvider/SvixProvider setup, signatures, health, and rollout.
- [WEBHOOK_OPERATIONS_RUNBOOK.md](WEBHOOK_OPERATIONS_RUNBOOK.md) - webhook readiness, bounded metrics, SLOs, alerts, and Local/self-hosted Svix incident recovery.
- [PAYMENTS.md](PAYMENTS.md) - payment provider configuration, Stripe Connect platform secrets, and readiness reconciliation.
- [INTEGRATIONS.md](INTEGRATIONS.md) - incoming provider callbacks, Coop/Osprey/Svix operational verification, and idempotency.
- [Google Forms Pub/Sub Integration](integrations/google-forms-pubsub.md) - Google Forms OAuth scopes, Pub/Sub OIDC setup, renewal/sweep behavior, and unsupported Drive/file boundaries.
- [SEO.md](SEO.md) - sitemap, robots, render-policy, and public-discovery SEO behavior.
- [SECURITY_OVERVIEW.md](SECURITY_OVERVIEW.md) - auth/authz model and trust boundaries.
- [SECURITY-MODEL.md](SECURITY-MODEL.md) - platform security invariants, privilege isolation, and privacy boundaries.
- [PRIVACY_ERASURE.md](PRIVACY_ERASURE.md) - Privacy Erasure Authority, concrete meaning, storage topologies (EmbeddedSqlite, CoLocated, ExternalDatabase), independent restore-isolation rationale, and self-hoster decision guide.
- [OPERATIONS.md](OPERATIONS.md) - deployment, health, observability.
- [TESTING.md](TESTING.md) - TUnit framework, test projects, TDD workflow.
- [TEST_RELIABILITY.md](TEST_RELIABILITY.md) - flaky/deferred runtime, stress, and manual-test tracking.
- [BENCHMARKS.md](BENCHMARKS.md) - BenchmarkDotNet runtime benchmarks, API pipeline benchmarks, PostgreSQL/Testcontainers benchmark runs, and cold-start agent benchmark boundaries.
- [AGENTIC_CONTEXT_ENGINEERING.md](AGENTIC_CONTEXT_ENGINEERING.md) - canonical visual guide to the agentic context engineering system, cold-start lifecycle, multi-harness bootloaders, twin rules, dev-doc triad, and QA evidence gates.
- [AI_RAG_FOUNDATION.md](AI_RAG_FOUNDATION.md) - tenant-safe summary-only boundary for future AI vector/RAG support.
- [AI_AGENT_CONTRACT_INVENTORY.md](AI_AGENT_CONTRACT_INVENTORY.md) - generated inventory of registry-governed AI agent tool contracts.
- [AI_AGENT_EXPERIENCE_HARDENING.md](AI_AGENT_EXPERIENCE_HARDENING.md) - guarded context summaries, proposal-only plan previews, and fake/replay AI usability reports.
- [HOSTING_ARCHITECTURE.md](HOSTING_ARCHITECTURE.md) - composition roots (Explore.API, Explore.Blazor, Event.Standalone, Event.MigrationService), startup lifecycle phases, and provider abstraction.
- [SELF_HOSTING.md](SELF_HOSTING.md) - internal hosting specification, headless onboarding, and operator identity gates (for operator runbooks, see [public docs](../public/documentation/readme/self-hosting/)).
- [CERBOS_COOLIFY.md](CERBOS_COOLIFY.md) - Coolify deployment specification for external Cerbos PDP and PostgreSQL storage schema.
- [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md) - backup, restore, upgrade, and rollback runbook.
- [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) - release readiness and documentation contract.
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - practical issue resolution.
- [DEPLOYMENT_MODES.md](DEPLOYMENT_MODES.md) - single-tenant vs multi-tenant runtime behavior.
- [DEPLOYMENT_TIERS.md](DEPLOYMENT_TIERS.md) - infrastructure tier choices.

## Multi-Tenancy And Platform Control
- [MULTI_TENANCY.md](MULTI_TENANCY.md) - tenant isolation and resolution.
- [ADMIN_GUIDE.md](ADMIN_GUIDE.md) - task-focused instance, tenant, organization, and group administration.
- [ADMIN_HIERARCHY.md](ADMIN_HIERARCHY.md) - authority boundaries across instance, tenant, organization.
- [AUTHORIZATION_PATTERNS.md](AUTHORIZATION_PATTERNS.md) - request and pipeline authorization patterns.
- [TEMPLATE_SYNC.md](TEMPLATE_SYNC.md) - event and session template synchronization workflow.
- [CONTACT_SHARING.md](CONTACT_SHARING.md) - registration contact share consent, withdrawal, organization access, and export boundaries.
- [NOTIFICATIONS.md](NOTIFICATIONS.md) - notification inbox behavior and user-owned lifecycle actions.
- [SEO.md](SEO.md) - public discovery, robots, sitemap, and render-policy boundaries.
- [RENDER_POLICIES.md](RENDER_POLICIES.md) - route render policy governance.

## Extensibility And Federation
- [EXTENSIBILITY.md](EXTENSIBILITY.md) - modular extension strategy.
- [ERP_INTEGRATION_GUIDE.md](ERP_INTEGRATION_GUIDE.md) - operator & partner guide for integrating ISLAMU Event as a white-label module in enterprise ERP platforms.
- [WEBHOOKS.md](WEBHOOKS.md) - outgoing webhook provider extension point and self-hosted Svix integration.
- [INTEGRATIONS.md](INTEGRATIONS.md) - incoming integration callback boundary.
- [PAYMENTS.md](PAYMENTS.md) - payment provider extension guide and adapter contracts.
- [Microsoft Forms Power Automate Template](integrations/microsoft-forms-flow-template.md) - organizational Forms callback setup, activation gates, and CSV reconciliation.
- [Google Forms Pub/Sub Integration](integrations/google-forms-pubsub.md) - Google Forms managed provisioning and Pub/Sub response-sweep setup.
- [COOP_INTEGRATION.md](COOP_INTEGRATION.md) - multi-tenant human review dashboard integration and escalation flow.
- [OSPREY_INTEGRATION.md](OSPREY_INTEGRATION.md) - single-tenant machine moderation challenges and hybrid policy engine.
- [MODULAR_EVENTS.md](MODULAR_EVENTS.md) - event aspect composition model.
- [CUSTOM_PROPERTIES.md](CUSTOM_PROPERTIES.md) - governed custom fields on existing resources; see ADR-006 for the runtime-schema boundary.
- [LEXICONS.md](LEXICONS.md) - ATProto-compatible NSID lexicon hierarchy and evolution.
- [FEDERATION.md](FEDERATION.md) - current federation implementation status vs roadmap.

## Reference And Change History
- [PROJECT.md](PROJECT.md) - product context and current scope.
- [CODEBASE_STRUCTURE.md](CODEBASE_STRUCTURE.md) - file and folder map.
- [CODEBASE_INSIGHTS.md](CODEBASE_INSIGHTS.md) - non-intuitive patterns and hidden knowledge.
- [BENCHMARKS.md](BENCHMARKS.md) - runtime benchmark project, API performance harnesses, Testcontainers-backed benchmark lane, and benchmark interpretation rules.
- [ACCESSIBILITY.md](ACCESSIBILITY.md) - WCAG AA compliance, service contracts, testing.
- [ACCESSIBILITY_ARTIFACTS.md](ACCESSIBILITY_ARTIFACTS.md) - statement, AT matrix, release gate checklist.
- [PUBLIC_DOCS_ROADMAP.md](PUBLIC_DOCS_ROADMAP.md) - deferred public documentation website plan.
- [API_CHANGELOG.md](API_CHANGELOG.md) - API-specific change log.
- [semantic_versioning/CHANGELOG.md](semantic_versioning/CHANGELOG.md) - frozen pre-automation release planning/history.
- [semantic_versioning/v0.1.0.md](semantic_versioning/v0.1.0.md) - frozen v0.1.0 planning/history notes.
- [adr/ADR-001-authorization-provider-architecture.md](adr/ADR-001-authorization-provider-architecture.md) - authorization provider architecture.
- [adr/ADR-002-outbox-pattern.md](adr/ADR-002-outbox-pattern.md) - outbox pattern for reliable messaging.
- [adr/ADR-003-css-layer-architecture.md](adr/ADR-003-css-layer-architecture.md) - CSS @layer architecture.
- [adr/ADR-004-accessibility-architecture.md](adr/ADR-004-accessibility-architecture.md) - accessibility service architecture.
- [adr/ADR-005-footer-customization.md](adr/ADR-005-footer-customization.md) - tenant footer customization.
- [adr/ADR-006-custom-properties-runtime-boundary.md](adr/ADR-006-custom-properties-runtime-boundary.md) - custom properties versus runtime schema engine boundary.
- [adr/ADR-008-email-dispatch-state-machine.md](adr/ADR-008-email-dispatch-state-machine.md) - email dispatch state machine and optional RabbitMQ dispatch profiles.
- [adr/ADR-009-postgresql-partitioning-deferral.md](adr/ADR-009-postgresql-partitioning-deferral.md) - PostgreSQL partitioning deferral, activation gates, and runbook requirements.
- [adr/ADR-010-mcp-adapter-hosting-strategy.md](adr/ADR-010-mcp-adapter-hosting-strategy.md) - MCP adapter hosting, transport, auth, tenancy, and disable posture.
- [adr/ADR-011-local-mcp-stdio-diagnostic-host.md](adr/ADR-011-local-mcp-stdio-diagnostic-host.md) - local-only stdio diagnostic-host decision and deferral.
- [adr/ADR-021-keycloak-authentication-standard.md](adr/ADR-021-keycloak-authentication-standard.md) - standardizing platform authentication on Keycloak and authorization on Cerbos.
- [adr/ADR-026-domain-owned-lifecycle-and-contextual-completeness.md](adr/ADR-026-domain-owned-lifecycle-and-contextual-completeness.md) - technology-neutral lifecycle ownership, contextual completeness, nullable persistence, and layered validation.
