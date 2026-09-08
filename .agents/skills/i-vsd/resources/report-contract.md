<!-- ABOUTME: Canonical identity, schema, evidence, and lifecycle contract for I-VSD reports. -->
<!-- ABOUTME: Gives standalone and integrated workflows one durable report format without pinning prose. -->

# I-VSD Report Contract

This resource is the single authority for report paths, metadata, findings, lifecycle, and required headings. Other I-VSD resources link here instead of restating the contract.

```yaml
report_contract_version: 1
identity: subject-and-report-kind
finding_ids: stable
report_states: [draft, current, stale, superseded, closed]
dispositions: [advisory, ready-for-planning, plan-aligned, changes-required, escalation-required]
last_updated_required: true
```

## Report Identity

Determine identity before checking for an existing report:

- Planning workstream: `islamic-value-sensitive-design/i-vsd-<task-name>.md`.
- Standalone subject review: `islamic-value-sensitive-design/i-vsd-<subject>-<report-kind>.md`.
- Repository-wide review: `islamic-value-sensitive-design/i-vsd-repository-<report-kind>.md`.
- Moral diff review: `islamic-value-sensitive-design/i-vsd-<workstream-or-branch>-moral-diff-review.md`.
- Multiple reports: one subject-specific report per report kind plus `i-vsd-review-index.md`.

Action-only filenames such as `i-vsd-consultancy-report.md` are allowed only when the workspace has one declared subject and the report metadata names it. Never reuse a generic path for unrelated subjects.

## Review Metadata

Every new or materially updated report includes this block after `Last Updated`:

```text
## Review Metadata
- Mode: standalone | planning | plan-review
- Subject: <stable subject>
- Workstream: <task-name or none>
- Report kind: <action/report kind>
- Report status: draft | current | stale | superseded | closed
- Disposition: advisory | ready-for-planning | plan-aligned | changes-required | escalation-required
- Evidence cutoff: YYYY-MM-DD
- Reviewed input: <workstream name, Git object, or working-tree>
- Supersedes: <report path or none>
```

For committed repository audits, reference the Git commit object. For active implementation workstreams, name the workstream and list reviewed artifacts under `Evidence Reviewed`.

## Finding Contract

Every material finding uses a stable `IVSD-Fnnn` ID and records:

- lifecycle: `open`, `accepted`, `resolved`, `superseded`, or `not-reviewed`;
- severity and claim type;
- principle, domain, stakeholder, and provider-controlled decision;
- evidence IDs or locators and validation level;
- linked mitigation `IVSD-Mnnn`;
- owner or next validation;
- escalation boundary when applicable.

Every mitigation keeps its ID when wording or ownership changes. A superseded finding points to its replacement instead of disappearing. Compliance-style `Pass` requires cited scope-relevant evidence; absent evidence is `Concern`, `Not reviewed`, or `Requires scholarly review`.

## Required Report Headings

Every new or materially updated report includes:

```text
# <Report Title>

Last Updated: YYYY-MM-DD

## Review Metadata
## Scope
## Claim Boundary
## Findings
## Recommendations
## Stakeholders
## I-VSD Principles And Domains
## Validation Gaps
## Escalation Needed
## Evidence Reviewed
## Missing Evidence
## Context Inventory
## Review Lifecycle
```

Feature reports additionally include `## Common Overlooked Failures And Outcomes`. Planning and plan-review reports include `## Planning Handoff` from [integration-contract.md](integration-contract.md). Put rejected alternatives under `## Recommendations`; use `None considered` when no real alternative existed.

## Review Lifecycle

Record material transitions in a compact table:

```text
| Date | Previous status | New status | Trigger | Evidence/replacement |
```

- `draft`: context or decisions are incomplete.
- `current`: the report matches its named evidence revision.
- `stale`: a refresh trigger changed after review.
- `superseded`: another report/revision replaces it.
- `closed`: all findings are resolved, superseded, accepted with ownership, or escalated.

Never mark a stale report current by changing only `Last Updated`; re-evaluate affected findings and record the evidence revision.

## Update Rule

Update an existing report only when its subject and report kind match. Preserve still-valid findings, evidence, decisions, and lifecycle history. Normalize legacy reports to this contract when they are next materially updated; do not bulk-rewrite unrelated reports merely to satisfy the new format.

The report owns provider-responsibility reasoning, evidence, mitigations, and escalation. Architecture sequencing belongs in the implementation plan, execution status belongs in task-owned context/tasks, and technical readiness belongs in the CTO review.
