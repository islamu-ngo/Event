# Reader-First Technical Writing

> **Audience:** Contributors and AI agents
> **Status:** Contribution guidance
> **Owner:** Contributor Experience
> **Source Anchors:** `docs/internal/GOVERNANCE.md`, `.agents/skills/conventional-commit/SKILL.md`, `.agents/skills/implementation-plan/resources/operational-artifacts.md`, `.agents/skills/implement-tasks/SKILL.md`

## Meaning Before Mechanism

Write for a developer who understands software but has not memorized this workstream's vocabulary. The reader should understand the consequence without translating a chain of internal terms.

- Start with the concrete problem, who it affects, and what changes. For internal work, state the engineering consequence; do not invent a visitor benefit.
- Follow with how the implementation produces that result: relevant components, data/control flow, and constraints. Pair technical terms with their purpose on first use.
- Keep names a reviewer or operator needs: classes, endpoints, fields, migrations, settings, status codes, protocols, and commands. Put them after the explanation rather than using them as the explanation.
- Use an example when a rule is otherwise hard to visualize. Do not add an analogy or glossary when one direct sentence suffices.
- Separate claims from evidence and label planned, implemented, verified, pending, and pre-existing behavior accurately.
- Let the required substance determine length. Add sentences, paragraphs, and examples wherever understanding requires them; do not shorten an explanation to meet an arbitrary brevity target. Organize one complete account rather than making the reader reconcile separate simplified and technical versions.

Improve readability by expanding explanation, not deleting technical substance or terminology. Retain the mechanisms, rationale, invariants, exceptions, trade-offs, evidence, and operator actions; explain what each consequential term means and does. A longer complete report is preferable to a shorter report that loses information. "Browsing is better" loses the behavior; "current-authority reprojection with epoch fencing" leaves the reader to discover it.

## Commit Messages

Keep Conventional Commit syntax and all required trailers intact. The subject names one concrete outcome, not a workstream label or a list of mechanisms. The body explains why the change is needed, then its implementation and consequential limits.

Illustrative rewrites, not claims about newly implemented or verified behavior:

| Hard to decode or too vague | Concrete subject |
| --- | --- |
| `fix(discovery): enforce occurrence-correct projection` | `fix(discovery): match search dates and locations to the same session` |
| `feat(discovery): allocate canonical home identities` | `feat(discovery): show each event in only one homepage section` |
| `fix(events): fence stale completion generations` | `fix(events): keep late responses from replacing the newly opened event` |
| `fix(database): wrap session projection in execution strategy` | `fix(database): retry session index rebuilds after temporary failures` |
| `test(testing): harden persistence integration tests` | `test(testing): isolate database fixtures between parallel runs` |

Example body with both meaning and mechanism:

```text
fix(discovery): match search dates and locations to the same session

A program with Paris on Saturday and London on Sunday must not appear
in a search for London on Saturday.

Use one eligible published session for both date/location filtering
and the event card, rather than combining matches from different sessions.
```

Use the internal-work examples only when that is the actual change. Add `!`, `BREAKING CHANGE:`, `Change-Id:`, or changelog-skip trailers when the classification requires them. A breaking-change footer names what consumers/operators must change, not merely "contract replaced."

## Pull Request Descriptions

Describe the final change for a reviewer who did not follow the execution chat. Lead with the outcome, not "deliver the approved workstream." Organize by behaviors/components rather than phase IDs or commit hashes.

Use the sections below where relevant. Preserve the repository's exact `## Release Impact` heading, applicable checklist labels, and non-empty `Details:` section.

```markdown
## Summary
<What problem this solves, for whom, and the concrete result.>

## What changes
- **<Behavior or engineering outcome>:** <Before/after explanation.>
  <Relevant mechanism and important limitation, without repeating the outcome.>

## How it works
<Components, data/control flow, invariants, failure handling, and design rationale.
Explain terms on first use; retain exact contracts and consequential trade-offs.
Expand this section or add focused subsections rather than removing detail.>

## Deployment and compatibility
<Required operator/consumer actions, migrations, configuration, and rollback
limits. Omit this section if there is no such impact.>

## Verification
<What the checks proved and where they ran. Identify pending/failed checks
and link durable evidence when detailed reproduction is needed.>

## Known limitations
<Unresolved issues, their practical effect, and whether new or pre-existing.
Omit when none exist; do not hide release blockers in an appendix.>

## Release Impact
<Exact applicable checklist from the execution skill or release validator.>

Details:
<Impact explanation and release-note/documentation links, or why none is needed.>
```

### Evidence Without An Execution Diary

