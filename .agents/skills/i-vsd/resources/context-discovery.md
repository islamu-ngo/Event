<!-- ABOUTME: Concise context discovery gate for I-VSD reviews across workspace, artifact, and project-context sources. -->
<!-- ABOUTME: Separates short user-facing intake from deeper evidence search before report generation. -->

# Context Discovery

Use this resource before any covered artifact-based or project-context-based I-VSD review. First apply [scope-boundaries.md](scope-boundaries.md); do not run context discovery for totally unrelated, adjacent-but-out-of-framework, or religious-legal authority requests. For covered requests, moral, ethical, and values analysis must not be based only on code, a diff, or a single prompt when broader evidence may exist in workspace docs, source packages, project plans, policies, operational records, issue trackers, knowledge bases, app-store material, customer/support records, or user-provided paths.

## Discovery Rule

Start with a concise, action-oriented context gate before writing findings or generating report files. Prefer product, provider, and project evidence over assumptions. If useful evidence exists but was not reviewed, mark it as `Missing Evidence` or `Not Reviewed`; do not invent provider intent.

In standalone mode, the first user-facing response after action selection must summarize only relevant context sources and ask for confirmation before the deep dive unless the user explicitly requested only the action menu or a short advisory that does not require artifact/project research. It creates no report. For `guided-discovery`, lack of existing context is acceptable; the next step is an interview, not a missing-evidence blocker.

In planning mode, the explicit implementation-plan request satisfies this agreement step, but not the evidence or alignment gates. Consume the planner's shared evidence packet and stop for material missing context when it could change provider responsibility, stakeholder impact, recommendations, or task structure.

## Context Inventory Gate

Before deep analysis, quickly inventory context channels to choose evidence sources. Do not dump every visible file, tool, skill, command, MCP, or integration into the user-facing response.

1. Workspace, repository, source package, or filesystem context: summarize as docs, text artifacts, policies, plans, code/config/tests, reports, and generated outputs.
2. Project-context MCPs or hosted integrations: issue trackers, roadmaps, docs/internal/knowledge bases, product docs, support, incidents, observability, analytics, or customer feedback systems.
3. Project-context CLIs: authenticated tools that expose tickets, docs, source-control discussions/PRs, cloud/runtime posture, incidents, support, or analytics.
4. Retrieval skills or workflow helpers: only those that retrieve project context, review project commitments, or guide evidence collection.
5. User-provided paths, exported documents, screenshots, URLs, or access instructions.

Only list context channels that are visible or reasonably checkable in the current environment. Do not pretend an MCP server, CLI, skill, or external platform is available when it is not visible. Do not request credentials directly; ask the user to connect the relevant MCP/CLI or provide exported context/path access.

## Relevance Filter

- Say `repository/workspace docs` instead of listing every documentation file.
- Say `code/config/tests` instead of listing every project, test assembly, or configuration file.
- Name a specific file only when it is central to the requested action, such as `SECURITY.md`, `CODE_OF_CONDUCT.md`, a privacy/terms file, or a user-provided path.
- Mention skills only when they retrieve project context, guide evidence collection, or produce project review/report artifacts. Do not list host-specific implementation skills unless the requested I-VSD action directly depends on them.
- Mention MCPs, hosted integrations, or CLIs only when they can reveal project context, such as Plane, Jira, Linear, Confluence, Notion, GitHub/GitLab issues, discussions, PRs, Slack, support, incident, analytics, observability, or product-doc systems.
- Do not list browser tools, filesystem tools, package-doc search, social/web exploration tools, or code-editing tools unless the user asked for them or the action specifically needs them.
- Limit `Available context` to two to four bullets.
- Limit `Missing or useful context` to three to six bullets.
- Ask for extra context only when the missing evidence is material to the requested I-VSD action.

## First Response Contract

After the inventory gate and before the extensive deep dive, respond to the user with one of these shapes:

```text
I found limited context for an I-VSD <action>.

Available context:
- Repository/workspace docs and code/config/tests are available.
- <Only relevant project-context MCPs/CLIs/paths, or "No relevant external project-context integration is visible.">

Missing or useful context:
- <Only action-relevant gaps, e.g. terms/privacy/retention, moderation policy, pricing/refunds, support/incident logs, stakeholder review, issue tracker/roadmap, scholarly review.>

Planned output:
- `islamic-value-sensitive-design/<tier>/i-vsd-<action>.md`

Tell me "agreed" and I will check whether the mapped report already exists, then update it or create it with `Missing Evidence` boundaries. Or point me to additional paths/exports/integrations first.
```

For `guided-discovery`, adapt the closing sentence:

```text
Tell me "agreed" and I will start the discovery interview, then check whether the mapped report already exists and update or create it from your answers.
```

```text
I found enough initial context to begin an I-VSD <action>.

Available context:
- Repository/workspace docs plus code/config/tests.
- <Only relevant project-context MCPs/CLIs/paths, or omit this line if none are relevant.>

Planned outputs:
- `islamic-value-sensitive-design/<tier>/i-vsd-<action>.md`
- <additional mapped files, including `i-vsd-review-index.md` for multi-report work>

Tell me "agreed" and I will check whether these mapped reports already exist, then run the deep review and update or create the Markdown files. Or point me to additional context first.
```

