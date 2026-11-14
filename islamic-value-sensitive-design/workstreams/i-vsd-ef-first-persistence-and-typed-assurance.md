# EF-First Persistence And Typed Assurance — I-VSD Planning Review

Last Updated: 2026-11-02 Europe/Brussels

## Review Metadata
- Mode: planning
- Subject: Application-owned persistence, trustworthy tests, and retained database safeguards
- Workstream: ef-first-persistence-and-typed-assurance
- Report kind: implementation-planning provider-responsibility review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-02
- Reviewed input: PR79 working tree at HEAD `8b9d3aac41aaa0af8078300b6f436251c4566a55`, including uncommitted edits; initial repository audit and shared discovery packet
- Supersedes: none; complements the separate PR79 assurance workstream

## Executive Summary

People whose identity, payments, private locations, registration records, and moderation evidence are stored by the platform depend on consistent decisions and recoverable writes. Moving business rules out of SQL can make those responsibilities easier to understand and test. It can also remove safeguards accidentally if treated as a syntax conversion. The provider must preserve tenant separation, authority-first erasure, legal holds, payment idempotency, and atomic audit/outbox effects while replacing whole workflows with C# and EF Core.

The user explicitly rejects compatibility baggage and authorizes clean breaking designs during development. That resolves implementation compatibility; it does not authorize destroying retained data or weakening privacy and security. This report supplies design constraints, not a religious ruling, operational certification, or passing test evidence. The completed r1 triad was revalidated: all five findings/mitigations map to named scenarios and implementation tasks, provider authority and topology remain bounded, and no material scholarly/product-policy escalation is pending. The plan-aligned disposition means the proposed work includes those obligations; it does not mean they are implemented.

## Scope

All fourteen audit finding groups: SQL-owned workflows, ordinary SQL reads, manual row mapping, internal JSON command/result protocols, test setup and public-seam bypasses, directory exemptions, source fingerprints, reminder query amplification, SQL-dependent test coordination, hard-coded test credentials, and duplicated tenant-session probes. Primary storage supports PostgreSQL, SQLite, SQL Server, MariaDB, and MySQL. Privacy authority topology restrictions remain separate.

## Claim Boundary

This is provider-responsibility reasoning grounded in source inspection and framework capability research. It does not establish runtime correctness, financial compliance, stakeholder agreement, or scholarly approval. Existing business policy is preserved unless this plan explicitly corrects a defect; moving a rule does not change who is entitled to act.

## Findings

### IVSD-F001 — Removing SQL authority must preserve protection of entrusted records

Lifecycle: accepted; severity: critical; claim type: design obligation. Amanah (trustworthy custody) and avoidance of harm apply to attendees, administrators, account holders, and self-hosting operators. The provider controls application authorization, purpose-specific database access, transactions, and concurrency handling. Existing native workflows sometimes supply all of these together; simply granting broad access and deleting routines would separate storage from the rules that made it safe.

Evidence E01/E02/E03 establishes the architectural decision and existing SQL/EF split, not successful replacement. IVSD-M001 requires one Application/Domain authority, tenant-qualified storage rights, invariant-first tests, and atomic state/audit/outbox writes. Application and persistence maintainers own validation. A genuinely new trust boundary or permission purpose is an escalation; the already accepted credential-only compromise trade-off is not reopened routinely.

### IVSD-F002 — Erasure must remain durable and resist resurrection

Lifecycle: accepted; severity: critical; claim type: privacy obligation. Privacy, trust, and avoidance of harm apply to erased subjects and people mentioned in retained evidence. The provider controls durable intent, identity fencing, provider-work capture, local disposal, restore replay, legal holds, and retained evidence floors.

Evidence E04 shows distinct authority-first and local-settlement commits. IVSD-M002 requires preserving those boundaries and all supported topology constraints, including the separate external authority, held evidence, original transaction ownership, and uncertain-commit recovery. Privacy maintainers own adversarial anti-resurrection, outage, and restart tests. Altered retention or legal-hold policy requires fresh product/qualified legal review; this refactor does not make that change.

### IVSD-F003 — Payment and queue retries must not create duplicate effects

Lifecycle: accepted; severity: critical; claim type: reliability and fairness obligation. Trust and justice apply to purchasers, organizers, recipients, and operators. The provider controls idempotency identities, lease tokens, fencing, money state transitions, and delivery acknowledgements.

