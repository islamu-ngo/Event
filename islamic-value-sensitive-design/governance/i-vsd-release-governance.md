<!-- ABOUTME: I-VSD review of the three provider-controlled value decisions inside provider-neutral release governance. -->
<!-- ABOUTME: Covers public-record truthfulness, embargo disclosure timing, and contributor recognition versus privacy. -->

# I-VSD Consultancy Report: Release Governance

Last Updated: 2026-08-23

## Scope

This report reviews the provider-controlled decisions that the git-cliff release-engineering
workstream actually owns. It is deliberately narrow: this is release tooling, not user-facing
product behaviour, and most of the workstream is mechanical determinism with no value content.
Three decisions are not mechanical, and each is a real choice ISLAMU makes on behalf of people
who cannot make it themselves:

1. **Truthfulness of the public release record** when a forge release page — mutable, unsigned,
   and editable by any maintainer or by the forge itself — can diverge from the signed notes
   committed at preparation commit `B`.
2. **Who bears the timing decision** for disclosing an embargoed security fix, given that
   self-hosters cannot act until they are told, and telling everyone tells attackers too.
3. **The deliberate trade of contributor recognition against contributor privacy**, because
   canonical release artifacts strip author and committer identities by construction.

Out of scope: SemVer arithmetic, canonicalization byte rules, tag-object attestation, provider
transport mechanics, and the ref-namespace model. Those are correctness concerns governed by
[RELEASE_POLICY.md](../docs/RELEASE_POLICY.md) and
[ADR-025](../docs/adr/ADR-025-provider-neutral-release-governance.md); they carry no value
trade-off that this review can add to.

## Claim Boundary

This is provider-responsibility design analysis. It is not a fatwa, Sharia certification, legal
opinion, security-disclosure legal advice, or a guarantee that any release record is accurate.
Selected Sunni ethical principles inform software-provider duties only within decisions
controlled by ISLAMU Event and its operators.

Whether a specific disclosure act is obligatory, permitted, or blameworthy requires qualified
Sunni scholarly review. Vulnerability-disclosure obligations, contributor attribution rights,
and moral-rights requirements vary by jurisdiction and require qualified legal advice.

## Executive Recommendation

Approve the release-governance model with the following boundaries:

1. Never let a published forge page present itself as the authoritative release record. Each page
   must carry the canonical `release-notes.md` hash and its tag reference so any reader can check
   the page against the repository.
2. Report publication drift; do not silently repair it, and do not let drift invalidate a release.
   A silent auto-repair would hide the fact that the public record was altered — which is the harm
   being guarded against, not the symptom.
3. Keep embargo timing an explicitly named human decision with a recorded owner. The tooling must
   make premature leakage impossible and must not make the timing choice itself.
4. Treat "self-hosters can patch before attackers can exploit" as the disclosure objective, and
   record whose judgement set each embargo window.
5. State plainly, in contributor-facing documentation, that canonical release notes carry no
   author identities, and provide a separate recognition surface that contributors opt into.
6. Do not reintroduce identities into canonical artifacts as a recognition mechanism; recognition
   must not become a permanent, unremovable, machine-readable identity record.

## Findings By Severity

### High — A mutable public page must never be dressed as an invariant

A forge release body can be edited after publication by any maintainer, by a compromised token,
or by the forge operator. Any acceptance criterion of the form "published bodies match canonical
notes" is unenforceable by construction. Asserting it would make the weakest surface in the whole
system look like the strongest, which is a truthfulness failure rather than a technical one:
readers would rely on a guarantee that does not exist.

**Provider duty:** publish, but publish as an explicitly derived view. Every page states the
canonical notes hash and the tag it projects. Divergence is reported, attributed to the page, and
never treated as invalidating the signed release.

**Implemented by:** Task 8.3 (publication projection and `report-publication-drift`), and
[Decision 12](../dev/active/git-cliff-release-engineering/git-cliff-release-engineering-plan.md).

### High — Embargo timing is a human moral decision that tooling must not absorb

An embargoed security fix creates an unavoidable asymmetry. Self-hosted operators — including
small mosques and community organisations with no security staff — cannot protect their attendees
until they are told what to patch. Publishing early tells attackers at the same moment. There is
no configuration that resolves this; someone decides, and someone bears responsibility for the
window.

