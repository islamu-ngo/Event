<!-- ABOUTME: Comprehensive reference and visual guide to ISLAMU Event's Agentic Context Engineering system. -->
<!-- ABOUTME: Visualizes cold start, multi-harness rules, dev-doc state, self-sufficient phase packets, verification, and improvement priorities. -->

# Agentic Context Engineering & AI Workflow Architecture

> **Audience:** Contributors | AI Agents | Platform Architects | Maintainers  
> **Status:** Canonical & Implemented  
<<<<<<< HEAD
> **Last Verified:** 2026-09-06 Europe/Brussels<br>
> **Source Anchors:** [`AGENTS.md`](../../AGENTS.md), [`.agents/CONTEXT_ENGINEERING.md`](../../.agents/CONTEXT_ENGINEERING.md), [`.agents/contract/intents.yaml`](../../.agents/contract/intents.yaml), [`implementation-plan`](../../.agents/skills/implementation-plan/SKILL.md), [`senior-cto-feedback`](../../.agents/skills/senior-cto-feedback/SKILL.md), [`conventional-commit`](../../.agents/skills/conventional-commit/SKILL.md), [`docs/QUICK_REFERENCE.md`](QUICK_REFERENCE.md)
=======
> **Last Verified:** 2026-09-05 Europe/Brussels<br>
> **Source Anchors:** [`AGENTS.md`](../../AGENTS.md), [`.agents/CONTEXT_ENGINEERING.md`](../../.agents/CONTEXT_ENGINEERING.md), [`.agents/contract/intents.yaml`](../../.agents/contract/intents.yaml), [`implementation-plan`](../../.agents/skills/implementation-plan/SKILL.md), [`senior-cto-feedback`](../../.agents/skills/senior-cto-feedback/SKILL.md), [`implement-tasks`](../../.agents/skills/implement-tasks/SKILL.md), [`conventional-commit`](../../.agents/skills/conventional-commit/SKILL.md), [`docs/QUICK_REFERENCE.md`](QUICK_REFERENCE.md)
>>>>>>> origin/develop

---

## 1. Executive Overview & Design Philosophy

The **ISLAMU Event Agentic Context Engineering System** provides a deterministic, token-efficient, and multi-harness governance framework for autonomous and human-in-the-loop AI software engineering.

In modern agentic development, AI agents fail not from a lack of programming syntax knowledge, but from **context drift, assumption hallucination, test tautology ("The Ugly Mirror"), token budget exhaustion, and execution sprawl**. 

This system enforces six core design tenets:

1. **Smallest Decision-Complete Working Set**: Context is retrieved once, summarized once, and reused via an in-session context ledger (`path + symbol + revision`). Agents never reread unchanged files or inject entire registries.
2. **Zero-Turn Structural Injection**: When graph tooling is available, pre-flight blast-radius slices reduce manual traversal by injecting callers, callees, impacted flows, and tests on Turn 1.
3. **Behavior-Bound Test-First Invariants**: Requirements are written as observable system behavior (RFC 2119 + `WHEN`/`THEN` Scenarios) and mapped directly to failing Red tests at pre-agreed public seams *before* production code is touched.
4. **Portable Root Contract With A Scoped Twin Pair**: `AGENTS.md` is the portable authority. Reciprocal path-rule twins currently cover only `.agents/rules` and `.omo/rules`; Claude, Cursor, Copilot, Gemini, and other harness adapters remain separate drift-prone integration surfaces.
<<<<<<< HEAD
5. **Phase-Atomic Native Git Delivery**: Planning pre-authors a self-sufficient phase packet containing exact commit metadata, wholly owned paths, inspection/staging/path-limited commit commands, and post-commit verification. Parallel contributors use separate branches/worktrees, while every verified phase closes with literal commit paths and leaves unrelated work untouched.
6. **Self-Contained Human Interaction & Zero Plan-Opening UX**: Prompts, feedback requests, milestone reports, and approval inquiries must be completely self-contained. The active implementation plan (`dev/active/<task>/`) is internal machine working memory, not the developer console. Agents must never force developers to open plan files to understand bare IDs (e.g. *"P04/P06 with P03 gates open"*); all decisions must be presented as inline, actionable Decision Briefs.
=======
5. **Phase-Atomic Declarative Git Delivery**: Execution runs on dedicated task branches (`feat/<task-name>`). Planning pre-authors declarative Conventional Commit contracts in `tasks.md`. Phases close atomically by staging owned paths and committing directly via Git without recording commit hashes in markdown (the Git commit log is the single source of truth).
>>>>>>> origin/develop

```mermaid
flowchart TB
    subgraph Harnesses["AI Agent Harnesses"]
        OmO["OmO (OpenCode / Senpi / Codex)"]
        Claude["Claude Code"]
        Cursor["Cursor / Windsurf"]
        Copilot["GitHub Copilot"]
        Gemini["Gemini / Antigravity"]
    end

    subgraph CoreContract["Canonical Contract & Bootloader"]
        AgentsMD["AGENTS.md (Canonical Entrypoint)"]
        IntentsYaml[".agents/contract/intents.yaml"]
        ContextEng[".agents/CONTEXT_ENGINEERING.md"]
    end

    subgraph TwinRules["Multi-Harness Twin Rules System"]
        AgentRules[".agents/rules/*.md"]
        OmoRules[".omo/rules/*.md"]
    end

    subgraph DevDocTriad["Active Workstream (dev/active/<task>/)"]
        PlanMD["<task>-plan.md\n(Architecture & Scenarios)"]
        TasksMD["<task>-tasks.md\n(Execution Ledger + Commit Packets)"]
        ContextMD["<task>-context.md\n(Working Memory & Handoffs)"]
    end

    subgraph Verification["Verification & Quality Gates"]
        TUnitFilter["Fast TUnit Slice\n(--treenode-filter)"]
        ArchTests["Architecture Tests\n(Clean Architecture & Conventions)"]
        TwoAxisReview["Two-Axis Review\n(Standards vs Spec Fidelity)"]
        EvidenceGate["QA Evidence Capture\n(.omo/evidence/<task>/)"]
        PhaseCommit["Phase-Owned Conventional Commit\n(Task branch/worktree, explicit paths)"]
    end

    Harnesses --> CoreContract
    CoreContract --> TwinRules
    CoreContract --> DevDocTriad
    DevDocTriad --> Verification
    EvidenceGate --> PhaseCommit
```

## 2. The Three-Tier Architecture: Orchestration, Phase Closure, And Domain Guardrails

The repository's agentic system divides its 40+ skills into three distinct tiers:

