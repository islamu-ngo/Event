# I-VSD Planning Revalidation: Governed Release Notes and Public Changelog

Last Updated: 2026-09-10

## Review Metadata

- Mode: planning
- Subject: governed-release-public-changelog
- Workstream: governed-release-public-changelog
- Report kind: workstream planning revalidation
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-10
- Reviewed input: governed-release-public-changelog working-tree plan/tasks, exact SHA-256 bindings below; source baseline `a312b9190ba72b609a10b53b1fb6d3850f86d42d`; required publication handoff from the root working tree.
- Supersedes: none. This new workstream report replaces reliance on the shared report for this workstream only; it does not supersede or modify the shared release-governance report for other subjects.
- Report contract: version 1; integration contract: version 1.

The completed specification is plan-aligned at the named revisions. This means its provider-responsibility constraints and required follow-up address the reviewed risks, not that those controls have been implemented or activated. All four findings remain accepted obligations with owners and pending validation, not resolved harms. The user's explicit authorization on 2026-09-10 permits implementation of the named plan; planning-only authorization statements in the reviewed triad are historical. Neither this report nor the earlier CTO review supplies that authorization. This child task changes only this report.

## Scope

Active delivery is deterministic categorization in the existing C# release renderer, trust-flow regression tests, and same-PR public/internal guidance. Breaking changes precede features, fixes, performance and other approved improvements; each visible primary entry appears once, while impact evidence and the complete range remain available. Canonical context, visibility, version policy, backport identity and human authority do not change.

The complete requested experience also requires the separate `governed-changelog-publication` workstream: one dated public page, offline generation, protected projection acceptance, a separate mutable GitBook mirror, retained human-authorized release inventory, actual transport, retry/concurrency recovery and drift reporting. This is required sequential delivery, not optional polish or implemented functionality. Its detailed implementation triad requires its own fresh I-VSD binding before execution; the present review covers the handoff requirements, not a nonexistent future plan.

Provider-controlled responsibilities include what adopters are told, who authorizes disclosure, whose identities become permanent, who may accept or mutate public content, and whether self-hosters can verify and recover without a hosted account. Monetary contracts, AI/ranking, moderation, tenant APIs/databases, tracking and new application secrets are not changed and are non-applicable to this slice. Retaining historical signed bytes and matching bundles is evidence preservation, not a compatibility code path.

## Claim Boundary

This is provider-responsibility design reasoning informed by selected Sunni ethical principles. It is not a fatwa, religious-legal ruling, Sharia/product certification, legal opinion, security approval, technical-readiness certificate, or assurance of stakeholder outcomes. No legal or scholarly approval was obtained or inferred.

Evidence supports level 2 design validation and bounded level 3 implementation traceability from inspected code, tests, policy and tasks. Reading a test is not executing it. Levels 1 (qualified scholarly validation), 4 (stakeholder validation) and 5 (operational validation) are not supplied. No build, product tests, pinned renderer execution, signing, publication, permissions change, live GitBook inspection or activation was performed for this report.

A signed release, a protected accepted projection, and a mutable public view are three distinct objects with distinct authorities. An accepted docs digest cannot make the mirror immutable; a mutable page cannot establish canonical release identity; an unsuccessful publication cannot be labelled delivered. "Offline forever" is a retention and portability objective, not an empirically established guarantee.

## Findings

IDs are allocated here for the first time and are stable within this report identity. The shared report had heading-based findings, not these IDs. `accepted` means the design accepts the responsibility and mitigation with ownership; it does not mean implementation has passed or remaining risk is waived.

### IVSD-F001 - Public-record truthfulness includes both category completeness and publication authority

- Lifecycle: accepted.
- Severity: High.
- Claim type: provider-responsibility design concern; level 2 design validation with bounded level 3 traceability.
- Legacy finding: "High - A mutable public page must never be dressed as an invariant."
- Principles/domains: Truthfulness, Trust, Non-Harm and Promise-Keeping; Design, Technical, Governance, Operational and Evaluation.
- Stakeholders: public readers, self-hosters, maintainers, attendees affected by unsafe upgrades, and operators on maintenance lines.
- Provider-controlled decision: which changes receive prominence, what constitutes release truth, which authority accepts the public projection, and how missing or altered history is reported.
- Evidence: E01 sections 3.1-3.3, 5.1-5.3, 7-9 and 11; E02 tasks 1.1-1.3 and 2.1-2.2; E04 P1-P3 and activation scenarios; E05 first finding; E06-E09.
- Revalidation: fixed categories with breaking precedence and retained impact evidence address the risk that polished notes conceal upgrade actions. The proposed protected `docs/publication` acceptance branch and separately mutable `docs/gitbook-sync` mirror correctly separate reviewed approval from SaaS write-back. Per-release canonical hash/tag attribution remains necessary even when the page carries a protected projection digest. All-line inventory reconciliation addresses omission as a truthfulness failure, including a release whose dispatch never reaches the publisher. Neither headings nor a concurrency group can substitute for those controls.
- Linked mitigation: IVSD-M001.
- Owner/next validation: release-engine maintainer owns actual renderer and clean-candidate assertions; Platform/Ops and Documentation maintainer own protected acceptance, inventory reconciliation, public claims and observed delivery.
- Escalation boundary: G01 before authoritative categorized release use; G02 before publication activation or claiming the entire experience delivered. No assertion that live branch protections or mirror permissions already exist.

