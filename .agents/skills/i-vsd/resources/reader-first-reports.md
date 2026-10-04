# Readable I-VSD Reports Without Losing Detail

> **Audience:** I-VSD report authors and reviewing developers
> **Status:** Report-writing guidance
> **Owner:** Contributor Experience
> **Source Anchors:** `report-contract.md`, `report-templates.md`, `consultancy-workflow.md`, `islamic-value-sensitive-design/workstreams/i-vsd-event-publication-and-identity-discovery.md`

Use the [shared writing guide](../../conventional-commit/resources/reader-first-writing.md) for the general meaning-before-mechanism approach. This resource applies it to I-VSD findings, recommendations, and evidence boundaries. The [report contract](report-contract.md) remains authoritative for report identity, metadata, IDs, headings, and lifecycle.

## Expand Understanding, Preserve The Analysis

Do not trade technical completeness for readability. Add definitions, causal explanations, scenarios, and connecting sentences around the existing substance. A longer report that preserves the argument is preferable to a concise report that omits mechanisms, ethical reasoning, evidence, exceptions, or uncertainty.

The reader needs to understand both the recommendation and why it follows. An executive summary is an entry point, not a replacement for the detailed assessment. Keep the complete technical analysis, principle mapping, stakeholder differences, mitigations, rejected alternatives, owners, validation needs, and escalation boundaries.

Historical status, evidence cutoffs, finding IDs, and accepted decisions must not change merely because wording improves. A writing example or source observation cannot become a claim that tests, stakeholder validation, operational readiness, or scholarly review occurred.

## Executive Summary

After review metadata, orient the reader before the full scope and analysis:

- State the practical question and the recommendation in ordinary developer language.
- Explain who benefits or could be harmed, and the concrete consequence of the recommended choice.
- Introduce the central technical mechanism and its ethical rationale. Explain necessary terms in context without removing them from later analysis.
- State the unresolved decision or activation requirement and why it matters.
- Explain the report's actual confidence and status. For example, a stale report refers to an earlier design and cannot approve the new revision; a plan-aligned assessment means the plan addresses the mapped concerns, not that the implementation works or operations are staffed.

Use as many paragraphs as the subject needs. Do not impose a word limit that forces material qualifications out of the summary. Detailed findings and evidence still follow.

## Findings: Explanation And Traceability Together

Give every material finding a stable ID and a descriptive title. A table can help navigate findings, but cannot carry the whole argument in crowded cells.

For each finding, explain:

1. **Concrete situation:** What can happen, who is affected, and why it matters.
2. **Provider-controlled choice:** What the maintainer/operator controls, distinguished from attendee, organizer, external-provider, or scholarly responsibility.
3. **Mechanism:** How the relevant architecture, policy, data flow, or process creates or prevents that outcome. Retain exact technical terms and identifiers with explanations.
4. **Ethical reasoning:** Why the named principle applies to this particular consequence; a list of principle names alone does not establish the connection.
5. **Recommendation and trade-offs:** What to change, how it reduces the risk, what it costs or cannot guarantee, and the rejected alternatives.
6. **Evidence and next action:** What was observed versus proposed, what remains unproved, who owns the response, and any operational or scholarly escalation.

Keep the report contract's lifecycle, severity, claim type, principle/domain, stakeholder, controlled decision, evidence locator/validation level, mitigation ID, owner/next validation, and escalation fields alongside this prose. Do not erase those fields when expanding the narrative.

## Explain Technical And Ethical Terms In Context

Connect a term to the choice being reviewed rather than appending a detached glossary:

- **Tenant-qualified admission:** The decision whether publication is allowed is scoped to the publisher's community, so another community's usage or permission cannot grant or consume its capacity.
- **Atomic ledger/occupancy/state/outbox:** Publication usage, occupied capacity, event state, and queued follow-up work succeed or roll back together in one database transaction. Explain which inconsistency this prevents.
- **Transactional outbox:** Follow-up work is stored with the committed change so it can be dispatched later; this addresses a committed publication being separated from its queued work. It does not by itself prove that an external delivery succeeded.
- **HAL affordance gating:** The server supplies allowed-action links, and the interface shows the corresponding actions from those links. Explain what this communicates; hidden buttons do not replace server-side authorization.
- **Amanah (trust/responsible stewardship):** Explain the specific information, permission, capacity, or promise entrusted to the provider and the duty attached to it.
- **Adl (justice):** Explain the concrete unequal treatment or fairness concern under review, rather than assuming that a quota or algorithm is fair because the principle is named.