- **Explain What the Check Proves**: Lead by explaining the business invariant, failure mode, or safety boundary the test guards, before listing commands and numbers:
  > *Example*: "The concurrency integration test exercises simultaneous publication requests on the last remaining capacity slot to prove that concurrent writers receive HTTP 409 conflict and the tenant quota cannot be overdrawn."
- **Partition Verification Layers Truthfully**:
  - *Ring 1 (In-Memory Sliced Tests)*: State the targeted domain state machines and business logic verified in <2 seconds.
  - *Ring 2 (Persistence & Provider Integration)*: State the concrete provider tested (e.g. SQLite in-memory or PostgreSQL testcontainer), transaction rollback semantics, and concurrency locks proven.
  - *Architecture & Guardrail Gates*: State schema integrity, HAL affordance gates, and intent contracts validated.
  - *Pending Checks & CI Pipelines*: Explicitly separate local verification from browser acceptance and pending asynchronous CI workflows. Never turn pending CI or untested providers into an inferred pass.
- Put final evidence in the main account. Failed filter attempts, superseded runs, and command-budget adjustments belong in execution records unless they leave a limitation affecting confidence.
- Link tracked documentation, test commands, CI runs, or published evidence for detail. Gitignored plans and parked-worktree paths are not accessible PR evidence.
- Keep screenshots' publication status truthful. A local capture is not an uploaded image.
- Required deployment actions, breaking contracts, rollback limits, security/privacy consequences, and unresolved blockers stay visible. An optional technical appendix is for depth, not for concealing them.

### Balanced Discovery Example

These excerpts translate the supplied discovery PR example; they do not report checks performed while editing this guidance.

**Summary:** Search now matches dates and locations from the same event session, the homepage avoids duplicate events across sections, and loading more results keeps a stable order. Authorized independent reviewers can correct duplicate listings without moving existing registrations or payments.

**How browsing works:** Starting a search captures a fixed ordered list of event IDs, called a search snapshot. Later batches follow that list even if popularity changes, while event details and viewing permissions are checked again before display. The snapshot fixes membership and order; it does not preserve stale public data. ASP.NET Core Data Protection protects continuation tokens against modification.

**API and recovery:** Page numbers and catalog-wide totals are replaced by continuation links. `snapshotCount` counts the captured list, not every event in the catalog; `hasMore` and the HAL `next` link indicate another batch. Invalid continuations return `400`; expired searches return `410`; changed access or listing identity returns `409`; unavailable authority or capacity returns `503`. The interface offers an explicit restart rather than silently reusing old results.

**Deployment:** Apply the generated `EventDiscoveryIdentity` and `EventDiscoveryTraversal` migrations and deploy matching API and interface versions together. Replicas must share the Data Protection key ring and application identity. Rollback requires matching old application and database versions; do not mix the old application with the replacement schema. Assign duplicate-review permission explicitly.

## Progress And Completion Reports

Within any required status format, explain the delivered result first. Then give the implementation, rationale, constraints, and verification, followed by what remains or needs a decision. Expand explanations as needed to preserve the full substance. A completion report is neither a commit inventory nor a generic "implemented successfully."

For example: "Opening event B can no longer be overwritten by a slow response for event A. The detail page uses a load generation to discard responses from earlier navigation. The regression check completes A's request after B opens and confirms B remains visible."

That example describes what suitable evidence would prove; use it only after the check actually ran. Name changed files/components needed to understand the implementation and their responsibilities, rather than using a file inventory as the explanation. Keep blockers and unrun checks explicit.

## Feedback And Decision Briefs

State the question in terms of the consequence the user is choosing. Supply enough technical rationale to decide, without requiring them to open a plan.

1. Explain what works now and the specific unresolved issue.
2. Ask the exact decision, including why it is needed now.
3. Give concrete options and a recommended default. Explain the user/operator effect and the technical cost of each.
4. State what happens after the answer. Ask only when the answer changes the work.

Illustrative question:

> Should an expired search ask the visitor to restart, or refresh automatically? I recommend an explicit restart: the visitor sees that the result order may change, and the server creates a new snapshot only after confirmation. Automatic refresh removes that click but can change the list while the visitor is browsing. If you choose explicit restart, I will add the expiry message and restart action.

Use established contracts rather than reopening settled decisions. Pair a phase ID with a descriptive name if the ID is useful; the name alone is usually enough.

## Readability Check

Before delivering, read the opening, headings, and first sentence of each bullet without their technical follow-ups. Can the reader tell what changed, why it matters, and what they must do or decide?

Then read the technical details. Can a reviewer identify the mechanism, rationale, consequential constraints, required actions, and the evidence supporting the claims? If the first pass fails, add actors, actions, definitions, and examples around the technical language. If the second fails, restore the missing technical substance. Never pass the first check by sacrificing the second.
