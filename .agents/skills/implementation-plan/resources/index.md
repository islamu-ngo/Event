<!-- ABOUTME: Reading map for the implementation-plan skill's durable workflow resources. -->
<!-- ABOUTME: Routes planning agents to the PR scope contract, investigation, artifact templates, and quality gates. -->

# Implementation Plan Resources

Load a resource only when the named decision is open:

1. [PR Scope Contract](pr-scope-contract.md) — shared by planning, review and execution. Use it for programme -> PR -> phase -> task hierarchy, stable scope IDs, registry fields, readiness, delegated snapshots, reconciliation and programme retention.
2. [Investigation Workflow](investigation-workflow.md) — evidence collection, intent classification, existing-programme re-baselining, current-state reporting, I-VSD/grill-me intake, and PR scope slicing.
3. [Plan Template](plan-template.md) — writing or re-baselining `<task-name>-plan.md`, including PR scope definitions and verification strategy.
4. [Operational Artifacts](operational-artifacts.md) — writing the `tasks.md` PR registry and ledger and the `context.md` programme resume pointer.
5. [Quality Gates](quality-gates.md) — before handing the programme to the user, a reviewer or an implementation agent.

Execution-side selection, extraction, reconciliation and cleanup steps live in the [executor lifecycle](../../implement-tasks/resources/pr-execution-lifecycle.md); planning states outcomes and ownership but never performs them.

This skill and its resources are the standard tool-neutral implementation-planning workflow.
