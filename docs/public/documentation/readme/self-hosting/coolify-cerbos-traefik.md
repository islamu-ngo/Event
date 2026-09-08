---
description: Deploy and operate an external Cerbos Policy Decision Point (PDP) behind Traefik on Coolify.
---

# Coolify with Cerbos & Traefik

This runbook guides operators through deploying and operating an external **Cerbos Policy Decision Point (PDP)** as a standalone application on **Coolify** behind Traefik, providing fine-grained authorization for ISLAMU Event.

> [!TIP]
> **⚙️ ISLAMU's Production Deployment on Coolify Cloud:**
> In our own production environment, we connect our Hetzner CPX32 server to a **Coolify Cloud** subscription and deploy the following services directly through Coolify:
> - **`islamu-event-api`**: Core ASP.NET Core REST API & Background Workers Docker image
> - **`islamu-event-ui`**: Blazor WebAssembly BFF Docker image
> - **`postgres` (ISLAMU Event)**: Primary application relational database
> - **`postgres` (Cerbos)**: Dedicated policy and schema storage database for Cerbos PDP
> - **`cerbos`**: Policy Decision Point Docker image for fine-grained authorization (configured below)
> - **Phase Two Keycloak (`phasetwo/phasetwo-keycloak`)**: Identity Provider Docker image supporting multi-tenancy and OIDC
> - **CockroachDB**: Dedicated database backend powering Phase Two Keycloak
> - **Infisical**: Secrets management engine providing dynamic runtime configuration
> - **Hetzner Object Storage**: S3-compatible media storage located in Falkenstein, Germany (`eu-central`)
>
> For server sizing benchmarks, see the **[Deployment Tiers & Sizing Guide](deployment-tiers.md)**.

---

## 1. Architecture Overview

```
                          Internet / Clients
                                 │
                                 ▼
                     Traefik (Coolify Ingress)
                      ├─── TLS Terminated ───┐
                      │                      │
                      ▼                      ▼
           HTTP Port 3592 (Admin)   gRPC Port 3593 (PDP)
                      │               (Traefik h2c)
                      ▼                      ▼
              ┌─────────────────────────────────────┐
              │      Cerbos PDP (Docker Image)       │
              └──────────────────┬──────────────────┘
                                 │
                                 ▼
                      PostgreSQL Policy Store
```

- **Runtime Decisions**: ISLAMU Event connects to Cerbos over gRPC (`:3593`) to evaluate authorization policies for every MediatR request.
- **Administration & Policies**: The Admin API (`:3592`) is used by `cerbosctl` or CI/CD to upload and validate policy bundles.
- **Fail-Closed Security**: If Cerbos is unreachable or returns an error, ISLAMU Event **fails closed** (access denied). It does not silently fall back to local RBAC.

---

## 2. Cerbos Docker Image Setup on Coolify

1. In the Coolify dashboard, create a new **Docker Image** application.
2. Configure the container image:
   - **Image**: `ghcr.io/cerbos/cerbos`
   - **Tag**: `0.51.0` *(Do not use `latest`; always pin your Cerbos release tag)*

---

## 3. Admin Password Hash Generation

The Cerbos server configuration requires a bcrypt-hashed password for the Admin API, rather than plaintext:

```bash
mkpasswd -m bcrypt -R 10 | base64 -w0
```

- Enter your intended administrative password when prompted.
- Copy the base64-encoded output string. This value will be set as `CERBOS_ADMIN_PASSWORD_HASH`.
- Keep the plaintext password stored securely in your secret manager (e.g., Infisical or password vault) for policy uploads using `cerbosctl`.

---

## 4. Environment Variables Configuration

Set these environment variables on the Cerbos application in Coolify:

| Key | Required | Purpose | Example Value |
|---|---|---|---|
| `CERBOS_ADMIN_USER` | Yes | Admin API username | `cerbos_admin` |
| `CERBOS_ADMIN_PASSWORD_HASH` | Yes | Base64 bcrypt hash generated above | *(generated base64 hash)* |
| `CERBOS_PG_URL` | Yes | PostgreSQL connection string | `postgres://cerbos_user:secret@postgres:5432/cerbos?sslmode=prefer&search_path=cerbos` |
| `CERBOS_CONFIG` | Yes | Mounted server configuration path | `/config/conf.yaml` |

