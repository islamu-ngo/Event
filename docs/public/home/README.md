---
description: >-
  Evaluate, deploy, and operate a self-hosted event platform for communities and
  organizations.
icon: house
---

# ISLAMU Event

ISLAMU Event is a self-hostable platform for public event discovery, organizer workflows, registration and admission, paid events, administration, notifications, integrations, and selective open-protocol federation.

{% hint style="warning" %}
**Pre-1.0 software:** the current API version is `0.1`. Pin the exact release you deploy, read the release and API changelogs before upgrading, and prove restore procedures before changing production data.
{% endhint %}

## Who this documentation is for

Use this site if you are:

* evaluating whether ISLAMU Event fits an organization or community;
* responsible for a self-hosted deployment;
* operating identity, authorization, backups, upgrades, email, payments, or integrations;
* integrating through HAL/REST, webhooks, MCP, forms, or AT Protocol records;
* contributing to the repository and its governed release process.

This is not an attendee guide. User interfaces remain discoverable through the product; these pages focus on adoption, deployment, operations, security, and integration.

## Start with the deployment decision

| Path                 | Best fit                                                       | Durable state                                                    | Important boundary                                                                             |
| -------------------- | -------------------------------------------------------------- | ---------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| Standalone container | Evaluation, small installations, lowest operational load       | SQLite plus the privacy-erasure authority file and local storage | One API replica; preserve every durable file during backup and restore                         |
| Split Docker Compose | Long-running self-hosting with independently operated services | PostgreSQL and configured service volumes                        | Run initialization and migrations in order; validate the rendered Compose model before startup |
| Coolify              | Teams already operating Coolify and Traefik                    | Same application databases and volumes as the selected topology  | Repository guidance covers Cerbos operations, not a one-click full-platform template           |
| .NET Aspire          | Local development and adopter-owned orchestration              | Resources declared by the AppHost                                | Aspire can target clouds, but this repository does not ship turnkey Azure or AWS templates     |

Kubernetes/Helm, ActivityPub, first-party AT Protocol PDS/AppView hosting, and initial arm64 images are not current supported promises.

## Architecture at a glance

Browser traffic enters through the Blazor BFF. The BFF owns browser session handling and forwards access tokens to the API. The API dispatches application requests through MediatR, persists business state, and emits durable work through outboxes. Local Identity, Keycloak, or AT Protocol can be the primary browser identity authority. Runtime authorization is explicitly selected between Cerbos and local DB-backed RBAC.

Clients do not infer mutation rights from roles or claims. HAL `_links` returned by the server are the source of truth for actions such as edit, delete, check-in, refund, and administration.

## Operational trust model

* Secret and authorization authorities fail closed; they do not silently weaken to fallback providers.
* Tenant resolution and persistence filters fail closed when tenant context is missing.
* Payment and refund state is authoritative only after provider-confirmed evidence.
* In-app notification rows are durable truth; SSE and Web Push are refresh hints.
* Privacy erasure has a separate durable authority whose backup and restore obligations depend on the selected topology.
* Branding never replaces required platform, directory-operator, or tenant legal-disclosure identity.

These are system contracts, not claims of regulatory compliance or religious certification. Deployment-specific law, provider agreements, tax, accounting, accessibility validation, and operating policy remain adopter responsibilities.

## Evaluate in this order

1. Read the [Documentation](https://islamu.gitbook.io/islamu-event/documentation/) overview and choose a deployment path.
2. Review security, tenancy, privacy-erasure recovery, secrets, backup, and upgrade boundaries.
3. Check unsupported capabilities and provider-owned responsibilities against your requirements.
4. Use the [API Reference](https://islamu.gitbook.io/islamu-event/api-reference/) for protocol and integration decisions.
5. Keep the [Self-Hoster & Adopter FAQ](https://islamu.gitbook.io/islamu-event/help-center/) available during installation.
6. Review [Release Notes](https://islamu.gitbook.io/islamu-event/changelog/) before every upgrade.

## What success looks like

A production-ready adopter can identify every durable data location, resolve every secret from an approved authority, verify identity and authorization health, complete a backup and restore drill, observe `/alive`, `/health`, and `/metrics` safely, and explain which party owns payment, privacy, moderation, and external-provider decisions.

If any one of those answers is unknown, treat the deployment as not ready rather than relying on a silent default.
