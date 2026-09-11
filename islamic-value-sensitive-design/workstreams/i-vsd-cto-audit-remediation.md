<!-- ABOUTME: I-VSD assessment for CTO audit remediation covering migration, NSwag, controller, warning, and tenant RLS work. -->
<!-- ABOUTME: Non-Behavioral Delta for Phases 1-5; tenant data stewardship finding for Phase 6 (RLS). -->

# I-VSD Report: CTO Audit Remediation

Last Updated: 2026-09-02 Europe/Brussels

## Report Identity

- **Task:** `cto-audit-remediation`
- **Mode:** Planning
- **Status:** `current`
- **Disposition:** `plan-aligned`
- **Reviewed Input Revision:** Planning evidence packet from CTO audit session (2026-09-02)
- **Change Classification:** Non-Behavioral Delta (Phases 1–5) + Behavioral Delta (Phase 6)

## Assessment

Phases 1–5 are **pure internal architecture refactors** with zero externally observable behavior changes. Phase 6 (PostgreSQL RLS) adds a defense-in-depth security layer — no user-facing behavior changes, but a meaningful data protection improvement.

### Applicable Principles

| Principle | Applicability | Rationale |
|---|---|---|
| Self-hosting promise | Preserved | All 5 database providers remain supported. RLS applies only to PostgreSQL; other providers continue with EF Core query filters. |
| Provider responsibility for data stewardship | **Phase 6 — APPLICABLE** | The provider has an obligation of *amānah* (trustworthiness) to protect tenant data. RLS adds database-level enforcement that prevents cross-tenant access even when application code errs. |
| Privacy / PII | Phase 6 — Strengthened | RLS reduces the risk of accidental PII exposure across tenant boundaries. |
| Monetization / fairness | Not affected | No pricing, paywall, or access control changes. |
| Content moderation / AI behavior | Not affected | No content or AI pipeline changes. |
| Accessibility / inclusion | Not affected | No UI changes. |

### Findings

- **IVSD-F001**: Phases 1–5 — No material I-VSD findings. Pure internal refactoring.
- **IVSD-F002**: Phase 6 — Provider has an Islamic obligation (*amānah*) to implement the strongest available defense against tenant data cross-contamination. RLS is the database-level safety net that catches developer error, `IgnoreQueryFilters()` misuse, raw SQL queries, and direct database access. Implementing RLS on ALL tenant-scoped tables (not selectively) is the responsible course.

### Mitigations

- **IVSD-M001**: Phases 1–5 — No mitigations required.
- **IVSD-M002**: Phase 6 — RLS must cover ALL tenant-scoped entity tables without exception. Invariant-breaker tests must prove that bypassing EF Core filters does NOT bypass tenant isolation. Runtime PostgreSQL role must have `NOBYPASSRLS`.

### Escalation

No scholarly escalation needed. The *amānah* principle is well-established and directly applicable to data stewardship without requiring a religious ruling.

### Refresh Triggers

- If a future change removes a database provider entirely, reassess self-hosting promise.
- If RLS is selectively applied (not all tenant tables), reassess IVSD-F002.
- If a new data access pattern bypasses both EF Core filters AND RLS, escalate.
