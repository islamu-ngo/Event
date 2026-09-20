<!-- ABOUTME: Plan-review I-VSD assessment of successor-B candidate B0 for the Setup Assistant presentation targets. -->
<!-- ABOUTME: Binds the restricted shared Razor and static no-secret browser probe to exact revisions and fail-closed gates. -->

# I-VSD Plan Review: Setup Assistant Presentation Targets B0

> **SUPERSEDED AND NON-AUTHORIZING — HISTORICAL RECORD ONLY**
>
> B1 replaced B0 before user approval or probing. No finding, mitigation,
> disposition, or conditional permission in this report may authorize B1 or
> revive B0.

Last Updated: 2026-08-31

## Review Metadata

- Mode: plan-review
- Subject: Setup Assistant presentation targets candidate B0
- Workstream: `setup-assistant-presentation-targets`
- Report kind: successor-candidate-plan-review
- Report status: superseded
- Disposition: historical-only
- Evidence cutoff: 2026-08-31
- Reviewed input revision: `sha256:d061551fdab2ad452adf2d9802aa05724e490bab8148a6a87e7f903abf44a836`
- Superseded by: successor-B revision B1 in
  `dev/active/setup-assistant-security-and-portability/`
- Binding ID: `setup-assistant-presentation-targets-b0-20260831`
- Candidate disposition: **Superseded before probe; no current authority.**

## Scope

This review covers provider-controlled design decisions in the exact B0
candidate: a shared semantic Razor presentation on .NET 10 and a static,
standalone Blazor WebAssembly browser target that can generate only no-secret
output. Desktop, browser secret entry, persistent browser storage, service
workers/PWA, telemetry, remote assets, APIs, providers, live authority, and all
network authority remain disabled.

The review considers strategy, dependency governance, UX, architecture,
privacy, accessibility, localization/RTL, legal draft authority, public-origin
truth, self-hosting, support claims, evaluation, stop conditions, and rollback.
It authorizes no broader successor-B capability.

## Claim Boundary

This is provider-responsibility design reasoning under I-VSD. It is not a
fatwa, halal/haram determination, Sharia certification, legal opinion, privacy,
security, accessibility, dependency, or release certification. No
religious-legal conclusion is made.

The disposition permits I-VSD progression of the exact B0 candidate to one
locked restore and publish probe after the other named approval gates are met.
It does not grant user approval, CTO approval, package activation authority,
implementation approval, release approval, shipping approval, or a support
claim. The user remains the scope and implementation authority. Technical
readiness remains with the revision-bound CTO review.

For this no-secret target, truthful copy may say that the reviewed release uses
bundled local assets and performs no designed runtime network/provider/live
authority operation. It must not say that every future official-origin response
is identical, that browser execution is intrinsically private, that the origin
cannot alter served code, or that unevidenced browsers and assistive
technologies are supported.

## Findings

### Finding Register

