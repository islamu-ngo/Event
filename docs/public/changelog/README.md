---
description: Curated pre-1.0 release and upgrade guidance for ISLAMU Event adopters.
icon: clock-rotate-left
---

# Release Notes

ISLAMU Event is in major version zero and the current API version is `0.1`. This page summarizes adopter-visible mainline themes and the release discipline operators must apply. It does not invent a stable-version history that the repository has not published.

{% hint style="warning" %}
Pin exact tags and image digests. Review the repository [release checklist](https://github.com/islamu-ngo/Event/blob/develop/docs/internal/RELEASE_CHECKLIST.md) and [API changelog](https://github.com/islamu-ngo/Event/blob/develop/docs/internal/API_CHANGELOG.md) before upgrading. Back up and prove restore procedures before applying migrations or changing provider configuration.
{% endhint %}

## Current release model

Releases currently use manual Semantic Versioning tags and manually authored GitHub Releases. The repository also contains an approved design for a governed release engine, trust roots, signed bundles, and automated evidence, but that system is prospective and is not an active production release authority.

A trustworthy release record should identify the tag, preparation commit, image digests, supported deployment modes, schema/configuration/secret changes, migration order, operator verification, rollback or forward-recovery path, security impact, and documentation impact.

## Reading governed release notes

Governed notes present a maintainer summary, categorized changes, applicable impact evidence, and the complete technical commit range. Within the categorized list, Breaking Changes come first, including breaking bug fixes. Features, Bug Fixes, Performance, and Other Improvements follow only when they contain changes. Each change appears once in that list; evidence references and the complete range may refer to it again.

Before upgrading, read the impact evidence as well as the category headings. It records applicable migration order, configuration changes, security and API impact, and operator actions. A missing category is not a safe-upgrade guarantee. Use the release-specific instructions for backup, restore, or forward recovery rather than assuming that a previous binary can run against a changed database.

These categories do not activate signing or publication. The single dated, newest-first release history and automated GitBook delivery remain a required follow-up. A public page is a mutable view, not the signed release record: verify its tag and canonical notes hash against retained release evidence. A publication failure or page discrepancy does not change the signed release, and a correction must not replace historical signed notes.

## Current mainline themes

### API contract: version 0.1

The API supports media-type, query, and header version negotiation while keeping canonical routes stable. HAL remains the default resource contract, RFC 7807 ProblemDetails is the failure format, and generated OpenAPI/client artifacts are server-owned outputs. Pre-v1 removals, renames, authorization changes, error-shape changes, pagination changes, and generated-client changes may be breaking and must be recorded in the canonical API changelog.

### Deployment and operations

* Standalone and split Docker Compose remain the documented self-hosting paths.
* Split startup requires configuration validation, Keycloak initialization, migrations, and health-gated service ordering.
* Aspire and cloud platforms are adopter-owned adaptations, not turnkey Azure/AWS templates.
* Backup and restore guidance now includes privacy-erasure authority topology and durable object storage.

### Security, tenancy, and privacy

* Keycloak identity and Cerbos/local authorization remain separate authorities.
* Authorization and secret-provider failures fail closed without silent downgrade.
* HAL links are the action-affordance authority for clients.
* Privacy erasure uses durable anti-resurrection facts and topology-specific recovery rules.
* Public disclosure identity remains separate from branding.

### Events, payments, and communication

* Modular event aspects and governed custom properties cover typed and long-tail metadata without becoming a user-defined rules engine.
* Registration, admission credentials, online check-in, and audit facts remain separate responsibilities.
* Organizer-direct Stripe Connect payments use provider-confirmed state; durable refund and reconciliation workflows are current.
* Durable in-app notifications, SMTP outbox delivery, Web Push refresh hints, and optional external Listmonk synchronization have explicit boundaries.

### Integrations and protocols

* Incoming provider callbacks and outgoing webhooks are separate systems.
* Local outgoing webhook delivery is the smallest self-hosted mode; self-hosted Svix is optional.
* Google Workspace Forms and Microsoft organizational Forms use different bounded provider contracts.
* MCP is optional, stateless, API-key-first, and proposal-first for mutations.
* AT Protocol federation remains selective OAuth/publication/Jetstream integration, not ActivityPub or first-party PDS/AppView hosting.

## Upgrade checklist

1. Read the release notes and canonical API changelog for every version between current and target.
2. Record the exact current tag, target tag, commit, image digests, deployment mode, database engine, secret authority, authorization provider, and integration modes.
3. Back up every database, privacy-erasure authority store, and storage authority; prove a restore in isolation.
4. Validate rendered configuration and resolve new required settings without embedding secrets.
5. Stage the upgrade where possible, run initialization and migrations in documented order, and wait for health.
6. Verify identity, authorization, tenant resolution, public discovery, writes, outboxes, email/webhooks, payments, and operational endpoints.
7. If verification fails, use the release-specific rollback or forward-recovery decision. Never restore only the primary database while ignoring erasure authority.

## Canonical detail

The repository's [API changelog](https://github.com/islamu-ngo/Event/blob/develop/docs/internal/API_CHANGELOG.md) is the detailed pre-v1 contract history. The [release checklist](https://github.com/islamu-ngo/Event/blob/develop/docs/internal/RELEASE_CHECKLIST.md) is the current operator/releaser authority. These mainline references describe current guidance; use the exact tag's retained documents when checking a particular release. Prospective release-policy documents must not be read as proof that automated signing, approval, or promotion is active.