### IVSD-F002 - Embargo timing remains a named human decision, including retries

- Lifecycle: accepted.
- Severity: High.
- Claim type: provider-responsibility design concern; level 2 design validation with bounded level 3 traceability, not proof of no leakage.
- Legacy finding: "High - Embargo timing is a human moral decision that tooling must not absorb."
- Principles/domains: Non-Harm, Trust and Rights of People; Governance, Technical, Operational and Evaluation.
- Stakeholders: self-hosters with limited patching capacity, their attendees, security reporters, and accountable release stewards.
- Provider-controlled decision: who selects and records a disclosure window, which approved public fields cross the restricted boundary, and whether dispatch/reconciliation is permitted to discover or disclose a release.
- Evidence: E01 sections 3.2, 4, 5.3 and 9; E02 task 1.1 and task 2.2; E04 P1 authorization inventory, P2 final lane and P3 recovery; E05 second finding and escalation requirements; E06 disclosure policy/runbook; E07 restricted-summary validation; E08 adapter authority.
- Revalidation: grouping is presentation only; security remains validated impact evidence, not a new inferred commit type or a title heuristic. Final-lane inventory must retain human authorization and full verified tag-object identity before dispatch. The publisher reads that authority but cannot create it; neither successful signing, a timer, a queued event nor accepted-page history alone grants disclosure. Missing authorization or complete inventory stops publication, including retry and reconciliation. The accountable human's judgment must consider operators' opportunity to patch, not release convenience.
- Linked mitigation: IVSD-M002.
- Owner/next validation: named security/release steward for each actual window; Platform/Ops for restricted-lane access, retained authorization and fail-closed publication tests. Role ownership here is not a fabricated named production decision-maker.
- Escalation boundary: G03 before each disclosure and before the first governed security release. Qualified authorities, not tooling, decide jurisdiction-specific duties or religious-legal conclusions.

### IVSD-F003 - Identity minimization retains a recognition cost

- Lifecycle: accepted.
- Severity: Moderate.
- Claim type: provider-responsibility tradeoff; level 2 design validation with bounded level 3 traceability; contributor acceptance not validated.
- Legacy finding: "Moderate - Identity stripping is a real cost, chosen deliberately."
- Principles/domains: Rights of People, Justice, Trust and Avoiding Spying; Design, Technical, Governance and Evaluation.
- Stakeholders: volunteers and newcomers seeking recognition, contributors needing distance from the project, readers and maintainers.
- Provider-controlled decision: whether a permanent signed/mirrored release artifact includes author/committer identities, handles, raw bodies or optional attribution.
- Evidence: E01 sections 3.2, 5.1 and 9; E02 task 1.1 unsafe-input cases and task 2.1 guidance; E04 P1 entry format, P3 receipt minimization and quarantined hardening; E05 third finding and contributor-notice recommendation; E06 privacy policy; E07 identity-free renderer projection and text checks.
- Revalidation: category constants and preserved display IDs do not require identity enrichment. Privacy must continue across generated pages and receipts, not only canonical checksums. Existing Markdown punctuation retains existing semantics in the active slice: checking `EscapeUntrustedMarkdown(...).IsValid` while discarding transformed text is not a literal-display guarantee. This known boundary must not be marketed as stronger sanitization. Optional recognition remains explicitly outside this implementation, not an erased stakeholder cost or an implied duty to create an immutable credits list.
- Linked mitigation: IVSD-M003.
- Owner/next validation: release-engine maintainer for identity/input regressions; Documentation maintainer and Project Steward for contributor-facing notice and any future consent/removal design.
- Escalation boundary: G04 retains the legacy contributor-notice obligation before first governed release use. G03 retains legal attribution review before the first governed security release. Any attribution/enrichment change requires separate steward review and I-VSD refresh; no such feature is approved here.

