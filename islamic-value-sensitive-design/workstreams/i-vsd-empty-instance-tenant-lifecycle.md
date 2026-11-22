# I-VSD Review of Empty Instances and Tenant Lifecycle

Last Updated: 2026-11-22

## Review Metadata

- Mode: planning
- Subject: Tenant-independent initialization, selection, and offboarding
- Workstream: empty-instance-tenant-lifecycle
- Report kind: provider responsibility and lifecycle design review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-22
- Reviewed input: `.worktrees/pr79-testing-assurance-follow-up/` at `e85aa0d350e2dc3d26bb76059b116f7e0fa87e80`, labeled assurance-only working-tree observations, and the rebaselined tenant lifecycle triad
- Supersedes: none

## Executive Summary

Platform administrators should be able to configure, export and recover a healthy installation before its first tenant and after its last tenant leaves. People whose data belongs to a tenant should retain its isolation throughout that process. The design guides an operator toward creating a real tenant, lets a host complete only what they are responsible for, distinguishes the future tenant owner's authority, and makes permanent cleanup progress visible. The plan can proceed to technical review. Runtime outcomes still require evidence at each PR and the final integrated gate.

## Scope

Provider-controlled defaults, configuration portability, setup guidance and accessibility, installer and owner handoff, authority and isolation, suspension, permanent removal, observable operation progress, retention and recovery in separate and combined deployments.

## Claim Boundary

This is assessment of software design and provider responsibilities from source and planned acceptance evidence. No religious ruling, legal retention opinion, operational assurance, or evidence of stakeholder acceptance was obtained.

## Findings

### IVSD-F001 — Operators need an honest, recoverable empty state

Status accepted. Medium severity. Design requirement. A small self-hoster can otherwise lose the only path to administer a healthy installation, or receive a placeholder that misstates whose community they are setting up. The provider controls the initial state, defaults and navigation. E2 and E3 show the current coupling and existing bootstrap authority at source level. `IVSD-M001` belongs to the first PR's setup and status acceptance.

### IVSD-F002 — Changes in authority must preserve tenant boundaries

Status accepted. High severity. Security design concern. Members and event attendees depend on the provider to keep their records scoped during redirection, activation, or replacement. A fixed identity or stale resolver can confuse distinct communities and their authorized representatives. E3–E5 identify current routing, persistence and mutation assumptions. E12 establishes tenant-independent instance authority and fail-closed mixed-batch behavior already implemented in the follow-up. `IVSD-M002` builds on that boundary and requires tests that observe both request admission and committed data under real concurrency.

### IVSD-F003 — Offboarding must communicate what is still held

Status accepted. High severity. Provider responsibility concern. An operator and affected community need to distinguish loss of access from data removal. A status that implies completed erasure while storage, delivery, backups or required evidence remain can prevent a person from seeking further action. E4 shows different paths for lifecycle markers and tenant deletion. `IVSD-M003` owns clear scope, retention decision evidence, exact cleanup progress and an explicit blocker when an obligation is unresolved.

### IVSD-F004 — Instance portability should carry only the intended scope

Status accepted. High severity. Data minimization and operator-control concern. The current shape requires a tenant even when the operator only needs to configure or move platform settings. A broadly scoped download can carry settings for communities outside that purpose. E9 identifies the cardinality restrictions and shared snapshot reader at source level. `IVSD-M004` makes the narrow scope the default and verifies that omitted tenant configuration neither enumerates tenant data in an instance-only export nor changes tenant graphs on apply.

### IVSD-F005 — A future tenant owner needs independent authority and a clear handoff

Status accepted. High severity. Trust and accessibility design concern. An installer may be able to configure identity providers and hosting but may lack the information and authority to speak for the community. Forcing tenant fields during platform setup pressures that installer to invent data, retain owner access, or share setup proof. E10 and E11 identify the existing purpose-bound setup and tenant grant mechanisms. `IVSD-M005` uses explicit, verified tenant ownership, treats re-entry and pause as normal, and confines recovery controls to the entitled role.

## Recommendations

- **IVSD-M001** Preserve tenant-independent operator access and guide first setup through HAL-backed accessible navigation. Allow the operator to defer. Require validated tenant data and an authorized submission before creating a tenant identity.
- **IVSD-M002** Use exact committed selection and lifecycle eligibility with tenant-qualified grants/effects. Race-test creation, selection, role and offboarding changes, and verify old grants cannot cross into a new tenant.
- **IVSD-M003** Require exact-target confirmation and audited progress, retain only approved evidence, preserve retention obligations, and explain blocked outcomes and data still held. Validate obligations at the release gate before enabling destructive actions.
- **IVSD-M004** Make instance-only import and export an explicit capability, with tenant-inclusive export selected deliberately. Check authority, secret exclusion, metadata agreement, exact-target package behavior and absence of tenant writes from omission. Verify these controls in the portability PR.
- **IVSD-M005** Give setup a primary create/resume path and a usable secondary finish-without-tenant path. Expire proof at instance completion regardless of tenant count. Let the recipient sign in independently with a verified, exact tenant grant and complete tenant setup. Verify each route and principal in the setup and routing PR, including ordinary tenant users and stale setup credentials.

