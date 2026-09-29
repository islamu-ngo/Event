<!-- ABOUTME: Self-hosting and operator checklist for CTO review of implementation plans. -->
<!-- ABOUTME: Ensures plans document deployability, configuration, health, recovery, and upgrade behavior for enterprise-style self-hosted installs. -->
# Enterprise Self-Hostable Checklist

Use this checklist for any plan affecting deployment, operations, security, tenancy, integrations, or configuration.

This is especially important for `/dev-docs` workstreams because the plan must tell future implementation agents and self-hosters what will change operationally.

## Core Self-Hosting Questions

A self-hoster must be able to answer:

1. What services are required?
2. What services are optional?
3. Which environment variables are required?
4. Which defaults are safe?
5. What secrets are needed?
6. How are secrets rotated?
7. What DNS/proxy/TLS changes are needed?
8. What database migrations run?
9. What data/config may break?
10. How do I verify the deployment is healthy?
11. How do I recover if onboarding, auth, or a background dependency fails?
12. How do I disable or revert the feature?

If the plan cannot answer these, approval should usually stop at “approve with required changes” or worse.

## Enterprise Readiness Areas

### Configuration

Require:

- documented config keys,
- environment variable names,
- default values,
- allowed values,
- validation behavior,
- startup failure behavior,
- runtime update behavior if applicable.

Reject:

- magic string settings with no docs,
- hidden config in UI only,
- settings that silently fail,
- unsafe defaults for public deployment.

### Secrets

Require:

- environment-first support,
- compatible secret-provider path if applicable,
- no secrets in client/browser,
- no secrets in logs,
- explicit failure mode when missing,
- rotation story for long-lived credentials.

Reject:

- admin passwords entered casually in frontend without clear storage boundary,
- secrets persisted as plain settings,
- diagnostic logs that reveal endpoint credentials or tokens.

### Identity and Authorization

Require:

- clear distinction between authentication and authorization,
- server-side enforcement,
- tenant/instance/admin boundaries,
- machine-to-machine behavior if integrations are involved,
- fallback/fail-closed behavior for policy providers.

Reject:

- client-side role checks as enforcement,
- admin features protected only by routing,
- permissive fallback when policy service fails,
- operator actions mixed with tenant-admin actions.

### Multi-Tenancy

Require:

- single-tenant behavior,
- multi-tenant behavior,
- tenant resolution path,
- tenant data filter behavior,
- instance-admin escape hatches explicitly named,
- tests for tenant isolation and wrong-tenant access.

Reject:

- “tenant-aware” claims without query/filter detail,
- global records created from tenant flows with no ownership model,
- background jobs that process cross-tenant data without partitioning/context.

### Persistence

Require:

- table ownership,
- tenant/audit/soft-delete markers,
- indexes,
- uniqueness constraints,
- migration sequencing,
- data migration or reset path,
- downgrade/rollback caveat when downgrade is not supported.

Reject:

- EAV or JSON used for policy-critical typed data,
- large unindexed query surfaces,
- hidden cross-tenant joins,
- schema changes with no migration notes.

### API and Integrations

Require:

- stable routes,
- stable operation names,
- DTO version/shape rationale,
- OpenAPI regeneration,
- client regeneration,
- contract tests,
- error shape,
- idempotency for external callbacks/webhooks.

Reject:

- duplicated routes,
- unnamed operations,
- “temporary” endpoints with no removal plan,
- webhooks without signature/idempotency/retry semantics.

### BFF and Browser Boundary

Require:

- unsafe methods protected by antiforgery,
- trusted headers resolved server-side,
- tokens kept server-side,
- cookies configured intentionally,
- upload/download URLs bound to trusted context,
- no privileged browser-controlled forwarding.

Reject:

- browser supplies privileged downstream headers,
- access tokens stored in browser storage,
- direct API calls bypassing BFF security assumptions.

### Observability

Require:

- structured logs,
- correlation IDs,
- health checks for new dependencies,
- metrics for background work,
- dead-letter visibility,
- operator-facing failure messages,
- no sensitive values in telemetry.

Reject:

- background workers with only “logged warning” as observability,
- no way to know if a feature is stuck,
- metrics that cannot distinguish tenant/operator impact.

### Upgrade and Release

Require:

- release notes for breaking changes,
- config migration notes,
- database migration notes,
- self-hoster runbook updates,
- compatibility removal explanation,
- test evidence.

Reject:

- breaking config with no operator warning,
- data reset hidden in implementation,
- “works on fresh install” only when upgrades are expected.

## Plan-Artifact Expectations

For `/dev-docs` workstreams, make sure operational impact appears in the right file:

- `plan.md`: architecture, migration, rollout, observability, recovery, and docs impact;
- `context.md`: current operator-related risks or blockers;
- `tasks.md`: explicit docs/internal/config/runbook update tasks and verification commands.