The danger is that a fully automated release pipeline quietly makes the decision by default —
whatever the pipeline emits becomes the disclosure timeline, and no person is accountable for it.

**Provider duty:** the release engine guarantees that restricted detail cannot reach public
artifacts before authorization, and stops rather than guessing. The window itself is set by a
named operator whose decision is recorded with the release evidence. Absence of authorization is
a stop, never a default-publish.

**Implemented by:** Task 3.3 (embargo lane and no-leak interface),
[RELEASE_POLICY.md](../docs/RELEASE_POLICY.md) disclosure section, and the runbook's restricted
security input procedure.

### Moderate — Identity stripping is a real cost, chosen deliberately

Canonical release artifacts omit author and committer identities, emails, raw commit bodies, and
provider handles. This is deliberate and defensible: release notes become a permanent, mirrored,
machine-readable, non-deletable record, and a contributor who later needs distance from this
project — for safety, for employment, for any private reason — cannot retract a signed tag. The
same property that makes releases verifiable makes them unforgettable.

The cost is equally real. Contributors do work and receive no name in the artifact that records
it. Under-recognition is not neutral; it disproportionately affects volunteers and newcomers, who
are exactly the contributors a community project depends on.

**Provider duty:** be honest that this trade was made and why, rather than presenting privacy as
free. Recognition, if offered, belongs in a separate opt-in surface whose removal is possible,
not in the immutable canonical record.

**Implemented by:** Task 3.2 (canonicalization and untrusted-text hardening) and the canonical
artifact privacy clause in [RELEASE_POLICY.md](../docs/RELEASE_POLICY.md). Optional forge
enrichment (contributor acknowledgements) remains explicitly deferred and must not alter canonical
checksums.

### Moderate — Verifiability is itself a stakeholder protection, not only a correctness property

The tag-anchored model exists so a self-hoster can check, offline and without trusting any forge,
that the code they run is the code that was released. That matters most to operators with the
least infrastructure: no CI, no security team, no ability to audit a supply chain. Any regression
that makes verification depend on a mutable ref, a live forge API, or a provider account silently
removes protection from exactly the people who have the least of it.

**Provider duty:** treat "verifiable from the tag alone, offline, forever" as a stakeholder
guarantee subject to review, not as an internal implementation preference.

**Implemented by:** Phase 7 (tag-anchored release identity correction) and
[Decision 13](../dev/active/git-cliff-release-engineering/git-cliff-release-engineering-plan.md).

## Stakeholder Traceability

| Stakeholder | Primary interest | Provider-controlled protection |
|---|---|---|
| Self-hosting operators | Knowing what changed, and being able to patch in time | Deterministic three-layer notes; embargo window owned by a named human; offline tag verification |
| Attendees of self-hosted events | Not being exposed by an unpatched deployment they never chose | Disclosure timing judged against operator patching capability, not release convenience |
| Contributors | Fair recognition without a permanent identity record | Identity-free canonical artifacts; recognition only through a separate opt-in surface |
| Contributors needing distance from the project | Not being permanently, irrevocably indexed | No identities in signed, mirrored, non-deletable artifacts |
| Readers of the public release record | Not being misled by an altered page | Canonical hash and tag reference on every published page; drift reported |
| Security reporters | Predictable, non-arbitrary handling | Restricted lane outside the public checkout; fail-closed rather than guess |
| Release operators | Clear authority boundaries and no hidden defaults | Tool verifies and emits evidence; it never approves, tags, pushes, publishes, or deploys |

## Principles And Product Domains

| Principle | Application |
|---|---|
| Amanah / trust | The public record must not claim more certainty than its weakest surface supports. |
| Truthfulness | A mutable page is labelled as a projection; drift is surfaced, never silently repaired. |
| Non-harm | Embargo windows are judged by operator patching capability, not release scheduling convenience. |
| Rights of people | Contributors are not permanently indexed by an unremovable machine-readable record. |
| Justice / fairness | Under-recognition is acknowledged as a real cost, not presented as a free privacy win. |
| Accountability | Every disclosure-timing decision has a named human owner recorded with the evidence. |

## Governance And Operational Recommendations