### IVSD-F004 - Offline verifiability requires retained evidence, not just a tag name

- Lifecycle: accepted.
- Severity: Moderate; failure can have high operational consequences for adopters.
- Claim type: stakeholder-protection design concern; level 2 design validation with bounded level 3 traceability, not demonstrated perpetual availability.
- Legacy finding: "Moderate - Verifiability is itself a stakeholder protection, not only a correctness property."
- Principles/domains: Trust, Justice, Promise-Keeping and Excellence; Technical, Operational, Strategic and Evaluation.
- Stakeholders: offline/self-hosted operators, small community organizations without CI or a forge account, future maintainers, and attendees depending on their deployments.
- Provider-controlled decision: whether verification depends on mutable branches, current SaaS state, the current engine, or expiring evidence storage.
- Evidence: E01 sections 3.3, 5.2-5.3 and 7.1; E02 prerequisite and tasks 1.1-1.3, 2.1-2.2; E04 P1 pinned inputs and historical evidence, P3 retention and activation; E05 fourth finding; E06 tag-only policy, bootstrap and retention controls; E07 exact-B recomposition; E08-E09.
- Revalidation: retain tag-object-bound verification after carrier-branch movement/deletion, canonical notes and original bundles; never regenerate historical final evidence with today's template. The required generator consumes full local objects, final evidence, the accepted manifest and complete retained authorized inventory. It does not infer release authority from directories or a forge account. Synthetic signed fixtures with a placeholder DLL can test bundle verification boundaries but cannot establish production promotion or execution of the promoted engine.
- Linked mitigation: IVSD-M004.
- Owner/next validation: release-engine maintainer for method-selected real-binary tests and tag-only verification; independent tooling promoter and Platform/Ops for genuine bundle promotion, durable download and retention/custody evidence.
- Escalation boundary: G01 before authoritative use and G02 before publication activation. Missing retained objects, bundles or final evidence fails closed, without retagging or invalidating an otherwise valid signed release because a publication provider is unavailable.

## Recommendations

Adopt the bound specification with all four mitigations retained. Do not widen the formatter slice into a publisher or advertise the two-PR outcome after only the formatter ships.

| Stable mitigation | Required behavior and verification | Owner | Current state |
| --- | --- | --- | --- |
| IVSD-M001 | Prove breaking precedence, exactly-once primary entries, stable within-category order, retained upgrade evidence and real-renderer bytes. Publish only a derived, hash/tag-attributed page from the complete authorized all-line set. Protect acceptance separately from the mutable mirror; preserve/report drift and require reviewed repair. Prove coalesced/missing dispatch recovery, CAS union, fresh review after changed input set, required-check execution and observed delivery. | Release-engine maintainer; Platform/Ops; Documentation maintainer | Accepted design; execution and G01/G02 evidence pending. |
| IVSD-M002 | Preserve restricted-lane/no-identity/no-secret boundaries and approved public impact fields. A named human records authorization/window/reason in controlled release evidence without restricted detail. Retain authorization before dispatch; reject missing authorization/inventory and premature publication on first run, retry and reconciliation. Signing and publication approvals remain separate. | Security/release steward; Platform/Ops | Accepted design; actual named decisions and G03 evidence pending. |
| IVSD-M003 | Preserve identity-free canonical artifacts, public entries and bounded receipts; exercise existing unsafe-input rejections. Document the recognition/privacy trade. Do not introduce enrichment or claim literal escaping. Keep broader grammar/literal-title hardening in its separately scoped publication prerequisite. Any future recognition surface requires opt-in and a meaningful removal path outside signed artifacts. | Release-engine maintainer; Documentation maintainer; Project Steward | Accepted design; G04 notice validation and future enrichment review remain open; no enrichment implementation task is implied. |
| IVSD-M004 | Prove exact-B candidate byte comparison, specifically `candidate_release_notes_mismatch` with clean committed tampering, and `renderer_config_digest_mismatch` after bundle verification. Execute separately selected real 2.13.1 renderer/full-flow tests with applicable impacts; retain tag-only re-verification after branch deletion. Promote actual engine/config/lock together under independent authority, retaining historical bundles, final evidence and authorization inventory for offline rebuild/download. | Release-engine maintainer; tooling promoter; Platform/Ops | Accepted design; test execution, promotion and retention evidence pending at G01/G02. |

Rejected alternatives:

