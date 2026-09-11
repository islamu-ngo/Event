# Islamic Value-Sensitive Design (I-VSD)

> **Provider-Responsibility and Ethical Architecture Archive**  
> Canonical Guidance & Working Repository for ISLAMU Event

---

## 1. Overview

**Islamic Value-Sensitive Design (I-VSD)** evaluates provider-mediated software decisions—governance policies, commercial models, architectural boundaries, data minimization, defaults, and user experiences—through an explicit Sunni Islamic ethical lens and provider-responsibility framework.

I-VSD produces durable findings, mitigations, and escalation boundaries. It does **not** issue religious-legal rulings (*fatawa*) or marketing certifications; rather, it designs software systems such that provider actions remain accountable, transparent, non-coercive, and ethically sound.

---

## 2. Directory Architecture (The 3-Tier Purpose Split)

To keep the repository organized, discoverable, and aligned with Clean Architecture, all I-VSD artifacts are partitioned into three functional tiers:

```text
islamic-value-sensitive-design/
├── README.md                      # This directory catalog and navigation guide
├── governance/                    # Tier 1: Constitutional policies & project-wide covenants
├── consultations/                 # Tier 2: Substantive domain & feature design consultations
└── workstreams/                   # Tier 3: Task-anchored planning assessments (per dev/active/<task>)
```

### Tier 1: `governance/` (Constitutional Policies)
Permanent, high-authority covenants and policies that govern the entire ISLAMU Event ecosystem. These documents are directly referenced by root legal and governance files ([`README.md`](../README.md), [`CONTRIBUTING.md`](../CONTRIBUTING.md), [`CLA.md`](../CLA.md), [`docs/internal/legal/IP_GOVERNANCE.md`](../docs/internal/legal/IP_GOVERNANCE.md)).

| Document | Subject & Purpose |
|---|---|
| [`governance/i-vsd-licensing-and-commercial-strategy.md`](governance/i-vsd-licensing-and-commercial-strategy.md) | **Anti-SaaS covenant**, AGPL-3.0-or-later licensing, CLA alternative licensing bounds, and commercial sustainability. |
| [`governance/i-vsd-release-governance.md`](governance/i-vsd-release-governance.md) | Governed release engineering, public versioning, and changelog verification truth. |
| [`governance/i-vsd-branding-legal-identity-authority.md`](governance/i-vsd-branding-legal-identity-authority.md) | Legal identity separation, operator attribution, and no-fallback naming boundaries. |
| [`governance/i-vsd-cla-workflow-hardening.md`](governance/i-vsd-cla-workflow-hardening.md) | Contributor License Agreement automation, inbound rights, and signatory protections. |
| [`governance/i-vsd-universal-self-hosting-configuration-assistant-product-strategy.md`](governance/i-vsd-universal-self-hosting-configuration-assistant-product-strategy.md) | Open-source stewardship and cross-platform configuration assistant product strategy. |
| [`governance/i-vsd-work-criticality-and-agentic-governance.md`](governance/i-vsd-work-criticality-and-agentic-governance.md) | Agentic AI governance, epistemic validation, and task criticality tiers. |

---

### Tier 2: `consultations/` (Domain & Feature Consultations)
Substantive, deep design investigations into specific product capabilities, ethical dilemmas, and architectural decisions. These establish the moral rationale (*"Why"*) behind features and are cited by Architecture Decision Records (ADRs) and domain specifications.

