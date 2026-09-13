---
description: Adopter documentation for evaluation, self-hosting, operations, security, and integration.
---

# Documentation

This space is the operating manual for organizations evaluating or self-hosting ISLAMU Event. It follows the lifecycle of an adoption decision: understand the system, select a topology, configure authoritative state, secure it, operate it, then add product capabilities and integrations.

> [!WARNING]
> **Active Pre-Release Development:** ISLAMU Event has **no official release yet**. We are actively fixing startup bugs and preparing for our very first release soon! Pre-built container images and release tags are not yet published.

---

## Recommended Reading Paths

### Evaluator
1. [Getting Started](readme/getting-started/README.md) — Product vision, local quickstart, and architecture.
2. [Self-Hosting](readme/self-hosting/README.md) — Deployment topology selection and capacity planning.
3. [Security & Identity](readme/security-and-identity/README.md) — Authentication, authorization, and GDPR privacy erasure.
4. [Events & Ticketing](readme/events-and-ticketing/README.md) — Event modeling, custom forms, ticketing, and check-in.
5. [Integrations & AI](readme/integrations-and-ai/README.md) — Storage, webhooks, MCP, and external services.

### Self-Hoster & Operator
1. [Self-Hosting](readme/self-hosting/README.md) — Runbooks for Docker Standalone, Docker Compose, and Coolify.
2. [Configuration & Operations](readme/configuration-and-operations/README.md) — Environment variables, secrets, backup, and health checks.
3. [Security & Identity](readme/security-and-identity/README.md) — Keycloak setup and Cerbos vs. Local RBAC.
4. [Administration & Branding](readme/administration-and-branding/README.md) — Web console onboarding, custom domains, and white-labeling.
5. [Communications & Notifications](readme/communications-and-notifications/README.md) — MailKit SMTP outbox and Listmonk subscriber sync.

### Integrator & Contributor
1. [Integrations & AI](readme/integrations-and-ai/README.md) — Outgoing webhooks, Model Context Protocol (MCP), and S3 storage.
2. [Federation & Open Protocols](readme/federation-and-open-protocols/README.md) — AT Protocol federation and custom Lexicons.
3. [Contributing](readme/contributing/README.md) — Local development workflow, Clean Architecture, and TUnit testing.

---

## Documentation Contract

Repository behavior is authoritative. Pages distinguish implemented behavior from deferred or adopter-owned work. They do not claim turnkey cloud deployment, legal or regulatory compliance, religious certification, guaranteed provider behavior, or support for infrastructure the repository does not ship.

The platform uses fail-closed authority boundaries. When identity, authorization, tenant resolution, secrets, privacy erasure, payment evidence, or mandatory public disclosure cannot be established, the system rejects or withholds the operation rather than silently weakening the contract.
