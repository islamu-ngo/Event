<!-- ABOUTME: Provider-responsibility revalidation for the breaking Unicode location search implementation. -->
<!-- ABOUTME: Binds full-text preservation, privacy and truthful portability obligations to the approved source-free contract. -->

# Unicode Location Search Simplification — I-VSD Planning Report

Last Updated: 2026-09-07

## Review Metadata

- Mode: planning
- Subject: Unicode location matching, derived-address privacy and self-hosted runtime/schema changes
- Workstream: unicode-location-search-simplification
- Report kind: planning report
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-06
- Reviewed input revision: backlog SHA-256 `1c30d905825826910a8f563db9916be3855478b4978233b09f3375b97792fb78`; repository `3f9b796b4a0aea6fff40baaaf88e07add663e863`
- Supersedes: Unicode-specific direction IVSD-F002–004/M002–004 in [the parent report](i-vsd-prevent-ci-failures-and-unicode-simplification.md); CI findings remain with their separate workstream.

## Scope

Revalidate the approved full [backlog contract](../dev/backlog/unicode-location-search-simplification.md) and its promoted plan/tasks. This includes whole accepted text, multilingual literal matching, atomic rejection, derived PII erasure, authority-before-results, actual five-provider behavior, and safe breaking schema/runtime operations. No religious-legal conclusion, certification, universal linguistic ranking or migration compatibility guarantee is proposed.

## Claim Boundary

Plan-aligned means the design addresses named provider responsibilities; it does not mean runtime proof exists. Existing source shows token storage and lifecycle/query protections; new implementation, operational behavior, stakeholder outcomes and all five engines require execution evidence. The parent report's VARCHAR300, per-row allocated-size, full casefold, universal ordering and EF9 claims are not carried forward. Source500/derived2000 UTF-16 units and provider-local stable order are explicit limitations, not universal text promises.

## Findings

| ID | Lifecycle / severity / claim | Principle, stakeholder and controlled decision | Evidence / validation | Mitigation and owner |
| --- | --- | --- | --- | --- |
| IVSD-F002 | accepted / high / runtime drift risk | Sidq and amanah; self-hosters/contributors; whether persisted normalization changes silently with runtime/ICU | Encoder source and official .NET globalization facts; source/documentation verified, new runtime unverified | IVSD-M002: focused corpus, coordinated runtime change and rebuild/reset; implementer/operator |
| IVSD-F003 | accepted / high / silent text loss risk | Ihsan and stewardship; address owners/operators; whether replacement stores the whole accepted value | Existing raw500/token14000 mappings and revised contract; design verified | IVSD-M003: validate raw500 and whole derived2000, never truncate, atomic failure and generated Unicode schema; implementer |
| IVSD-F004 | accepted / high / unequal or misleading matching risk | Dignity and truthfulness; multilingual users/tenants; normalization, literal matching and stated ordering support | Existing NFC/uppercase/NFC and authorized query; new provider behavior unverified | IVSD-M004: preserve marks/joiners, exact semantic examples, five real engines and provider-local order tests; implementer/reviewer |
| IVSD-F006 | accepted / critical / privacy and resurrection risk | Amanah and prevention of harm; private-address subjects; whether repair/rejected writes expose or recreate PII | Existing erasure and authority mechanisms; stricter-input partial mutation found in update/provider path | IVSD-M006: invariant-first aggregate/request atomicity, erasure/stale-writer tests, no diagnostic PII and safe disposable migration targets; implementer/security reviewer |

Parent IVSD-F001/F005 and mitigations are not re-reviewed here; CI routing and preflight remain separately owned.

## Recommendations

Implement the entire chosen contract. Keep one standard-library normalization helper and existing authority/query/lifecycle boundaries. Delete old format readers rather than supporting two meanings. Preserve current operational stale-state checks. Require actual migration/runtime/provider evidence before declaring support. Treat unsafe or unknown reset targets as a need for authority, not permission inferred from development mode.

Rejected alternatives: truncated300 text (loses accepted data); unbounded ICU digest lists (obscure drift without an operating contract); globally changing ASCII collation (widens identity/setup risk); query-time lossy casing or in-memory prefilter (breaks semantics/authority); added FTS/Unicode packages without measured need (new operational/licensing burden).

## Stakeholders

Address owners and erased subjects bear disclosure risk. Multilingual attendees and organizers need accurate bounded search and original display values. Self-hosters need truthful support boundaries and recoverable upgrades. Maintainers need focused invariant tests and explicit runtime responsibilities.

## I-VSD Principles And Domains

Trust requires preserving deletion and authorization. Truthfulness requires distinguishing invariant uppercase from full casefold and provider-local order from universal ranking. Stewardship favors less encoding machinery without sacrificing complete text. These are software-design responsibilities, not scholarly rulings or proof of social outcomes.

## Validation Gaps

At planning admission, all new runtime behavior was unverified. Subsequent scoped evidence now covers five-provider corpus/DDL/migration-service behavior, rejection atomicity, concurrent erasure/stale writer, secret-safe query diagnostics and actual-command plans/timings. Independent privacy and persistence reviews approve that scope with no confirmed defects. This remains a planning disposition, not whole-workstream completion or certification.

