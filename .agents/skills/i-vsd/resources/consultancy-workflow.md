<!-- ABOUTME: Professional I-VSD consultancy workflow from intake through recommendations and validation gaps. -->
<!-- ABOUTME: Supports advisory memos, executive reports, audits, compliance checks, and implementation reviews. -->

# Consultancy Workflow

## Intake

Capture product, provider, lifecycle stage, deployment model, jurisdictions, business model, known harms, religious commitments, users, affected communities, partners, and operational dependencies.

## Scope

State what is reviewed and what is not reviewed. Mark whether the work is design feedback, implementation traceability, stakeholder validation, operational audit, or scholarly/legal question.

## Stakeholder Map

Identify direct users, indirect affected people, vulnerable groups, administrators, maintainers, partners, future users, non-users affected by data/content, and communities affected by moderation or recommendations.

## Provider Responsibility Map

Review data, money, content, ranking, identity, moderation, infrastructure, support, public promises, governance, admin powers, and lifecycle commitments.

## Feature-Specific Risk Pattern

When the consultation is about adding or changing a concrete feature, use [feature-risk-patterns.md](feature-risk-patterns.md) before final recommendations. Identify common overlooked failures for that feature type, possible bad outcomes, provider questions that must be answered before implementation, and positive outcomes from a morally responsible implementation.

## Principle And Domain Review

Select relevant principles from [principles-and-domains.md](principles-and-domains.md). Review strategic, design, technical, operational, governance, and evaluation domains even if only one appears in the prompt.

## Evidence Gathering

Use [context-discovery.md](context-discovery.md) for artifact-based or project-context-based work before drawing moral conclusions. First send the concise user-facing context gate with only relevant workspace/repository/source-package context, project-context integrations, retrieval helpers, and user-provided paths, then ask for agreement before deep review. After confirmation, search documentation, README files, text artifacts, policies, planning docs, architecture decisions, configuration, tests, UI flows, code, logs, audits, interviews, metrics, terms, marketing copy, pricing pages, incident records, appeal records, support logs, data inventories, retention schedules, architecture diagrams, and relevant external project systems.

## Findings

Classify findings as strengths, concerns, high-risk violations, unknowns, or escalation needs. Include the principle, domain, stakeholder, evidence, missing evidence, severity, and validation level.

For feature consultations, include a `Common Overlooked Failures And Outcomes` section or finding group. It should explain feature-specific mistakes teams often miss, concrete harms and operational consequences, and the moral/compliance/audit benefits of implementing the feature responsibly.

## Recommendations

Group recommendations into quick fixes, design/policy changes, architecture changes, operational changes, evidence-building steps, and deferred scholarly/expert review.

## Output Modes

All output modes below MUST be persisted to a Markdown file under `islamic-value-sensitive-design/consultations/` (or `governance/` for constitutional/strategic policies) using the `i-vsd-*.md` naming pattern. Never deliver I-VSD findings, consultations, advisories, or recommendations only as inline conversation text. The conversation response should summarize what was written and point to the file path, not replace it. If no explicit action name maps to the request, derive a descriptive filename (e.g., `i-vsd-account-deletion-consultation.md`) and write the full output to that file.

- Short advisory memo: direct answer, basis, risks, caveat. Written to `i-vsd-short-advisory.md` or a topic-specific filename.
- Executive report: summary, risks, prioritized recommendations. Written to `i-vsd-executive-review.md`.
- Detailed audit: scope, method, evidence, findings, roadmap. Written to `i-vsd-detailed-audit.md`.
- Compliance checklist: pass/concern/fail/not reviewed/requires scholarly review. Written to `i-vsd-compliance-check.md`.
- Design feedback: defaults, pricing, consent, accessibility, localization, anti-manipulation. Written to `i-vsd-design-ux-review.md` or a topic-specific filename.
- Implementation review: trace principle to code, architecture, policy, tests, and operations. Written to `i-vsd-implementation-code-review.md` or `i-vsd-technical-review.md`.
- Consultation: any I-VSD-framed advisory that does not match a named action above. Written to a descriptive filename such as `i-vsd-<topic>-consultation.md`.