1. **User-Invoked Orchestration Tier (Human-in-the-Loop Governance & Execution)**: High-level governance, ethical design, planning, critique, and autonomous execution skills that the developer explicitly invokes. During execution, the developer combines the `/goal` harness command with the `implement-tasks` skill, instructing the agent to utilize Tavily MCP for external research, Context7 MCP for framework documentation, and Code-Review-Graph MCP for structural discovery, upholding Clean Architecture, enterprise-grade engineering, and pre-release greenfield breaking-change freedom (zero backward compatibility baggage).
2. **Plan-Authoring Phase Closure Tier (Commit Contract Dependency & Standalone Human Tool)**: The `conventional-commit` skill is a dependency of `implementation-plan` and `senior-cto-feedback`, used during planning and review to pre-author and validate declarative commit contracts in `tasks.md`. The active execution loop does NOT load `conventional-commit` (saving context tokens); it directly executes the pre-authored contract. Human developers also invoke `conventional-commit` standalone when making changes directly or for ad-hoc commits.
3. **Indirectly-Invoked Domain Execution Tier (Autonomous Machine Guardrails)**: Technical domain patterns, Clean Architecture rules, and tool wrappers that the AI activates autonomously in the background based on edited file paths, matched intents, and domain patterns.

### The Canonical 5-Stage Human-in-the-Loop Lifecycle

This is the standard, end-to-end path for implementing substantial features and architectural changes:

```mermaid
flowchart TD
    subgraph Stage1["Stage 1: Ethical & Value Framing (User-Invoked)"]
        UserTrigger1["User Prompt:\n'Run i-vsd on <feature>'"] --> IVSD["i-vsd Skill\n• Evaluates Provider Responsibility\n• Sunni Islamic Moral Boundaries\n• Mitigation & Uncertainty Traceability"]
        IVSD --> IVSDDoc["Persists Deliverable:\nislamic-value-sensitive-design/\ni-vsd-<task-name>.md"]
    end

    subgraph Stage2["Stage 2: Implementation Planning & Interrogation (User-Invoked)"]
        IVSDDoc --> PlanTrigger["User Prompt:\n'Create implementation plan for <feature>'"]
        PlanTrigger --> PlanSkill["implementation-plan Skill\n• Ingests i-vsd Deliverable\n• Runs /grill-me Technical Socratic Interrogation\n• Dependencies: conventional-commit for commit contracts\n• (If Major Fork: robin-neutral Steelmanning)"]
        PlanSkill --> DevDocTriadInit["Initializes dev/active/<task>/\n• <task>-plan.md (Scenarios & Architecture)\n• <task>-tasks.md (Tests + Declarative Commit Contracts)\n• <task>-context.md (Ephemeral Working Memory)"]
    end

    subgraph Stage3["Stage 3: Adversarial CTO Audit & Socratic Stress-Test (User-Invoked)"]
        DevDocTriadInit --> CTOTrigger["User Prompt:\n'Run senior-cto-feedback'"]
<<<<<<< HEAD
        CTOTrigger --> CTOSkill["senior-cto-feedback Skill\n• Audits & Directly Refines Triad (Zero Review Files)\n• 3D Scorecard (Completeness, Correctness, Coherence)\n• 4-Point Right-Sizing Check (Split PR Heuristic)\n• 'Worst Break' Catastrophic Invariant Check\n• High-Signal Chat Summary"]
=======
        CTOTrigger --> CTOSkill["senior-cto-feedback Skill\n• 3D Scorecard (Completeness, Correctness, Coherence)\n• 4-Point Right-Sizing Check (Split PR Heuristic)\n• 'Worst Break' Catastrophic Invariant Check\n• Validates Declarative Per-Phase Commit Contracts"]
>>>>>>> origin/develop
    end

    subgraph Stage4["Stage 4: Implementation Execution & Autonomous Domain Guardrails"]
        CTOSkill --> ExecTrigger["User Combines /goal + implement-tasks:\n• Research: Tavily MCP & Context7 MCP\n• Structure: Code-Review-Graph MCP\n• Conventions: Clean Architecture & Enterprise Patterns\n• Pre-Release Freedom: Zero backward compatibility baggage"]
        ExecTrigger --> AutoExecution["Autonomous Domain Execution Loop\n• clean-architecture-rules\n• cqrs-mediatr-guidelines\n• dotnet-efcore-guidelines\n• auth-patterns & outbox-pattern\n• debug-issue & refactor-safely\n• Fast TUnit Slicing (--treenode-filter)"]
        AutoExecution --> PhaseVerification["Phase Verification\n• One Release build\n• At most one selected project test\n• Ownership disposition for failures"]
        PhaseVerification --> PhaseCommit["Immediate Phase Commit\n• Execute pre-authored declarative tasks.md contract\n• No conventional-commit skill reload\n• Commit owned paths directly (no hash logging)"]
        PhaseCommit --> MorePhases{"More approved phases?"}
        MorePhases -->|"Yes"| AutoExecution
    end

    subgraph Stage5["Stage 5: Workstream Completion & Knowledge Graduation"]
        MorePhases -->|"No"| FinalReview["Final Workstream Review & Teaching Summary\n• Comprehensive technical explanation\n• No unrelated shared-tree files committed\n• Dual-documentation parity (public vs internal)"]
        FinalReview --> KnowledgeGraduation["Knowledge Graduation\n• Move completed task to dev/backlog/\n• Durable ADRs to docs/internal/adr/\n• Durable findings to dev/_journal/"]
        KnowledgeGraduation --> Shipped(["Task Complete on Dedicated Branch / Ready for PR"])
    end
```

### Skill Tier Taxonomy

| Tier | Invocation Model | Key Skills | Role & Primary Responsibility |
|---|---|---|---|
<<<<<<< HEAD
| **Orchestration Tier** | **User-Invoked** (Direct developer prompt or slash command) | `i-vsd`, `implementation-plan`, `senior-cto-feedback`, `/grill-me`, `/goal`, `robin-neutral` | Sets ethical boundaries, interrogates requirements, authors workstream triads (`dev/active/<task>/`), audits architecture, and directly refines triads before implementation. |
| **Phase Closure Tier** | **Planning/Review-Invoked; override-only during execution** | `conventional-commit` | Planning writes exact self-sufficient contracts; CTO review validates them; normal execution does not reload the skill. Only material divergence loads it to author recorded replacements before committing owned paths. |
=======
| **Orchestration Tier** | **User-Invoked** (Direct developer prompt or slash command) | `i-vsd`, `implementation-plan`, `senior-cto-feedback`, `implement-tasks`, `/goal`, `/grill-me`, `robin-neutral` | Sets ethical boundaries, interrogates requirements, authors workstream plans (`dev/active/<task>/`), audits architecture, and executes phases autonomously to completion via `/goal`. |
| **Phase Closure Tier** | **Planning/Review-Invoked Dependency; Standalone Human Tool** | `conventional-commit` | Dependency of `implementation-plan` and `senior-cto-feedback` for authoring declarative commit contracts in `tasks.md`. Execution does not load this skill. Also used standalone by humans for manual or ad-hoc commits. |
>>>>>>> origin/develop
| **Domain Execution Tier** | **Indirectly-Invoked** (Autonomously activated via matched intent, rule path, or graph trigger) | `clean-architecture-rules`, `cqrs-mediatr-guidelines`, `dotnet-efcore-guidelines`, `blazor-ui-conventions`, `auth-patterns`, `outbox-pattern`, `debug-issue`, `refactor-safely`, `review-changes`, `review-pr`, `accessibility` | Enforces layer boundaries, immutable record contracts, zero-internal-mocking, fail-closed auth, transactional outbox dispatch, and two-axis review during active coding. |

