<!-- ABOUTME: Defines unperformed field evaluation of anonymous registration, proof work, and private handover. -->
<!-- ABOUTME: Assigns accountable owner categories, minimized evidence, and predeclared decision measures without inventing results. -->

# Anonymous Registration Field Evaluation

> **Audience:** Contributors | Operators | AI agents
> **Status:** Planned
> **Owner:** Product/Admin
> **Last Verified:** 2026-09-08
> **Source Anchors:** `src/Explore.Infrastructure/Services/Registration/AnonymousRegistrationChallengeService.cs`, `src/Explore.Application/Services/Registration/AnonymousCancellationService.cs`, `src/Explore.Domain/Services/Registration/AnonymousRegistrationRetentionPolicy.cs`

## Status and trigger

**No field study has been conducted or reported by this item.** Functional tests do not establish real no-show reduction, attacker economics, false-rejection rates, mobile cost, or operator comprehension. The 18-bit challenge default and 16-22 bounds are implementation choices, not measured usability guarantees.

Begin only after native zero-email host acceptance and an approved, consented pilot with participating operators and an agreed research/privacy protocol. This is separate empirical work outside the email-optional implementation revision; it must not be used to defer required security or functional acceptance.

Product/Admin is accountable for the study and decision record. Platform/Ops owns deployment and incident readiness; Security owns abuse/handover review; Frontend owns accessibility/mobile evaluation. Assign a named study lead and participating operator before recruitment; this backlog claims no accepted participant, staffing commitment, or delivery date.

## Questions and evidence plan

| Question | Minimized collection and method | Decision measure to predeclare |
| --- | --- | --- |
| No-shows and capacity use | Operator-reported aggregate confirmed/cancelled/attended counts per consented event cohort; record event/approval-policy context without attendee identifiers | No-show denominator, cancellation lead time, released-seat reuse, sample size, and uncertainty; distinguish associations from causal claims |
| Abuse and legitimate false rejection | Controlled authorized abuse exercises plus privacy-reviewed pilot incident categories; voluntary participant reports supply context that logs cannot infer | Accepted abusive attempts, duplicate allocation/release incidents, legitimate rejection proportion, completion/dropout rate, and safe stopping threshold |
| Mobile proof work | Consented lab tasks on low-end/mobile devices, slow networks, and assistive-technology setups; capture only coarse device/network classes and bounded aggregate timings | Completion-time distribution, timeout/cancellation rate, work/energy proxy limits, accessible progress and recovery success; tune only within approved bounds unless a new design is reviewed |
| Operator comprehension | Scenario-based interviews covering disabled versus degraded delivery, local capture versus actual delivery, lost links, and uncertain send outcomes | Correct decisions and dangerous misconceptions, not agreement with wording; predeclare an acceptable comprehension threshold |
| Out-of-band handover | Simulated temporary-credential and private-link handover using disposable test authority, including lost response/link and exact-operation recovery | Correct recipient, successful private replacement/save, repeat-issuance mistakes, unintended disclosure, and successful recovery without credential retrieval |

Use a consented comparator only when the study design supports it. Stratify by event scarcity, approval mode, device constraints, and assistance needs; do not attribute differences to anonymous participation while ignoring selection effects. No challenge-bypass route, invasive tracking, or deliberate overbooking of live events is authorized.

## Data minimization and participant protection

- Prefer aggregate counters, synthetic test material, coarse categories, and volunteered observations. Do not collect names, email addresses, registration answers, raw IP/subnet identifiers, device fingerprints, passwords, tokens, private URLs, or exported attendee lists for research.
- Keep recruitment/consent contact records separate from event and security data, under an approved owner and retention deadline. Pseudonymization is not permission for indefinite tracking.
- Set a study-specific minimum retention and deletion plan before collection. Do not extend product PII deadlines, repurpose legal holds, or link security intake identifiers into behavioral analytics.
- Prevent small-cohort reidentification through suppression/aggregation and an approved reporting threshold. Publish only safe aggregates and redacted qualitative themes.
- Explain assistance, withdrawal, failure reporting, and alternatives without penalizing attendees. Use existing staff-assisted/approval workflows, not a public unprotected allocation bypass.
- Define incident stopping and escalation rules before the pilot. Any duplicate capacity effect, authority leak, or retained credential is a defect to address, not a tolerated experimental outcome.

## Deliverables and acceptance

- [ ] Named accountable owner, consented operator/site, privacy/security review, recruitment method, and data-deletion owner are recorded.
- [ ] A dated protocol fixes hypotheses, denominators, sample-size rationale, comparator limits, success thresholds, and stopping rules **before** observations are analyzed.
- [ ] Native host and functional acceptance are referenced separately from field-effectiveness evidence.
- [ ] No-show/abuse/false-rejection, mobile/accessibility, operator comprehension, and out-of-band handover questions each have observed evidence or an explicit unmeasured disposition.
- [ ] Results include sample sizes, uncertainty, exclusions, confounders, adverse outcomes, and data-deletion confirmation; no invented precision or certification language.
- [ ] The decision record recommends retain/tune/redesign with reasons. Any product change gets its own authority review, deterministic regression tests, and public/internal documentation.

Until these conditions hold, describe effectiveness as **unmeasured**, not safe by field validation. Do not copy speculative results into the single I-VSD report or release claims.

## References

- [ADR-029](../../docs/internal/adr/ADR-029-email-optional-self-hosting.md)
- [Implementation pitfalls](../_journal/domains/email-optional-self-hosting.md)
- [Public anonymous participation guidance](../../docs/public/documentation/readme/events-and-ticketing/email-optional-participation.md)
- [Challenge implementation](../../src/Explore.Infrastructure/Services/Registration/AnonymousRegistrationChallengeService.cs)
- [Retention policy](../../src/Explore.Domain/Services/Registration/AnonymousRegistrationRetentionPolicy.cs)
- [Single I-VSD report](../../islamic-value-sensitive-design/i-vsd-email-optional-self-hosting.md)