- A mutable forge/GitBook body as release truth, or one writable SaaS branch doubling as protected acceptance: confuses publication with approval and conceals edits.
- Refusing all public publication: protects neither discoverability nor timely adoption; publish an honestly labelled derived view instead.
- A single dispatch plus page history, or a larger workflow queue as durable delivery: can permanently omit an authorized release never accepted onto the page. Retain authorization independently and reconcile the complete set.
- Silent page repair, reverse-merging the GitBook mirror or reusing review after a changed tag union: erases drift evidence or extends approval beyond its reviewed input.
- Fixed-schedule disclosure or default publication without explicit authorization: substitutes automation for accountable human timing.
- Permanent author identities in canonical notes: makes recognition irrevocable. Optional, removable recognition is a separate future decision, not this renderer's output.
- Fake-renderer-only assurance, dirty-tree-only tamper tests, or dummy-DLL fixture signing as production promotion: tests a weaker boundary than the release/adopter claim.
- A cumulative page inside `B` containing its own final hash, or regeneration of historical notes with a new template: creates circular identity or rewrites evidence.

## Stakeholders

| Stakeholder | Interest and retained tradeoff | Findings |
| --- | --- | --- |
| Self-hosters, especially resource-constrained communities | Clear upgrade action and timely notice without a forge-account dependency; no invented safe-upgrade/support promise | IVSD-F001, IVSD-F002, IVSD-F004 |
| Attendees of those deployments | Security and data integrity despite having no release-authority choice | IVSD-F001, IVSD-F002, IVSD-F004 |
| Contributors and volunteers | Fair recognition without irreversible indexing; no evidence yet that the chosen tradeoff meets their needs | IVSD-F003 |
| Security reporters | Predictable restricted handling and an accountable disclosure owner | IVSD-F002 |
| Readers across stable, maintenance and prerelease lines | Complete, clearly labelled history, attribution and visible correction rather than misleading completeness | IVSD-F001, IVSD-F004 |
| Release/security stewards, tooling promoters and docs operators | Separated approval powers, bounded diagnostics, reproducible recovery and retained evidence | All four findings |

## I-VSD Principles And Domains

| Principle | Concrete application | Domains |
| --- | --- | --- |
| Trust / Amanah | Separate signing, promotion, protected acceptance and mutable publication authority | Technical, Governance, Operational |
| Truthfulness / Sidq; Avoiding Deception | Category prominence does not hide breaking action; public page states derived status; pending/no-op is not delivery | Design, Governance, Evaluation |
| Non-Harm / La Darar | Preserve migration/security/configuration evidence and human disclosure timing; fail closed rather than invent safe recovery | Design, Technical, Operational |
| Justice / Adl; Rights of People | Account-free verification and semantic headings; acknowledge contributor recognition versus privacy | Strategic, Design, Governance |
| Promise-Keeping; Excellence / Ihsan | Preserve historical evidence and all release lines; test real rendering, recovery and retained downloads before claims | Technical, Operational, Evaluation |
| Avoiding Spying / Tajassus | No identity enrichment, restricted prose, credentials or tracking in release/publication artifacts | Design, Technical, Governance |

No finance/riba conclusion is implicated by this tooling change. Strategic portability, UX clarity, technical trust, operational recovery, governance decision rights and evaluation gaps were considered; monetization, moderation, AI and tenant behavior remain outside the changed scope.

## Common Overlooked Failures And Outcomes