| ID | Lifecycle | Severity | Claim type | Principle / domain | Stakeholder and provider-controlled decision or risk | Evidence and validation level | Mitigation | Owner / next validation / escalation |
|---|---|---|---|---|---|---|---|---|
| IVSD-F047 | accepted | Blocker | Restricted capability and approval boundary | Amanah, Truthfulness, Promise-Keeping / Governance | Operators and affected users could be exposed if approval of a graph probe is presented as approval of browser implementation, desktop, secret entry, support, or shipping. The provider controls capability flags and approval language. | B0 plan Sections 1, 3, 5, 12; tasks B-010/B-020; context; intake review. Exact planning evidence only. | IVSD-M047 | Workstream owner; obtain bound CTO and exact-revision user approval before B-020; revalidate on any scope drift. |
| IVSD-F048 | accepted | Blocker | Public artifact privacy boundary | Privacy, Non-Harm, Avoiding Spying / Technical and Evaluation | The static bundle is world-readable. A secret, credential, tenant identifier, internal hostname, source map, reporter, remote asset, or hidden network/storage path would violate minimum exposure and local-only expectations. | B0 plan Sections 5, 7, 8; intake security disposition. Design and planned-probe evidence only. | IVSD-M048 | Browser/security owner; B-020, B-050, and B-080 must fail closed on any forbidden content or behavior. |
| IVSD-F049 | accepted | High | Human agency and non-manipulation requirement | Autonomy, Justice, Avoiding Gharar / UX and Governance | Progressive setup, readiness, legal drafts, and download actions could steer users through urgency, obstruction, hidden defaults, bundled consent, shame, or false completion. The provider controls choice architecture and status copy. | B0 plan Sections 6, 9, 10; tasks B-040 to B-070. Requirements only; no stakeholder or runtime evidence. | IVSD-M049 | Product/accessibility owner; verify equal cancel/clear/review paths, explicit scope, no preselected authority broadening, value-free errors, and truthful incomplete states before B-040 is accepted. |
| IVSD-F050 | accepted | Blocker | Dependency telemetry and provenance boundary | Amanah, Avoiding Spying, Promise-Keeping / Supply chain and Operations | The proposed Microsoft/.NET line is not a complete approved graph until locked restore reveals all direct, transitive, build, workload, and published components. Telemetry, incompatible obligations, advisories, or unexpected publish nodes would invalidate the candidate. | Binding, dependency evidence, B0 plan Section 7, intake dependency disposition. Official-metadata and planning evidence; no B0 lock or publish artifact yet. | IVSD-M050 | Dependency/release owner; perform only the bounded probe, record exact graph/SBOM/NOTICE/audit/signature/publish evidence, and roll back on any stop condition. |
| IVSD-F051 | accepted | High | Accessibility, localization, and support-truth boundary | Justice, Ihsan, Truthfulness / Design and Evaluation | Shared semantic Razor does not prove keyboard, browser, screen-reader, zoom/reflow, Arabic, RTL, reduced-motion, or contrast support. Unsupported parity or platform claims would mislead disabled and localized users. | B0 plan Sections 5 and 9; tasks B-060/B-090; intake accessibility disposition. Planned contract only. | IVSD-M051 | Accessibility/localization owner; keep assets bundled, test the named browser/AT matrix, publish target-labelled limitations, and block support claims until evidence exists. |
| IVSD-F052 | accepted | Critical | Legal draft authority boundary | Truthfulness, Justice, Rights of People / Governance and Design | A legal editor can misattribute instance/tenant authority, import unsafe content, imply legal approval, or mutate publication/acceptance evidence. | B0 plan Section 10; task B-070; umbrella IVSD-F023-F028. Existing Core contract evidence plus planned presentation behavior. | IVSD-M052 | Product/legal owner; use Core constrained Markdown, draft/readiness only, blank or approved attributed bundled templates, and qualified legal review for substantive templates/claims. |
| IVSD-F053 | accepted | High | Reversible activation and evidence integrity | Amanah, Non-Harm / Operations | Continuing after graph, publish, security, accessibility, or evidence drift would normalize a known-bad target; destructive rollback could harm unrelated work. | B0 plan Section 13; tasks Rules and B-020; intake stop conditions. Planning evidence only. | IVSD-M053 | Workstream owner; stop immediately, restore disabled package-free shells with file edits only, preserve evidence, and require a new binding after material drift. |

### Existing Umbrella Finding Mapping

Every existing umbrella finding relevant to B0 retains its stable identity and
one-to-one mitigation. This report does not renumber or weaken it.

