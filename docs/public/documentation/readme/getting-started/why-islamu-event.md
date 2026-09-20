---
description: Evaluate the platform’s purpose, scope, maturity, and operating model.
---

# Why ISLAMU Event?

ISLAMU Event is a self-hostable event platform for communities and organizations that need explicit control over deployment, durable data, identity, authorization, payments, admission, moderation, and federation.

---

## What the Platform Brings Together

The product covers public event discovery, organizer administration, [registration and waitlists](../events-and-ticketing/ticketing-and-check-in.md), [modular event aspects](../events-and-ticketing/modular-event-aspects.md), [organizer-direct payments and refunds](../events-and-ticketing/paid-events-and-payouts.md), [durable email notifications](../communications-and-notifications/email-smtp.md), [outgoing webhooks](../integrations-and-ai/webhooks.md), [custom registration forms](../events-and-ticketing/custom-properties.md), [Model Context Protocol (MCP)](../integrations-and-ai/mcp.md), [local or S3 storage](../integrations-and-ai/storage.md), and selective [AT Protocol federation](../federation-and-open-protocols/at-protocol-and-bluesky-jetstream.md).

These capabilities remain strictly bounded:
* Enabling an integration does not silently grant it identity, authorization, payment, or moderation authority.
* Browser returns are not payment truth (see [Paid Events & Payouts](../events-and-ticketing/paid-events-and-payouts.md)).
* Realtime transports are not notification truth.
* External moderation signals (such as [Coop & Osprey](../integrations-and-ai/coop-and-osprey.md)) do not replace local administrative decisions.

---

## Core Architectural Principles

* **Deployment Control:** Choose the documented [Docker Standalone](../self-hosting/docker-standalone.md) or [Docker Compose](../self-hosting/docker-compose.md) topology and own your infrastructure.
* **Fail-Closed Authority:** [Keycloak Authentication](../security-and-identity/authentication.md), the selected [Authorization Provider](../security-and-identity/authorization.md), tenant resolution, and [Secret Providers](../configuration-and-operations/secrets.md) do not silently fall back to weaker paths.
* **Durable Workflows:** Transactional outboxes and idempotent intake protect external effects such as email, webhooks, payments, and federation.
* **Discoverable Permissions:** [HAL links](../security-and-identity/authorization.md#the-golden-rule-of-client-ui-affordances) tell clients which actions are currently allowed for a resource.
* **Explicit Limits:** Unsupported deployment modes, protocol surfaces, and provider responsibilities are documented rather than implied.

---

## Maturity & Operational Responsibility

ISLAMU Event is in active pre-release development with no official release yet. We are actively squashing bugs and preparing for our initial release. Breaking changes happen freely during this pre-release phase to refine the architecture. Published releases, pre-built container images, and stable upgrade paths will begin with our first release.

Self-hosting transfers operational responsibility to the operator. You must assess TLS, DNS, identity-provider hardening, authorization policy, secrets, backups, incident response, retention, data residency, accessibility, and provider agreements for your deployment. Product documentation describes implemented behavior; it is not a compliance, security, accessibility, or religious certification.

---

## Related Guides & Next Steps

* **[5-Minute Quickstart](5-minute-quickstart.md)** — Spin up an evaluation instance locally using Docker.
* **[Architecture & Request Flows](architecture-and-request-flows.md)** — Trace HTTP requests from the browser-facing service through authorized operations to storage.
* **[Deployment Tiers & Sizing](../self-hosting/deployment-tiers.md)** — Review hardware requirements and deployment topologies.
* **[Clean-Room IP & Licensing](../contributing/clean-room-ip-and-licensing.md)** — Understand our open-source IP stewardship and governance.