| Scenario locator | Foreseeable failure | Required observable outcome and mapping |
| --- | --- | --- |
| S01 - categorized breaking fix | Breaking migration becomes routine fix; duplicated entries inflate apparent changes | One primary Breaking Changes entry, no Bug Fixes duplicate; required impact/upgrade evidence retained. IVSD-F001/IVSD-M001; active task 1.1 then 1.2-1.3. |
| S02 - clean tampered candidate/config drift | A dirty-file or signature error is mistaken for proof of recomposition | Clean alternate unsigned B reaches `candidate_release_notes_mismatch`; post-verification config mutation reaches `renderer_config_digest_mismatch`. IVSD-F004/IVSD-M004; active task 1.1. |
| S03 - actual renderer and offline tag | Stub output looks correct while pinned rendering or tag-only verification fails | Method-selected 2.13.1 renderer and applicable-impact full flow execute; tag verifies after disposable carrier deletion. IVSD-F001/IVSD-M001 and IVSD-F004/IVSD-M004; active prerequisite and tasks 1.1-1.3. |
| S04 - missing disclosure authorization | Retry or reconciliation publishes embargoed material | Stop without public artifacts or restricted diagnostics; only recorded human-authorized public fields enter retained publication inputs. IVSD-F002/IVSD-M002; plan 3.2, active task 1.1 existing restrictions, follow-up P1-P3 rejection cases and G03. |
| S05 - identity or misleading text | Categorization reintroduces handles, or validation is advertised as literal escaping | Existing identity/HTML/control rejections remain; no enrichment and no stronger literal-display claim. IVSD-F003/IVSD-M003; active tasks 1.1, 2.1; follow-up P1/P3 and separate hardening prerequisite; G04. |
| S06 - maintenance/coalesced dispatch | A newer-line entry or an unpublished middle release disappears | Reconcile accepted manifest plus complete retained authorized inventory; A-running/B-dropped/C-queued and absent-dispatch cases retain all authorized identities. Missing inventory fails closed. IVSD-F001/IVSD-M001 and IVSD-F002/IVSD-M002; follow-up P1/P2/P3. |
| S07 - mutable mirror/proposal race | SaaS write-back becomes approval; a changed union reuses old review | Report drift against protected accepted digest, no reverse merge or silent overwrite; CAS recomputes union and changed proposal requires fresh review. IVSD-F001/IVSD-M001; follow-up P2/P3. |
| S08 - checks or delivery absent | Bot proposal merges without checks; provider outage is reported as successful publication | Remain pending with explicit trigger/approval remedy; delivered requires accepted commit and observed sync. Release remains valid, retries do not retag. IVSD-F001/IVSD-M001 and IVSD-F004/IVSD-M004; follow-up P2/P3 and G02. |
| S09 - evidence expiry or page overflow | Offline recovery depends on current SaaS; oversized page silently loses history | Retain original bundles/final evidence/inventory beyond CI expiry; offline rebuild; near/over 1 MiB budget tests fail explicitly without truncation. Desktop/mobile and public links are activation evidence. IVSD-F001/IVSD-M001 and IVSD-F004/IVSD-M004; follow-up P1/P3 and G02. |

## Validation Gaps

- Categorized output, exact diagnostics, real full-flow integration and the owning Release build/suite were specified, not executed in this review. Missing tooling or skipped explicit methods must not be counted as a successful baseline or intended behavioral Red.
- Source/test reads show traceability, not complete security assurance. Existing loop-presence validation does not prove balanced single-loop grammar, and safe-text validation does not guarantee literal Markdown display. No executed exploit is claimed; separate hardening must precede stronger publisher claims.
- No contributor interviews, self-hoster patching research, usability study or measured accessibility outcomes were reviewed. Semantic textual headings and no account requirement are design constraints, not proof of inclusion.
- The source-free GitBook/GitHub/git-cliff/TUnit interface register in the plan and handoff was consumed as supplied evidence. No independent external retrieval was performed here; those interface descriptions do not establish this installation's settings or locked-version runtime behavior.
- Complete inventory authority, durable storage and current bot trigger behavior still require operator evidence. A queued workflow is not retained authorization or delivery.

## Escalation Needed

No unresolved material provider-responsibility decision blocks the isolated category implementation under the user's current authorization. The following named gates remain mandatory; plan alignment does not waive them.

| Gate | Deadline and owner | Required evidence / limitation | IDs |
| --- | --- | --- | --- |
| G01 - Authoritative categorized-release use | Before using the changed bundle for a governed release; release-engine maintainer, independent tooling promoter and release steward | Active prerequisite/Red/Green/full-flow results, actual Release engine DLL plus exact config/lock promotion, separate real principals, independent bootstrap review, approved version/baseline where applicable, protected receipt/signature and custody/rotation authority. Fixture credentials/dummy DLL are not promotion. | IVSD-F001/IVSD-M001, IVSD-F004/IVSD-M004 |
| G02 - Publication activation and full-experience claim | Before live protected writes/GitBook switch or claiming automated publication; Platform/Ops with Documentation maintainer and Project Steward | Follow-up implementation triad and fresh binding; retained authorized inventory and reconciliation evidence; actual acceptance/mirror branch-specific rights with no acceptance bypass; exact-proposal required checks; read-only previews; stable GitBook keys including space-4, repository/branch and Project directory docs/public, initial Git-to-GitBook direction; full-tree preview; real transport, outage/CAS/drift recovery; durable evidence download; measured public links/anchors and desktop/mobile rendering at the explicit 1 MiB budget. Docs-only writes must not deploy the product. | All four findings/mitigations through follow-up P1-P3 |
| G03 - Disclosure and qualified-authority boundary | Named steward authorization before each disclosure; qualified legal review before first governed security release; qualified Sunni scholars before any religious-legal claim | Record actual decision owner, window/reason and permitted public fields in controlled evidence. Review jurisdiction-specific vulnerability-disclosure, contributor-attribution and moral-rights duties. No release date, report disposition or generic role assignment supplies these decisions. | IVSD-F002/IVSD-M002; IVSD-F003/IVSD-M003 attribution boundary |
| G04 - Contributor notice and any later enrichment | Contributor-facing notice before first governed release; Project Steward and Documentation maintainer. New enrichment requires separate review before implementation/activation. | Locate or deliver clear notice that canonical artifacts omit identities and why; verify contributor-facing reach. Recognition is optional and outside this slice; if offered, use consent/removal outside immutable artifacts. This review did not establish that existing notice suffices. | IVSD-F003/IVSD-M003 |

