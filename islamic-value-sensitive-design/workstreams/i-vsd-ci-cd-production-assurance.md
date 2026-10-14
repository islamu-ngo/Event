# CI/CD Production Assurance - Provider Responsibility Review

Last Updated: 2026-10-13 Europe/Brussels

## Review Metadata
- Mode: planning
- Subject: Faster verification and evidence-bound production promotion
- Workstream: ci-cd-production-assurance
- Report kind: operational design assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-10-13
- Reviewed input: develop `200aeacb9c23289553c813b443a44d3555d9b850`, observed working-tree workflows, GitHub run and settings evidence
- Supersedes: none

## Executive Summary

Slow checks burden contributors, but a faster green check that omits security tests or approves the wrong deployment would transfer that burden to operators and users. The recommended design reduces serial work, retains every applicable assertion, and makes release approval depend on the exact candidate, image digests, security evidence, and recovery plan.

The provider must distinguish a passing test selection from complete release assurance. Live branch rules currently require only the build/test status, while production routing accepts broad version tags. Existing signed-release governance is not activated. These are substantive assurance gaps, not merely performance problems. Automation should prepare and verify releases, while preserving protected production approval and the documented separation between tooling promotion and release signing.

Revision 1 of the completed plan/context/tasks triad maps every finding and mitigation to named scenarios and implementation tasks. The assessment is plan-aligned: its required controls are represented in that design. It is not a claim that production is safe or that stakeholder outcomes have been demonstrated.

## Scope

GitHub Actions verification, source-equivalent PR evidence reuse, immutable container promotion, release trust activation, production approval, deployment completion, migration recovery, and evidence retention. Application business behavior and religious-content policy are outside this workstream.

## Claim Boundary

This is provider-responsibility reasoning, not a religious ruling, security certification, licensing clearance, or operational audit. Repository and GitHub settings were inspected; Coolify configuration, production recovery rehearsals, signing authority, and stakeholder acceptance were not.

## Findings

### IVSD-F001: Faster checks must retain meaningful protection

- Lifecycle: accepted; severity: high; claim type: design risk.
- Principle/domain: amanah (responsible stewardship), avoiding preventable harm; reliability and truthful communication.
- Stakeholders: contributors, operators, tenant administrators, and users whose data or authority the tests protect.
- Provider-controlled decision: how tests are selected, parallelized, counted, and reported.
- Evidence: `.github/workflows/_build-test.yml`, `security-tests.yml`, `TESTING.md`, run `38444656942`; validation level: repository and provider observations.
- Mitigation IVSD-M001: preserve the gate inventory and test-count floors, keep shared-resource serialization, reject missing/failed/cancelled selected gates, and identify reused or missing measurements explicitly.
- Owner/next validation: platform maintainer; implementation policy regressions and real workflow receipts.

The practical risk is a green status with fewer assertions than before. Separate runners may reduce elapsed time without changing assertions; deleting a suite, loosening a timeout-sensitive assertion, or hiding a missing coverage report cannot. Negative gate tests must demonstrate that a failed dependency still prevents admission.

### IVSD-F002: Production must identify and recover the actual release

- Lifecycle: accepted; severity: high; claim type: observed control gap and design risk.
- Principle/domain: amanah, avoiding preventable harm; operations and accountability.
- Stakeholders: hosted users, self-hosting operators, and maintainers recovering a failed upgrade.
- Provider-controlled decision: tag admission, stable aliases, deployed artifact identity, migration sequencing, and rollback authority.
- Evidence: `deploy-coolify.yml`, `.ci/actions/deploy-coolify/action.yml`, `RELEASE_POLICY.md`, `RELEASE_RUNBOOK.md`; validation level: repository observation; live deployment identity unverified.
- Mitigation IVSD-M002: bind evidence to preparation commit B and component digests, separate prereleases, await deployment completion and identify the running revision, require migration/recovery evidence, and prohibit automatic destructive restore.
- Owner/next validation: release operator and platform maintainer; candidate and deployment evidence.

An HTTP-successful webhook and healthy old application do not prove the intended release is running. Likewise, reverting an image after an incompatible database migration can make recovery worse. Automation must verify the deployed identity and readiness and stop when recovery requires an operator decision.

### IVSD-F003: Evidence reuse and automation must respect trust and privacy