| Existing finding / mitigation | B0 state | Exact B0 mapping |
|---|---|---|
| IVSD-F001/M001 | Mitigated for planning | Shared browser access advances self-hosting without requiring official live authority; B-040/B-050. |
| IVSD-F002/M002 | Blocking invariant | Core remains the sole semantic owner; plan Section 6 and B-030/B-040/B-050. Duplication stops acceptance. |
| IVSD-F003/M003 | Non-applicable to B0 capability | Browser secret entry is disabled. Public-origin truth remains applicable through IVSD-F017/M017 and IVSD-F021/M021. |
| IVSD-F004/M004 | Mitigated | B0 is no-secret only; no remembered or hidden secret mode exists. |
| IVSD-F005/M005 and IVSD-F006/M006 | Non-applicable to B0 secret handling | There is no secret-mode transition or secret state. B0 still applies the stricter zero-network/storage public-bundle conditions through IVSD-F048/M048. |
| IVSD-F007/M007 | Mitigated subject to tests | Relevant secret keys may appear only as empty Core-produced placeholders with truthful incomplete readiness; B-040/B-050. |
| IVSD-F008/M008 and IVSD-F009/M009 | Mitigated subject to parity tests | Core owns catalogue/relevance/rendering and keeps portable configuration separate from dotenv; plan Section 6 and B-030 to B-050. |
| IVSD-F010/M010 and IVSD-F011/M011 | Non-applicable | Desktop and secret values are disabled. |
| IVSD-F012/M012 | Blocking until probe evidence | Telemetry, reporters, crash upload, update checks, remote logs/assets, and persistent state are forbidden; B-020/B-050/B-080 and IVSD-F050/M050. |
| IVSD-F013/M013 | Blocking until exact graph evidence | Locked recursive provenance, license, NOTICE, SBOM, audit, signature, and publish-role checks are B-020/B-080 stop gates. |
| IVSD-F014/M014 | Deferred; blocks shipping, not the probe | Release identity, immutable artifact evidence, and integrity claims remain for release work after B0 implementation evidence. |
| IVSD-F015/M015 | Blocking support/shipping claims | Static publication and semantic components do not prove browser support; B-060/B-080/B-090 and IVSD-F051/M051. |
| IVSD-F016/M016 | Mitigated by design | Browser source remains tracked and auditable; only generated outputs are ignored. No hosting monopoly claim is allowed. |
| IVSD-F017/M017 | Deferred for release identity; mitigated for probe language | Source/public origin truth must distinguish repository source, an identified artifact, and the origin serving it. |
| IVSD-F018/M018 and IVSD-F019/M019 | Blocking target acceptance until evidence | Semantic HTML, keyboard/focus, bundled localization, Arabic/RTL, reflow, contrast, and reduced-motion requirements map to B-060. |
| IVSD-F020/M020 | Non-applicable | No secret retrieval, generation, provider, or live authority exists in B0. |
| IVSD-F021/M021 | Mitigated subject to copy review | Local/browser/origin/support claims are bounded by this report and plan Sections 5, 8, 9, and 12. |
| IVSD-F022/M022 | Blocking shipping; the probe is the mitigation path | Locks, scans, tests, and top-level metadata alone do not prove the bundle; B-020/B-050/B-060/B-080 collect exact evidence. |
| IVSD-F023/M023 through IVSD-F028/M028 | Deferred to B-070; non-blocking for the graph probe | Legal source is typed, role-correct, constrained, bounded, local, draft-only, and cannot publish or create acceptance. IVSD-F052/M052 carries B0 enforcement. |
| IVSD-F029/M029 through IVSD-F033/M033 | Non-applicable to changed B0 behavior | B0 neither changes nor replaces successor-A CLI/terminal/agent boundaries. Browser availability must not diminish those self-hosting paths. |
| IVSD-F034/M034 | Mitigated subject to UX review | Explicit user review remains required; B0 cannot auto-publish, broaden authority, infer user approval, or disguise incomplete output. IVSD-F049/M049 adds the no-dark-pattern condition. |
| IVSD-F035/M035 and IVSD-F036/M036 | Non-applicable | They govern terminal accessibility and future skill lifecycle, not B0 browser support. Browser limitations map to IVSD-F018/M018 and IVSD-F051/M051. |
| IVSD-F037/M037 through IVSD-F046/M046 | Non-applicable | Composition, live control-plane, provider binding, data migration, payments, recovery, and agent authority remain outside B0 and disabled. |

### Status Summary

- **Blocking execution now:** revision-bound CTO review and exact-revision user
  approval required by B-010. I-VSD cannot satisfy either authority.
- **Approved only as a bounded evidence-producing action:** B-020 locked
  restore, recursive graph review, and browser publish probe after B-010.
- **Blocking continuation after the probe:** any graph, license, provenance,
  telemetry, audit, signature, TFM, lock, SBOM/NOTICE, or publish surprise.
- **Blocking support or shipping:** incomplete browser security, runtime,
  accessibility/AT, localization/RTL, provenance, and support evidence.
- **Deferred without widening B0:** legal editor completion, release identity,
  and official support copy.
- **Non-applicable:** desktop, browser secret mode, secret storage/lifetime,
  live/provider/network authority, composition, migrations, payments, and
  agent/terminal changes.

