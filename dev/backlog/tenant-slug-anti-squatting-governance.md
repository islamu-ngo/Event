# Tenant-Slug Anti-Squatting Governance

- **Status:** Deferred; policy and operator workflow not implemented.
- **Owner:** Instance governance / trust and safety.
- **Origin:** Root-slug tenant resolution I-VSD finding F002/M002.
- **Decision dependency:** [ADR-034](../../docs/internal/adr/ADR-034-root-path-tenant-resolution.md).

## Problem

The reserved-slug catalog prevents technical route collisions and protects a
small set of authority-implying names. It does not decide who may claim community,
mosque, organization, geographic, trademark, or public-identity names. First-come
allocation alone can enable impersonation or exclusion, while a universal manual
approval gate would burden small self-hosters.

## Required Governance Work

Define an instance-operator policy for high-risk slug claims, reassignment,
disputes, and appeals. The policy must distinguish technical reservations from
social claims, identify the evidence and authority required for intervention,
and keep an auditable reason without publishing sensitive evidence.

## Acceptance Boundary

- Operators can explain which claims are automatically allowed, held for review,
  denied, or reassigned and under whose authority.
- Tenants receive bounded, non-deceptive outcomes and an appeal path.
- A slug transfer cannot silently retarget existing URLs, sessions, caches, or
  custom-domain mappings.
- Policy enforcement is tenant- and instance-authorized, auditable, and resistant
  to administrator self-dealing.
- Self-hosters may adopt a documented low-administration default without claiming
  that technical reservation solves impersonation.

## Non-Goals

Do not expand the compile-time reserved catalog into an unreviewed trademark or
organization registry. This note does not authorize automated identity judgments
or retroactive slug seizure.