| Domain | Document | Core Scope |
|---|---|---|
| **Commerce & Payments** | [`consultations/i-vsd-paid-event-payments-consultation.md`](consultations/i-vsd-paid-event-payments-consultation.md) | Stripe Connect `OrganizerDirect`, non-custodial payouts, anti-riba invariants, and immutable refund fee protections. |
| **Commerce & Payments** | [`consultations/i-vsd-event-ticketing-lifecycle.md`](consultations/i-vsd-event-ticketing-lifecycle.md) | Capacity governance, ticket hold reservations, and purchase allocation truth. |
| **Commerce & Payments** | [`consultations/i-vsd-paid-events-deactivation-consultancy-report.md`](consultations/i-vsd-paid-events-deactivation-consultancy-report.md) | Deactivation lifecycles, grace periods, and attendee protection against abrupt cancellation. |
| **Commerce & Payments** | [`consultations/i-vsd-minimum-attendee-threshold-consultation.md`](consultations/i-vsd-minimum-attendee-threshold-consultation.md) | Event quorum thresholds, conditional ticketing, and automatic full-refund triggers. |
| **Privacy & Identity** | [`consultations/i-vsd-account-deletion-consultation.md`](consultations/i-vsd-account-deletion-consultation.md) | Right to erasure, irreversible data redaction, audit log retention, and tombstoning. |
| **Privacy & Identity** | [`consultations/i-vsd-event-location-privacy.md`](consultations/i-vsd-event-location-privacy.md) | Physical location disclosure thresholds, fuzzy radius defaults, and attendee safety. |
| **Privacy & Identity** | [`consultations/i-vsd-registration-data-collection.md`](consultations/i-vsd-registration-data-collection.md) | Data minimization for attendee registration and admission workflows. |
| **Privacy & Identity** | [`consultations/i-vsd-tenancy-experience-directory-vs-dedicatedportal.md`](consultations/i-vsd-tenancy-experience-directory-vs-dedicatedportal.md) | Tenancy discovery posture: Directory vs Isolated DedicatedPortal boundaries. |
| **Self-Hosting & Infra** | [`consultations/i-vsd-email-optional-self-hosting.md`](consultations/i-vsd-email-optional-self-hosting.md) | First-class zero-SMTP operation, headless instances, and credential-free hosting parity. |
| **Self-Hosting & Infra** | [`consultations/i-vsd-configuration-manifest.md`](consultations/i-vsd-configuration-manifest.md) | Configuration portability, manifest schemas, and strict non-secret environment boundaries. |
| **Self-Hosting & Infra** | [`consultations/i-vsd-setup-assistant-security-and-portability.md`](consultations/i-vsd-setup-assistant-security-and-portability.md) | Local CLI/TUI assistant security, encrypted profiles, and cross-platform installation. |
| **Platform & Operations** | [`consultations/i-vsd-sentry-and-glitchtip-error-tracking.md`](consultations/i-vsd-sentry-and-glitchtip-error-tracking.md) | Observability, client-side scrubbers, and self-hosted GlitchTip error telemetry. |
| **Platform & Operations** | [`consultations/i-vsd-automapper-mapperly-and-mediatr-replacement-consultation.md`](consultations/i-vsd-automapper-mapperly-and-mediatr-replacement-consultation.md) | Exiting third-party proprietary/restrictive libraries to source-generated C# alternatives. |
| **Platform & Operations** | [`consultations/i-vsd-compliance-check.md`](consultations/i-vsd-compliance-check.md) | Repository-wide moral compliance check across all six I-VSD domains. |
| **Events & Scheduling** | [`consultations/i-vsd-address-geocoding-and-spatial-discovery.md`](consultations/i-vsd-address-geocoding-and-spatial-discovery.md) | Spatial search, geocoding provider autonomy, and external API data leakage. |
| **Events & Scheduling** | [`consultations/i-vsd-event-resource-consultancy-report.md`](consultations/i-vsd-event-resource-consultancy-report.md) | Governed event equipment, rooms, and shared communal resources. |
| **Events & Scheduling** | [`consultations/i-vsd-flexible-event-end-times.md`](consultations/i-vsd-flexible-event-end-times.md) | Contextual, open-ended event end times respecting cultural/religious schedules. |

---

### Tier 3: `workstreams/` (Implementation-Planning Assessments)
Task-bound assessments generated during the execution of `.agents/skills/implementation-plan/` for active feature tasks in `dev/active/<task>`. These provide the binding provider-responsibility constraints that technical plans implement.