If the user confirms, run the standalone [Grill-Me alignment gate](action-routing.md#standalone-alignment-gate), then check for the canonical report identity, perform the deeper artifact/context search, and update or create the report. If the user adds paths or connects tools, include them in the evidence plan before writing. If the user declines or changes scope, reroute through [action-routing.md](action-routing.md).

## Sources To Search

Look beyond source code. Search and read relevant files such as:

- `README*`, `CONTRIBUTING*`, `CHANGELOG*`, `SECURITY*`, `CODE_OF_CONDUCT*`, governance files, roadmap files, and release notes.
- Markdown, plain text, reStructuredText, AsciiDoc, MDX, and other human-readable documentation.
- Product briefs, PRDs, epics, user stories, task files, architecture decision records, design notes, strategy docs, research notes, meeting notes, and implementation plans.
- Terms, privacy notices, acceptable-use policies, moderation policies, data-retention notes, support policies, incident reports, postmortems, risk registers, and compliance documents.
- Configuration and infrastructure files that reveal provider responsibilities, such as authentication, authorization, telemetry, logging, backups, data storage, retention, deployment, billing, AI, moderation, or third-party integrations.
- Tests and examples that document promised behavior, user flows, policy enforcement, safety expectations, or edge cases.

Also check external or non-workspace context sources when visible and relevant:

- MCP servers or hosted integrations that expose tickets, epics, product documentation, architecture notes, decisions, customer support, incidents, analytics, legal/policy docs, or knowledge-base articles.
- CLI tools for project management, documentation platforms, cloud resources, source control hosts, support desks, observability stacks, incident systems, or deployment environments.
- Available skills, prompts, commands, or local workflow helpers that retrieve project context or guide evidence collection.
- User-specified directories outside the current workspace, exported documentation bundles, PDFs, screenshots, URLs, meeting notes, app-store listings, customer/support exports, or research archives.

## Search Procedure

1. Identify the requested I-VSD action and likely domains from [action-routing.md](action-routing.md).
2. Inventory local docs/internal/text artifacts, relevant context-retrieval skills, visible project-context MCPs/integrations, user-provided paths, and relevant authenticated CLIs before reading code-heavy areas.
3. Send the concise first response contract to the user, listing only relevant available context, material missing context, planned output files, and whether project-context MCP/CLI/filesystem context would improve the review.
4. After user confirmation, run the standalone alignment gate and check whether the canonical subject report already exists so the work can update it instead of writing from scratch. For `guided-discovery`, run its interview with one material question per response before writing the report.
5. Search for value-bearing terms such as privacy, consent, retention, telemetry, tracking, ads, pricing, billing, refund, cancellation, moderation, appeal, abuse, safety, accessibility, security, admin, partner, sponsor, AI, model, ranking, recommendation, policy, terms, rights, user data, deletion, export, lock-in, incident, and support.
6. Search for project-specific vocabulary from the user prompt, product name, feature name, domain model, connected project tools, or action under review.
7. Read the most relevant documents and external context records; cite them in `Evidence Reviewed` with paths, tool/source names, record identifiers, URLs, or clear descriptions.
8. Only then inspect code, diffs, configuration, tests, runtime behavior, or external operational records needed to validate whether implementation matches documented commitments.

## Evidence Priority

Use this order when evidence conflicts:

1. Current implementation and tests for what the system actually does.
2. Current product, policy, legal, operational, and architecture documentation for what the provider commits to do.
3. Connected project systems such as issue trackers, documentation platforms, roadmaps, support desks, incident tools, and knowledge bases for current organizational commitments or operating reality.
4. Planning documents, roadmaps, issues, and design notes for intended direction.
5. User-provided context for unstored business or stakeholder facts.
6. External standards or official documentation only when project context cannot answer the question.

Conflicts are findings. For example, if documentation promises deletion, portability, consent, refunds, appeals, or safety review but implementation evidence does not support that promise, report the mismatch under Truthfulness, Trust, Rights of People, Promise-Keeping, or Non-Harm as applicable.

## Output Requirements

Every artifact-based report must include:

- `Evidence Reviewed` near the bottom listing the code, docs, text artifacts, policies, configs, tests, project-context integrations, or user-provided context used.
- `Missing Evidence` near the bottom listing relevant docs or artifacts that were unavailable, skipped, stale, contradictory, or outside scope.
- `Context Inventory` near the bottom summarizing workspace/repository/source-package context and relevant project-context integrations, CLIs, retrieval helpers, and user-provided paths considered before deep review.
- Findings that distinguish documented commitments from implemented behavior.
- Claim boundaries that state the review is I-VSD design reasoning and traceability, not certification or proof.
- `Last Updated: xxxx-xx-xx` directly under the title and above `Scope`, using the current date whenever the report is created or updated.

## Safety Boundaries

Do not expose secrets, private account/workspace/tenant data, PII, credentials, or proprietary details unnecessarily in generated reports. Summarize sensitive evidence by category when exact text is not needed.