---

## 5. Server Configuration File Mount

Create a persistent file mount in Coolify:
- **Source**: `/data/coolify/applications/{application-id}/conf.yaml`
- **Destination**: `/config/conf.yaml`

Content of `conf.yaml`:

```yaml
server:
  httpListenAddr: ":3592"
  grpcListenAddr: ":3593"

  adminAPI:
    enabled: true
    adminCredentials:
      username: ${CERBOS_ADMIN_USER}
      passwordHash: ${CERBOS_ADMIN_PASSWORD_HASH}

engine:
  lenientScopeSearch: true

compile:
  cacheDuration: 30s

storage:
  driver: "postgres"

  postgres:
    url: "${CERBOS_PG_URL}"
    connPool:
      maxLifeTime: 60m
      maxIdleTime: 45s
      maxOpen: 10
      maxIdle: 3
```

---

## 6. PostgreSQL Database Bootstrap

Before starting Cerbos, execute this bootstrap script in your PostgreSQL database to create the required schema, audit triggers, and database user permissions:

Update the placeholders 'your_cerbos_password', then optionally also the databas name thats defaultt "cerbos" if you want to.

```sql
CREATE SCHEMA IF NOT EXISTS cerbos;
SET search_path TO cerbos;

CREATE TABLE IF NOT EXISTS policy (
    id BIGINT NOT NULL PRIMARY KEY,
    kind VARCHAR(128) NOT NULL,
    name VARCHAR(1024) NOT NULL,
    version VARCHAR(128) NOT NULL,
    scope VARCHAR(512),
    description TEXT,
    disabled BOOLEAN DEFAULT FALSE,
    definition BYTEA
);

CREATE TABLE IF NOT EXISTS policy_dependency (
    policy_id BIGINT,
    dependency_id BIGINT,
    PRIMARY KEY (policy_id, dependency_id),
    FOREIGN KEY (policy_id) REFERENCES cerbos.policy(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS policy_ancestor (
    policy_id BIGINT,
    ancestor_id BIGINT,
    PRIMARY KEY (policy_id, ancestor_id),
    FOREIGN KEY (policy_id) REFERENCES cerbos.policy(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS policy_revision (
    revision_id SERIAL PRIMARY KEY,
    action VARCHAR(64),
    id BIGINT,
    kind VARCHAR(128),
    name VARCHAR(1024),
    version VARCHAR(128),
    scope VARCHAR(512),
    description TEXT,
    disabled BOOLEAN,
    definition BYTEA,
    update_timestamp TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS attr_schema_defs (
    id VARCHAR(255) PRIMARY KEY,
    definition JSON
);

CREATE OR REPLACE FUNCTION process_policy_audit()
RETURNS TRIGGER AS $policy_audit$
BEGIN
    IF (TG_OP = 'DELETE') THEN
        INSERT INTO policy_revision(action, id, kind, name, version, scope, description, disabled, definition)
        VALUES('DELETE', OLD.id, OLD.kind, OLD.name, OLD.version, OLD.scope, OLD.description, OLD.disabled, OLD.definition);
    ELSIF (TG_OP = 'UPDATE') THEN
        INSERT INTO policy_revision(action, id, kind, name, version, scope, description, disabled, definition)
        VALUES('UPDATE', NEW.id, NEW.kind, NEW.name, NEW.version, NEW.scope, NEW.description, NEW.disabled, NEW.definition);
    ELSIF (TG_OP = 'INSERT') THEN
        INSERT INTO policy_revision(action, id, kind, name, version, scope, description, disabled, definition)
        VALUES('INSERT', NEW.id, NEW.kind, NEW.name, NEW.version, NEW.scope, NEW.description, NEW.disabled, NEW.definition);
    END IF;
    RETURN NULL;
END;
$policy_audit$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS policy_audit ON policy;
CREATE TRIGGER policy_audit
AFTER INSERT OR UPDATE OR DELETE ON policy
FOR EACH ROW EXECUTE PROCEDURE process_policy_audit();

DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'cerbos_user') THEN
        CREATE ROLE cerbos_user WITH LOGIN PASSWORD 'your_cerbos_password';
    ELSE
        ALTER ROLE cerbos_user WITH LOGIN PASSWORD 'your_cerbos_password';
    END IF;
END
$$;

GRANT CONNECT ON DATABASE "cerbos" TO cerbos_user;
GRANT USAGE ON SCHEMA cerbos TO cerbos_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON cerbos.policy, cerbos.policy_dependency, cerbos.policy_ancestor, cerbos.attr_schema_defs TO cerbos_user;
GRANT SELECT, INSERT, DELETE ON cerbos.policy_revision TO cerbos_user;
GRANT USAGE, SELECT ON cerbos.policy_revision_revision_id_seq TO cerbos_user;
```