## Evidence Reviewed

Paths are relative to the worktree unless explicitly labelled root. Full-file SHA-256 values bind uncommitted planning inputs, not just dates or historical commit labels.

| Evidence ID | Artifact and exact binding | Review use |
| --- | --- | --- |
| E01 | [Plan](../dev/active/governed-release-public-changelog/governed-release-public-changelog-plan.md); SHA-256 `7f7b4840660b647b5819c752bc6dfb411b20f3f518e23b33553dc4b5e5130850` | Completed specification, scenarios, split and legacy-heading Section 9 mappings; read in full. |
| E02 | [Tasks](../dev/active/governed-release-public-changelog/governed-release-public-changelog-tasks.md); SHA-256 `a85a1197124cdbdb2c2089ab9b184dc553b7983fb28c6c8b82cfda8950b3d8c5` | Prerequisites, five implementation tasks, exact diagnostic and explicit-test obligations; read in full. |
| E03 | [Context](../dev/active/governed-release-public-changelog/governed-release-public-changelog-context.md); current SHA-256 `2f78820f3a7991407a4d1c836ab98e3f3be3e7ac0e090929a430eb06b5e8b7ee`; intake SHA-256 `c6d0f04809d6e67ccb9b7afadb51c557b3412fa16466c99d043247c1279e3e83` | Implementation-session update reread before handoff; E01/E02 bindings remain unchanged. Current authorization and baseline evidence supersede historical planning-only statements below that update. |
| E04 | Root working-tree handoff `dev/backlog/governed-changelog-publication.md`; SHA-256 `7d68d6d5c7f4eef79615aefd70d1490d24b5352c6b59d5d25e951f055acd95d0` | Required current uncommitted handoff, read in full. Worktree copy was absent; no stale or missing worktree link was treated as evidence, and no copy was created. |
| E05 | Root shared report `islamic-value-sensitive-design/governance/i-vsd-release-governance.md`; SHA-256 `1d977e7a689491a3fa4a317cac0328bb4dd3291b6e4455016aa9be21a8067972`; worktree copy has the same hash | Read all four findings, mitigations, gaps and escalations. Its 2026-08-23 recommendation and old workstream/path mappings are historical, not current approval. Both shared copies remain unchanged. |
| E06 | [Release policy](../docs/internal/RELEASE_POLICY.md), [runbook](../docs/internal/RELEASE_RUNBOOK.md) sections on final-lane authority, projection/drift, trust activation, promotion and restricted input; [checklist](../docs/internal/RELEASE_CHECKLIST.md) current/prospective release model; [ADR-025](../docs/internal/adr/ADR-025-provider-neutral-release-governance.md) decisions/consequences | Source baseline `a312b9190ba72b609a10b53b1fb6d3850f86d42d`; canonical identity, disclosure/privacy and prospective-activation boundaries. The old docs/RELEASE_* and docs/adr paths in E05 are not current locators. |
| E07 | `eng/release/src/ISLAMU.ReleaseEngineering/PrepareCommand.cs:15-114`, `ReleasePreparation.cs:1-260`, `CandidateCommand.cs:1-210`, `GitCliffRenderer.cs:1-240,375-438,553-592` | Bounded direct trace: prepare validation -> composition -> renderer; candidate exact committed inputs -> recomposition -> byte comparison. Projection uses empty identity/provider records; grammar and escaping limitations are visible. No runtime execution. |
| E08 | [.ci/release/adapter-contract.md](../.ci/release/adapter-contract.md) lanes/publication sections; [.github/workflows/release-publish.yml](../.github/workflows/release-publish.yml); [GitBook mapping](../docs/public/gitbook-docs.yaml) | Adapter authority remains prospective; workflow declares projection and reports drift/no-op, not actual transport, and retains docs/releases path. YAML preserves space-4 but cannot attest live branch/root permissions. |
| E09 | `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/GitCliffRendererTests.cs:225-299`; bounded fixture/full-flow source inspection reported in E01/E03 and direct fixture/test locators | Existing real-binary method is Explicit and requires ISLAMU_RELEASE_TOOL_BUNDLE; fixed-output governed fixtures and non-applicable high impacts cannot prove the new category/full-impact behavior. No tests were run here. |
| E10 | [.agents/skills/i-vsd/SKILL.md](../.agents/skills/i-vsd/SKILL.md) and resources integration-contract, report-contract, scope-boundaries, evidence-and-validation-levels, principles-and-domains, governance-and-accountability-framework and index | Mode, report identity/schema, evidence levels, provider duty and authority boundaries. AGENTS.md/local overrides and relevant Quick Reference/rule guidance constrain this to a documentation-only edit. |

