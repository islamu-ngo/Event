# I-VSD Planning Assessment: Governed Changelog Publication

Last Updated: 2026-09-30 Europe/Brussels

## Review Metadata

- Mode: planning
- Subject: governed-changelog-publication
- Workstream: governed-changelog-publication
- Report kind: workstream planning assessment
- Report status: draft
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-30
- Reviewed input: `dev/backlog/governed-changelog-publication.md` (2026-09-10); predecessor `islamic-value-sensitive-design/workstreams/i-vsd-governed-release-public-changelog.md` (2026-09-10, plan-aligned); source baseline on `develop`.
- Supersedes: none. This is the new workstream report for publication; the predecessor workstream report covers the categorization slice only.
- Report contract: version 1; integration contract: version 1.

This assessment is drafted during the planning intake for the `governed-changelog-publication` workstream. It inherits the four accepted findings (IVSD-F001 through IVSD-F004) and their mitigations (IVSD-M001 through IVSD-M004) from the predecessor workstream report's publication handoff requirements. Those findings were allocated stable IDs in the predecessor and remain stable here. This report maps them to the new triad's scenarios and tasks.

## Scope

This workstream delivers the publication pipeline that the predecessor categorization workstream explicitly deferred: an offline single-page generator (`sync-public-changelog` CLI command in `eng/release/`), protected branch transport from a `docs/publication` acceptance branch through a separate `docs/gitbook-sync` mutable mirror, retained authorization inventory reconciliation, recovery and drift detection, and the CI workflow that connects them.

A quarantined renderer hardening prerequisite (loop balancing and markdown escaping application in `GitCliffRenderer.cs`) must be resolved before the publisher can claim literal-title display safety.

Provider-controlled responsibilities carried from predecessor: what adopters are told, who authorizes disclosure, whose identities become permanent, who may accept or mutate public content, and whether self-hosters can verify and recover without a hosted account.

Non-applicable domains (unchanged from predecessor): monetary contracts, AI/ranking, moderation, tenant APIs/databases, tracking, new application secrets.

## Claim Boundary

This is provider-responsibility design reasoning informed by selected Sunni ethical principles. It is not a fatwa, religious-legal ruling, Sharia/product certification, legal opinion, security approval, technical-readiness certificate, or assurance of stakeholder outcomes. No legal or scholarly approval was obtained or inferred.

Evidence supports level 2 design validation from inspected backlog specification, predecessor I-VSD report, existing code and tests, and adapter contracts. No build, publication, signing, live GitBook inspection, or activation was performed for this report.

## Findings

All four findings are inherited from the predecessor report with their stable IDs. They are mapped to the new workstream's tasks and scenarios.

### IVSD-F001 — Public-record truthfulness includes both category completeness and publication authority

- Lifecycle: accepted (inherited).
- Severity: High.
- Workstream mapping: Phase 1 (offline generator: all-line union, omission-fails-closed invariant); Phase 3 (transport: protected acceptance vs mutable mirror separation); Phase 4 (recovery: coalesced/missing dispatch reconciliation from retained inventory).
- Task mapping: Generator must preserve every previously accepted release identity. Omission of any existing authorized entry fails closed. Protected `docs/publication` branch acceptance is separate from mutable GitBook mirror. Drift is reported, never silently repaired.
- Escalation: G01 (before authoritative use) is inherited from predecessor. G02 (before publication activation or claiming entire experience delivered) applies to this workstream's activation phase.

### IVSD-F002 — Embargo timing remains a named human decision, including retries

- Lifecycle: accepted (inherited).
- Severity: High.
- Workstream mapping: Phase 1 (generator reads authorization inventory but cannot grant disclosure); Phase 3 (transport: final-lane inventory retains human authorization before dispatch); Phase 4 (recovery: missing authorization stops publication including retry and reconciliation).
- Task mapping: The generator reads the retained authorization inventory but has no authority to disclose a release. Missing authorization or incomplete inventory stops publication. The accountable human's judgment about disclosure timing is preserved through the authorization inventory, not automated by a timer or queue event.
- Escalation: G03 (before each disclosure and before the first governed security release) applies.

