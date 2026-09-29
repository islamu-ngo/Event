<!-- ABOUTME: Rules for promoting journal findings into primary documentation, skills, rules, or ADRs. -->
<!-- ABOUTME: Prevents the journal from becoming a graveyard of unread wisdom. -->

# Journal Promotion Rules

> A journal entry graduates when it stops being a one-time debugging lesson and becomes a rule, pattern, or decision that applies to new work.
>
> **Default state**: Entries stay in the journal. Promotion is the exception, not the norm.

Last Updated: 2026-04-24

---

## 1. Promotion Triggers

Promote an entry when **any** of the following is true:

1. **Referenced by ≥ 2 subsequent journal entries** — the underlying pattern is recurring.
2. **Referenced by ≥ 1 PR review comment** citing it as a correctness rule.
3. **Cited in an intent's `must_read_docs` or `load_rules`** — the entry is being used operationally, not just historically.
4. **Generalizes beyond the specific feature** where it was first observed.
5. **Contradicts or sharpens an existing primary documentation** — the doc must be corrected.

---

## 2. Where to Promote

| Entry Type | Destination | Why |
|---|---|---|
| Non-inferable rule applying to new code | `docs/QUICK_REFERENCE.md` | Hard invariants, auto-loaded by every agent |
| Rule scoped to a specific folder / project | New or existing `.claude/rules/*.md` | Path-scoped, loads only when editing those files |
| Pattern teaching a way of working | `.agents/skills/<name>/SKILL.md` | Skills are progressive-disclosure how-tos |
| System-wide decision (layer boundary, tech choice) | `dev/_journal/MAJOR_DECISIONS.md` + new ADR under `docs/adr/` | Decisions deserve their own formal record |
| Convention / code style choice | `docs/GOVERNANCE.md` | Decision frameworks and conventions live here |
| Operational quirk (env-specific, tool-specific) | `docs/TROUBLESHOOTING.md` | Where cold-start agents look for "weird" errors |
| Terminology clarification | `docs/DOCUMENTATION_STYLE_GUIDE.md` (Terminology section) | Keeps vocabulary consistent |

---

## 3. Promotion Workflow

1. **Draft the promotion** in the destination file as a separate commit or PR. Reference the journal entry by date and title.
2. **Annotate the journal entry** in place — do not delete it. Add a line at the end:
   ```
   **Promoted → docs/QUICK_REFERENCE.md §<section>** (PR #NNN, YYYY-MM-DD)
   ```
3. **Update cross-references** in any agent/skill/rule that referenced the journal entry.
4. **Run `Event.Architecture.Tests.AgentContextLinkTests`** to verify no dead links remain.

The journal entry itself is **never deleted**. It is durable evidence of when and why the authoritative rule came to exist.

---

## 4. When NOT to Promote

- The finding is a **debugging war story** that does not apply to new code.
- The fix is a **one-shot workaround** for an external bug (document it, stay local).
- The pattern is **still under review** — wait for the second occurrence.
- Promoting would **duplicate** an existing authoritative rule — instead, add a cross-reference to the existing rule in the journal entry.

---

## 5. Anti-Patterns

- **Promoting too early** → primary documentation bloat with conjecture.
- **Promoting too late** → contributors rediscover the same lesson.
- **Promoting without annotation** → the journal loses traceability.
- **Deleting journal entries after promotion** → loss of historical context.
- **Two authoritative locations for the same rule** → when promoted, pick ONE home (see table above) and cross-reference from others.

---

## 6. Audit Cadence

- **Per-PR review**: reviewers should check if their change promotes a standing journal entry. If yes, include the promotion diff in the same PR.
- **Quarterly sweep**: a maintainer grep-searches entries older than 90 days. Candidates for promotion are surfaced as issues.
- **Per-intent review**: before refining an intent in `.claude/contract/intents.yaml`, re-read the journal entries matching the intent's category.

---

## 7. Related

- [`README.md`](README.md) — how to use the journal.
- [`FINDING_TEMPLATE.md`](FINDING_TEMPLATE.md) — entry format.
- [`AGENTS.md`](../../AGENTS.md) §8 — Memory & Findings overview.
- [`docs/QUICK_REFERENCE.md`](../../docs/QUICK_REFERENCE.md) — where most promotions land.
- [`.claude/contract/intents.yaml`](../../.claude/contract/intents.yaml) — where promoted rules become routing targets.
