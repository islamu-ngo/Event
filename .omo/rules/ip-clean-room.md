---
name: ip-clean-room
description: Apply when source, documentation, or active planning may be externally informed or change dependency licensing.
paths:
  - "src/**/*"
  - "docs/internal/**/*"
  - "dev/active/**/*"
related_skills: [ip-clean-room, agentic-research]
related_docs: [docs/internal/legal/IP_GOVERNANCE.md, docs/internal/QUICK_REFERENCE.md, docs/internal/legal/dependencies/mapperly.md, legal/CLA.md]
minimum_tests: [Event.Architecture.Tests]
related_intents: [add-get-endpoint, add-write-endpoint, add-hal-link, add-cqrs-handler, add-ef-migration, update-repository-query, blazor-component-affordance, bff-auth-bug, openapi-contract-change, ci-cd-change, external-infrastructure-bootstrap, ip-clean-room-governance, create-agent-context-skill, update-ai-context-disclosure, registration-data-collection, webhook-delivery-redesign, platform-privacy-erasure]
---

<!-- ABOUTME: Apply when source, documentation, or active planning may be externally informed or change dependency licensing. -->
<!-- ABOUTME: Twin copy at .agents/rules/ip-clean-room.md. When modifying this file, update both paths. -->

# IP Clean-Room And Outbound-License Protection

> **Applies to:** implementation source, documentation, and active feature/design workstreams.
> **Authority:** [`docs/internal/legal/IP_GOVERNANCE.md`](../../docs/internal/legal/IP_GOVERNANCE.md), [`AGENTS.md`](../../AGENTS.md) §5, and [`docs/internal/QUICK_REFERENCE.md`](../../docs/internal/QUICK_REFERENCE.md).

## Rules (Correct / Wrong)

| # | Rule | Correct | Wrong |
|---|---|---|---|
| 1 | External input | Source-free functional observations and constraints | External code, snippets, ASTs, SQL, migrations, tests, comments, prose, or assets |
| 2 | Context boundary | Fresh implementation context receives the sanitized handoff only | Research and implementation continue in one source-bearing context |
| 3 | Literal copying | Independent repository-native implementation | Translation, paraphrase, mechanical rewrite, or copied expressive artifacts |
| 4 | SSO | Record AFC filtration and independent naming, structure, sequence, data, UI, and tests | Assume a language or framework change proves independence |
| 5 | Dependencies | Prove every intended ISLAMU outbound model remains lawful or obtain documented separate rights | Treat scanner success or the CLA as third-party relicensing authority |
| 6 | Evidence | Source register + clean handoff + SSO decision + dependency record + PR/journal links | Anonymous benchmark notes or retained raw source material |

## Must-Reads for This Path

- [IP Governance](../../docs/internal/legal/IP_GOVERNANCE.md)
- [Quick Reference](../../docs/internal/QUICK_REFERENCE.md#critical-rules)
- [IP Clean-Room Skill](../../.agents/skills/ip-clean-room/SKILL.md)

## Anti-Patterns (Forbidden on These Paths)

- Ingesting restricted third-party source or source-derived representations into implementation prompts or tooling.
- Copying expressive organization, workflows, schemas, UI composition, tests, comments, or documentation even when identifiers are renamed.
- Adding dependencies whose terms block an intended ISLAMU distribution, hosting, or alternative-license path.
- Removing attribution/provenance rather than removing source expression.
- Continuing after contamination instead of discarding unmerged output and restarting from a sanitized handoff.

## Verification

- Dependency changes: `dotnet run .ci/scripts/validate-dependency-license-policy.cs -- .`
- Agent context: `dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet`
- Review the completed [SSO checklist](../../.agents/skills/ip-clean-room/resources/sso-and-provenance-review.md).

## Related

- [CLA](../../legal/CLA.md)
- [Mapperly Dependency And Projection Contract](../../docs/internal/legal/dependencies/mapperly.md)
- [CI/CD Governance](../../docs/internal/CI_CD_GOVERNANCE.md)