### Multi-Session Cognitive Lifecycle & Session Isolation Architecture

To achieve maximum reliability, eliminate AI cognitive bias, and prevent context saturation, substantial feature work is partitioned across **three independent agent sessions**. Rather than forcing planning, adversarial critique, and implementation into a single long conversation window, each stage runs in an isolated session:

```mermaid
flowchart TD
    subgraph S1["Session 1: The Architect / Planner"]
        M1["Model Tier: Highest Reasoning (e.g. xhigh)\nSkill: implementation-plan"]
        A1["• Zero-turn graph exploration (code-review-graph)\n• Authors RFC 2119 specs & WHEN/THEN scenarios\n• Drafts initial triad in dev/active/<task>/"]
    end

    subgraph S2["Session 2: The Adversary / Senior CTO"]
        M2["Model Tier: High / Highest Reasoning\nSkill: senior-cto-feedback\nEnvironment: Fresh Clean Session"]
        A2["• Cognitive Orthogonality (zero self-confirmation bias)\n• Zero Context Pollution (0-token fresh scratchpad)\n• Socratic stress-testing & 'Worst Break' check\n• Directly refines triad in place (Zero review files)\n• Marks plan.md §0 'Applied & Aligned'"]
    end

    subgraph S3["Session 3: The Implementer"]
        M3["Model Tier: High Reasoning\nSkill: implement-tasks\nEnvironment: Fresh Clean Session + Isolated Worktree"]
        A3["• Verifies Section 0 review approval\n• Executes plan mv into .worktrees/<task>\n• Red -> Green -> Refactor test-first loops\n• Closes phases with planned Conventional Commits\n• Knowledge graduation & worktree teardown"]
    end

    S1 -->|Pristine Triad| S2
    S2 -->|CTO-Refined Triad| S3
```

#### Why Multi-Session Isolation Is Structurally Superior

1. **Immunity to Self-Confirmation Bias (Ego-Preservation)**: When the same LLM session authors an architecture and is subsequently asked to review it, the model experiences an algorithmic attractor to defend its own previous tokens, downplay its own blind spots, and offer superficial praise. Starting Session 2 in a fresh conversation wipes the autoregressive token history. The reviewing agent evaluates the triad purely as cold, external source material with zero emotional or conversational attachment to authoring choices.
2. **Elimination of Context Pollution & Attention Degradation**: Intensive planning sessions accumulate 40,000–100,000+ tokens of raw AST searches, grep outputs, and discarded design alternatives. Forcing implementation inside that degraded context window leads to "needle-in-a-haystack" instruction loss and recency bias. Starting Session 3 fresh allows the implementing agent to dedicate 100% of its attention budget to compilation diagnostics, unit test results, and active source files.
3. **Model Tier Matching Strategy**:
   - **Planning (Session 1)**: Use the highest available reasoning model (`xhigh` / high reasoning effort) to maximize cross-module foresight, architectural synthesis, and contract precision.
   - **Review (Session 2)**: Use equal or higher reasoning effort (`xhigh` for Tiers 0–1 Sovereign/Security; `high` for Tiers 2–4). A reviewer must match or exceed the author's reasoning power to spot subtle concurrency races, transaction boundary leaks, and tenant isolation gaps.
   - **Implementation (Session 3)**: Use high reasoning models (`high`) optimized for fast, accurate code generation, precision typing, and test-first execution loops.

---

## 3. Zero-Knowledge Cold-Start & Classification Lifecycle

When an AI agent starts work in this repository, it follows a deterministic cold-start sequence.

```mermaid
flowchart TD
    Start(["New User Request"]) --> Classify["1. CLASSIFY\nMatch intent in .agents/contract/intents.yaml\nResolve Criticality Tier (0–4)"]
    
    Classify --> CheckTier{"Criticality Tier"}
    
    CheckTier -->|"Tier 0: Sovereign\nTier 1: Security\nTier 2: Privacy"| HighCritIntake["2A. HIGH-CRITICALITY INTAKE\n• Load i-vsd and author i-vsd report\n• Proactive Socratic /grill-me session\n• Identify 'Worst Break' catastrophic scenario"]
    CheckTier -->|"Tier 3: Domain State"| StandardIntake["2B. STANDARD INTAKE\n• Bounded caller/callee tracing\n• Resolve open questions if ambiguous"]
    CheckTier -->|"Tier 4: UI / Docs"| AutonomousIntake["2C. AUTONOMOUS INTAKE\n• Local file reading only\n• Zero unnecessary interruptions"]
    
    HighCritIntake --> BlastRadius["3. ZERO-TURN BLAST RADIUS\nRun code-review-graph MCP\n(get_impact_radius_tool, get_affected_flows_tool)\nInject ~1KB Structural Context Slice"]
    StandardIntake --> BlastRadius
    AutonomousIntake --> DevDocsInit
    
    BlastRadius --> DevDocsInit["4. DEV-DOC INITIALIZATION\nCreate dev/active/<task>/\n• plan.md: RFC 2119 + WHEN/THEN Scenarios\n• tasks.md: TDD Tasks + Declarative Commit Contracts\n• context.md: Ephemeral session pause memory"]
    
    DevDocsInit --> TDDExecution["5. BEHAVIOR-BOUND TDD EXECUTION\n(Driven by implement-tasks + /goal)\n• Task N.1 (Red): Failing Invariant Tests + Compilable Stubs\n• Task N.2 (Green): Minimal Production Logic\n• Task N.3 (Refactor): Clean Architecture & DI Slicing"]
    
    TDDExecution --> LayerVerification["6. LAYER-BOUNDED VERIFICATION\n• Fast Loop: dotnet run -- --treenode-filter\n• Phase Exit: 1 Release Build + 1 Project Test\n• Tier 0–2: Capture QA Evidence to .omo/evidence/"]
    
    LayerVerification --> FailureOwnership{"7. FAILURE OWNERSHIP\nPhase-attributable?"}
    FailureOwnership -->|"Yes: fix before commit"| TDDExecution
    FailureOwnership -->|"No failure"| PhaseCommit["8. PHASE COMMIT\n• Stage owned paths\n• Commit via declarative tasks.md contract\n• No skill reload, no hash logging"]
    FailureOwnership -->|"Proven unrelated shared-tree failure"| ExternalRecord["Record exact external evidence\nLeave unrelated files untouched\nRequire phase-owned lane green"]
    ExternalRecord --> PhaseCommit
    PhaseCommit --> MorePhases{"More phases?"}
    MorePhases -->|"Yes"| TDDExecution
    MorePhases -->|"No"| ReviewTeaching["9. GRADUATION & TEACHING SUMMARY\n• Graduate task to dev/backlog/\n• Two-Axis Review & Dual-Doc Parity\n• Comprehensive Technical Teaching Summary"]
    
    ReviewTeaching --> Done(["Work Complete & Verified"])
```

