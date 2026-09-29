<!-- ABOUTME: Context-engineering practices for durable skill-authoring workstreams. -->
<!-- ABOUTME: Preserves source evidence, plan state, and handoff accuracy across agents and compaction. -->

# Context Engineering

The governing retrieval budgets, context ledger, model tiers, delegation boundary, and handoff shape live in [Context Engineering Contract](../../../CONTEXT_ENGINEERING.md). Do not duplicate that policy here.

For skill work:

1. Reuse injected `AGENTS.md` and the resolved intent; do not reread either.
2. Load `SKILL.md` as the router, then one resource only when a named authoring decision requires it.
3. Keep schema and design decisions in the main agent. Send broad file/resource inventory to an economical read-only scout using the standard result cap.
4. Resume substantial work from the task-owned `*-context.md`; zoom only the current plan heading and task evidence.
5. Before compaction or handoff, update that task context with decisions, changed paths, verification, next action, and risks instead of copying conversation history.

When the user redirects, stop the old path immediately, preserve only material task state when needed, reclassify, and build a fresh bounded working set.
