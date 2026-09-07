<!-- ABOUTME: Thin pointer for GitHub Copilot. All agent rules live in the tool-neutral AGENTS.md. -->
<!-- ABOUTME: Do not duplicate rules here. Only tool-specific Copilot operational notes belong in this file. -->

# GitHub Copilot Instructions

> **Primary source of truth is [`AGENTS.md`](../AGENTS.md)**. Read it first.
> This file contains only Copilot-specific operational notes.

Last Updated: 2026-04-24

---

## Cold-Start Sequence

1. Open [`AGENTS.md`](../AGENTS.md) and follow the Contribution Contract (§1) and Cold-Start Flow (§3).
2. Classify your task against [`.agents/contract/intents.yaml`](../.agents/contract/intents.yaml).
3. Load only the files the matched intent specifies — respect `must_read_docs`, `paths_in_scope`, `paths_forbidden`, and `minimum_tests`.
4. Follow Clean Architecture dependency direction: Domain → Application → Infrastructure → API/Blazor.
5. Run the intent's `verification_commands` before considering the change complete.

---

## Rule Authority

Priority order (highest wins):

1. [`AGENTS.md`](../AGENTS.md) §5 CRITICAL RULES
2. [`docs/QUICK_REFERENCE.md`](../docs/QUICK_REFERENCE.md)
3. [`docs/GOVERNANCE.md`](../docs/GOVERNANCE.md)
4. Path-scoped rules in [`.agents/rules/`](../.agents/rules/) whose `paths:` glob matches your edit.
5. Skills in [`.agents/skills/`](../.agents/skills/) — loaded on demand.

---

## Copilot-Specific Notes

- Copilot does not auto-load `.agents/rules/*.md`. When suggesting code under `Explore.*/` or `Event.*Tests/`, retrieve the matching rule once before generating multi-file changes.
- Inline completions that only fit `AGENTS.md §5` rules (e.g., self-documenting code without synthetic headers, file-scoped namespace, manual validator instantiation) are safe defaults.
- For any change that crosses a layer (Domain → Application, Application → Persistence, etc.), stop and load the relevant skill before continuing.
- Do not suggest:
  - Defining or hard-coding secrets, passwords, or tokens in `AppHost.cs`, tests, controllers, or code; secrets belong exclusively in Infisical or `.env` / `.env.example`.
  - Ad-hoc Python (`python -c ...`) or Node/JS scripts for repository tasks, inspections, or text transformations.
  - Creating temporary/scratch scripts when standard Bash or native edit tools suffice.
  - Adding developer/agent helper scripts into `.ci/scripts/` (which is reserved exclusively for CI/CD pipelines; persistent repo tools belong in `eng/`).
  - `as any` / `@ts-ignore` / `@ts-expect-error` equivalents (n/a here, but stated for completeness).
  - Backward-compatibility shims — the repo is in active development mode.
  - `rm`, `mv`, or overwrite redirection (`>`) in shell output — see `AGENTS.md` Appendix.

---

## Verification Commands

Same as `AGENTS.md` §7 — do not duplicate here. Run via the `/check` slash command (when using Claude Code) or invoke the test projects directly (always with `--project`, never solution-level `dotnet test`).

---

## See Also

- [`AGENTS.md`](../AGENTS.md) — tool-neutral entrypoint (canonical).
- [`docs/index.md`](../docs/index.md) — documentation navigation root.
- [`.agents/contract/intents.yaml`](../.agents/contract/intents.yaml) — intent registry.