- Record the embargo decision owner, the window, and the reason with release evidence; never with
  the restricted detail itself.
- Keep `report-publication-drift` advisory and non-blocking. A release is closed by its signed tag;
  a drifted page is a reporting event, not a release failure.
- Document the identity-stripping trade in contributor-facing docs before the first governed
  release, so contributors know the terms before they contribute.
- Re-review this report if forge enrichment (contributor acknowledgements, handles, PR links) is
  ever activated, because that changes the recognition/privacy balance materially.
- Re-review if additional signature schemes or providers change who can alter the public record.

## Rejected Alternatives

1. **Treating published forge bodies as canonical** — rejected: unsigned mutable state cannot be
   release truth, and claiming otherwise misleads readers.
2. **Refusing all publication** — rejected: real ergonomic loss for self-hosters who rely on
   release pages to learn what to patch.
3. **Best-effort publish with no verification** — rejected: silent divergence is precisely the
   failure being guarded against.
4. **Automating embargo disclosure timing on a fixed schedule** — rejected: it removes the
   accountable human without removing the moral weight of the decision.
5. **Publishing security detail immediately to "be transparent"** — rejected: transparency that
   reaches attackers before operators can patch harms the people it claims to serve.
6. **Reintroducing author identities into canonical notes for recognition** — rejected: it makes
   recognition permanent and unremovable, converting a courtesy into a lasting exposure.

## Validation And Evaluation Plan

Implementation evidence must demonstrate:

- every published page carries the canonical notes hash and its tag reference;
- drift is detected and reported without auto-repair and without invalidating the release;
- a provider outage or a missing release API degrades to a recorded no-op, not a failed release;
- restricted security input cannot reach public artifacts, context, notes, or evidence;
- absence of disclosure authorization stops the flow rather than defaulting to publish;
- canonical artifacts contain no identities, emails, raw bodies, handles, or tokens;
- a release verifies offline from its tag with no forge API and no branch present.

## Validation Gaps

- No contributor interviews or surveys were conducted on the recognition/privacy trade.
- No self-hoster research exists on how operators actually learn about security releases or how
  quickly they patch.
- No production embargo has been run; the disclosure procedure is unexercised.
- Jurisdiction-specific attribution, moral-rights, and vulnerability-disclosure obligations were
  not assessed.
- Publication drift reporting is planned (Task 8.3) and not yet implemented, so its real-world
  signal quality is unknown.

## Escalation Needed

- Qualified legal review for jurisdiction-specific vulnerability-disclosure duties, contributor
  attribution, and moral rights before the first governed security release.
- Qualified Sunni scholarly review if release documentation ever classifies disclosure, silence,
  or recognition in religious-legal terms.
- Product/steward review before activating forge enrichment that reintroduces contributor
  identities in any published surface.
- Steward decision, recorded by name, for each embargo window before its first use.

## Evidence Reviewed

### Repository Evidence

- `docs/RELEASE_POLICY.md` — canonical release contract, ref namespace, disclosure and operation.
- `docs/RELEASE_RUNBOOK.md` — restricted security input, maintenance lines, operator commands.
- `docs/adr/ADR-025-provider-neutral-release-governance.md` — architecture and superseded model.
- `.ci/release/adapter-contract.md` — provider transport boundary and reserved ref namespace.
- `dev/active/git-cliff-release-engineering/git-cliff-release-engineering-plan.md` — Decisions 12,
  13, and 14; Tasks 3.2, 3.3, and 8.3.
- `eng/release/src/ISLAMU.ReleaseEngineering/CanonicalArtifactPolicy.cs` — identity, provider, and
  secret-shape rejection in canonical artifacts.

### External Functional References

None. This review required no third-party source analysis and ingested no external code.

## Missing Evidence

- Real signer principals, custody owners, and rotation owners are not yet recorded.
- No governed release has been executed, so no disclosure or publication outcome data exists.
- Provider protected-ref settings evidence, including the reserved `refs/heads/v*` creation rule,
  is not yet captured.

## Context Inventory

- Workstream: `dev/active/git-cliff-release-engineering/`.
- Owning tasks: 8.3 (public-record truthfulness), 3.3 (embargo timing), 3.2 (identity stripping).
- Blocks: Phase 8 activation.