### IVSD-F003 — Identity minimization retains a recognition cost

- Lifecycle: accepted (inherited).
- Severity: Moderate.
- Workstream mapping: Phase 1 (generator entry format: no author handles, raw PR bodies, or committer identities in the public page); Phase 4 (receipts: no contributor identity in publication receipts).
- Task mapping: Generated changelog entries contain curated summaries composed from validated policy fields, never raw commit messages or author handles. Publication receipts record tag/version/digest data only, never contributor identity.
- Escalation: G04 (contributor-notice obligation before first governed release use) is inherited.

### IVSD-F004 — Offline verifiability requires retained evidence, not just a tag name

- Lifecycle: accepted (inherited).
- Severity: Moderate (high operational consequences for adopters).
- Workstream mapping: Phase 1 (generator consumes local Git objects and retained final evidence, not forge API or SaaS state); Phase 2 (prerequisite: renderer hardening ensures literal-title display for verification links); Phase 4 (recovery: retained evidence survives branch movement/deletion; historical evidence is never regenerated with today's template).
- Task mapping: The generator requires full annotated tag object IDs pinned to the authorized inventory. Verification links in each entry use immutable tag/path references. Generator needs no credentials or network access. Historical final evidence may live in retained artifacts rather than committed at `B`.
- Escalation: G01 and G02 inherited.

## Mitigations

| ID | Required behavior | Owner | Workstream task |
| --- | --- | --- | --- |
| IVSD-M001 | Publish only a derived, hash/tag-attributed page from the complete authorized all-line set. Protect acceptance separately from the mutable mirror. Preserve/report drift. | Platform/Ops + Documentation maintainer | Phase 1 generator, Phase 3 transport, Phase 4 drift |
| IVSD-M002 | Final-lane inventory retains human authorization and full verified tag-object identity before dispatch. Missing authorization stops publication. | Security/release steward + Platform/Ops | Phase 3 transport authorization check, Phase 4 reconciliation |
| IVSD-M003 | No contributor identity in generated pages or publication receipts. | Release-engine maintainer | Phase 1 entry format, Phase 4 receipt format |
| IVSD-M004 | Retain tag-object-bound verification after carrier-branch movement/deletion. Never regenerate historical final evidence. | Release-engine maintainer + Platform/Ops | Phase 1 pinned inputs, Phase 4 retained evidence |

## Escalation Gates

| Gate | Trigger | Required before |
| --- | --- | --- |
| G01 | First authoritative categorized release use | Implementation activation |
| G02 | Publication activation or claiming entire experience delivered | Workstream close |
| G03 | Each disclosure; first governed security release | Security release publication |
| G04 | Contributor-notice obligation | First governed release use |

## Refresh Triggers

Revalidate this report when the implementation plan materially changes:
- Product scope, affected stakeholders, or provider authority;
- Which public fields cross the restricted boundary (embargo/disclosure);
- Data collection, retention, or trust boundaries in publication receipts;
- The authorization inventory model or who can grant disclosure;
- Any mitigation, escalation gate, or implementation task mapped from an IVSD-* ID.

Formatting, wording, task-status updates, evidence-location corrections, and architecture details that preserve provider-controlled behavior do not invalidate this report.

## Planning Handoff

- Workstream: governed-changelog-publication
- Status: draft
- Reviewed input: `dev/backlog/governed-changelog-publication.md` + predecessor I-VSD report
- Findings and mitigations: IVSD-F001 → IVSD-M001, IVSD-F002 → IVSD-M002, IVSD-F003 → IVSD-M003, IVSD-F004 → IVSD-M004
- Required plan mappings: F001 → Phase 1 all-line union + Phase 3 protected acceptance + Phase 4 reconciliation; F002 → Phase 3 authorization inventory + Phase 4 missing-authorization stop; F003 → Phase 1 identity-free entries + Phase 4 identity-free receipts; F004 → Phase 1 pinned local inputs + Phase 4 retained evidence
- Escalations required before: G01/G02 before activation; G03 before security release publication; G04 before first governed release use
- Refresh triggers: scope/stakeholder/authority/disclosure/retention/trust-boundary/mitigation changes