---

## 7. Traefik Routing & gRPC `h2c` Configuration

1. In Coolify, configure the exposed ports: `3592,3593`.
2. Set up domain mapping with separate hostnames:
   ```text
   https://cerbos-api.example.org:3592,https://cerbos-grpc.example.org:3593
   ```
3. **Critical Traefik gRPC Scheme**: Traefik must communicate with Cerbos container port `3593` using cleartext HTTP/2 (`h2c`). Add this Traefik label to the gRPC service:
   ```text
   traefik.http.services.https-{route-index}-{application-id}.loadbalancer.server.scheme=h2c
   ```
   *(Replace `{route-index}` and `{application-id}` with Coolify’s generated service identifier for port 3593)*.

---

## 8. Verifying & Testing the gRPC Route

Test reachability of the public gRPC endpoint using `grpcurl`:

```bash
grpcurl cerbos-grpc.example.org:443 list
```

Expected output:
```text
cerbos.policy.v1.CerbosAdminService
cerbos.response.v1.CerbosService
grpc.health.v1.Health
grpc.reflection.v1alpha.ServerReflection
```

If this fails with `transport: protocol error: stream terminated without error code`, verify that Traefik is using the `h2c` scheme and that requests target Cerbos port `3593`.

---

## 9. Uploading Policies with `cerbosctl`

Upload the repository's authorization schemas and policies using the official `cerbosctl` Docker image:

```bash
# Upload Schemas
docker run --rm -it \
  -v "$PWD/cerbos/policies/_schemas:/schemas:ro" \
  ghcr.io/cerbos/cerbosctl:0.51.0 \
  --server=cerbos-grpc.example.org:443 \
  --username=cerbos_admin \
  --password="your_admin_password" \
  put schema -R /schemas

# Upload Policies
docker run --rm -it \
  -v "$PWD/cerbos/policies:/policies:ro" \
  ghcr.io/cerbos/cerbosctl:0.51.0 \
  --server=cerbos-grpc.example.org:443 \
  --username=cerbos_admin \
  --password="your_admin_password" \
  put policy -R /policies
```

---

## 10. Connecting ISLAMU Event to Cerbos

In your ISLAMU Event `.env` configuration file (see [Environment Variables Reference](../configuration-and-operations/environment-variables.md#10-advanced-authorization-cerbos-pdp)), configure the Cerbos PDP connection:

```env
AUTHORIZATION_PROVIDER=cerbos
CERBOS_GRPC_ENDPOINT=https://cerbos-grpc.example.org:443
CERBOS_USE_TLS=true
CERBOS_PLAINTEXT_MODE=false
```

Restart `event-api` and check `/health`:
```bash
curl http://localhost:7039/health
```
Verify that the `cerbos` health check reports `Healthy` (see [Health Check Endpoints](../configuration-and-operations/troubleshooting-and-health.md#health-check-endpoints-reference)).

---

## Related Guides & Next Steps

* **[Deployment Tiers & Sizing](deployment-tiers.md)** — Review hardware requirements, sizing matrices, and ISLAMU's real-world reference setup.
* **[Authorization Architecture & Policies](../security-and-identity/authorization.md)** — Understand how MediatR requests evaluate policies and generate HAL affordances.
* **[Docker Compose Runbook](docker-compose.md)** — Deploy the core application stack behind Traefik or Caddy.
* **[Troubleshooting Recipe: Cerbos 403 Forbidden](../configuration-and-operations/troubleshooting-and-health.md#recipe-5-all-authenticated-actions-return-403-forbidden-cerbos-fail-closed)** — Diagnose missing policies or network timeouts.
* **[Secrets Management](../configuration-and-operations/secrets.md)** — Securely store your `CERBOS_ADMIN_PASSWORD_HASH`.
