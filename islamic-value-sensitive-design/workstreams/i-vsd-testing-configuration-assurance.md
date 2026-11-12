# Testing Configuration Assurance & Behavioral Matrix — I-VSD Planning Assessment

Last Updated: 2026-11-06 Europe/Brussels

## 1. Review Metadata

- **Mode:** planning
- **Subject:** Behavioral verification matrix for configuration toggles, environment variables, startup ceilings, and runtime governance in ISLAMU Event.
- **Workstream:** testing-configuration-assurance
- **Report kind:** provider-responsibility planning review
- **Report status:** current
- **Disposition:** plan-aligned
- **Evidence cutoff:** 2026-11-06
- **Reviewed input:** Configuration toggle audit report (`configuration-toggle-testing-audit.md`), test portfolio index (`TEST_PORTFOLIO_OWNERSHIP.md`), and repository source at `develop`.

---

## 2. Executive Summary & Value Alignment

In Islamic software engineering and ethical platform governance, **truth in configuration (`Amanah` and `Sidq`)** is a core moral requirement. When an application exposes configuration toggles to system operators, community administrators, or tenants—such as privacy controls, moderation gates, registration limits, and protocol transports—the software makes an explicit promise:
1. Setting a feature to **Disabled** must reliably prevent access, leak zero metadata, and consume no unauthorized resources (`Hifz` — protection of privacy and bounds).
2. Setting a feature to **Enabled** must ensure the working path functions correctly with full tenant isolation, fail-closed authorization, and graceful error handling (`Adl` and `Ihsan`).
3. Leaving configuration at **Default (omitted)** must yield predictable, secure behavior without hidden defaults or operational panics.

### The Moral Hazard of "Phantom Toggles"
Retaining toggles in UI or API schemas that have no working implementation in production code (such as `Mcp:EnableLegacySse` / `mcp.enable_legacy_sse`, where production code hardcodes runtime failure) creates false confidence for operators and users (`Gharar` — uncertainty and deceptive affordance). Eliminating these phantom toggles and establishing verified end-to-end tests for every supported configuration state directly upholds platform integrity, transparency, and provider responsibility.

---

## 3. Moral & Architectural Responsibilities Mapping

| Invariant ID | Moral Concern / Value | Responsibility in this Workstream | Validation Seam |
|---|---|---|---|
| **IVSD-F001** | Truthfulness in Affordances (`Sidq`) | Eliminate phantom toggles that offer illusory configuration without working code (e.g., MCP Legacy SSE). | Schema and UI contract tests prove obsolete settings are removed. |
| **IVSD-F002** | Complete System Protection (`Hifz`) | Ensure every security and privacy toggle has a verified Disabled-state test proving fail-closed denial. | API integration tests assert HTTP 404/403 with RFC 7807 problem details. |
| **IVSD-F003** | Honest Verification Evidence (`Amanah`) | Replace hardcoded test fixture defaults with parameterized tests so tests do not blind entire working paths. | Parameterized TUnit tests (`[Arguments(true)] / [Arguments(false)]`) in API and persistence suites. |
| **IVSD-F004** | Tenant Justice & Isolation (`Adl`) | Verify multi-tenant governance delegation locks (`governance.lock_tenant_*`) enforce instance ceilings against unauthorized tenant overrides. | Hierarchical settings integration tests with PostgreSQL. |
| **IVSD-F005** | Split Identity Integrity (`Waqf` / Trust) | Verify `IdentityDatabaseTopology.Split` where credentials and personal identity are physically isolated from community event data. | Multi-schema PostgreSQL integration tests. |

---

## 4. Assessment Status & Disposition

Status: **current / plan-aligned**. The workstream introduces no backward-compatibility compromises, removes deceptive phantom settings, refactors test fixtures for honest multi-state coverage, and establishes deterministic verification across all critical configuration axes.
