<!-- ABOUTME: Authoritative template for a new journal finding. Append to journal.md, do not edit this template in place. -->
<!-- ABOUTME: Matches the format validated by AgentContextSchemaTests (date prefix + required fields). -->

[YYYY-MM-DD Europe/Brussels] — <Short descriptive title (≤ 70 chars)>

**Context**: 1–3 sentences describing where and when this came up. Mention the intent classification if relevant (e.g., "while working on `add-write-endpoint`").

**Symptom / Observation**: Exactly what you saw. Copy error messages verbatim. Reference files with paths and line numbers (`Explore.Application/Features/.../Handlers/Foo.cs:42`).

**Root Cause**: The underlying reason, not the surface fix. One paragraph max.

**Resolution**: What fixed it. Include the PR/commit SHA if merged. Include the exact verification command(s) that now pass.

**Why This Matters for Future Work**: What should another agent or human take away? This is the non-obvious insight that justifies a journal entry.

**References**:
- `path/to/file.cs:line`
- `docs/<related-doc>.md`
- `.agents/skills/<related-skill>/SKILL.md`
- `.claude/rules/<related-rule>.md`
- PR / commit: `<url-or-sha>`

**Promotion Consideration**:
- [ ] Candidate for `docs/QUICK_REFERENCE.md` (new non-inferable rule)
- [ ] Candidate for new `.claude/rules/*.md` entry
- [ ] Candidate for skill update: `<skill name>`
- [ ] Candidate for ADR / `MAJOR_DECISIONS.md`
- [ ] Stays in journal only (one-off debugging lesson)

---