## Recommendations

1. **IVSD-M047:** Treat the binding as indivisible. Obtain CTO review and user
   approval naming binding ID `setup-assistant-presentation-targets-b0-20260831`
   before B-020. Any material change requires a new binding and fresh review.
2. **IVSD-M048:** Inspect the complete static artifact as public data. Reject
   secrets, credentials, tenant/internal identity, remote references, source
   maps, storage, service workers, reporters, and runtime network paths.
3. **IVSD-M049:** Make no-secret generation the primary useful path without
   coercion. Preserve clear/cancel/review, explain omitted/defaulted/incomplete
   states before download, never preselect authority-expanding choices, and do
   not use urgency, shame, obstruction, or visual asymmetry to manufacture
   consent.
4. **IVSD-M050:** Record direct, transitive, build, workload, and publish nodes;
   lock digests; audit/signature results; license/provenance/NOTICE/SBOM mapping;
   telemetry posture; and exact publish inventory. A clean probe is evidence
   for later implementation review, not shipping approval.
5. **IVSD-M051:** Bundle all resources; test keyboard, focus, errors,
   announcements, Arabic/RTL, 200% zoom/reflow, reduced motion, contrast, and
   representative assistive technology. Publish only target-labelled support
   that the resulting evidence proves.
6. **IVSD-M052:** Keep legal content draft-only and role-scoped. Ship blank or
   qualified-review-approved, immutable, attributed local templates; never
   imply legal or Islamic approval.
7. **IVSD-M053:** On any stop condition, remove activation with file edits,
   restore package-free disabled shells and pre-activation locks, preserve the
   failure evidence, and do not use destructive Git operations.

Rejected alternatives:

- Activating Avalonia, Photino desktop, MudBlazor, another component package,
  hosted Blazor, server rendering, SignalR, secret mode, or a weaker fallback:
  rejected because B0 has no provenance/authority for them.
- Treating Microsoft ownership, a scanner pass, framework references, or a
  successful publish as sufficient provenance/security/accessibility proof:
  rejected because each proves only a narrower fact.
- Hiding browser source or claiming official hosting prevents forks: rejected
  as misleading and contrary to auditable self-hosting.
- Continuing after a failed gate with a waiver, package exception, shim, or
  undocumented target reduction: rejected. B0 has no weaker fallback.

## Stakeholders

| Stakeholder | B0 interest | Provider-controlled protection |
|---|---|---|
| New and experienced self-hosters | Useful inspectable no-install setup | No-secret primary path, deterministic Core output, public source, local bundle |
| Disabled administrators | Equal completion and truthful limitations | Semantic controls, keyboard/focus/error contracts, AT evidence gate |
| Arabic and RTL users | Correct reading, ordering, and comprehension | Bundled localization, logical properties, Arabic/RTL tests |
| Security-conscious operators | Minimum exposure and no hidden communications | Public-artifact inspection, zero network/storage/reporters, no secrets |
| Instance and tenant operators | Role-correct legal drafts and portable configuration | Typed authority, constrained Markdown, no publication/acceptance mutation |
| Third-party self-hosters | Freedom to audit and serve compliant source | Tracked source, bundled assets, no official-host monopoly claim |
| Maintainers and release operators | Reproducible, supportable graph | Locks, provenance, SBOM/NOTICE, stop conditions, reversible activation |
| People affected by compromise or misleading setup | No leaked data or false completion/support promises | Fail-closed bundle checks, truthful readiness and support claims |

## I-VSD Principles And Domains

- **Amanah / trust:** exact binding, graph, publish contents, capability flags,
  and rollback are auditable.
- **Sidq / truthfulness:** source, artifact, public origin, support, legal draft,
  and incomplete readiness claims remain distinct.
- **Adl / justice:** accessibility, Arabic/RTL, no-install use, CLI continuity,
  and self-hosting are not traded away for a convenient browser target.
- **Privacy and avoiding spying:** no secrets, telemetry, analytics, reporters,
  remote assets, storage, or network/provider/live authority enter B0.
- **Non-harm:** public-artifact inspection and fail-closed stop conditions limit
  foreseeable exposure.
- **Autonomy and avoiding gharar:** no dark patterns, hidden scope, fabricated
  completion, bundled consent, or inferred approval.