Evidence E05/E06 shows native queue workflows, typed alternatives, and documented commit-uncertainty limits. IVSD-M003 requires one winner under competing workers, stale-lease rejection, integer money semantics, durable outbox work, and no automatic replay of an operation whose external or cross-store outcome is unknown. Financial/reliability maintainers own validation. No fee, refund, payout, or entitlement policy change is authorized by this plan; such a change requires separate alignment.

### IVSD-F004 — Assurance must describe the path the application really uses

Lifecycle: accepted; severity: high; claim type: epistemic obligation. Truthfulness and accountability apply to contributors, reviewers, operators, and downstream adopters. The provider controls whether tests use realistic identities and graphs, exercise public contracts, and report actual provider coverage.

Evidence E07/E08 shows ordinary tests with disabled enforcement, SQL function bypasses, and source/folder exemptions. IVSD-M004 requires valid typed arrangements, purpose-qualified runtime contexts, independent expected outcomes, real-provider final evidence, and explicit disposition before deleting any test cohort. Testing maintainers own validation; counts or faster runs cannot substitute for a preserved invariant.

### IVSD-F005 — Maintainability and simpler operation must not conceal data exposure

Lifecycle: accepted; severity: high; claim type: maintainability and operational obligation. Stewardship, trust, and non-harm apply to future maintainers and self-hosting operators. The provider controls abstraction count, secret binding, diagnostic content, permission installation, and deployment instructions.

Evidence E01/E07/E09 supports reusing native CQS, existing EF models, dynamic credential fixtures, and narrow provider primitives. IVSD-M005 requires no new framework or dependency without a demonstrated need, no inline credentials, PII-free diagnostics, public/internal documentation parity, and direct greenfield replacements without dual readers. Architecture/operations maintainers own validation.

## Recommendations

Adopt IVSD-M001 through IVSD-M005 as implementation acceptance constraints. Replace complete workflow families and their callers, tests, installers, and documentation together. Retain declarative integrity, RLS, schema installation, and documented lock/session capabilities. Reject both a blanket ban on every SQL statement and blanket approval of whole provider directories: neither establishes responsible application behavior.

Reject compatibility shims and dual execution paths under the user's explicit development-mode direction. Reject a generic SQL dispatcher or universal repository framework: it would obscure rather than remove authority and mapping debt. Preserve data by default; actual deletion/reset remains a separate destructive operation requiring explicit authority.

## Stakeholders

Account holders and erased subjects; attendees and purchasers; organizers and moderators; people represented in evidence; self-hosting operators; contributors; security, privacy, and financial maintainers. No direct stakeholder interviews were conducted for this planning artifact.

## I-VSD Principles And Domains

Selected principles: trust (Amanah), justice, privacy, truthfulness, avoidance of harm, and stewardship. Applicable domains: architecture, data lifecycle, operations, governance, support/recovery, self-hosting, and engineering assurance. Monetization and religious-content policies are not redesigned. Ethical principles are design lenses, not a claim of scholarly certification.

## Validation Gaps

No build, provider test, load test, mutation test, or runtime security review was run while planning. The knowledge graph is stale/incomplete; bounded direct-source traces supplement it. Production-shaped permission admission, concurrency, restore replay, and query translation remain implementation evidence obligations.

## Escalation Needed

None needed to draft the agreed architecture. Escalate a new trust boundary, secret exposure, destructive data action, changed monetary entitlement, changed retention/legal hold, or unsupported authority topology. Qualified scholars/legal specialists own any new religious/legal conclusions; none are requested here.

## Evidence Reviewed