### Criticality Tier Matrix

| Criticality Tier | Target Domain & Scope | Intake & Inquiry Protocol | Exploration Budget | Test & Verification Strategy | Multi-Agent Review Protocol |
|---|---|---|---|---|---|
| **Tier 0: Sovereign** | Money, payments, checkout, RSVPs, capacity holds, refund authority | Mandatory `/grill-me` on concurrency, hold expiration, rollback | Exhaustive Knowledge Graph (callers, callees, outbox, DB locks, ADRs) | Invariant-Breaker concurrency tests + real PostgreSQL evidence | Anonymized Epistemic MAD (Weighted Voting) |
| **Tier 1: Security** | Auth, Cerbos policies, tenant boundaries, migrations, tokens | Mandatory `/grill-me` on threat models, fail-closed auth, tenant spoofing | Exhaustive Graph + Policy filters + Global query filters | Invariant-Breakers + multi-provider DB and fail-closed authorization tests | Anonymized Epistemic MAD (Weighted Voting) |
| **Tier 2: Privacy** | PII fields, erasure, AI context gateway, export, audit redaction | Mandatory `/grill-me` on erasure authority, anti-resurrection, receipt tokens | Exhaustive Data Flow tracing (`*Pii`, log sinks, vector DBs) | Invariant-Breakers + log sink PII scans + purge verification | Anonymized Epistemic MAD (Weighted Voting) |
| **Tier 3: Domain State** | Aggregate root domain logic, MediatR command/query handlers | Standard Q&A (only if requirements are ambiguous) | Bounded caller/callee tracing of target aggregate/handler | Behavioral CQRS unit and integration tests | Peer Review (`backend-engineer-agent`) |
| **Tier 4: Standard UI / Docs** | Blazor client components, CSS isolation, Markdown docs, agent context | Autonomous defaults (zero unnecessary interruptions) | Local surface reading (target razor/css/doc file only) | Affordance & component render tests; Markdown schema checks | Lightweight Self-Check (`presentation-engineer-agent`) |

---

## 4. Multi-Harness Bootloaders And Actual Twin Scope

The portable authority is root `AGENTS.md` plus the intent/skill/rule system. Harness adapters are currently heterogeneous; only `.agents/rules` and `.omo/rules` are maintained as reciprocal twins.

```mermaid
flowchart TD
    subgraph Canonical["Portable Repository Contract"]
        AgentsMD["AGENTS.md\nCanonical authority"]
        IntentRouter[".agents/contract/intents.yaml\nIntent routing"]
        CanonicalAgentRule[".agents/rules/*.md\nContract-system rules"]
    end

    subgraph Adapters["Current Harness Adapters"]
        OmOHook["OmO\nNative .omo rules injector"]
        ClaudeAdapter["Claude Code\nCLAUDE.md pointer + graph-only settings hooks"]
        CursorAdapter["Cursor / Windsurf\n.cursorrules graph guidance only"]
        CopilotAdapter["GitHub Copilot\ncopilot-instructions.md pointer"]
        SessionAdapter["Gemini / other harnesses\nSession/root AGENTS injection when available"]
    end

    subgraph TwinPair["Only Reciprocal Twin Pair"]
        OmOTwinRule[".omo/rules/*.md\nOmO-native copies"]
    end

    AgentsMD --> IntentRouter --> CanonicalAgentRule
    OmOHook --> OmOTwinRule
    ClaudeAdapter --> AgentsMD
    CopilotAdapter --> AgentsMD
    SessionAdapter -.-> AgentsMD
    CanonicalAgentRule <-->|"Twin Sync Contract\n(Exact Copy, No Symlinks)"| OmOTwinRule
```

Current adapter facts:

- OmO auto-loads `.omo/rules`; the contract system routes `.agents/rules`.
- Root [`CLAUDE.md`](../../CLAUDE.md) and [Copilot instructions](../../.github/copilot-instructions.md) point to `AGENTS.md`; Claude settings currently register graph hooks rather than rule mirrors.
- `.cursorrules` currently contains graph guidance, not a mirrored rule tree.
- No additional Claude/Cursor/Copilot/Gemini twin directories are asserted as implemented.

Harness injection order does not change repository authority. Root [`AGENTS.md`](../../AGENTS.md) remains controlling: Critical Rules → `docs/QUICK_REFERENCE.md` → `docs/GOVERNANCE.md` → matching path-scoped rules. Adapter convergence remains proposal **#5** below.

### The Twin Rules Synchronization Contract

