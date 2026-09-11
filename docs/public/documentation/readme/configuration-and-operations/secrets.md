---
description: Select, bind, rotate, and recover secrets through fail-closed authorities.
---
<!-- ABOUTME: Operator guide for selecting, binding, and rotating external secret authorities. -->
<!-- ABOUTME: Explains fail-closed resolution and credential ownership without exposing secret material. -->

# Secrets Management

Secrets are external authority bindings, not application configuration values to copy between repositories or plaintext files.

---

## Approved Secret Authorities

The platform resolves sensitive credentials through one of three approved authorities (configured via `SECRET_PROVIDER`):

* **Environment** (`SECRET_PROVIDER=Environment`): Direct injection of variables from an uncommitted `.env` file or container orchestrator secrets. See [Environment Variables Reference](environment-variables.md).
* **Infisical** (`SECRET_PROVIDER=Infisical`): Centrally managed secret delivery using Infisical Universal Auth. See [Infisical Setup](infisical.md) for the project, folder tree, and machine identity.
* **Shared .NET User Secrets** (`SECRET_PROVIDER=UserSecrets`): Available **strictly in Development and Testing environments** for local developer isolation (see [Local Development](../contributing/local-development.md)).

> [!WARNING]
> User Secrets are strictly prohibited in production and will cause startup failure if selected when `ASPNETCORE_ENVIRONMENT=Production`.

---

## Prohibited Locations

Never store passwords, tokens, connection strings, encryption keys, or setup secrets in:

* Git source code or repository commits.
* [Configuration Manifests](configuration-manifests.md).
* Public container images or Docker build args.
* Application logs, OpenTelemetry traces, or health payloads.
* Public issue trackers or support tickets.

---

## Fail-Closed Authority Status

The selected secret provider reports distinct operational states: `Unconfigured`, `Unavailable`, `Unauthorized`, or `Invalid`. If a provider cannot supply a credential, ISLAMU Event **fails closed**: it will never silently downgrade to a weaker fallback source or run with empty security keys.

First-run setup secrets generated in volumes must be retrieved securely and purged after onboarding (see [Setup Secret Recovery](troubleshooting-and-health.md#recipe-6-lost-setup-secret-recovery)).

---

## SMTP Credential Ownership

Email is disabled until explicitly enabled through `email.delivery_enabled`. Credentials
stay in the selected secret authority. Use `MAIL_SMTP_USERNAME` and `MAIL_SMTP_PASSWORD`
for instance SMTP; register tenant-scoped bindings for a tenant's own server. A tenant's
server cannot inherit instance credentials, even if the tenant has no credential bindings.
Without either binding the tenant transport is anonymous; providing only one credential
or failing to resolve a configured credential prevents delivery.

Changing Event SMTP does not configure Keycloak or the account provider's authentication
emails. Manage those providers' credentials and delivery settings with their own controls.

## Secret Rotation Procedure

Plan secret rotations as rolling service restarts. The platform requires process reload to rebind database and encryption credentials:

1. Generate the replacement secret in your selected authority (Environment or Infisical).
2. For database credentials, grant the new password concurrently before rotating.
3. Restart or redeploy the target containers (`event-api`, `event-ui`).
4. Verify `/health` reports all services as `Healthy` (see [Health Check Endpoints](troubleshooting-and-health.md#health-check-endpoints-reference)).
5. Revoke the retired credential in the backing authority.

---

## Related Guides & Next Steps

* **[Environment Variables Reference](environment-variables.md)** — Master catalog of all baseline and secret environment variables.
* **[Docker Compose Runbook](../self-hosting/docker-compose.md)** — Inject secrets into production split containers.
* **[Keycloak Authentication](../security-and-identity/authentication.md)** — Securely bind Keycloak client secrets and database passwords.
* **[Troubleshooting & Operational Health](troubleshooting-and-health.md)** — Diagnose secret provider startup failures.