The authorized merge and four combined application catalogs are complete. Final merged Release, Domain, Application, Architecture and standalone gates pass; full Persistence has1608passed/0failed/5structured-environment skips. Explicit five-engine Unicode/runtime20/20, generated lifecycle20/20, production-service clean/repeat10/10 and four pending-model checks pass. Every final corpus process was observed loading .NET10.0.10 and ICU78.3. The canonical test-isolation policy replaces temporary local logging exceptions; generated files remain tool-owned.

Remaining delivery gaps are governed release validation and required CI, plus explicit disposition of a separate existing SecretBinding contract: three engines pass, MySQL/MariaDB fail because a physically global uniqueness constraint rejects same-key bindings for different tenants. This fifth structured skip is not covered by Unicode20/20. No repair or stakeholder-responsibility expansion is approved by this status update. Already-published invalid release metadata must not be rewritten or silently bypassed. These status-only corrections preserve the reviewed Unicode design and IVSD mappings; they do not confer final execution approval. Local clean/reset/repeat tests do not certify arbitrary retained-data upgrades, future ICU profiles, stakeholder outcomes or scholarly review.

## Escalation Needed

No religious-legal decision identified. Explicit target approval/disposability is required before any destructive database action. If a provider fails the corpus, resolve the actual cause or obtain a scope decision; never count its skipped test as conformance.

## Evidence Reviewed

- Backlog digest and repository revision above; promoted `dev/active/unicode-location-search-simplification/` plan and tasks carry the same complete UNI contract and finding mappings.
- Initial promoted plan SHA-256: `92798a62699ecbd8a56f8069ae933f10b161c90aba001b2462719c877a95dd72`; initial tasks SHA-256: `bd99cf5b49bf4c6fe262e7f8b1a0665773fbfc1aa8ede084ec62bed5a3a4238a`. Subsequent task-status evidence updates preserve the reviewed design.
- Domain encoder/Location/LocationPii, two location EF configurations, portable model policy and suggestion query; Application update and promotion caller discovery.
- Existing Domain lifecycle, Application location-write and Persistence provider/migration test seams; exact source paths remain implementation evidence handles, not universal coverage claims.
- [Unicode normalization](https://www.unicode.org/reports/tr15/) via Firecrawl CLI; [.NET ICU behavior](https://learn.microsoft.com/en-us/dotnet/core/extensions/globalization-icu) via Context7; [SQL Server CHARINDEX](https://learn.microsoft.com/en-us/sql/t-sql/functions/charindex-transact-sql) via Tavily MCP, accessed 2026-09-06. Only functional facts inform implementation; no source/assets/dependency added.

## Missing Evidence

Whole-suite green results, final delivery/CI evidence and measured production/operator outcomes remain absent. The scoped reviews and focused executions do not waive those gates. Local SDK10.0.302/.NET10.0.10 on Linux and available ICU78 describe the test environment, not immutable tables or proof of the loaded library. Query measurements used20 candidate rows and4 matches; they are not a production capacity claim.

## Context Inventory

Four application assemblies serve five engines. Nested authority/identity/Data Protection catalogs remain separate. User approved direct-develop execution and breaking replacement; arbitrary retained-data deletion remains unauthorized. Implementation uses a fresh source-free agent context after an unsolicited external example was excluded from the handoff.

## Common Overlooked Failures And Outcomes

Derived search text is still PII. Returning a validation failure after mutating a tracked aggregate is not atomic rejection. Current revision does not detect ICU changes automatically. A max-length annotation is not universal database enforcement. A result limit does not bound scanned rows. Regenerating source migrations neither migrates nor recovers an existing database.

## Planning Handoff

- Workstream: unicode-location-search-simplification
- Status: current / plan-aligned, design only
- Reviewed input revision: `1c30d905825826910a8f563db9916be3855478b4978233b09f3375b97792fb78`
- IVSD-F002/M002 → UNI-11, focused tests and runtime/operator proof.
- IVSD-F003/M003 → UNI-03/04/08, complete bounds and generated schema.
- IVSD-F004/M004 → UNI-01/02/05/06/09/10, real-provider multilingual membership/authority.
- IVSD-F006/M006 → UNI-03/07/08, erasure and mutation atomicity.
- Mapped execution: tasks1.1–1.5 complete vertical slice; task2.1 durable decisions. Escalate before destructive operations and before release if proof remains missing.
- Refresh triggers: semantics/bounds, authority, retention/erasure, runtime policy, provider support or reset-safety changes; task status alone does not invalidate design alignment.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
| --- | --- | --- | --- | --- |
| 2026-09-06 | parent Unicode direction stale | current / plan-aligned | User authorizes full split Unicode contract; re-evaluated changed bounds, ordering and privacy risks | Exact backlog digest and repository evidence above; runtime gates remain open |
| 2026-09-06 | current / plan-aligned | unchanged; scoped execution evidence appended | Status/evidence reconciliation only; no changed semantics, responsibility, stakeholder or finding mapping | Final scoped reviews and test logs under `.omo/evidence/20260906-unicode-location-search-simplification/`; broad gates and delivery remain open |