- Lifecycle: accepted; severity: high; claim type: design risk.
- Principle/domain: amanah and confidentiality; security, privacy, governance, and resource stewardship.
- Stakeholders: contributors, operators, tenant users, and independent tooling promoters.
- Provider-controlled decision: evidence provenance, cache authority, credential exposure, signer separation, and artifact retention.
- Evidence: `CI_CD_GOVERNANCE.md`, `eng/release/README.md`, `RELEASE_RUNBOOK.md` trust activation, live rulesets/environments; validation level: repository and provider observations.
- Mitigation IVSD-M003: trusted policy evaluates reuse; expired, missing, mismatched, or untrusted results cause fresh execution. Fork code receives no production authority. Retain redacted, digest-bound evidence; keep signing private keys outside repository, chat, and CI secret stores.
- Owner/next validation: platform maintainer and separate release/tooling authorities; rejection tests and activation evidence.

Reusing equivalent-source verification saves compute, but only when the original result, source inputs, configuration, and policy are established. A PR-authored JSON file or cache entry is not verification authority. More automation must not collapse the two-person trust model or expose credentials through diagnostic artifacts.

## Recommendations

Implement IVSD-M001, IVSD-M002, and IVSD-M003 as acceptance conditions, not optional backlog work. Reject timeout increases alone, latest-commit-only Markdown heuristics, weaker CodeQL coverage, skipped security assertions, unsigned release activation, and image rebuilds during promotion. Independent jobs are preferred initially over shared compiled artifacts because they keep trust and fixture ownership understandable.

## Stakeholders

Contributors need bounded feedback and actionable failures. Operators need exact identity and recovery instructions. Users need preserved tenant, authorization, and privacy protections. Tooling promoters and release signers need separate authority. Self-hosting adopters need durable, independently interpretable evidence.

## I-VSD Principles And Domains

The applied principles are responsible stewardship, confidentiality, prevention of avoidable harm, and truthful reporting of assurance limits. They inform engineering controls; they do not determine a religious-legal conclusion. Applicable domains are infrastructure, governance, operations, portability, security, privacy, and evaluation.

## Validation Gaps

No changes or live recovery exercise have occurred. The performance target, fresh candidate matrix, signer activation, registry promotion, running digest observation, and rollback behavior require implementation evidence.

## Escalation Needed

No scholarly determination is required by the scoped engineering design. Before release activation, operators must supply two independent public signing authorities and a promoted bundle, confirm environment protections, and approve actual production changes. Changes to key custody, reduced checks, or destructive recovery require renewed alignment.

## Evidence Reviewed

- Local workflow/test inventory and current CodeQL/manual-build configuration.
- GitHub runs `38444656312` and `38444656942`, including job steps and CodeQL logs.
- Live main/develop rulesets and production environment policies on 2026-10-13.
- Internal CI governance, testing, release policy/runbook/checklist, and release trust documentation.

## Missing Evidence

Live Coolify capabilities/configuration, actual running image identity, verified recovery time/objectives, promoted signer roots, approved runner expenditure, and repeated post-change timings.

## Context Inventory

Pre-release repository; one proposed CI/CD programme; earlier CI repairs and concurrent Configurator extraction are separate work. Current work owns planning documents only.

## Common Overlooked Failures And Outcomes

A skipped dependent job can appear acceptable to branch protection. A doc-only follow-up can hide previously unverified code. A prerelease can move `latest`. A healthy old deployment can satisfy smoke probes. A restore can resurrect erased data. A new production run can overtake an older approved run. The plan must prevent these outcomes explicitly.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
|---|---|---|---|---|
| 2026-10-13 | none | draft | Evidence-grounded planning intake | Current repository, runs, and provider settings |
| 2026-10-13 | draft | current | Completed triad revalidated; every finding/mitigation has scenario/task ownership | ci-cd-production-assurance revision 1, plan Sections 3/9 and tasks 1.1-8.3 |

## Planning Handoff

- Workstream: ci-cd-production-assurance
- Status: current
- Reviewed input: CI/CD Production Assurance, revision 1
- Findings and mitigations: IVSD-F001 -> IVSD-M001; IVSD-F002 -> IVSD-M002; IVSD-F003 -> IVSD-M003
- Required plan mappings: F001/M001 -> scenarios S1-S3 and phases 1-5; F002/M002 -> S5-S8 and phases 5-8; F003/M003 -> S3-S6/S9 and phases 1/4/6/8
- Mapping validation: plan Section 9 names all six stable IDs and their exact task ranges; the tasks preserve test integrity, exact deployment identity/recovery, evidence provenance, and independent final authority.
- Escalations required before: release activation
- Refresh triggers: weakened coverage or gate policy, changed signer/approval authority, new credential exposure, broader artifact retention, destructive automated recovery, or altered production topology
