---
name: i-vsd
description: "Load when evaluating an Islamic software feature, provider policy, AI behavior, moderation, monetization, privacy, defaults, ranking, self-hosting, or deployment model for provider responsibility and Islamic value-sensitive design; not for generic fiqh rulings or ordinary technical implementation."
type: workflow
enforcement: suggest
priority: high
---
<!-- ABOUTME: Workflow for Islamic Value Sensitive Design of provider-mediated software. -->
<!-- ABOUTME: Preserves scope, evidence, scholarly, and durable-report boundaries. -->

# Islamic Value Sensitive Design

## Resources

- [Integration contract](resources/integration-contract.md) — load to select standalone, planning, or plan-review mode and enforce handoff freshness.
- [Report contract](resources/report-contract.md) — load before creating or materially updating any report.
- [Action routing](resources/action-routing.md) — load for standalone action selection, menu behavior, and domain lenses.
- [Grill-Me alignment](../grill-me/SKILL.md) — load after standalone action/context routing to resolve material user decisions before substantive analysis.
- [Resource index](resources/index.md) — load only the workflow, evidence, or domain resource selected by the current mode/action.

## Rules

- Apply [scope boundaries](resources/scope-boundaries.md) first; I-VSD is provider-responsibility design reasoning, not a fatwa, Sharia/product certification, or proof of ethical outcomes.
- Escalate halal/haram/makrooh/wajib, riba, contested religious-content, and other religious-legal conclusions to qualified Sunni scholarly authority.
- Selected Sunni ethical principles are normative; laws and industry standards may operationalize them only where compatible.
- Review responsibility across strategy, business model, UX, architecture, data/AI, operations, governance, support, portability, and evaluation—not only the surface named in the prompt.
- Trace findings to principles, affected stakeholders, provider-controlled decisions, evidence reviewed, missing evidence, rejected alternatives, and concrete mitigations.
- Persist substantive I-VSD findings under `islamic-value-sensitive-design/` partitioned by purpose (`governance/` for constitutional policies, `consultations/` for domain deep dives, and `workstreams/` for `dev/active/<task>` planning deliverables) using the `i-vsd-*.md` naming pattern; menus, refusals, context inventories, clarification questions, and agreement prompts remain conversational.
- State uncertainty and evidence limits; never replace absent stakeholder, operational, audit, or scholarly evidence with confidence.

## Invocation Modes

- **Standalone**: with no context, return the [action menu](resources/action-routing.md). After action/context routing, follow [Grill-Me](../grill-me/SKILL.md): resolve repository facts directly, recommend one answer, ask one material decision question per response, and continue until the report is aligned or unresolved risks are explicitly deferred. Guided discovery uses its own interview areas with the same one-question decision discipline.
- **Planning**: consume the planner's shared evidence packet, create a draft workstream report, allow material questions, then revalidate the completed plan/task mapping before declaring it plan-aligned.
- **Plan review**: bind the moral review to exact plan/I-VSD revisions; a material CTO rewrite makes the report stale and requires revalidation.

Follow [integration-contract.md](resources/integration-contract.md). Load only the routed resources, never the full library.

## Output contract

Follow [report-contract.md](resources/report-contract.md) for identity, metadata, stable finding/mitigation IDs, lifecycle, evidence levels, and required headings. Planning and plan-review reports also include its `Planning Handoff`.

The chat response names the written file and summarizes the recommendation and unresolved evidence; it does not duplicate the report.

## Verification

- Confirm the request is provider-mediated software responsibility and not a religious ruling.
- Confirm no-context standalone invocation still returns actions without creating a report.
- Confirm the workflow can stop for material context in every mode.
- For substantive output, confirm a Markdown report was created or updated at the mapped path; for a routing response, confirm no report was written.
- Check report identity, status, disposition, evidence revision, stable finding IDs, and every authority boundary.
- For planning/review, confirm every material `IVSD-*` ID maps to a scenario/task, explicit non-applicability, or escalation gate and the reviewed revision is current.
- Run host-repository link, schema, and whitespace checks when this skill or its resources change.