- **Promise-keeping and ihsan:** dependency, accessibility, support, and
  local-only claims require exact evidence rather than intention.

Domains reviewed: strategy, UX, architecture, data/privacy, dependency and AI
posture, operations, governance, support, portability/self-hosting, legal draft
authority, accessibility/localization, and evaluation. B0 contains no AI
component or provider; adding either is a refresh trigger.

## Common Overlooked Failures And Outcomes

| Failure | Foreseeable outcome | Required response |
|---|---|---|
| Build/workload node omitted from dependency review | Undisclosed telemetry or redistribution duty | Stop and roll back; expand graph evidence |
| Remote font, help, translation, source map, or dev server survives publish | Privacy/support claim becomes false | Reject artifact and restore disabled shells |
| Empty secret placeholder is announced as ready | Operator deploys incomplete or unsafe configuration | Preserve `Incomplete` state with key names only |
| Visual flow hides clear/cancel or preselects broader scope | Manufactured consent and loss of agency | Fail UX acceptance under IVSD-M049 |
| Shared semantics are mistaken for browser accessibility | Disabled users receive false support promises | Block support claim pending representative AT evidence |
| Legal template appears polished but lacks authority/provenance | Operator mistakes draft for approved law/compliance | Ship blank or approved attributed local asset only |
| Static/browser-local wording erases origin control | Users over-trust a mutable hosted response | Distinguish source, artifact, release, and serving origin |
| Failed probe is retained as a partial activation | Known-bad graph becomes normalized | Apply IVSD-M053 and require a new binding |

## Validation Gaps

No B0 restore, lock closure, audit, signature result, SBOM, NOTICE set, publish
inventory, runtime browser trace, storage inspection, browser compatibility
matrix, accessibility/AT run, Arabic/RTL usability run, or stakeholder evidence
exists yet. The exact SDK-supported browser TFM is intentionally unresolved
until the probe. No legal evidence establishes approved bundled templates.
No official-host deployment, release identity, reproducibility, incident, or
support evidence is in B0.

These gaps are acceptable only for the restricted locked graph/publish probe.
They block broader implementation acceptance, support, and shipping as mapped
above.

## Escalation Needed

- Revision-bound CTO review and exact-revision user approval are required
  before B-020. This I-VSD report grants neither.
- Dependency/IP review must evaluate the exact recursive graph, licenses,
  provenance, notices, source obligations, telemetry, and outbound paths.
- Security and accessibility review must evaluate the exact published target
  before support or release.
- Qualified legal review is required for substantive legal templates, legal
  claims, official-origin/privacy copy, trademark/attribution, and support
  commitments.
- Qualified Sunni scholarly authority is required only if future copy or
  product behavior introduces a religious-legal conclusion. B0 introduces
  none, and this report makes none.

## Evidence Reviewed

The B0 binding file itself has SHA-256
`d061551fdab2ad452adf2d9802aa05724e490bab8148a6a87e7f903abf44a836`.
It names the exact immutable binding and four bound revisions:

| Evidence ID | Bound artifact | SHA-256 | Contribution |
|---|---|---|---|
| B0-E001 | `setup-assistant-presentation-targets-plan.md` | `d7483c2d20da9bdbd23e826bf1ee56f431a8d6fa23019f2a5506171298a56d27` | Scope, graph, capabilities, security/accessibility rules, approvals, rollback |
| B0-E002 | `setup-assistant-presentation-targets-tasks.md` | `3110628d5b3106ba266baf0716ae7699e974edf5d1b242230e66427d5a7e01e1` | Execution sequence, probe evidence, stop conditions, shipping gates |
| B0-E003 | `setup-assistant-presentation-targets-context.md` | `e783ed6de19368f4d3cdef80cbfd2f164c7c3f844dd0c175cfc0163ba729748b` | Current disabled state, inherited evidence, decisions, open questions |
| B0-E004 | `setup-assistant-presentation-targets-intake-review.md` | `3ce321d693429eb602f7d2f3436e4cf3bf5c4d6326675956051784caaac4a63b` | Dependency, security, accessibility, target disposition and stop gates |