Current parent-reported prerequisite evidence, corroborated by E03's implementation-session record: the owning Release build exited 0 with 10 existing warnings, and the original explicit actual-renderer method passed once. The parent also reports 9 renderer-slice passes. The ordinary suite is not green: 243 passed and 1 pre-existing failure, exit 2, on the clean fetched base. E03 records `ReleaseInputPolicyTests.RepositoryChangeFragmentsPassReleaseInputPolicy` with `fragment_restricted_detail_marker:CHG-01M1SMGJ33KVXHCN9BHBTMM9ME; fragment_incompatible_group:email-optional-self-hosting; fragment_incompatible_group:database-backed-atproto-auth`. The parent quarantined this existing fragment-data failure; it is not an I-VSD blocker or permission to suppress a test. This child did not execute or independently reproduce those runs. They establish reported baseline/tool readiness, not the unimplemented categorized behavior or production promotion.

The working-tree source baseline was clean at intake. Graph discovery was unavailable after two searches in the supplied investigation; no graph tools are available to this child, and no current graph coverage is claimed. Direct source reads confirm the bounded preparation and candidate-verification seams; this is not a repository-wide code audit. External source/code, dependencies and credentials were not imported.

## Missing Evidence

- Current execution results for real categorized rendering, clean candidate tampering, post-verification config mutation, applicable-impact full flow and branch-independent re-verification.
- Production signer/promoter principals, custody/rotation owners, independently promoted actual engine/config bundle, durable artifact/inventory authority and protected-ref/environment acceptance evidence.
- Actual GitBook installation settings, mirror-only app rights, selected bot's required-check execution, observed sync, live page rendering/size behavior and durable evidence downloads.
- An exercised governed embargo with named decision maker and recorded patching rationale; legal or scholarly determinations where required.
- Contributor-facing notice sufficiency, opt-in recognition research, self-hoster usability/patching feedback and operational incident/drift outcomes.

These are explicit validation or activation obligations, not facts filled in by this review. They do not authorize default publication, invented people, bypass permissions or expanded credentials.

## Context Inventory

- Workspace: implementation worktree, referenced relative to its repository root.
- Writable scope: this new report only. Shared reports, triad, backlog, source, tests, settings and workflows were not edited by this child.
- Intake: named implementation authorization supplied by the user on 2026-09-10; current parent handoff overrides earlier planning-only statements. No new scope approval is needed for this report or isolated categorized implementation.
- Shared evidence packet: E01-E05; bounded source/policy corroboration: E06-E09; governing review resources: E10.
- Dependency: required sequential publication workstream in root E04; its absence from this worktree is an evidence-location condition, not permission to drop delivery scope.
- Owners: task ledger owns execution; context owns current state; planner owns plan mappings; I-VSD owns these findings/mitigations; CTO owns bound technical review; operators/stewards and qualified authorities retain their distinct decisions.
- Verification scope: one new nonbehavioral Markdown report; schema/metadata, ID-to-scenario/task/gate coverage, local links, whitespace/fences and exact hash preservation. No new prose-pinning tests or product suites.

## Planning Handoff

