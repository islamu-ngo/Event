---
description: Use Aspire locally and adapt declared resources to adopter-owned cloud infrastructure.
---

# .NET Aspire & Cloud Deployment

[.NET Aspire](../contributing/local-development.md) is the repository's development orchestrator and an architectural foundation adopters can adapt for custom cloud infrastructure. Note that ISLAMU Event intentionally does not ship turnkey supported Azure or AWS production cloud templates.

---

## Local Development Orchestration

To run the complete platform orchestrator during local development:

```bash
aspire start --apphost src/Explore.AppHost/Explore.AppHost.csproj
```

The AppHost declares resources, dependency ordering, service discovery, configuration injection, health checks, and restart relationships (see [Local Development Guide](../contributing/local-development.md)). Use the Aspire dashboard and resource logs to inspect startup. Ports in Aspire are assigned dynamically; do not assume static Docker Compose port numbers.

The local Cerbos service waits for its PostgreSQL server's connection health
check before starting. A container marked Running is insufficient. Existing
database credentials, data volume and initialization remain selected by the
same operator configuration; no replacement credential or manual retry is
required by this readiness ordering.

Allow the migration service to finish migrations and lookup seeding before
checking application readiness. Startup now avoids repeating preparation of
unchanged application model metadata; this requires no new credentials or
database permissions. Verify the application's private operations health and
metrics endpoints after startup. These checks establish application readiness
and metric exposition; delivery recovery and log collection require their own
operational checks.

---

## Cloud Adaptation Considerations

Aspire can output deployment manifests targeting cloud container environments such as Azure Container Apps (ACA) or AWS ECS when an adopter supplies the hosting infrastructure. The repository does not provide native managed-secret adapters for Azure Key Vault or AWS Secrets Manager; operators must use the approved [Secret Providers (Environment or Infisical)](../configuration-and-operations/secrets.md).

A production cloud adaptation must explicitly define:

1. **Durable Relational Data**: Primary application database and isolated [Privacy-Erasure Authority](../security-and-identity/privacy-erasure.md).
2. **Object Storage**: [S3-compatible Object Storage](../integrations-and-ai/storage.md) for media and attachments.
3. **Identity & Authorization**: [Keycloak OIDC Authentication](../security-and-identity/authentication.md) and [Authorization (Local RBAC or Cerbos)](../security-and-identity/authorization.md).
4. **Migration Service**: One-shot execution of `Event.MigrationService` before opening web traffic.
5. **Secrets Delivery**: Safe binding via [Secrets Management (Infisical or Environment)](../configuration-and-operations/secrets.md).
6. **Networking & TLS**: Ingress reverse proxy with trusted forwarder headers (`X-Forwarded-Proto`).
7. **Observability**: Centralized logs, metrics, and health probes (see [Troubleshooting & Health](../configuration-and-operations/troubleshooting-and-health.md)).
8. **Disaster Recovery**: Automated snapshots and restore rehearsals (see [Backup, Restore & Upgrade](../configuration-and-operations/backup-restore-upgrade.md)).

---

## Acceptance Criteria

Do not deem a cloud deployment operational merely because containers boot:
* Prove migrations execute cleanly.
* Confirm that Keycloak OIDC redirects back to the Blazor BFF over HTTPS.
* Verify that privacy-erasure replay runs during startup without errors.
* Rehearse a complete restore from database backup.

---

## Related Guides & Next Steps

* **[Local Development with .NET Aspire](../contributing/local-development.md)** — Step-by-step developer setup instructions.
* **[Docker Compose Runbook](docker-compose.md)** — Recommended split container deployment guide.
* **[Deployment Tiers & Sizing](deployment-tiers.md)** — Infrastructure sizing matrix for small to large deployments.
* **[Backup, Restore & Upgrade](../configuration-and-operations/backup-restore-upgrade.md)** — Production backup routines and disaster recovery.
