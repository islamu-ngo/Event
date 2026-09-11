---
description: Configuration, secrets, backup, upgrade, health, and recovery runbooks.
---

# Configuration & Operations

This section covers the operational contract after selecting a deployment topology. Keep configuration parameters, secret values, application relational data, and privacy-erasure authority distinct: each possesses a different backup mechanism and trust boundary.

---

## In this Section

* **[Environment Variables](environment-variables.md)** — Master reference for baseline settings (`.env.example`) and advanced built-in defaults.
* **[Configuration Manifests](configuration-manifests.md)** — Version and export governed tenant settings without secrets, PII, or application data.
* **[Secrets Management](secrets.md)** — Select fail-closed secret authorities (Environment or Infisical) and rotate credentials safely.
* **[Infisical Setup](infisical.md)** — Create the Infisical project, folder tree, machine identity, and bootstrap inputs the platform reads.
* **[Backup, Restore & Upgrade](backup-restore-upgrade.md)** — Protect every durable authority and rehearse recovery procedures.
* **[Troubleshooting & Operational Health](troubleshooting-and-health.md)** — Step-by-step diagnostic recipes for startup, identity, policy, and provider issues.

---

## Operational Lifecycle

1. Start by reviewing **[Environment Variables](environment-variables.md)** and configuring your `.env` file from the baseline template.
2. Store passwords and API keys securely according to **[Secrets Management](secrets.md)**.
3. Establish automated backup scripts as detailed in **[Backup, Restore & Upgrade](backup-restore-upgrade.md)**.
4. Consult **[Troubleshooting & Health](troubleshooting-and-health.md)** if containers fail startup health probes.

---

## Related Guides & Next Steps

* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Production split container deployment.
* **[Docker Standalone Runbook](../self-hosting/docker-standalone.md)** — Single-container deployment with SQLite.
* **[Authentication Setup](../security-and-identity/authentication.md)** — Configure Keycloak OIDC integration.
* **[First-Run Administration Guide](../administration-and-branding/admin-guide.md)** — Complete instance onboarding in the browser.