The review also used the linked umbrella
[plan](../dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-plan.md),
[context](../dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-context.md),
[dependency evidence](../dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-dependency-evidence.md),
and current umbrella
[I-VSD report](i-vsd-setup-assistant-security-and-portability.md). These supply
the inherited provider-responsibility findings, successor boundaries,
self-hosting and origin truth, dependency/provenance posture, and authority
gates. No production, test, package, external source, stakeholder, runtime, or
private deployment evidence was reviewed.

## Missing Evidence

- Exact B0 direct/transitive/build/workload graph and lock digests.
- Vulnerability, deprecation, audit-source, signature, license, provenance,
  SBOM, NOTICE, source-obligation, and telemetry results.
- Exact browser TFM and complete publish inventory/digests.
- Proof that build/development nodes, reporters, service workers, source maps,
  remote assets, blocked identities, and unexpected assemblies do not ship.
- Runtime proof of no outbound network, persistent storage, hidden secret path,
  dynamic script, or non-local asset.
- Browser/OS compatibility, keyboard, focus, zoom/reflow, contrast,
  reduced-motion, representative assistive-technology, Arabic, and RTL results.
- Stakeholder evidence that users understand omissions, incomplete readiness,
  public-origin trust, and target limitations without coercive choice design.
- Approved legal-template provenance and qualified legal review.
- Release identity, reproducibility, official hosting, incident response, and
  truthful support evidence.
- Fresh CTO review and exact-revision user approval.

## Context Inventory

Reviewed only the exact B0 binding and its four bound artifacts, plus the linked
umbrella plan, context, dependency evidence, and I-VSD report. The current state
is planning-only: all presentation shells remain package-free and disabled,
B0 is unapproved, and no successor-B production file or package graph has been
activated.

Not reviewed: production/test/package files, external package payloads or
source, browser execution, publish output, user data, secrets, logs, support
cases, stakeholder interviews, legal advice, scholarly rulings, or later
successor plans.

## Planning Handoff

- Workstream: `setup-assistant-presentation-targets`
- Status: current
- Reviewed input revision: `sha256:d061551fdab2ad452adf2d9802aa05724e490bab8148a6a87e7f903abf44a836`
- Binding: `setup-assistant-presentation-targets-b0-20260831`; exact B0-E001
  through B0-E004 hashes are recorded under Evidence Reviewed.
- Findings and mitigations: existing relevant `IVSD-F001`-`IVSD-F046` mappings
  are classified above; successor-specific `IVSD-F047`-`IVSD-F053` map
  one-to-one to `IVSD-M047`-`IVSD-M053`.
- Required plan mappings: IVSD-F047/M047 -> plan Sections 1/12, B-010/B-020;
  IVSD-F048/M048 -> Sections 5/8, B-020/B-050/B-080;
  IVSD-F049/M049 -> Sections 6/9/10, B-040/B-050/B-060/B-070;
  IVSD-F050/M050 -> Section 7, B-020/B-080;
  IVSD-F051/M051 -> Sections 5/9, B-060/B-090;
  IVSD-F052/M052 -> Section 10, B-070;
  IVSD-F053/M053 -> Section 13 and task rollback rules/B-020.
- Explicit non-applicability: desktop, browser secret mode, service worker/PWA,
  network/provider/live authority, persistent storage, secret retrieval,
  composition, data/payment migration, and agent/terminal changes remain
  disabled and outside B0.
- Escalations required before the probe: fresh revision-bound CTO review and
  exact-revision user approval.
- Escalations required before support or release: exact-graph dependency/IP,
  security, accessibility/RTL/localization, legal-copy/template, release, and
  support evidence for the exact artifact.
- Refresh triggers: any change to binding ID or bound digest; graph, package,
  SDK/TFM, framework reference, lock, publish inventory, telemetry, remote
  asset, network/storage, capability flag, secret behavior, desktop status,
  legal authority/template behavior, accessibility/support claim, source/public
  origin posture, rollback, or mapped IVSD task.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-08-31 | none | current / plan-aligned | Fresh plan-review of exact successor-B candidate B0 | Binding `setup-assistant-presentation-targets-b0-20260831` and B0-E001 through B0-E004 |

This report becomes stale immediately if any refresh trigger changes. A failed
probe does not convert the disposition into partial implementation approval; it
requires rollback and a newly bound candidate before reconsideration.
