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
* **[Clean Architecture Conventions](clean-architecture.md)** — Understand domain invariants, MediatR CQRS slices, and HAL link assembly.
* **[TUnit Testing Conventions](tunit.md)** — Run fast, targeted unit and integration test slices with TUnit.
* **[Clean-Room IP & Licensing](clean-room-ip-and-licensing.md)** — AGPLv3 guidelines, CLA requirements, and clean-room provenance rules.

---

## Contributor License Agreement (CLA)

Every non-bot contributor must sign the **ISLAMU Event Contributor License Agreement (CLA) v1.0**. When you open a pull request, the CLA Assistant bot will guide you to sign directly in a comment:

```text
I have read and agree to the ISLAMU Event Contributor License Agreement v1.0, and I confirm that I have the right to submit my contribution under it.
```

* **Anti-SaaS Protection:** The CLA protects the community: the Project Steward is legally bound never to grant closed-source commercial SaaS licenses. Any entity offering ISLAMU Event as a hosted service must remain open source under `AGPL-3.0-or-later`. Your contributions will never be locked behind a proprietary vendor wall. See [Clean-Room IP & Licensing](clean-room-ip-and-licensing.md).
