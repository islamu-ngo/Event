<!-- ABOUTME: I-VSD planning report for Plane Compose project management integration into agentic workflows. -->
<!-- ABOUTME: Evaluates project truthfulness (Sidq), computational stewardship (Amanah), privacy/confidentiality (Sirr), and developer dignity (Raf' al-Haraj). -->

# Plane Compose Workflow Integration — I-VSD Planning Report

Last Updated: 2026-11-08 Europe/Brussels

## Review Metadata
- Mode: planning
- Subject: Declarative project management integration (Plane Compose), work-item sharding, worktree symlinking, and gitignored project tracking
- Workstream: plane-compose-workflow-integration
- Report kind: planning report
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-08
- Reviewed input revision: `develop`
- Supersedes: none

## Scope

This report evaluates provider-controlled architectural decisions governing how AI agents and human developers track and synchronize project management state via Plane Compose within the ISLAMU Event repository. In scope:
1. **Privacy & Confidentiality Protection (`Sirr` / `Khususiyyah`)**: Ensuring internal vulnerability investigations, private architectural discussions, and detailed backlogs in Plane Compose remain strictly gitignored and never leak into public/external git repositories.
2. **Computational Stewardship & Token Waste Prevention (`Amanah` / `Israf`)**: Preventing token exhaustion and context pollution caused by reading/editing a 1.75 MB monolithic `workitems.yaml` (2,616 items) by adopting Plane Compose's native multi-file work-item sharding (`active-<task>.yaml`).
3. **Truthfulness & Reality Alignment (`Sidq` / `Adalah`)**: Guaranteeing that project tracking states in Plane reflect verified code reality (evidence-based progression) rather than fabricated agent claims, while preventing duplicate work-item sprawl.
4. **Friction Removal & Developer Dignity (`Raf' al-Haraj`)**: Automating worktree state preservation (`.plane/` isolation) so agents operating in isolated git worktrees do not suffer from broken state caches or manual reconciliation friction.
5. **Codification across the Dev-Doc Triad and Skills**: Seamlessly embedding Plane ticket references (`Refs: EVENT-XXX`) into planning (`implementation-plan`), review (`senior-cto-feedback`), execution (`implement-tasks`), and commits (`conventional-commit`).

Out of scope: modifying Plane Cloud's remote billing/tier rules, implementing custom webhooks, or altering Sunni moral boundaries regarding event moderation.

## Claim Boundary

Claims describe the repository at Git revision `develop` as executed on a Linux x86_64 host with `plane v0.5.2` and connection `connection-1` targeting workspace `islamu` and project `EVENT`.

## Findings

| ID | Lifecycle | Severity | Claim type | Principle / Domain | Stakeholder | Provider-controlled decision | Evidence | Validation | Mitigation |
|---|---|---|---|---|---|---|---|---|---|
| `IVSD-F001` | open | High | Information Leakage / Breach of Trust | Sirr (confidentiality), Amanah (trust) | Users, operators, maintainers | Whether Plane Compose project files (`event/`) should be committed to Git | Commit `bdc47c3e1e` explicitly noted leak risks of publicizing internal security issues; `event/` gitignored | `.gitignore` inspection | `IVSD-M001` |
| `IVSD-F002` | open | High | Waste & Cognitive Exhaustion | Amanah (stewardship), Israf (waste prevention) | AI agents, developers, platform sponsors | Whether agents must parse and edit a 1.75 MB monolithic `workitems.yaml` (2,616 items) | File size is 1.75 MB; exceeds single-read tool limits; risks regex editing corruption | Tool benchmark & schema audit | `IVSD-M002` |
| `IVSD-F003` | open | High | False State & Deception | Sidq (truthfulness), Amanah | Project stakeholders, developers | Whether agents may mark Plane work items 'Done' without concrete verification evidence | Observed tendency of autonomous tools to advance tickets based on intent rather than verified test runs | Skill audit of `plane-compose` | `IVSD-M003` |
| `IVSD-F004` | open | Medium | Operational Friction & Hardship | Raf' al-Haraj (hardship removal) | AI agents, contributors | Whether worktrees in `.worktrees/<task>` lose Plane state due to gitignored `.plane/` | `.worktrees/<task>` lacks `event/.plane/state.json`, causing unmapped sync state | Worktree reproduction | `IVSD-M004` |

## Recommendations

- `IVSD-M001`: **Preserve Absolute Gitignore Isolation for `event/`**: Keep `event/` strictly gitignored in root `.gitignore`. Never commit internal Plane YAML, state tokens, or local work items to Git history.
- `IVSD-M002`: **Adopt Native Active Task File Sharding (`work/active-<task>.yaml`)**: Utilize Plane Compose's native multi-file loading. Active development creates small, scoped YAML files (< 5 KB) containing only the work items relevant to the active workstream, leaving the 1.75 MB historical archive untouched.
- `IVSD-M003`: **Enforce Evidence-Based State Transitions & Preflight Dry-Runs**: Agents may only advance work items to `Done` upon passing Ring 1/2/3 verification. All remote syncs must pass `plane schema validate`, `plane validate`, and `plane push --dry-run` before `plane push --work-only`.
- `IVSD-M004`: **Standardize the Worktree Symlink Protocol**: Update `implement-tasks` and `AGENTIC_CONTEXT_ENGINEERING.md` to automatically symlink the root `event/` folder into `.worktrees/<task>/event` (`ln -s ../../event event`), ensuring a single shared state cache without manual re-initialization.

## Refresh Triggers
1. Changes to Plane Compose CLI version or schema format.
2. Introduction of public release of Plane workspace data.
3. Transition from Plane Cloud to Self-Hosted Plane instance.