The permanent-minimum model made recovery depend on creating an additional tenant or performing a bundled reset. A fixed placeholder weakened identity clarity. An in-place reset would combine unrelated duties for data removal, obligations and third-party credentials. The selected design makes owner and state choices visible in the workflow and gives new organizations their own identity.

## Stakeholders

Self-hosters, platform operators, independent API clients, tenant admins, tenant members and attendees, external processors, and support responders. Account for users who depend on the tenant without holding an administrative role and operators requiring keyboard interaction or localized instructions.

## I-VSD Principles And Domains

Trust and stewardship (amānah) inform exact authority and accountable custody. Justice (ʿadl) informs isolation and predictable access for participants and administrators. Avoidance of harm informs fail-closed routing, reversible suspension and cautious permanent removal. Truthfulness informs distinct pending, blocked and settled outcomes and meaningful notice. These map to architecture, privacy, product defaults, accessibility, operations, and governance.

## Common Overlooked Failures And Outcomes

A failed migration can leave a healthy process with an inconsistent tenant selection. An empty manifest can be mistaken for an instruction to remove existing tenants. A stale setup cookie can appear to grant rights after platform-only completion. A suspended tenant's old link can remain valid in cache. A delayed job can act after a replacement begins. A deleted row can leave obligations with another provider. The plan covers these cases with explicit scope and omission semantics, current admission, exact tenant-binding of work, and reconciliation records.

## Validation Gaps

Source evidence and a testable design only. Verify host, authorization, data consistency and real-provider outcomes, user comprehension of cleanup states, localized keyboard recovery and actual retention ownership as the PRs execute.

## Escalation Needed

Before enabling permanent removal, obtain accountable policy/legal review where a hold, financial obligation or external operator responsibility lacks an existing approved rule. Route any religious-legal question arising from future requirements to qualified Sunni scholarly authority.

## Evidence Reviewed

E2–E5 and E9–E12 in the [branch evidence packet](../../dev/active/empty-instance-tenant-lifecycle/empty-instance-tenant-lifecycle-context.md), requirements R1–R11 and the matching [plan](../../dev/active/empty-instance-tenant-lifecycle/empty-instance-tenant-lifecycle-plan.md) and [task ledger](../../dev/active/empty-instance-tenant-lifecycle/empty-instance-tenant-lifecycle-tasks.md). External sources in the evidence packet informed engineering practices only.

## Missing Evidence

Merged source revision, runtime/host acceptance, stakeholder feedback, specific operator retention policy and real settlement evidence for the affected external providers.

## Context Inventory

Application setup, instance and tenant lifecycle, native DB custody, tenant projection, HTTP/BFF routing, docs, and planned client recovery. The user explicitly chose empty-instance support, guided tenant creation and greenfield API freedom. Prior economic, payment and privacy obligations are inherited.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
|---|---|---|---|---|
| 2026-11-21 | Draft | Current | Planning design revalidated against requirement and task mapping | R1–R8 and Phases 1–4 |
| 2026-11-22 | Current | Current | Revalidated after adding scoped configuration portability, first-run navigation, and verified recipient handoff | R9–R11, Phase 0 and the revised setup and routing phases |
| 2026-11-22 | Current | Current | Rebaselined after user corrected the source worktree | E1–E12 at `e85aa0d350`, including inherited ADR-021 |

## Planning Handoff

- Workstream: empty-instance-tenant-lifecycle
- Status: current
- Reviewed input: Empty Instances and Tenant Lifecycle plan, tasks and evidence packet
- Findings and mitigations: `IVSD-F001 → IVSD-M001`, `IVSD-F002 → IVSD-M002`, `IVSD-F003 → IVSD-M003`, `IVSD-F004 → IVSD-M004`, `IVSD-F005 → IVSD-M005`
- Required plan mappings: M001 to R1/R2/R7/R10 and T1.4/T2.3/T2.3a, M002 to R3/R4/R8 and T1.2/T1.5/T2.1/T3.1, M003 to R5/R6 and T3.2/T4.1–T4.3, M004 to R9 and T0.2–T0.5, M005 to R10/R11 and T1.4a/T2.1/T2.3/T2.3a
- Escalations required before: destructive release if applicable policy ownership or approval remains absent
- Refresh triggers: material changes to admission authority, data disclosure, retention, control-plane access or the offboarding result contract
