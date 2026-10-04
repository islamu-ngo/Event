<!-- ABOUTME: Reusable I-VSD output templates for audits, memos, reviews, strategy, governance, and postmortems. -->
<!-- ABOUTME: Ensures formal outputs include scope, evidence, claim boundaries, findings, recommendations, and validation gaps. -->

# Report Templates

Use [report-contract.md](report-contract.md) for every report's subject path, metadata, stable IDs, required headings, evidence fields, and lifecycle. Use [action-routing.md](action-routing.md) to select the standalone report kind and [context-discovery.md](context-discovery.md) before artifact-based findings.

Use [reader-first-reports.md](reader-first-reports.md) while filling these templates. Expand labels into explanatory paragraphs; retain the full technical vocabulary, ethical reasoning, traceability, alternatives, and uncertainty. The templates name information to supply, not a terse label-only output format.

## Persistence Boundary

Persist substantive findings, recommendations, advisories, audits, and consultations. Do not create reports for refusals, menus, context inventories, clarification questions, or agreement prompts.

## Required Headings For Generated Reports

The required headings and ordering live only in [report-contract.md](report-contract.md#required-report-headings). Templates below add action-specific content without replacing them. Preserve stable finding/mitigation IDs and lifecycle history when updating an existing matching report.

When a report reviews a concrete feature request, add `## Common Overlooked Failures And Outcomes` after `## Recommendations` or as an action-specific subsection under `## Findings`. Use [feature-risk-patterns.md](feature-risk-patterns.md) to name feature-specific mistakes, possible bad outcomes, provider questions, and positive outcomes from responsible implementation.

## Executive I-VSD Review

```text
Practical conclusion and who is affected:
What this status/disposition means for the next decision:
Scope:
Claim boundary: I-VSD design reasoning, not fatwa, certification, or proof.
Top risks:
Strengths:
Priority recommendations:
Technical mechanisms and ethical rationale behind the recommendations:
Stakeholders:
Validation gaps:
Evidence reviewed:
```

The executive section orients the reader; detailed findings, evidence, and recommendations still follow under the required headings.

## Detailed Moral Design Audit

```text
Practical problem and affected people:
Scope and exclusions:
Method: stakeholder map, provider responsibility map, principle/domain review, evidence classification.
Findings: severity, principle, domain, evidence, missing evidence, recommendation.
Per-finding explanation: concrete scenario, harm/benefit, provider-controlled decision,
technical mechanism, ethical rationale, mitigation, trade-offs, and evidence limits.
Roadmap: quick fixes, structural changes, evidence-building, escalation.
Validation gaps:
```

## Compliance-Style Checklist

Use categories and finding levels from [compliance-checks.md](compliance-checks.md). Include “not reviewed” instead of guessing. Explain why each material result matters, what evidence supports it, and what remains unproved; a Pass/Concern label or matrix alone is insufficient.

## Design Direction Memo

```text
User job:
Concrete before/after experience:
Provider responsibility:
Common overlooked failures and outcomes:
Protective defaults:
Pricing/limits/consent clarity:
Dark-pattern check:
Accessibility/localization:
Rejected manipulative alternatives:
Technical and ethical reasons for the chosen defaults:
Evidence needed:
```

## Implementation Or Code Review Memo

```text
What the change means for people using or operating the system:
Reviewed artifacts:
Principle-to-implementation traceability:
Architecture/security/data/account/workspace/tenant boundaries:
Common overlooked failures and outcomes:
Tests or docs supporting the claim:
Operational gaps:
Recommendations:
Why each recommendation addresses the risk, and its limitations/trade-offs:
```

## Moral Diff Review

Use [moral-diff-review-workflow.md](moral-diff-review-workflow.md) before PRs, pushes, or CI/CD handoff when the user asks to evaluate all changes. The review must include unpushed commits, commit titles/bodies, target/upstream branch context, staged changes, unstaged changes, and non-code artifacts: docs, configs, tests, policies, generated files, scripts, metadata, and untracked files intended for review.

Include a parseable YAML block in `## Findings` with `pass`, `hard_violations`, `soft_violations`, and `soft_violation_count`. Also include human-readable sections for diff scope, commit context reviewed, hard violations, soft violations, trust/promise changes, data/permission/telemetry changes, documentation/user-facing claim changes, and recommended fixes before PR. If commit context specifically and consistently justifies an otherwise suspicious permission, data, telemetry, or breaking-change diff, record the accepted justification instead of counting it as a violation.

## Product Strategy Moral Risk Report

Cover mission, funding, pricing, sponsorship, open source, self-hosting, lock-in, enshittification, command-oriented success, and autonomy from unethical pressure.

## AI/Data Governance Review

Cover source integrity, labels, hallucination/uncertainty, bias, intrusive signals, human escalation, non-personalized alternatives, retention, and access controls.

## Business Model And Monetization Review

Cover riba/gharar/deception concerns, data monetization, sponsor influence, hidden fees, cancellation, switching costs, stewardship, and scholarly/legal escalation.

## Incident Or Harm Postmortem

```text
Incident:
What happened to affected people, in concrete terms:
Affected stakeholders:
Provider responsibility breached:
Principles/domains:
Evidence:
Immediate correction:
Restitution or user repair:
Prevention:
How the prevention mechanism works and which failure it cannot rule out:
Operational validation needed:
Trust repair communication:
```