- Workstream: governed-release-public-changelog
- Status: current
- Reviewed input: E01 plan SHA-256 `7f7b4840660b647b5819c752bc6dfb411b20f3f518e23b33553dc4b5e5130850`; E02 tasks SHA-256 `a85a1197124cdbdb2c2089ab9b184dc553b7983fb28c6c8b82cfda8950b3d8c5`; mandatory root handoff E04 SHA-256 `7d68d6d5c7f4eef79615aefd70d1490d24b5352c6b59d5d25e951f055acd95d0`.
- Findings and mitigations: IVSD-F001 -> IVSD-M001; IVSD-F002 -> IVSD-M002; IVSD-F003 -> IVSD-M003; IVSD-F004 -> IVSD-M004.
- Required plan mappings: the table below is the exact crosswalk for Section 9's existing heading-based rows. IDs S01-S09 and gates G01-G04 are defined by this report, not invented existing task IDs; active task numbers and publication P1-P3 are actual reviewed locators.
- Escalations required before: no unresolved material escalation before active implementation; G01/G04 before governed release use, G03 before the applicable disclosure/security-release or religious-legal claim, and G02 before publication activation. Follow-up implementation requires its own rebaselined triad/review.
- Refresh triggers: material changes to categorization, visibility/impact evidence, input/escaping semantics, identity enrichment, disclosure authority, tag/bundle verification, signer trust, inventory retention/completeness, accepted/mirror permissions, public projection/size/delivery claims, required checks, or mapped tasks/gates. Any such plan or CTO rewrite makes the affected review stale pending revalidation. Nonmaterial metadata/task-status/link edits do not invalidate the reasoning, but changed artifact bytes must receive a new explicit binding before this report is cited for that newer revision.

| Section 9 legacy row | Actual finding -> mitigation | Active scenario/task mapping | Required follow-up / explicit gate or non-applicability |
| --- | --- | --- | --- |
| Mutable page must not look authoritative | IVSD-F001 -> IVSD-M001 | S01/S03; plan 3.1-3.3, 5.3; tasks 1.1-1.3 real categories/evidence and 2.1-2.2 honest documentation/handoff | S06-S09; publication P1 complete pinned inventory/manifest, ordering and hash/tag attribution; P2 protected acceptance/mutable mirror, required checks and CAS review; P3 drift/outage/crash recovery; G01/G02. Actual publisher is non-applicable to the active formatter PR, but required next PR. |
| Embargo timing is a human decision | IVSD-F002 -> IVSD-M002 | S04; plan 3.2/4/9; task 1.1 existing unsafe-input and applicable security-impact cases; task 2.2 inventory/authority handoff | S04/S06; P1 retained finally verified human-authorized inputs; P2 trusted final lane/authorization before dispatch; P3 missing-authorization and retry rejection; G03. No automated timing decision is applicable or authorized. |
| Identity stripping carries a real cost | IVSD-F003 -> IVSD-M003 | S05; plan 3.2/5.1/9; task 1.1 unsafe input and task 2.1 no literal-escaping claim | P1 identity-free entries and hardening prerequisite; P3 minimized receipts; G04 contributor notice and G03 attribution boundary. New recognition/enrichment is explicitly non-applicable to this slice and requires fresh review if proposed. |
| Offline verifiability protects stakeholders | IVSD-F004 -> IVSD-M004 | S02/S03; plan 3.3/5.2/7.1; prerequisite pinned binary and tasks 1.1-1.3 exact-B/config diagnostics, real full flow and tag-only verification; tasks 2.1-2.2 bundle guidance | S08/S09; P1 offline verified pinned-tag rebuild and original final evidence; P3 durable bundles/inventory/download and non-invalidating outage recovery; G01/G02. No hosted account is required for offline generation or verification. |

The bound plan already maps these four responsibilities by legacy heading and scenario/task; this report supplies their actual stable IDs and the expanded mirror/inventory mappings. Its Section 9 and triad metadata still reference the shared report, and E04's provenance paragraph also retains that old pointer. This child was prohibited from editing those artifacts and does not claim those pointers were updated. The planner owns adopting this path/ID crosswalk and recording this disposition; metadata-only adoption is not a new provider-risk decision. Preserve exact bindings when doing so. No finding is dropped merely because the publisher ships separately.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
| --- | --- | --- | --- | --- |
| 2026-09-10 | No workstream-specific report; shared report stale for this triad | current | Planning-mode revalidation of completed specification and required mirror/inventory handoff; four legacy findings reassessed and stable IDs allocated | E01/E02 exact hashes, root E04 hash, E05 unchanged legacy report, E06-E10 bounded corroboration; disposition plan-aligned with accepted owned obligations and unfulfilled activation gates. |

Current disposition is plan-aligned, not activated. No material planning decision remains unresolved within the reviewed scope. Implementation, operator evidence, contributor notice and qualified-authority gates remain exactly as recorded above; this report is not their completion record.
