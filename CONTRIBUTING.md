# Contributing to ISLAMU Event

We’re glad you are interested in contributing to ISLAMU Event!

There are many ways to help:
- Answer questions and share ideas in [GitHub Discussions](https://github.com/islamu-ngo/Event/discussions) or [Discord](https://discord.gg/wrkY824Yv5)
- Report reproducible bugs with clear diagnostic context
- Submit pull requests to fix issues, add features, or improve modules
- Help improve and translate documentation, or contribute translations via Weblate
- Refine UI accessibility (WCAG 2.2 AA) and localization
- Advocate for ethical, community-owned event infrastructure

> [!NOTE]
> **Not a coder or short on time? You can still support the project:**
> - ⭐ **Star the project on GitHub** to help increase its reach.
> - 📢 **Spread the word**: Recommend ISLAMU Event to non-profits, student unions, NGOs, and community organizers looking for an ethical, self-hostable ticketing and event platform.
> - 🌍 **Contribute translations**: Help translate UI strings into your language via Weblate.
> - 💬 **Share feedback**: Join our [Discord](https://discord.gg/wrkY824Yv5) and tell us about your deployment experience or feature needs.

ISLAMU Event is a self-hostable, purpose-agnostic, white-label event discovery and management platform. It is currently built and maintained by **Amir Akrari** on personal free time and funds, with **ISLAMU (ASBL en formation)** (a Belgian non-profit association in formation) being established as the operational and legal steward. Contributions are welcome — but **alignment matters more than quantity**.

This guide explains **what kind of contributions are likely to be accepted** and how to submit them properly. Following it saves time for both you and the maintainers.

> [!IMPORTANT]
> These guidelines may feel stricter than in many open-source projects. That is intentional.
> Clear structure, architectural boundaries, and legal hygiene prevent maintainer burnout and keep the project sustainable long-term.

---

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Security Vulnerabilities & Disclosure](#security-vulnerabilities--disclosure)
- [High-Level Expectations](#high-level-expectations)
- [State of the Project](#state-of-the-project)
- [What Makes a Strong Contribution](#what-makes-a-strong-contribution)
  - [Code Quality and Architecture Compliance](#code-quality-and-architecture-compliance)
  - [Atomic Changes](#atomic-changes)
- [Discussion Is Required for Larger Changes](#discussion-is-required-for-larger-changes)
  - [How to Propose an Enhancement](#how-to-propose-an-enhancement)
- [Maintainer Bandwidth & Expectations](#maintainer-bandwidth--expectations)
- [AI Contribution Policy](#ai-contribution-policy)
- [Contributor Legal Status & CLA](#contributor-legal-status--cla)
  - [Anti-SaaS Community Protection](#anti-saas-community-protection)
  - [Clean-Room & IP Protection](#clean-room--ip-protection)
- [Ways to Contribute](#ways-to-contribute)
  - [1. Support Contributions](#1-support-contributions)
  - [2. Bug Report Contributions](#2-bug-report-contributions)
  - [3. Documentation & Translation Contributions](#3-documentation--translation-contributions)
  - [4. Code Contributions](#4-code-contributions)
- [Required Testing Before Submitting](#required-testing-before-submitting)
- [Submitting a Pull Request](#submitting-a-pull-request)
- [FAQ](#faq)
- [Development Guides & Resources](#development-guides--resources)

---

## Code of Conduct

This project and everyone participating in it is governed by the [ISLAMU Event Code of Conduct](CODE_OF_CONDUCT.md) (adapted from Contributor Covenant 3.0). By participating, you are expected to uphold this code.

Please report unacceptable behavior to [contact@openislamu.org](mailto:contact@openislamu.org). All reports are investigated promptly, fairly, and with strict confidentiality.

---

## Security Vulnerabilities & Disclosure

> [!CAUTION]
> **Never report security vulnerabilities, authorization flaws, or sensitive data leaks in public GitHub issues, discussions, or PRs.**

If you discover a potential security vulnerability (e.g. tenant-isolation bypass, payment inconsistency, privilege escalation, or PII leak), please follow our [Security Policy](SECURITY.md):

1. Email your findings privately to **[contact@openislamu.org](mailto:contact@openislamu.org)**.
2. Include complete steps to reproduce, affected endpoints, and sanitized logs.
3. Allow the maintainer time to investigate and coordinate a patch before public disclosure.

---

## High-Level Expectations

- **Clear Product Direction:** ISLAMU Event is built on Clean Architecture (Domain → Application → Infrastructure/Persistence → API / Blazor), HATEOAS/HAL link affordances, and strict tenant isolation.
- **Centralized Ownership:** Decisions and architectural governance are centralized.
- **Limited Review Capacity:** Review bandwidth is constrained (single maintainer).
- **Quality Over Volume:** Not every contribution will be accepted — even if technically functional — if it increases maintenance burden or diverges from the project's architectural roadmap.

---

## State of the Project

ISLAMU Event is currently in **pre-1.0** active development. While the standalone single-container core (`Event.Standalone` with SQLite) and split topologies (PostgreSQL, Redis, Keycloak, Cerbos) are operational, several features and integrations are evolving rapidly.

- Breaking changes may occur between pre-1.0 releases (we actively avoid data-loss bugs, but configuration or schema adjustments may be necessary).
- Small bug fixes and documentation improvements are accepted directly on the active development branch (`develop`).
- New features, module integrations, and larger changes require prior discussion and alignment.

---

## What Makes a Strong Contribution

The following types of contributions are most likely to be accepted:

### Code Quality and Architecture Compliance
All contributions must adhere to the project's architectural invariants:
- **Clean Architecture:** Domain has no external dependencies; Application handles business logic and CQRS/MediatR; Persistence/Infrastructure implement contracts; API and Blazor remain decoupled.
- **HATEOAS / HAL Affordances:** UI affordances (e.g., Edit/Delete buttons) must be gated by the presence of `_links`, never local client-side claim inspection.
- **Entity Boundaries:** Repositories return domain entities, never DTOs (mapping happens in CQRS handlers).
- **Manual Validators:** FluentValidation validators are instantiated manually within handlers (no DI).
- **Test-Driven:** All logic changes must be accompanied by focused unit, integration, or architecture tests.

### Atomic Changes
If your change is small and obvious (typo fix, small bug, minor docs update), you may open a pull request directly.

If you are fixing a bug in an endpoint or component, do not:
- Reformat unrelated files
- Refactor unrelated code
- Fix style issues elsewhere
- Combine multiple unrelated changes

Even well-intentioned "improvements" increase review complexity exponentially.

**One pull request = one logical change.**

If you want to refactor or clean up code, discuss it first and submit it separately.

---

## Discussion Is Required for Larger Changes

For anything beyond a small fix, you must discuss it before opening a pull request.

This includes:
- New features or modules (ticketing workflows, payment providers, federation, AI context adapters)
- Blazor UI/UX changes or design system token updates
- Changes to default behavior or configuration cascades
- Refactors or structural decomposition
- Performance rewrites
- Database schema changes or migrations
- Changes touching multiple layers or many files

Discussion happens in:
- **GitHub Discussions:** https://github.com/islamu-ngo/Event/discussions
- **Discord:** https://discord.gg/wrkY824Yv5

Pull requests introducing major changes without prior discussion may be closed without review. This ensures alignment before significant work is done.

### How to Propose an Enhancement

When proposing an enhancement or new feature in [GitHub Discussions (Feature Requests)](https://github.com/islamu-ngo/Event/discussions/categories/feature-requests), structure your proposal using this format:

1. **Problem Statement:** What specific real-world problem or workflow limitation are you trying to solve? Who is affected (attendees, organizers, instance admins)?
2. **Current vs. Expected Behavior:** Describe the user journey today, and what you envision instead.
3. **Architectural & Invariant Alignment:**
   - Does this belong in the core event engine, or as a modular aspect (e.g. custom properties, add-on components)?
   - Is it white-label and purpose-agnostic (no hardcoded organization/domain assumptions)?
   - Does it uphold tenant isolation, minimal PII retention, and HATEOAS/HAL link affordances?
4. **Mockups or User Flow (if UI-related):** Provide wireframes, screenshots, or step-by-step navigation flows.
5. **Alternatives Considered:** Why can't this be solved with existing configuration, webhooks, or external integration?

---

## Maintainer Bandwidth & Expectations

To set clear, realistic expectations:
- ISLAMU Event is currently in **pre-1.0 greenfield development** with a single primary maintainer. Review capacity is strictly finite.
- We cannot provide beginner-level mentorship or hand-holding on foundational C# / Clean Architecture concepts.
- Unsolicited major architectural refactors, stylistic formatting rewrites, or PRs touching broad surfaces without prior discussion are unlikely to be accepted.
- We do not accept AI-generated spam PRs, unverified code dumps, or unsolicited third-party package additions.

Contributions that are focused, well-tested, architecturally compliant, and aligned with our roadmap are warmly welcomed and deeply appreciated.

AI usage is permitted as an assistant. However, contributors must fully understand what their changes do and why.

---

## AI Contribution Policy

AI-assisted contributions (using GitHub Copilot, ChatGPT, Claude, Gemini, Cursor, etc.) are **permitted and welcomed**, provided they follow strict engineering standards.

### The Rules for AI-Assisted Submissions:
1. **Mandatory Disclosure:** You must explicitly disclose if AI tools were used in the pull request template, naming the tool(s) and describing how they were used.
2. **Human Understanding & Ownership:** You are responsible for every line of code submitted. You must understand the logic, architecture compliance, edge cases, and test results.
3. **Human-Authored Summary:** The "Changes" section and PR discussion must be written by a human in your own words, not blindly copied from an LLM prompt.
4. **Automated Submissions & Labeling:** Any pull request or issue that appears entirely automated, generated without contextual understanding, or disconnected from project patterns will be labeled as automated and **closed after *x* days** unless a human genuinely responds and demonstrates comprehension.
   > [!NOTE]
   > The exact duration of *x* days has not been formalized yet. Because the project is in its early stages and until **ISLAMU (ASBL en formation)** is officially registered with legal personality, maintainer capacity is limited and review windows may take an **indefinite amount of time**.

---

## Contributor Legal Status & CLA

Inbound contributions are governed by the **ISLAMU Event Contributor License Agreement (CLA) v1.0**.

Every non-bot contributor must sign the CLA before a pull request can be merged.

### How to Sign the CLA:
When you open a pull request, the CLA Assistant bot will post a comment. Simply reply with the following comment:

```text
I have read and agree to the ISLAMU Event Contributor License Agreement v1.0, and I confirm that I have the right to submit my contribution under it.
```

To re-run the CLA check after signing, post a comment with `recheck`.

### Why a CLA Alongside AGPL-3.0-or-later?
ISLAMU Event is distributed publicly under the **GNU AGPL-3.0-or-later** license. The CLA does not take ownership away from you; you retain copyright in your contributions. It provides the ISLAMU project steward (currently Amir Akrari as interim trustee, transferring to ISLAMU ASBL upon incorporation and ratification) with the inbound licensing rights needed to offer ISLAMU Event under alternative terms for enterprise internal-use on-premises/VPC compliance (where corporate compliance policies ban AGPL copyleft on internal systems), public-sector procurement, or humanitarian missions.

**Anti-SaaS Community Protection:** The Project Steward is bound by a strict governance commitment **never to grant an alternative license permitting a third party to operate a closed-source, proprietary SaaS or cloud service**. Any entity offering ISLAMU Event as a SaaS must use `AGPL-3.0-or-later`, guaranteeing that all SaaS improvements remain open source. Your contributions will never be enclosed behind a proprietary vendor wall. See [`legal/CLA.md`](legal/CLA.md) and [`islamic-value-sensitive-design/governance/i-vsd-licensing-and-commercial-strategy.md`](islamic-value-sensitive-design/governance/i-vsd-licensing-and-commercial-strategy.md).

### Clean-Room & IP Protection
To protect the codebase and its outbound licensing options, you must **never** copy third-party proprietary, copyleft-incompatible, or unverified source code, snippets, ASTs, SQL, migrations, tests, comments, or assets into this repository. See [`docs/internal/legal/IP_GOVERNANCE.md`](docs/internal/legal/IP_GOVERNANCE.md) for details.

---

# Ways to Contribute

## 1. Support Contributions
We use Discord for real-time discussion and GitHub Discussions for structured help.

### Requesting Support
- Before opening a thread, search existing [GitHub Discussions](https://github.com/islamu-ngo/Event/discussions) and [Documentation Hub](docs/README.md) to see if your question is already covered.
- Provide complete and reproducible details (operating system, Docker/Aspire logs, reproduction steps, screenshots).
- Be respectful — support is provided voluntarily by community members and the maintainer.
- Avoid tagging maintainers directly unless requested.

### Providing Support
- Verify information before sharing.
- Be constructive and patient with community members.

---

## 2. Bug Report Contributions
Create a GitHub issue using the [Bug Report issue form](.github/ISSUE_TEMPLATE/01_BUG_REPORT.yaml) **only** if:
- The bug is reproducible from a clean checkout on the `develop` branch.
- You have verified that no existing issue or discussion covers it.
- The bug does **not** involve security, authentication bypass, or sensitive data leaks (see [Security Vulnerabilities & Disclosure](#security-vulnerabilities--disclosure) above).

Bug reports must include:
- Clear, step-by-step reproduction instructions from a fresh state.
- Expected behavior vs. actual behavior.
- Deployment topology (Standalone SQLite, Docker Compose Split, or Local Aspire).
- Relevant logs, environment details, and error output with **all secrets, tokens, and PII strictly redacted**.

### Bug Triage Lifecycle
Issues progress through defined labels:
1. `🔍 Triage` — Newly reported issue awaiting maintainer review.
2. `needs-repro` — Steps are missing or the issue cannot be reproduced on a clean checkout. Issues in this state may be closed if unconfirmed.
3. `needs-fix` — Reproducible bug confirmed; open for an aligned pull request.

Incomplete bug reports or reports generated purely with generic AI output may be closed.

---

## 3. Documentation & Translation Contributions
- **Docs:** Repository documentation lives in `docs/`, with our [official hosted guides](https://islamu.gitbook.io/islamu-event) available online. When editing or creating repository docs, follow [`docs/internal/DOCUMENTATION_ARCHITECTURE.md`](docs/internal/DOCUMENTATION_ARCHITECTURE.md) and include source anchors.
- **Translations:** UI translations are managed via Weblate. Contributions to language packs are warmly welcomed.

---

## 4. Code Contributions

### Issue / Discussion Requirement
Every non-trivial pull request must reference and close an existing Issue or Discussion. If none exists, start a discussion first.

### Commit Message Format
All commits must follow the **Conventional Commits** specification:

```text
type(scope): description
```

**Types:**
- `feat` — New features or functional capabilities
- `fix` — Bug fixes and error resolutions
- `refactor` — Code changes that neither fix a bug nor add a feature
- `docs` — Documentation additions or updates
- `test` — Adding or correcting tests
- `chore` — Maintenance, tooling, and release preparation
- `ci` — CI/CD workflow and script updates

**Approved Scopes:**
- *Public product scopes:* `events`, `registration`, `ticketing`, `discovery`, `notifications`, `privacy`, `access`, `storage`, `onboarding`, `federation`, `webhooks`, `localization`, `accessibility`, `self-hosting`
- *Engineering scopes:* `ui`, `api`, `architecture`, `ci`, `dependencies`, `database`, `observability`, `documentation`, `release`, `testing`, `build`

**Examples:**
- `fix(events): keep draft events private until organizers publish them`
- `feat(ticketing): add minor-unit price validation to catalog tiers`
- `fix(ui): gate edit action affordance by checking HAL links`
- `docs(self-hosting): clarify SQLite backup procedure for standalone container`

Keep the commit subject concise. Do not paste walls of change logs into the commit subject.

### Pull Request Title Format
PR titles follow the identical conventional format:
- `fix(events): keep draft events private until organizers publish them`
- `feat(ticketing): support configurable reservation expiry window`

---

## Required Testing Before Submitting

Before submitting a pull request, verify your changes locally.

> [!TIP]
> **Inner Loop Testing (The 3-Ring Model):**
> You do not need to run the entire test suite during active coding.
> - **Ring 1 (Inner Loop, < 2s):** Run fast in-memory TUnit sliced tests targeting domain/application classes (`--treenode-filter "/*/*/*<TestClass>/*"`). Zero Docker or network lag.
> - **Ring 2 (Component Validation):** Run the individual project test command below for the project you touched.
> - **Ring 3 (Pre-PR Gate):** CI will run the full multi-database and architecture test matrix.

### 1. Solution Build
```bash
dotnet build --configuration Release --verbosity quiet
```

### 2. Run Test Projects Individually
Do not run solution-wide `dotnet test`. Run the individual test projects relevant to your changes:

```bash
# Architecture tests (mandatory CI gate)
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet

# Domain unit tests
dotnet test --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --configuration Release --verbosity quiet

# Application unit tests
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet

# Secrets authority unit tests
dotnet test --project tests/Explore.Secrets.UnitTests/Explore.Secrets.UnitTests.csproj --configuration Release --verbosity quiet

# Infrastructure unit / category tests
dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category!=Runtime]" --minimum-expected-tests 1

# Persistence integration tests
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --verbosity quiet

# API integration tests
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet

# Blazor client unit & component tests
dotnet test --project tests/Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --verbosity quiet

# Blazor BFF integration tests
dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj --configuration Release --verbosity quiet
```

---

## Submitting a Pull Request

1. **Branching Strategy:**
   - **Target Branch:** All feature and bug fix pull requests must branch from and target **`develop`**.
   - `main` is reserved for tagged production releases.
2. **Complete the PR Template:**
   - Fill out every required section in `.github/PULL_REQUEST_TEMPLATE.md`.
   - Provide human-written change rationale.
   - Disclose AI tool usage.
   - Record Release Impact and Documentation Impact.
   - Sign the CLA via comment when prompted by the bot.

---

## FAQ

**Q: Should I ask before fixing a typo or small bug?**  
A: No. Narrow, obvious fixes for typos or isolated bug fixes with clear tests can be submitted directly as PRs against `develop`.

**Q: I have an idea for a new feature or integration.**  
A: Start a conversation in [GitHub Discussions](https://github.com/islamu-ngo/Event/discussions) or Discord first. Do not invest time writing code before reaching alignment with the maintainer.

**Q: My PR was closed without extensive feedback.**  
A: This usually means the PR did not align with the product roadmap, introduced unnecessary architectural complexity, lacked prior discussion for a large change, or exceeded maintainer review bandwidth.

**Q: Can I submit refactoring or cleanup PRs?**  
A: Please discuss structural refactorings beforehand. Unsolicited whitespace, formatting, or stylistic PRs will be closed to protect review bandwidth.

**Q: Can I use AI to help with my PR?**  
A: Yes, AI-assisted development is welcome. However, you must disclose AI usage in the PR template, understand every change thoroughly, and be able to explain and defend your implementation.

---

## Development Guides & Resources

- **Code of Conduct:** [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md)
- **Security Policy & Vulnerability Reporting:** [`SECURITY.md`](SECURITY.md)
- **Contributor License Agreement:** [`legal/CLA.md`](legal/CLA.md)
- **IP & Clean-Room Governance:** [`docs/internal/legal/IP_GOVERNANCE.md`](docs/internal/legal/IP_GOVERNANCE.md)
- **Documentation Hub & Router:** [`docs/README.md`](docs/README.md)
- **Getting Started & Local Setup:** [`docs/internal/GETTING_STARTED.md`](docs/internal/GETTING_STARTED.md)
- **Local Aspire Orchestration:**
  ```bash
  cp .env.example .env
  aspire run --apphost src/Explore.AppHost/Explore.AppHost.csproj
  ```
- **First-Time Contributor Walkthrough:** [`docs/internal/FIRST_CONTRIBUTION.md`](docs/internal/FIRST_CONTRIBUTION.md)
- **Detailed Technical Contribution Guide:** [`docs/internal/CONTRIBUTING.md`](docs/internal/CONTRIBUTING.md)
- **Testing Guide & Invariants:** [`docs/internal/TESTING.md`](docs/internal/TESTING.md)
- **Quick Reference & Invariants:** [`docs/internal/QUICK_REFERENCE.md`](docs/internal/QUICK_REFERENCE.md)
- **Architecture Overview:** [`docs/internal/ARCHITECTURE.md`](docs/internal/ARCHITECTURE.md)
- **Design System & CSS Tokens:** [`docs/internal/DESIGN_SYSTEM.md`](docs/internal/DESIGN_SYSTEM.md)
- **Documentation Architecture & Dual-Track Parity:** [`docs/internal/DOCUMENTATION_ARCHITECTURE.md`](docs/internal/DOCUMENTATION_ARCHITECTURE.md)