| ID | Locator | Validation level |
|---|---|---|
| E01 | `AGENTS.md`; `docs/internal/adr/ADR-application-owned-persistence.md`; `docs/internal/ARCHITECTURE.md` | Authoritative repository decision |
| E02 | `src/Explore.Persistence/Security/ProviderPrimitives/PostgresMixedSettingsContract.Sql.cs`; `HumanImportPublicationQueries.cs` | Source inspection |
| E03 | `src/Explore.Persistence/Database/ProviderPrimitives/EmailDispatchOutboxRepository.cs`; `Repositories/RegistrationFinalizationQueueRepository.cs` | Source inspection |
| E04 | `docs/internal/PRIVACY_ERASURE.md`; `Privacy/ErasureAuthority/PostgresSubjectPrivacyErasureSettlement.cs`; `ProviderPrimitives/PostgresSubjectPrivacyErasureContract*.cs` | Source/document inspection |
| E05 | Application persistence ports and queue/payment native contracts mapped in plan Section 2 | Shared repository discovery |
| E06 | Microsoft EF Core connection-resiliency, concurrency, transactions, and testing documentation linked in plan Section 2.4 | Paraphrased framework capability facts; no external implementation source |
| E07 | `tests/Event.Persistence.IntegrationTests/RegistrationFinalizationEffectPersistenceTests.cs`; shared provider fixtures; `tests/Shared/PostgresTestRoleBootstrap.cs` | Source inspection |
| E08 | `tests/Event.Architecture.Tests/PersistenceCapabilityBoundaryArchitectureTests.cs`; its JSON registry; `eng/tools/Explore.AssuranceAudit/` | Source inspection |
| E09 | User instructions on 2026-11-02; `Directory.Packages.props`; governance pattern-selection and record contracts | Explicit direction / repository facts |
| E10 | [r1 plan](../../dev/active/ef-first-persistence-and-typed-assurance/ef-first-persistence-and-typed-assurance-plan.md), [r1 tasks](../../dev/active/ef-first-persistence-and-typed-assurance/ef-first-persistence-and-typed-assurance-tasks.md), [r1 context](../../dev/active/ef-first-persistence-and-typed-assurance/ef-first-persistence-and-typed-assurance-context.md) | Completed-triad planning review; no execution proof |

## Missing Evidence

Successful implementation and provider qualification; exhaustive writer enrollment for each shared revision/fence; measured query plans after EF replacement; operator acceptance of final permission/deployment instructions. These are owned implementation tasks, not assumed successes.

## Context Inventory

The new workstream is a sibling of `dev/active/pr79-testing-assurance-follow-up/`. That existing workstream and its dirty transaction/test fixes remain independently owned. No repository branch, worktree, public product policy, deployment, or database was changed by this review.

## Common Overlooked Failures And Outcomes

Tracked entities can overwrite a set-based update; a caught database failure can still poison settlement; a saved checkpoint may never commit; an execution strategy can replay a known committed side effect; disabling all filters can expose another tenant; fixture-owned privileged SQL can conceal runtime permissions; deleting a body-hash test without its business invariant can remove meaningful assurance. Each must receive an explicit implementation disposition.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-11-02 | none | draft / ready-for-planning | Shared audit, explicit greenfield direction, and framework research | E01–E09 |
| 2026-11-02 | draft / ready-for-planning | current / plan-aligned | Revalidated completed r1 triad, scenarios B01–B13 and all mitigation mappings; explicit no-compatibility and retained-data boundaries | E10; plan Section 9 |

## Planning Handoff
- Workstream: ef-first-persistence-and-typed-assurance
- Status: current / plan-aligned
- Reviewed input: completed planning revision r1, E10, scenarios B01–B13, thirteen phases and thirty-nine implementation tasks
- Findings and mitigations: IVSD-F001 → IVSD-M001; IVSD-F002 → IVSD-M002; IVSD-F003 → IVSD-M003; IVSD-F004 → IVSD-M004; IVSD-F005 → IVSD-M005
- Required plan mappings: IVSD-M001 → B01–B03/B05/B13, Tasks 4.1–4.3 and 5.1–5.3; IVSD-M002 → B08–B10/B13, Tasks 10.1–10.3 and 12.1–12.3; IVSD-M003 → B02/B03/B06, Tasks 7.1–7.3 and 8.1–8.3; IVSD-M004 → B12, Tasks 1.1–1.3, 2.1–2.3 and 13.1; IVSD-M005 → B13/structural invariants, Tasks 1.2, 3.2 and 13.2 plus per-family operator docs. Every IVSD-F ID retains its corresponding mitigation mapping.
- Escalations required before: affected implementation action if a new boundary or destructive/policy change appears
- Refresh triggers: changed provider authority, topology, retention, financial behavior, tenant isolation, failure recovery, or material mitigation/task mappings
