---
name: cto-consultation
description: "Load when the user asks for CTO advice on feature strategy, architecture direction, build-vs-buy, infrastructure, market positioning, roadmap, self-hosting, cost, or organizational tradeoffs; not for reviewing an existing implementation-plan workstream."
type: workflow
enforcement: suggest
priority: high
---
<!-- ABOUTME: CTO consultation skill for product, architecture, infrastructure, and market-facing feature decisions. -->
<!-- ABOUTME: Grounds strategic advice in the ISLAMU Event architecture, governance docs, and self-hosting constraints. -->

## Must-Read Docs
- [../../../AGENTS.md](../../../AGENTS.md)
- [../../../docs/internal/PROJECT.md](../../../docs/internal/PROJECT.md)
- [../../../docs/internal/ARCHITECTURE.md](../../../docs/internal/ARCHITECTURE.md)
- [../../../docs/internal/DOMAIN.md](../../../docs/internal/DOMAIN.md)
- [../../../docs/internal/API.md](../../../docs/internal/API.md)
- [../../../docs/internal/BLAZOR.md](../../../docs/internal/BLAZOR.md)
- [../../../docs/internal/SECURITY-MODEL.md](../../../docs/internal/SECURITY-MODEL.md)
- [../../../docs/internal/MULTI_TENANCY.md](../../../docs/internal/MULTI_TENANCY.md)
- [../../../docs/internal/SELF_HOSTING.md](../../../docs/internal/SELF_HOSTING.md)
- [../../../docs/internal/DEPLOYMENT_TIERS.md](../../../docs/internal/DEPLOYMENT_TIERS.md)
- [../../../docs/internal/CONFIGURATION.md](../../../docs/internal/CONFIGURATION.md)
- [resources/consultation-framework.md](resources/consultation-framework.md)

## Top 5 Invariants
1. The recommendation must preserve the platform promise: enterprise-grade, self-hostable, tenant-governed, and usable by both small communities and larger operators.
2. Product advice must separate Instance, Tenant, Organization, Group, User, BFF, Client, and API authority instead of collapsing them into one admin or UI concept.
3. Infrastructure advice must treat optional dependencies as explicit deployment-mode or tier choices with health, fallback, and operator documentation impacts.
4. UX advice must keep HAL links as the source of truth for per-resource action affordances and keep the API as the hard authorization boundary.
5. Extensibility advice must choose Layer 1 core fields, Layer 2 typed sector schema, or Layer 3 governed custom properties before proposing storage or UI shape.

## Top 5 Anti-Patterns
1. Enterprise-only default, which raises the operational floor and harms single-organization or community self-hosters.
2. Local-role UX gating, which duplicates server authorization and breaks the HAL affordance contract.
3. Runtime-schema sprawl, which turns Layer 3 custom properties into a hidden domain model and bypasses typed policy semantics.
4. Dashboard-as-source-of-truth, which mistakes infrastructure internals such as scheduler dashboards for product/operator state.
5. Undated market claims, which make competitive or legal recommendations stale and unverifiable.

## Minimal Examples
```text
Consultation output shape:
1. Decision needed
2. Current repo facts
3. Options
4. Recommendation
5. Enterprise/self-hosting impact
6. Implementation path
7. Verification and documentation impact
```

```text
Example stance:
Quartz.NET is the single internal scheduler, including for Basic Dispatch Mode. Its status surface stays optional, instance-admin-only, and never the source of truth for email delivery state — `EmailDispatchOutbox` is.
```

## Verification Hooks
- `dotnet build --configuration Release --verbosity quiet`

## Related Skills
- [../agentic-research/SKILL.md](../agentic-research/SKILL.md)
- [../clean-architecture-rules/SKILL.md](../clean-architecture-rules/SKILL.md)
- [../cqrs-guidelines/SKILL.md](../cqrs-guidelines/SKILL.md)
- [../blazor-ui-conventions/SKILL.md](../blazor-ui-conventions/SKILL.md)
- [../outbox-pattern/SKILL.md](../outbox-pattern/SKILL.md)
