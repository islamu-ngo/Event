---
description: "How to contribute to ISLAMU Event through issues, documentation, translations, code, and AI-assisted workflows."
---

# Contributing to ISLAMU Event

Welcome! We are building a free, ethical, and community-sovereign event management platform. Whether you are reporting bugs, improving documentation, translating into your language, or contributing code, your help is deeply valued.

ISLAMU Event is maintained by **Amir Akrari** with **ISLAMU (ASBL en formation)** established as its operational and legal steward. Because maintainer review capacity is constrained, **alignment matters far more than volume**.

---

## Core Contribution Principles

### 1. Alignment Over Volume
Not every pull request will be accepted—even if technically functional—if it increases maintenance overhead or diverges from our Clean Architecture roadmap. Review bandwidth is focused on deliberate, high-quality, and architecturally aligned improvements.

### 2. Atomic Changes ("One PR = One Change")
Keep pull requests tightly scoped:
* Focus exclusively on the single bug, feature, or document at hand.
* **Do not** reformat unrelated files, clean up unrelated code, or bundle multiple concerns into one PR.
* Independent refactors must be proposed and submitted separately.

### 3. Discussion Required for Larger Changes
Before writing code for non-trivial features, migrations, schema updates, or Blazor UI overhauls, you **must** open a discussion first:
* [GitHub Discussions](https://github.com/islamu-ngo/Event/discussions)
* [Discord Community](https://discord.gg/wrkY824Yv5)

Pull requests introducing major architectural shifts without prior alignment may be closed without review.

---

## AI Contribution Policy

AI-assisted contributions (using Cursor, GitHub Copilot, Claude Code, Gemini, ChatGPT, etc.) are **permitted and welcomed**, provided they adhere to strict engineering standards:

* **Mandatory Disclosure:** You must explicitly disclose all AI tools used in your pull request description.
* **Human Ownership & Comprehension:** You are personally responsible for every line of code submitted. You must understand the underlying logic, edge cases, and architectural compliance.
* **Human-Authored Summary:** The PR description and rationale must be written in your own words. Blindly copying raw LLM output is rejected.
* **Zero Tolerance for Unchecked PR Spam:** Uncoordinated, automated PRs that lack contextual understanding or fail to demonstrate comprehension will be closed.

{% hint style="info" %}
**Agentic Engineering Guide:**
Interested in how we leverage autonomous AI agents with deterministic rigor? Read our dedicated **[Agentic Engineering & AI Workflow Guide](agentic-engineering.md)** to learn about our 5-stage lifecycle and worktree-isolated execution model.
{% endhint %}

---

## In this Section

* **[Agentic Engineering & AI Workflow](agentic-engineering.md)** — Understand our 5-stage AI lifecycle, worktree isolation, and agent governance.
* **[Local Development Guide](local-development.md)** — Set up your developer workstation with .NET 10, Docker, and .NET Aspire.
* **[Clean Architecture Conventions](clean-architecture.md)** — Understand domain invariants, explicit application operations, and HAL link assembly.
* **[TUnit Testing Conventions](tunit.md)** — Run fast, targeted unit and integration test slices with TUnit.
* **[Clean-Room IP & Licensing](clean-room-ip-and-licensing.md)** — AGPLv3 guidelines, CLA requirements, and clean-room provenance rules.
* **[Sponsorship & Sustainability](sponsorship.md)** — Support the project through individual donations or corporate sponsorship.

---

## API Benchmark Measurements

The native PostgreSQL benchmark redesign retains six API read operations, including authenticated my-events and event-creation context. It uses canonical database bootstrap, separate database purpose accounts, genuine Local login with mandatory initial password replacement, and 24 published InformationOnly events created through the API. Startup, dataset creation and session refresh occur outside the measured request. ASP.NET Core TestServer, disabled output caching and disabled background workers define the comparison profile.

The measured operation includes the GET, response materialization and a small bounded JSON body check. Empty or malformed HTTP 200 responses, wrong root shapes and missing route fields fail; full dataset and authorization semantics are checked during setup. Compare only runs with this same measured boundary. The existing native ReleaseEngineering operation requires a final `smoke` or `baseline` mode after its output/candidate/run context. The scheduled/manual performance lane explicitly uses `smoke` for one native six-case Dry/in-process run and uploads only its successful, nonempty `benchmark-summary.json`. That parent owns temporary storage, child-only credentials, completed-result checks, credential-canary scans and creator-owned cleanup. The workflow does not implement separate shell custody, and private logs, witnesses and full benchmark reports are not uploaded. Source integration still requires command/control verification and an actual successful native run; it establishes no latency baseline.

Native invocation requires the existing supervised parent-run ownership context before setup: paired `ISLAMU_TEST_RUN_ID`/`ISLAMU_TEST_ROOT`, private child temporary storage, the existing private ownership marker and an explicit Unix `DOCKER_HOST`. Preserve the selected parent endpoint when narrowing TMPDIR. Setup rejects missing ownership, selects cached `postgres:18-alpine` without pulling, and flushes private creation intent before constructing the named container. It keeps those writable files open for parent inspection through terminal resource cleanup. The recorded container ID is null until actual startup returns a validated ID; failed disposal retains the dependent context and file handles. These records stay outside request timing and public artifacts. The parent must independently establish actual child, daemon, image and resource ownership before cleanup; an intent record or claimed ID alone is insufficient. Compilation alone does not prove native execution or cleanup.

Functional calibration selects all six cases with `--filter '*ApiEndpointPostgreSqlBenchmarks*' --job Dry --inProcess --stopOnFirstError`. Method-only CLI listings do not establish a single Scenario selection. The existing four-minute setup cancellation bound remains; the supervised calibration has a 30-minute outer limit followed by SIGINT and a 60-second grace period before enforced termination and owned cleanup. Stop at the first failure, retain partial results honestly, and check actual artifacts for credential canaries. This does not authorize unattended retries or relaxing setup deadlines.

Execution admission is pending. The unsupported InMemory API factory will be retired only after all six native PostgreSQL cases succeed; the three database-free EF query-construction benchmarks remain. A Dry command or zero process exit code is insufficient: reviewers require completed case results, dataset correctness, secret-safe artifacts and exact owned-container cleanup. Credentials must come from the controlled benchmark child environment and must never appear in arguments, BenchmarkDotNet job metadata or reports. Each `baseline` invocation performs one run covering both retained types: six native API cases plus three database-free query-construction cases. It uses the existing default .NET 10 adaptive job targeting 1% relative error, without Dry, in-process or count overrides. The supervising coordinator invokes it three times sequentially with separate fresh private ownership/output roots. Each invocation requires nine successful cases, canary and creator-cleanup admission; only three comparable quiet repetitions establish the requested statistical baseline. No repetition/count option is added. Out-of-process worker identity and credential inheritance require actual parent verification. Each smoke or baseline call has an explicit 30-minute cap and 60-second SIGINT grace; this bounded choice does not predict statistical cost. Three calls do not extend that limit, and incomplete results do not authorize automatic retries, extensions or Dry substitution. Missing required physical pressure metrics or quiet-host evidence prevents capacity inference; this harness establishes no production latency or capacity guarantee.

Load assurance keeps bounded private pressure snapshots while sampling and a final snapshot after the sampler joins. The actual scrape URI, selected metric types/units and fixed CPU-mode numeric rows remain untrusted leads; arbitrary labels, headers and bodies are excluded. An Aspire proxy endpoint is not proof of the API process. The native parent must independently associate the real operations listener with the admitted API process and corroborate the exporter/reset interval while it is live. Raw snapshots are not public artifacts, and source or host-free control success does not establish measured capacity.

The event load programme targets the selected API's direct loopback HTTP binding; missing or ambiguous bindings stop the run. The lookup foundation retains its controlled endpoint. Before accepting measurements, the supervising parent must independently prove that workload traffic and the private operations listener belong to the same API process and lifetime. A local URL or proxy endpoint alone is insufficient.

## Dependency Graph Checks

The repository-owned Dependency Submission workflow is intended to replace
GitHub's managed automatic submission. Maintainers activate it only after its
first snapshot is accepted, then turn off managed automatic submission in Code
Security settings to avoid duplicate lanes. Dependabot alerts and security
updates stay enabled. Forks do not submit to the upstream repository.

Contributors should commit regenerated NuGet lockfiles with dependency changes
and use the pinned SDK. Workflow Security checks the snapshot and release-impact
rules on pull requests without submission privileges. Transient submission errors
have bounded retries; authorization or invalid-input failures need a maintainer
fix, not repeated reruns. Logs provide status and retry evidence without exposing
tokens or snapshot contents. This graph covers tracked NuGet resolutions, not a
complete container-image or distribution SBOM.

## Contributor License Agreement (CLA)

Every non-bot contributor must sign the **ISLAMU Event Contributor License Agreement (CLA) v1.0**. When you open a pull request, the CLA Assistant bot will guide you to sign directly in a comment:

```text
I have read and agree to the ISLAMU Event Contributor License Agreement v1.0, and I confirm that I have the right to submit my contribution under it.
```

* **Anti-SaaS Protection:** The CLA protects the community: the Project Steward is legally bound never to grant closed-source commercial SaaS licenses. Any entity offering ISLAMU Event as a hosted service must remain open source under `AGPL-3.0-or-later`. Your contributions will never be locked behind a proprietary vendor wall. See [Clean-Room IP & Licensing](clean-room-ip-and-licensing.md).