| Workstream Report | Associated Engineering Scope |
|---|---|
| [`workstreams/i-vsd-agentic-testing-acceleration.md`](workstreams/i-vsd-agentic-testing-acceleration.md) | Progressive verification model and in-memory test slicing. |
| [`workstreams/i-vsd-aspnet-identity-auth.md`](workstreams/i-vsd-aspnet-identity-auth.md) | ASP.NET Core Identity authentication provider integration. |
| [`workstreams/i-vsd-authentication-deployment-documentation-alignment.md`](workstreams/i-vsd-authentication-deployment-documentation-alignment.md) | Auth deployment guides and multi-provider documentation parity. |
| [`workstreams/i-vsd-cto-audit-remediation.md`](workstreams/i-vsd-cto-audit-remediation.md) | CTO audit technical remediation workstream. |
| [`workstreams/i-vsd-database-backed-atproto-auth.md`](workstreams/i-vsd-database-backed-atproto-auth.md) | ATProtocol decentralized identity and database session persistence. |
| [`workstreams/i-vsd-docs-quality-and-consolidation.md`](workstreams/i-vsd-docs-quality-and-consolidation.md) | Documentation architecture consolidation and verification rules. |
| [`workstreams/i-vsd-efcore-first-persistence-hardening.md`](workstreams/i-vsd-efcore-first-persistence-hardening.md) | Multi-database EF Core configuration and portable constraints. |
| [`workstreams/i-vsd-event-resources.md`](workstreams/i-vsd-event-resources.md) | Governed event resource scheduling implementation. |
| [`workstreams/i-vsd-governed-release-public-changelog.md`](workstreams/i-vsd-governed-release-public-changelog.md) | Automated changelog generator and release pipeline truth. |
| [`workstreams/i-vsd-headless-instance-onboarding.md`](workstreams/i-vsd-headless-instance-onboarding.md) | CLI/headless first-time instance setup workflows. |
| [`workstreams/i-vsd-mapping-and-cqs-migration.md`](workstreams/i-vsd-mapping-and-cqs-migration.md) | Migration from MediatR to native CQS dispatch and Mapperly. |
| [`workstreams/i-vsd-prevent-ci-failures-and-unicode-simplification.md`](workstreams/i-vsd-prevent-ci-failures-and-unicode-simplification.md) | CI pipeline hardening and Unicode search normalization. |
| [`workstreams/i-vsd-queue-driven-worker-migration.md`](workstreams/i-vsd-queue-driven-worker-migration.md) | Quartz.NET background worker migration and outbox processing. |
| [`workstreams/i-vsd-records-adoption.md`](workstreams/i-vsd-records-adoption.md) | C# record types migration for immutable DTOs and value objects. |
| [`workstreams/i-vsd-secrets-refactor-control-plane.md`](workstreams/i-vsd-secrets-refactor-control-plane.md) | Secrets authority isolation and Infisical provider refactor. |
| [`workstreams/i-vsd-setup-assistant-presentation-targets-b0.md`](workstreams/i-vsd-setup-assistant-presentation-targets-b0.md) | Setup assistant presentation layer candidate review. |
| [`workstreams/i-vsd-stateless-payment-checkout-tickets.md`](workstreams/i-vsd-stateless-payment-checkout-tickets.md) | Cryptographic checkout ticket tokens and stateless payment flows. |
| [`workstreams/i-vsd-strong-typing-reflection-remediation.md`](workstreams/i-vsd-strong-typing-reflection-remediation.md) | Elimination of runtime reflection in CQRS/validation dispatch. |
| [`workstreams/i-vsd-test-suite-health-remediation.md`](workstreams/i-vsd-test-suite-health-remediation.md) | Test suite flakiness quarantine and TUnit migration. |
| [`workstreams/i-vsd-test-suite-rationalization.md`](workstreams/i-vsd-test-suite-rationalization.md) | Test project consolidation and architecture test guardrails. |
| [`workstreams/i-vsd-unicode-location-search-simplification.md`](workstreams/i-vsd-unicode-location-search-simplification.md) | Portable database search text normalization across providers. |

---

## 3. Authoring & Agent Contracts

AI agents generating or updating I-VSD reports must adhere to the contracts defined in [`.agents/skills/i-vsd/`](../.agents/skills/i-vsd/):

- **Constitutional & Strategic Reviews**: Author under `governance/i-vsd-<subject>-<kind>.md`.
- **Feature & Architectural Consultations**: Author under `consultations/i-vsd-<subject>-<kind>.md`.
- **Implementation Planning Workstreams**: Author under `workstreams/i-vsd-<task-name>.md`.