- **Zero Divergence**: Every rule in `.agents/rules/<name>.md` has an exact twin at `.omo/rules/<name>.md`.
- **Reciprocal Headers**: Line 2 of every rule documents its counterpart relative path.
- **No Symlinks**: Symlinks are strictly prohibited because harness file-scanners (such as OmO's `realpathSync`) collapse symlinks into single candidates, defeating distance-weighted path matching.
- **Twin Editing Rule**: When editing any rule file, agents MUST apply the identical change to its twin in the same commit.

---

## 5. The Dev-Doc Triad & Active Workstream State Machine

Substantial or multi-session development tasks are governed by the **Dev-Doc Triad** in `dev/active/<task>/`. This structure enforces strict separation of concerns between architecture, execution, and ephemeral session memory.

> [!NOTE]
> **Local Working Memory & Gitignore Isolation**: Active workstreams in `dev/active/*` are gitignored to eliminate commit churn, task-checkbox noise, and branch merge conflicts. Agents and developers access, create, and update these files directly using native harness file tools by deterministic path. Only durable architectural decisions graduate to `docs/internal/adr/`, durable findings to `dev/_journal/`, and completed workstreams to `dev/backlog/`.

```mermaid
stateDiagram-v2
    [*] --> Draft: Task Initiated

    state "dev/active/<task>/" as ActiveWorkstream {
        state "plan.md (Canonical Design)" as PlanDoc
        state "tasks.md (Hot Execution Ledger)" as TasksDoc
        state "context.md (Ephemeral Working Memory)" as ContextDoc
        
        PlanDoc: • Architectural Decisions & ADRs
        PlanDoc: • RFC 2119 Behavioral Contract
        PlanDoc: • WHEN / THEN Scenarios
        PlanDoc: • Phase Exit Criteria & Rollback
        PlanDoc: ❌ NO checkboxes, statuses, or handoffs
        
        TasksDoc: • Phase-by-Phase Task Breakdown
        TasksDoc: • Test-First Order (Red -> Green -> Refactor)
        TasksDoc: • Declarative Commit Contracts
        TasksDoc: • Hot execution status ([ ], [x])
        TasksDoc: ❌ NO commit hash recording
        
        ContextDoc: • Quick Resume State & Priority
        ContextDoc: • Active Blockers & Investigation Notes
        ContextDoc: • Ephemeral Session Handoffs (Optional)
        ContextDoc: ❌ NO commit hash recording
    }

    Draft --> InImplementation: User Approves Plan
    InImplementation --> ReBaselined: Scope/Architecture Shift
    ReBaselined --> InImplementation: Plan Updated & Re-Approved
    InImplementation --> PhaseVerified: Phase Tasks [x] & Verification Resolved
    PhaseVerified --> PhaseCommitted: Staged Owned Paths & Committed
    PhaseCommitted --> InImplementation: Next Approved Phase
    PhaseCommitted --> Graduated: All Phases Verified
    Graduated --> [*]: Moved to dev/backlog/ & Ready for PR
```

### Triad Single Responsibility Matrix

| Artifact | Canonical Responsibility | Strictly Forbidden Content | Update Frequency |
|---|---|---|---|
| `*-plan.md` | High-level architecture, design decisions, RFC 2119 contracts, `WHEN`/`THEN` scenarios, phase exit criteria, rollback handling. | Granular task checklists, `- [ ]` checkboxes, dynamic statuses (`IN PROGRESS`), ephemeral session progress. | Only when architectural direction or scope shifts. |
| `*-tasks.md` | Hot execution ledger, granular Red/Green/Refactor tasks, exact phase-owned paths, verification commands, declarative planned commit contracts, and task statuses (`[ ]`, `[x]`). | Long architectural narratives, trade-off debates, session handoff logs, commit hash recording. | During planning, after each subtask, and after each phase commit. |
| `*-context.md` | Ephemeral working memory for session pauses/handoffs, quick resume state, active blockers, loaded evidence ledger, validation baseline results. | Duplicate task checklists, full source code copies, redundant documentation paste, commit hash recording. | At start of session, on blockers, and before handoff/pause. |

<<<<<<< HEAD
### The Triad As The Sole Inter-Session Serialization Protocol

Because implementation workflows span distinct sessions (Planner $\rightarrow$ Adversarial Review $\rightarrow$ Implementer), **the Dev-Doc Triad (`plan.md`, `context.md`, `tasks.md`) serves as the stateless serialization wire between agents**.

No conversational memory, hidden assumptions, or chat dialogue survives session boundaries. If an architectural constraint, edge-case mitigation, or test requirement is not explicitly recorded in the triad, **it does not exist**.

### Zero-Loss Information Preservation Architecture

The repository strictly prohibits writing standalone `*-cto-review.md` or `*-research.md` sidecar files in `dev/active/<task>/`. In practice, separate review markdown files became **orphaned sidecars**—implementing agents rarely opened them, causing the review's stress-tests and mitigations to drift out of sync with code execution.

Instead, **100% of the CTO review's analytical depth and research findings are absorbed directly into canonical locations across the triad**:

| Review Dimension / Element | Destination in the Triad | How & Why It Is Preserved |
|---|---|---|
| **3D Evaluation Scorecard**<br>*(Completeness, Correctness, Coherence)* | `plan.md` §0 Planning Metadata & `context.md` Review State | Records the baseline audit scores, gate status, and alignment date permanently in metadata so future sessions inherit the verified review baseline. |
| **Source-Free Research & Seams Evidence** | `plan.md` §2 Source-Grounded Current State (§2.1–§2.9) | Verified codebase reality, AST types, callers/callees, extension seams, and AST evidence live directly in the plan's current state report. |
| **Socratic Stress-Testing Challenges**<br>*(Scenarios, Questions, Edge Cases)* | `plan.md` §3 Proposed Future State & §5 Architecture Decisions | Translates Socratic challenges into explicit RFC 2119 requirements (`WHEN`/`THEN` behavioral contracts) and concrete architectural invariants. |
| **"The Worst Break" Catastrophic Failure Mode** | `plan.md` §7.1 Testing Strategy / §9 Security & `tasks.md` Phase Red Tasks | Documents the catastrophic production failure mode in the testing strategy, and immediately translates it into failing Invariant-Breaker specification tests in Phase Red before implementation. |
| **Ranked Top Risks & Minimum Acceptable Fixes**<br>*(Blocker, Critical, Major)* | `plan.md` §13/§14.2 Risk Register & `context.md` Known Risks | Preserves ranked risks with concrete mitigations, severity tiers, and verification triggers in the active risk register. |
| **Breaking Deletions & Legacy Elimination** | `plan.md` §12 Migration & Compatibility Plan & `context.md` Key Decisions | Explicitly records obsolete code, endpoints, tables, and adapter shims marked for outright deletion under greenfield development principles. |
| **Test-First Sequences & Atomic Commits** | `tasks.md` Phase Checklist & Planned Commit Contracts | Turns architectural advice into an executable, verifiable sequence of Red -> Green -> Refactor tasks with path-limited Conventional Commits. |

### The Human Interaction Boundary & Zero Plan-Opening UX (The Developer Console Principle)

While the Dev-Doc Triad (`plan.md`, `tasks.md`, `context.md` in `dev/active/<task>/`) guarantees deterministic **inter-session machine working memory**, it must **never** become an obstacle to human-agent collaboration.

A recurring breakdown in AI-assisted development is the **"curse of knowledge"**: an agent holding the entire triad in context sends cryptic, shorthand prompts to the developer:

> ❌ *“Do you approve proceeding with P04/P06 while keeping both P03 gates explicitly open?”*

This breaks flow by forcing the developer to context-switch, open `dev/active/<task>/`, and grep through markdown checklists just to understand what "P04" or "P03 gates" mean.

#### The Golden Invariant
> **The developer should never need to open the implementation plan (`dev/active/<task>/...`) just to understand an agent's question, status update, or approval request.**

- The triad in `dev/active/<task>/` is **internal working memory and an execution ledger**.
- The chat conversation is the **developer console**.
- Forcing a developer to open internal plan files to decode an agent's prompt is a severe UX defect.

#### The Mandatory Decision Brief Protocol
Whenever an agent pauses for user approval, flags an architectural choice, reports a milestone, or requests direction on blockers, the response MUST be formatted as an inline, self-contained **Decision Brief**:

1. **Context & Current Position**: 1–2 sentences on what was just completed or where the workstream currently stands.
2. **Plain-English Substance**: Always use descriptive feature/component names alongside any phase numbers (e.g., *"Phase 4 (PostgreSQL Outbox Worker & Dead-Letter Dispatch)"* instead of bare *"P04"*).
3. **The Exact Decision & Why It Matters**: Explain in plain English what decision is needed, why it arose, and what the real-world trade-off is (e.g., why gates are open, what dependencies exist).
4. **Structured Actionable Options**: Provide explicit, numbered choices with a clear recommendation (e.g., `Option A (Recommended): ...`, `Option B: ...`) and the pros/cons of each, allowing the developer to guide in seconds.
5. **Immediate Next Action**: State what the agent will execute the moment the developer replies.

#### Self-Contained Executive Plan Brief
When presenting a newly authored or re-baselined implementation plan (Stage 2 exit), agents must not merely provide file paths and ask the user to read them. The agent must deliver an inline **Executive Plan Brief**:
- **Executive Summary & Architectural Approach**: Core design pattern and business outcome.
- **Phase Roadmap**: Human-readable list of phases with descriptive names, touched layers, and invariant verification targets.
- **Key Architectural Decisions & Trade-offs**: Bulleted log of primary choices made during intake.
- **Potential Risks & "Worst Break" Catastrophic Failure Mode**: Concrete failure mode tested before production code.
- **Immediate Next Step & Approval Request**: Direct call for approval to begin Phase 1.

### Single-Session Task-Branch & Phase-Close Protocol

Each active workstream runs in a single agentic session on a dedicated Git branch (`feat/<task-name>`). Commits occur immediately upon phase verification. Planning pre-authors declarative commit contracts; execution consumes them directly without loading `conventional-commit` or recording commit hashes in markdown (Git commit history is the single source of truth):

| Step | Required action | Observable evidence |
|---|---|---|
| 1. Reconcile & stage | Check `git status --short`. Stage exact phase-owned files (`git add -- <paths>`). | Only files modified by the current phase are staged. No blind `git add .` or `git add -A`. |
| 2. Verify once | Run the phase verification: one Release build and one selected project test. | Build and test output clean and passing. |
| 3. Classify failures | Fix phase-attributable failures immediately. External shared-tree failures are noted without touching foreign code. | Working tree remains bounded to phase-owned scope. |
| 4. Execute planned commit | Commit staged files directly using the declarative title and description from `tasks.md`. Do not load `conventional-commit`. | Clean native Git commit (`git commit -m "..." -m "..."`). The Git log is the sole source of truth; no hashes recorded in markdown. |
| 5. Progress or graduate | Proceed to the next phase, or upon completing all phases, graduate the workstream (`dev/active/<task>/` -> `dev/backlog/<task>/`). | Working tree clean (`git status --short`). Release artifacts and teaching summary prepared. |

A phase-attributable failure blocks its commit. A proven unrelated failure does not authorize the agent to repair, stage, discard, or claim ownership of another contributor's work.

Representative `tasks.md` declarative commit contract:

```markdown
#### Planned Commit Contract
- **Title:** `fix(registration): reject expired holds before confirmation`
- **Description:** Keep registration and capacity state unchanged when confirmation references an expired inventory hold.
- **Changelog:** Public fix
- **Files:** `src/Registration/HoldConfirmation.cs`, `tests/Registration/HoldConfirmationTests.cs`
```

---

## 6. Test-Driven Development & Behavior-Bound Seam Architecture

The repository enforces strict **Test-First Invariant Task Sequencing** to eliminate **Post-Hoc Test Tautology ("The Ugly Mirror")**—the failure mode where tests are written after code and merely mirror implementation bugs.

```mermaid
flowchart TD
    subgraph PlanContract["1. Behavioral Contract (in plan.md)"]
        SpecDoc["Requirement: System SHALL enforce capacity limits\n\n#### Scenario: Overbooking Hold Expired\n- GIVEN a reserved ticket hold expired 10s ago\n- WHEN user submits payment confirmation\n- THEN return ProblemDetails 409 Conflict with ExpiredHold code\n\n#### Scenario: The Worst Break (Double Capture Race)\n- GIVEN 2 concurrent requests for the final available seat\n- WHEN both process simultaneously\n- THEN exactly 1 succeeds and 1 fails closed with atomic rollback"]
    end

    subgraph SeamDefinitions["2. Pre-Agreed Public Seams"]
        AppSeam["Application Seam:\nIRequest<T> -> BaseCommandResponse<T> / ProblemDetails"]
        ApiSeam["API Seam:\nHTTP Route -> RFC 7807 Payload / HAL _links / Status"]
        PersistSeam["Persistence Seam:\nIQuerySpecification<T> -> Domain Aggregate Entity"]
    end

    subgraph RedGreenRefactor["3. Execution Sequence (in tasks.md)"]
        RedPhase["Task N.1 (Red Phase)\nAuthor failing Invariant Tests for Scenarios\n• Test against Public Seams only\n• Include compilable stubs (build succeeds, test fails)\n• Include 'Worst Break' Adversarial tests\n• Verify test fails with expected assertion error"]
        GreenPhase["Task N.2 (Green Phase)\nImplement Handlers, Aggregates & Domain Logic\n• Minimal production code to satisfy test\n• Verify test turns GREEN via --treenode-filter"]
        RefactorPhase["Task N.3 (Refactor & Registration)\nClean Architecture Slicing & DI\n• The Deletion Test (Deep Modules)\n• StarRedactor / HmacRedactor Zero-PII logging\n• Wire DI Service Registrations"]
        VerificationPhase["Phase Verification\n• One Release build\n• One selected project test"]
        CommitPhase["Phase Commit on Task Branch\n• Stage owned paths (git add -- <paths>)\n• Commit declarative tasks.md contract directly\n• No skill reload, no hash recording"]
    end

    PlanContract --> SeamDefinitions
    SeamDefinitions --> RedPhase
    RedPhase --> GreenPhase
    GreenPhase --> RefactorPhase
    RefactorPhase --> VerificationPhase
    VerificationPhase --> CommitPhase
```

### The 3-Ring Progressive Verification Model

To eliminate the severe 50% test diagnosis and 15% container troubleshooting bottleneck, the repository strictly stratifies test execution into three progressive rings:

```mermaid
flowchart TD
    subgraph Ring1["Ring 1: Inner Loop (Subtask Level)"]
        R1Scope["Scope: Target Test Class via --treenode-filter\nLatency Budget: < 2 seconds\nInfrastructure: 0 Docker containers, 0 network I/O\nSuites: Event.Domain.UnitTests, Event.Application.UnitTests"]
    end

    subgraph Ring2["Ring 2: Phase Exit Gate (Phase Level)"]
        R2Scope["Scope: Single Modified Project + Release Build\nLatency Budget: < 15 seconds\nInfrastructure: 1 Canonical Provider (e.g. SQLite / single Postgres)\nSuites: Target project only (dotnet build -c Release -v q)"]
    end

    subgraph Ring3["Ring 3: Plan Exit Gate (Workstream Level)"]
        R3Scope["Scope: Full Multi-Provider Matrix & System Rules\nLatency Budget: Workstream Exit Only\nInfrastructure: Full 5-Database Matrix + Testcontainers\nSuites: Event.Persistence.IntegrationTests, Event.Architecture.Tests, Migrations"]
    end

    Ring1 -->|Subtask Green| Ring1
    Ring1 -->|All Phase Subtasks Green| Ring2
    Ring2 -->|Phase Committed| NextPhase["Next Phase Subtasks"]
    NextPhase --> Ring1
    Ring2 -->|All Plan Phases Complete| Ring3
    Ring3 --> PRReady(["Ready for PR Submission"])
```

### The Yak-Shaving Quarantine Protocol

When an agent encounters a broken test outside the phase-owned path, it MUST follow the quarantine protocol rather than attempting to fix the failure:

```mermaid
flowchart TD
    TestFail["Test Failure Observed During Run"] --> CheckScope{"Is failing test in\nphase-owned scope?"}
    CheckScope -->|"Yes"| OwnedTest["Phase-Owned Defect:\nFix immediately in current task"]
    CheckScope -->|"No"| ReproCheck["Base Branch Reproduction Check:\nRun failing test on untouched develop worktree"]
    ReproCheck --> Reproduces{"Reproduces on\nclean base?"}
    Reproduces -->|"Yes (Pre-existing)"| QuarantineAction["Yak-Shaving Quarantine Rule:\n1. Strictly FORBIDDEN from fixing in this task\n2. Record in *-context.md under Pre-Existing Technical Debt\n3. File dev/backlog/<slug>.md\n4. Quarantine failure & proceed with phase deliverable"]
    Reproduces -->|"No (Regression)"| RegressionAction["True Blast Radius Defect:\nInvestigate task's unintended side effects via graph"]
```

### Core Testing Invariants

1. **Pre-Agreed Public Seams**: Tests verify behavior strictly through public interfaces (MediatR requests, HTTP routes, aggregate root methods), never by inspecting private internal state or mocking internal collaborators.
2. **Compilable Stubs in Red Phase**: In strongly-typed C#/.NET, invariant tests must compile to execute. Agents must author minimal compilable stub types/method signatures (returning default or throwing `NotImplementedException`) alongside the test so that the solution compiles and the test suite executes to produce a genuine behavioral RED failure, never a compile break.
3. **No Tautological Assertions**: Expected values must originate from an independent known-good literal or specification. Assertions that recompute expected values using the same formula as production code (`Assert.Equal(items.Sum(x => x.Price), result.Total)`) are strictly forbidden.
4. **No Interface Bypassing**: Tests must verify state transitions through the public interface. A test must not bypass the domain aggregate to assert directly against raw database tables.
5. **Mock Boundary Rule**: Mock **ONLY** external third-party infrastructure (payment gateways, external email delivery, system clock, random generators). **NEVER mock internal domain entities, aggregate roots, repositories, or MediatR handlers.** Use real domain entities and in-memory or Testcontainers-backed databases.
6. **The 3-Ring Progressive Verification Hierarchy**: Subtasks run fast in-memory sliced tests (< 2s). Phase exits run single-project Release builds + single canonical provider tests (< 15s). Multi-database provider matrices, migration checks, and architecture rules run strictly at plan exit.
7. **The Yak-Shaving Quarantine Rule**: Unrelated pre-existing test suite rot or container flakiness is quarantined and logged under `*-context.md` / `dev/backlog/`, never repaired during unrelated feature work.
8. **Pure Domain Invariants over Persistence Queries**: 90%+ of algorithmic, normalization, validation, and state-machine checks live in `Event.Domain.UnitTests` without Docker dependencies.

---

## 7. Disciplined Bug Diagnosis Protocol

Bug diagnosis follows a disciplined 6-phase falsifiable loop from `debug-issue`. Speculative debugging and premature code editing are strictly forbidden.

```mermaid
flowchart TD
    BugReport(["Bug / Regression Reported"]) --> Phase1["Phase 1: Build Deterministic Red Feedback Loop\n(Spend 90% of effort here)\n• Author fast (<2s) TUnit test with --treenode-filter\n• OR minimal HTTP curl / integration harness\n• MUST assert user's exact symptom\n⚠️ NO CODE EDITING OR HYPOTHESIZING BEFORE THIS EXISTS"]
    
    Phase1 --> Phase2["Phase 2: Reproduce & Minimise\n• Verify failure matches user symptom\n• Strip inputs/config one by one\n• Retain ONLY load-bearing parameters"]
    
    Phase2 --> Phase3["Phase 3: 3–5 Ranked Falsifiable Hypotheses\nFormat: 'If <X> is root cause, then <changing Y>\nwill make bug disappear / <changing Z> will make it worse'"]
    
    Phase3 --> Phase4["Phase 4: Targeted Probing & Tagged Debug Logs\n• Use knowledge graph (callers, callees, flows)\n• Tag probes: [DEBUG-<id>] (e.g. [DEBUG-4a1f])\n• Ensures grep '[DEBUG-' leaves zero log pollution"]
    
    Phase4 --> Phase5["Phase 5: Fix & Regression Seam Verification\n• If no clean seam exists -> record Seam Deficiency Defect\n• Apply minimal fix to satisfy red test\n• Verify test turns GREEN\n• Re-run un-minimised Phase 1 loop"]
    
    Phase5 --> Phase6["Phase 6: Cleanup & Merge Gate\n• Remove all [DEBUG-...] probes\n• Document confirmed root cause in commit/summary\n• Run fast-loop test verification"]
    
    Phase6 --> Resolved(["Bug Defect Resolved & Verified"])
```

---

## 8. Two-Axis Review & Quality Gate Pipeline

Code review in ISLAMU Event evaluates pull requests along **two independent, non-polluting axes**, ensuring that clean code formatting does not mask missed functional requirements.

```mermaid
flowchart TD
    PRDiff["Pull Request Diff & Changes"] --> SplitReview{"Two-Axis Review Evaluation"}
    
    subgraph Axis1["Axis 1: Standards & Clean Architecture"]
        ArchCheck["Clean Architecture Boundaries\n(No DTOs in Repos, No DI in Validators)"]
        FowlerSmells["Fowler 12-Smell Baseline Check\n• Primitive Obsession\n• Feature Envy\n• Shotgun Surgery\n• Speculative Generality\n• Data Clumps / Message Chains"]
        SecCheck["Security & Tenancy Invariants\n(Fail-Closed Auth, Zero Hardcoded Secrets)"]
    end
    
    subgraph Axis2["Axis 2: Spec & Intent Fidelity"]
        ScenarioCheck["Scenario & Requirement Verification\n(Did we build what was asked?)"]
        ScopeCheck["Scope Creep & Speculative Hooks Check\n(Did we add unasked-for code?)"]
        WorstBreakCheck["Worst Break Invariant Verification\n(Are catastrophic failure paths tested?)"]
    end
    
    SplitReview --> Axis1
    SplitReview --> Axis2
    
    Axis1 --> AggregateReport["Aggregated Two-Axis Report"]
    Axis2 --> AggregateReport
    
    AggregateReport --> RightSizing{"4-Point Right-Sizing Check\n• Multi-intent 'and also' scope?\n• > 8–10 major tasks?\n• Big-bang layer mixing?\n• Independent backend shipping value?"}
    
    RightSizing -->|2+ Matches| SplitPR["Verdict: Split Before Approval\n(Break into reviewable vertical PRs)"]
    RightSizing -->|< 2 Matches| FinalVerdict["Final CTO / Maintainer Verdict\n(Approve / Approve with Required Changes)"]
```

### The Fowler 12-Smell Baseline Checklist

| # | Code Smell | Diagnostic Tell in Diff | Required Refactoring Action |
|---|---|---|---|
| 1 | **Mysterious Name** | Vague identifiers (`data`, `temp`, `res`, `process()`) that obscure intent. | Rename using canonical domain glossary terms. |
| 2 | **Duplicated Code** | Similar logic shapes recurring across multiple handlers/controllers. | Extract to shared domain aggregate or application service. |
| 3 | **Feature Envy** | Method repeatedly reaching into another object's fields to perform calculations. | Move the method onto the object that owns the data. |
| 4 | **Primitive Obsession** | Raw `string`, `int`, or `Guid` representing domain concepts (e.g. email, money). | Encapsulate into a strongly-typed Value Object or Enum. |
| 5 | **Data Clumps** | The same 3+ parameters traveling together across multiple signatures. | Bundle parameters into a cohesive Record contract. |
| 6 | **Shotgun Surgery** | A single logical change forcing small edits across dozens of scattered files. | Consolidate related responsibilities into a single deep module. |
| 7 | **Divergent Change** | One class or file edited for multiple completely unrelated reasons. | Split class by single responsibility (CQRS command vs query). |
| 8 | **Speculative Generality** | Unused hooks, generic type parameters, or abstractions added "for the future". | Delete unused abstractions; keep implementation minimal (YAGNI). |
| 9 | **Message Chains** | Long property traversals (`a.B.C.D.GetState()`) violating the Law of Demeter. | Hide the walk behind a single method on the root object. |
| 10 | **Middle Man** | A class or service that merely delegates calls without enforcing invariants. | Cut the middle man; apply **The Deletion Test**. |
| 11 | **Repeated Switches** | Identical `switch`/`if` cascades on the same enum recurring across the codebase. | Replace with polymorphism or strategy dictionary. |
| 12 | **Refused Bequest** | A subclass or implementer that overrides and throws on inherited methods. | Replace inheritance with composition. |

---

## 9. Contributor & Agent Quick Command Reference

### Fast-Loop Development Commands

```bash
# 1. Fast TUnit Slicing during active subtask coding (~1.5s):
dotnet run --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --no-build -- --treenode-filter "/*/*/*<TargetTestClassName>/*"

# 2. Phase-End Verification (Run ONCE after all phase tasks are complete):
dotnet build --configuration Release --verbosity quiet
dotnet test --project tests/<TargetProject>.Tests/<TargetProject>.Tests.csproj --configuration Release --verbosity quiet

# 3. Architecture & Convention Integrity Check:
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet

# 4. Immediate phase close on dedicated task branch (feat/<task-name>):
git status --short
# Stage exact files owned by this phase
git add -- <phase-owned-path-1> <phase-owned-path-2>
# Commit directly using the declarative tasks.md contract:
git commit -m "<type>(<scope>): <benefit-led phase outcome>" \
  -m "<phase-owned motivation, data flow, and required trailers>"
git status --short
# Git log is the single source of truth; no commit hashes recorded in markdown.

# 5. Markdown & Diff Integrity Check (Tier 4 / Documentation tasks):
git diff --check -- .agents/ docs/ dev/
```

### Critical Negative Constraints (What is FORBIDDEN)

> [!CAUTION]
> **Enforced Repository Invariants:**
> - ❌ **NO Ad-hoc Python/Node.js Scripts**: Agents must never generate or run `python`, `python3`, `node`, `npm` scratch scripts. Use native agent tools and POSIX Bash. Persistent dev tools belong in `eng/scripts/` or `eng/tools/` as C# scripts (`dotnet run eng/.../*.cs`).
> - ❌ **NO Hard-Coded Secrets**: Never put passwords, connection strings, or tokens in source code, `AppHost.cs`, or test fixtures. Secrets originate strictly from **Infisical** or **`.env`**.
> - ❌ **NO Repositories Returning DTOs**: Repositories return Domain Entities only. DTO mapping belongs strictly in MediatR handlers.
> - ❌ **NO DI for Validators**: FluentValidation validators must be manually instantiated in handlers.
> - ❌ **NO Hand-Editing EF Migrations**: Migrations are generated artifacts (`dotnet ef migrations add`). Never manually edit migration files or model snapshots.
> - ❌ **NO UI Authorization Inspection**: Blazor client affordances must be gated strictly by inspecting HAL `_links` presence, never by local role/claim checking.
> - ❌ **NO Cryptic Shorthand or Plan-Opening Overhead**: Agents must never prompt the developer with bare phase/task IDs or gates (e.g., *"Proceed with P04/P06 while keeping P03 gates open?"*). All prompts and reports must be self-contained Decision Briefs so the developer never has to open `dev/active/<task>/...` to understand a question or decision.
> - ❌ **NO Yak-Shaving Unrelated Test Rot**: Never derail feature tasks to fix pre-existing failures in unrelated test suites. Isolate on clean base, log under `*-context.md` / `dev/backlog/`, and quarantine.

---

## 10. Lightweight Workflow Guard

The repository intentionally does not implement an “Agent OS.” There are no
workstream manifests, concatenated plan/task/context digests, approval receipt
chains, file claims, heartbeats, lock daemons, persistent goal state machines,
custom context packet compilers, or harness-wide orchestration authority. Git
commits provide provenance; branches and worktrees provide concurrency.

`eng/agent-workflow` is a small, read-only guard with two commands:

```bash
dotnet run --project eng/agent-workflow/src/ISLAMU.AgentWorkflow/ISLAMU.AgentWorkflow.csproj -- validate-intents .agents/contract/intents.yaml
dotnet run --project eng/agent-workflow/src/ISLAMU.AgentWorkflow/ISLAMU.AgentWorkflow.csproj -- validate-commit -- git commit --only -m "message" -- src/ExactFile.cs docs/ExactFile.md
```

The first command checks that the canonical intents catalog is one bounded,
valid UTF-8 YAML document. The second checks only that a described `git commit`
uses distinct literal file pathspecs after `--`; it rejects `.`, directories,
globs, traversal, rooted paths, Git pathspec magic, controls, and duplicates.
The guard never executes Git or mutates repository state.

## 11. Related Documentation & Canonical Anchors

- [`AGENTS.md`](../../AGENTS.md) — Canonical agent contract and entrypoint.
- [`.agents/CONTEXT_ENGINEERING.md`](../../.agents/CONTEXT_ENGINEERING.md) — Context budget policy and retrieval limits.
- [`.agents/contract/intents.yaml`](../../.agents/contract/intents.yaml) — Machine-readable task and intent registry.
- [`docs/QUICK_REFERENCE.md`](QUICK_REFERENCE.md) — Global invariant quick reference.
- [`docs/GOVERNANCE.md`](GOVERNANCE.md) — Architectural governance, coding patterns, and conventions.
- [`docs/DOCUMENTATION_ARCHITECTURE.md`](DOCUMENTATION_ARCHITECTURE.md) — Documentation structure, layout, and maintenance rules.