These are explanation patterns, not a new technical or religious authority. Use a principle's established I-VSD meaning, keep contested religious-legal conclusions within the scholarly-consultation boundary, and do not claim ethical outcomes from architecture alone.

## Worked Expansion Of A Dense Finding

The discovery workstream report contains a compact `IVSD-F003` row about publication accounting. The excerpt below demonstrates how to expand its meaning while preserving the original fields. It is an illustration of that recorded assessment, not a fresh review, implementation claim, or status update to the historical report.

### IVSD-F003: Every Route To Publication Must Enforce The Same Capacity Rules

An organizer can make an event public through an explicit publish action or while creating the event. Checking the publication allowance in only the explicit publish action would leave the create-and-publish route uncovered. The consequence is practical: the promised publication limit could be bypassed, and other publishers could receive inconsistent treatment.

The recorded mitigation is **shared tenant-qualified admission**. Both routes must ask the same publication authority whether the accountable publisher still has capacity in that tenant. "Tenant-qualified" means one community's records, permissions, or allowance cannot authorize publication in another community.

The capacity ledger, occupied capacity, event state, and notification/federation outbox work must change **atomically**: all succeed together or all roll back. Otherwise, a failed publication could consume an allowance, or an event could become public without its usage being recorded. **Replay protection** addresses repeated submission of the same operation without treating it as a fresh publication. The analysis also requires coverage of every public-exposure transition, not only these two entry points.

This connects **trust (Amanah)** to reliable accounting and **non-harm** to avoiding inconsistent publication decisions. The provider controls these publication paths and their accounting; an organizer cannot repair a missing check inside the service. The source assessment proposes the mitigation, but source inspection does not prove its correctness under simultaneous requests.

The application and persistence maintainers own the implementation and validation. The recorded next evidence is invariant-first tests and real-engine contention at workstream exit, followed by security review before release. Those requirements remain open in this illustrative account; readable prose must not turn them into passing results.

| Traceability field | Preserved value from the source finding |
| --- | --- |
| Finding / lifecycle / severity | `IVSD-F003` / open / high |
| Claim type | Implementation gap |
| Principles / domains | Trust and non-harm / technical and operations |
| Stakeholders | Publishers and operators |
| Controlled decision | Public-exposure accounting |
| Evidence / validation level | `E02` and `E06`: inspected explicit publication and publish-on-create transaction paths; source traceability, not runtime proof |
| Mitigation | `IVSD-M003`: shared tenant-qualified admission; atomic ledger/occupancy/state/outbox; replay protection; every exposure transition |
| Owner / next validation | Application/persistence maintainers; invariant-first tests and real-engine contention at workstream exit |
| Escalation | Security review before release |

## Recommendations And Decision Requests

Do not say only "retain native CQS and the transactional outbox." Explain the role of each retained mechanism and its relationship to the recommendation, using the reviewed repository evidence rather than inferring details from its name.

For a decision request, state the practical choice, the affected people, the technical and ethical reasons for the recommendation, and the consequences of the alternatives. Preserve numeric limits, required operational coverage, uncertainty, and scope. A default is not a confirmed user decision merely because it is recommended.

For example, a proposed immediate-publication pilot needs more than a phrase such as "staffed moderation activation gate." Explain who will review reports, what happens to urgent attendee-safety issues, how an appeal is handled, and which of these arrangements is actually evidenced. If staffing is unverified, say the pilot's operational readiness is unverified instead of implying the technical controls supply it.

## Chat Handoff

The chat should let the developer understand the recommendation, rationale, major technical choices, evidence limits, and requested decision without opening the report. Name the written report so they can inspect complete traceability, but do not use its path as the explanation.

Do not reproduce the entire report by default. Expand the handoff enough to preserve every material qualification and consequential action. The report continues to own the complete ethical analysis and evidence record.

## Review Before Delivery

- Can the reader explain the recommendation and practical consequence without decoding finding-table cells or internal phase IDs?
- Does each material finding connect its technical mechanism and named principle to a concrete situation?
- Are the full traceability fields, rejected alternatives, evidence limits, responsibilities, and escalation requirements still present?
- Are observed behavior, proposed mitigation, tested implementation, and validated outcomes distinguished?
- Did explanatory editing preserve historical status, evidence revision, finding IDs, decisions, and lifecycle?

If understanding is missing, add explanation. If substance is missing, restore it. Do not solve one failure by creating the other.
