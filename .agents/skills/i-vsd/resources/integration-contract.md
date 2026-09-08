<!-- ABOUTME: Invocation and handoff contract for standalone, planning, and CTO-review I-VSD modes. -->
<!-- ABOUTME: Preserves action menus and context questions while preventing stale integrated reports. -->

# I-VSD Integration Contract

Exactly one mode is active for a request. I-VSD remains able to pause and ask for material context in every mode.

```yaml
integration_contract_version: 1
modes: [standalone, planning, plan-review]
standalone_no_context: action-menu
standalone_alignment: grill-me
routing_response_persistence: none
substantive_output_persistence: markdown
planning_request_satisfies_context_agreement: true
cto_review_grants_user_approval: false
material_rewrite_invalidates_i_vsd: true
```

## Standalone Mode

Use when the user invokes I-VSD directly for a consultation, compliance-style review, feature decision, audit, guided discovery, or moral diff.

1. No product/action context: return the action menu and ask the user to choose; do not create a report.
2. Covered context but no clear action: infer the likely actions and ask the user to confirm; do not create a report yet.
3. Named action with missing material context: stop and ask only the questions whose answers could change scope, findings, recommendations, evidence level, or output identity.
4. Guided discovery: continue in question batches until its readiness gate is met.
5. Sufficient context and agreement: create or update the canonical subject report from [report-contract.md](report-contract.md).

Refusals, menus, context inventories, clarification questions, and agreement prompts are routing responses, not substantive I-VSD outputs. They remain conversational. Findings, recommendations, advisories, audits, and consultations are substantive and must be persisted.

## Planning Mode

Use when `implementation-plan` invokes I-VSD for a named workstream. The explicit user request to create or re-baseline the implementation plan satisfies the normal agreement prompt for this integrated intake only; it does not authorize assumptions or suppress necessary questions.

1. The planner performs one shared repository/current-state investigation and supplies the evidence packet, stable task name, proposed scope, stakeholders, and provider-controlled decisions.
2. I-VSD creates or updates `islamic-value-sensitive-design/i-vsd-<task-name>.md` as `draft`, with stable findings, mitigations, escalation questions, and refresh triggers.
3. I-VSD or `grill-me` may stop for material user context. Do not draft around an unresolved decision that changes provider responsibility or task structure.
4. The planner resolves remaining requirements and drafts behavior, architecture, scenarios, and tasks.
5. I-VSD revalidates the completed triad against the same evidence packet plus proposed design. It sets `plan-aligned`, `changes-required`, or `escalation-required`.
6. Plan Section 9 maps every open/accepted `IVSD-Fnnn` and `IVSD-Mnnn` to a scenario/task, explicit non-applicability, or named escalation gate.

Planning mode owns no architecture sequence or task status. It supplies the moral/provider-responsibility constraints that planning must implement.

## Plan Review Mode

Use when `senior-cto-feedback` reviews or rewrites an implementation-plan workstream.

1. Bind the review to the exact plan, tasks, and I-VSD revisions; context supplies current status but is not a substitute for those reviewed inputs.
2. The Senior CTO review directly updates the workstream triad (`plan.md`, `context.md`, `tasks.md`) without writing any separate `*-cto-review.md` files.
3. A missing or stale I-VSD report yields `Changes required` in plan metadata, never technical approval.
4. Triad updates are applied directly without requiring prior user approval. If the update modifies provider-controlled risks or `IVSD-*` mappings, it marks the I-VSD report stale and requires planning-mode revalidation.
5. A material rewrite marks the I-VSD report stale and routes back through planning-mode revalidation before fresh CTO alignment.

CTO review grants technical readiness only. It cannot grant user approval, issue scholarly/legal conclusions, or convert I-VSD evidence into certification.

## Planning Handoff

Planning and plan-review reports include:

```text
## Planning Handoff
- Workstream: <task-name>
- Status: draft | current | stale | superseded
- Reviewed input: <workstream name or plan title>
- Findings and mitigations: IVSD-Fnnn -> IVSD-Mnnn
- Required plan mappings: <ID -> scenario/task/non-applicability/escalation>
- Escalations required before: planning approval | implementation | release
- Refresh triggers: <specific material changes>
```

## Refresh Triggers

Revalidate I-VSD when a plan or CTO rewrite materially changes:

- product scope, affected stakeholders, or provider authority;
- user defaults, consent, deletion, export, appeal, moderation, or accessibility behavior;
- data collection, retention, telemetry, AI/ranking, permissions, or trust boundaries;
- monetization, payments, refunds, sponsorship, portability, self-hosting, or deployment responsibility;
- a mitigation, escalation gate, or implementation task mapped from an `IVSD-*` ID.

Formatting, wording, task-status updates, evidence-location corrections, and architecture details that preserve provider-controlled behavior do not invalidate the report.

## Authority Boundaries

- I-VSD owns provider-responsibility findings, evidence levels, mitigations, and escalation.
- `implementation-plan` owns behavior, architecture, scenarios, sequencing, and task mappings.
- `tasks.md` owns execution; `context.md` owns current state and handoff.
- `senior-cto-feedback` owns technical-readiness review for a bound revision.
- Qualified authorities own scholarly/legal determinations.
- The user owns scope decisions and implementation approval.

The dependency is one-way: planning and CTO skills load this contract; I-VSD consumes supplied artifact paths and never loads or controls those skills.
