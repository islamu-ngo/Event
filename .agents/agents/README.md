---
name: Agents Documentation
description: Selection and coordination registry for repository subagent operational contracts
disabled: true
---

<!-- ABOUTME: Repository registry and routing guide for role subagents. -->
<!-- ABOUTME: Defines role selection, coordination, provenance, and the boundary between agents and skills. -->

# Repository Subagent Registry

Agent profiles are small operational contracts governed by [`_AGENT_SCHEMA.md`](_AGENT_SCHEMA.md). They specialize recurring repository work; they do not replace the root [Contribution Contract](../../AGENTS.md), the [intent registry](../contract/intents.yaml), or task-specific skills.

## Role Matrix

| Agent | Invoke for | Owns | Mode | Model tier |
|---|---|---|---|---|
| [Architect](architect-agent.md) | Cross-layer design, ADRs, implementation sequencing | Architecture decisions and planning artifacts | Docs-only mutation | Advanced |
| [Backend Engineer](backend-engineer-agent.md) | Domain, Application, Persistence, Infrastructure business flows | Backend implementation and focused tests | Mutation | Balanced |
| [Presentation Engineer](presentation-engineer-agent.md) | API/HAL contracts, BFF, generated-client consumption, Blazor UX | Presentation vertical slices and visual behavior | Mutation | Balanced |
| [Security & Privacy](security-privacy-agent.md) | Identity, authorization, tenancy, secrets, privacy, abuse boundaries | Security-sensitive implementation and threat evidence | Mutation | Advanced |
| [Platform Operations](platform-operations-agent.md) | Aspire, hosting, CI/CD, deployment, observability, recovery | Operational implementation and runbooks | Mutation | Balanced |
| [Quality Verifier](quality-verifier-agent.md) | Reproduce failures and run proportional verification | Build, tests, runtime evidence, failure classification | Read-only | Balanced |
| [Change Reviewer](change-reviewer-agent.md) | Review a diff or PR for real regressions and missing evidence | Risk-ranked review and merge recommendation | Read-only | Advanced |
| [Librarian](librarian-agent.md) | Repository docs, clean-room research, inventories, durable findings | Documentation truth and provenance | Docs-only mutation | Economical |

## Selection Rules

1. Use the lowest-complexity path that works. Direct work or a built-in `worker`/`explorer` is preferable for an atomic task.
2. Select one primary agent by the highest-risk owned boundary, then add read-only specialists only for independent evidence.
3. A cross-layer feature normally stays with one mutating agent until a real ownership boundary is reached; do not split files merely to use more agents.
4. Use `quality-verifier-agent` after implementation for empirical checks and `change-reviewer-agent` for independent semantic review. Neither edits the fix.
5. Send broad codebase search, file inventories, documentation routing, and mechanical evidence collection to an economical built-in read-only explorer. Use Librarian only when documentation/research ownership or mutation is required. Give one narrow question and use the result cap in [Context Engineering](../CONTEXT_ENGINEERING.md).
6. Keep goals, constraints, architecture decisions, and synthesis in the main agent. Escalate model tier only for a concrete unresolved judgement, never because the search surface is large.
7. Never run mutating agents concurrently on overlapping paths. Handoffs name the exact files, decisions, and remaining acceptance criteria.

## Common Routing

| Signal | Primary agent | Typical supporting agent |
|---|---|---|
| New aggregate, handler, repository, outbox flow | Backend Engineer | Security & Privacy when authority or tenant scope changes |
| Controller + HAL + generated client + Blazor affordance | Presentation Engineer | Change Reviewer |
| 401/403, Cerbos, tenant leakage, erasure, secret exposure | Security & Privacy | Quality Verifier |
| AppHost, topology, container, workflow, telemetry, incident recovery | Platform Operations | Security & Privacy for credential/trust changes |
| Major design or breaking cross-layer refactor | Architect | Owning implementation agent after approval |
| Failing build/test or uncertain runtime behavior | Quality Verifier | Owning implementation agent after root cause is proven |
| PR approval or regression audit | Change Reviewer | Quality Verifier for commands and runtime evidence |
| Documentation drift or externally informed specification | Librarian | Architect for durable architecture decisions |

## Agents Versus Skills

- An **agent** owns a recurring outcome, tool boundary, workflow, and handoff contract.
- A **skill** supplies a focused procedure or rule set inside that role. Agents load only the skills matching the classified intent.
- Do not create agents for generic exploration, generic implementation, one command, one library, or one current feature. Built-in agents and existing skills already cover those shapes.

## Coordination Contract

Every delegated task must state: goal, owned paths or read-only mode, required evidence, expected output, stop condition, model tier, and result-size cap. Pass only the minimum task context, not the parent transcript. The primary agent remains accountable for synthesis and verifies returned claims against repository evidence before acting.

Parallel work is preferred for independent read-heavy exploration, test execution, log analysis, and review. Write-heavy work is sequential unless file ownership is disjoint and explicitly assigned.

All agents follow [Context Engineering](../CONTEXT_ENGINEERING.md). A subagent is a context firebreak only when its bounded result keeps a larger discovery body out of the main context; otherwise direct lookup is cheaper.

Agent profiles and chat configuration are documentation/configuration artifacts. Validate their schema, links, and routing directly; do not add automated tests for them.

## Design Basis And Provenance

Source register, accessed 2026-08-13 Europe/Brussels:

- [OpenAI Codex Subagents](https://developers.openai.com/codex/subagents) — custom agents should be narrow, opinionated, tool-scoped, and used carefully for parallel writes.
- [GitHub custom agents configuration](https://docs.github.com/en/copilot/reference/custom-agents-configuration) — agent descriptions drive routing and tool allow-lists enforce capability boundaries.
- [Microsoft AI agent orchestration patterns](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns) — use the lowest sufficient orchestration complexity and specialize only when coordination cost is justified.

Repository-native design decisions are the eight-role matrix, intent-first skill routing, Clean Architecture ownership, read-only reviewer/verifier separation, and evidence-based handoffs. No third-party implementation source, prompt text, or agent manifest was copied; no dependency changed.
