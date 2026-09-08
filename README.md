<a name="readme-top"></a>

<div align="center">

<img src="assets/images/adopters/ISLAMU/islamu-logo-text-only-v2.png" alt="ISLAMU logo" width="200" />

# ISLAMU Event

Self-hostable, open-source event discovery and management software for communities, organizations, and platform operators.

ISLAMU Event powers ISLAMU’s Islamic events instance, but the software itself is purpose-agnostic, white-label, and designed to be rebranded for any event ecosystem.

> Pre-1.0 notice: ISLAMU Event is still before v1. Breaking changes may happen between releases. We avoid data-loss-class breaks where possible, but configuration changes may be required.

![GitHub Workflow Status][github-workflow-status-shield]
[![GitHub License][github-license-shield]][github-license-link]
[![GitHub Repo Stars][github-stars-shield]][github-stars-link]
[![GitHub Last Commit][github-last-commit-shield]][github-last-commit-link]
[![Contributors][github-contributors-shield]][github-contributors-link]
[![Discussions][github-discussions-shield]][github-discussions-link]
[![Discord][discord-shield]][discord-link]
[![Documentation][docs-shield]][official-docs-link]
[![Donate via Stripe][stripe-shield]][stripe-donation-link]

[**ISLAMU Live Instance**][islamu-platform] · [**Official Docs (GitBook)**](https://islamu.gitbook.io/islamu-event) · [**5-Minute Quickstart**](https://islamu.gitbook.io/islamu-event/documentation/readme/getting-started/5-minute-quickstart) · [**Self-Hosting**](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting) · [**Roadmap**][roadmap-link] · [**Developer Guide**](#-documentation-for-developers--contributors) · [**Sponsor**](#-sponsors)

</div>

## About ISLAMU Event

ISLAMU Event is a **self-hostable event discovery and management platform** for publishing, discovering, and operating events across one organization or many isolated tenants.

The public ISLAMU instance is Islamic-focused, but the software itself is **purpose-agnostic, white-label, and designed to be rebranded for any event ecosystem**.

![Event List Screenshot][event-list-image]

## ✨ Who It Serves

### Event Seekers

- **🔍 Intuitive Event Discovery:** Search effortlessly by keyword, date, location, category, and tags to find gatherings near you or online
- **👨‍👩‍👧‍👦 Culturally-Aware Filters:** Filter events by age groups, family/gender arrangements, community traditions, or prayer-relative timings to find gatherings tailored to your preferences
- **🌐 Multi-Language Sessions:** View event details and attend sessions in your preferred language with localized schedules
- **📱 Mobile-First Experience:** Fast, responsive experience across mobile and desktop, installable directly to your home screen as an app (PWA)
- **✅ Easy RSVP & Waitlists:** Reserve your spot in seconds, select specific sessions, and automatically join waitlists for sold-out events
- **🎟️ Clear Ticket Options:** Browse free and paid events with upfront ticket tiers, live availability, and no hidden surprises
- **💳 Secure Payments:** Pay for your event tickets safely directly on our platform with transparent pricing, secure checkout, and instant digital tickets — see [Payments](https://islamu.gitbook.io/islamu-event/documentation/readme/events-and-ticketing/paid-events-and-payouts)
- **🤖 AI Event Assistant:** Ask questions in natural language, discover events tailored to your interests, and get quick help drafting your registrations

### Event Organizers

- **📅 Comprehensive Event Builder:** Organize single-day workshops, multi-day conferences, or recurring programs with multi-track agendas, speaker profiles, and multi-language support
- **📊 Registration & Attendee Controls:** Set capacity limits, customize registration approval workflows, manage automated waitlists, and configure public or unlisted visibility
- **👥 Team Collaboration:** Invite co-organizers, assign granular roles (Owner, Admin, Editor, Viewer), and manage organization permissions collaboratively
- **🧩 Custom Registration Fields & Forms:** Collect attendee details with custom fields, single/multi-select questions, and reusable event templates — see [Custom Properties](https://islamu.gitbook.io/islamu-event/documentation/readme/events-and-ticketing/custom-properties)
- **🔔 Attendee Communication & Webhooks:** Keep attendees informed with templated emails, in-app notifications, and outgoing webhooks to sync with external tools — see [In-App Notifications](https://islamu.gitbook.io/islamu-event/documentation/readme/communications-and-notifications/in-app-notifications), [Webhooks](https://islamu.gitbook.io/islamu-event/documentation/readme/integrations-and-ai/webhooks), and [Email SMTP](https://islamu.gitbook.io/islamu-event/documentation/readme/communications-and-notifications/email-smtp)
- **📇 Consensual Contact Sharing:** Help attendees network responsibly with explicit, opt-in contact sharing that attendees can revoke at any time — see [Ticketing & Check-In](https://islamu.gitbook.io/islamu-event/documentation/readme/events-and-ticketing/ticketing-and-check-in)
- **🎟️ Flexible Paid & Free Ticketing:** Create multiple ticket tiers, early-bird pricing, group rates, and shared capacity pools. Draft, clone, and preview your ticket catalog before publishing — see [Ticketing & Check-In](https://islamu.gitbook.io/islamu-event/documentation/readme/events-and-ticketing/ticketing-and-check-in)
- **💳 Direct Payouts & Automated Financials:** Sell tickets with complete peace of mind. Connect your Stripe account to receive direct payouts into your bank account with no platform intermediary holding your funds. The platform handles all attendee transactions, receipts, fee calculations, and automated refund management — see [Paid Events & Payouts](https://islamu.gitbook.io/islamu-event/documentation/readme/events-and-ticketing/paid-events-and-payouts)
- **📬 Audience Growth:** Automatically sync registered attendees into your mailing list or newsletter (such as [Listmonk](https://islamu.gitbook.io/islamu-event/documentation/readme/communications-and-notifications/listmonk)) to build lasting community engagement
- **🤖 AI Event Co-Pilot:** Draft compelling event descriptions, generate relevant tags, and refine schedules using AI suggestions that you review and approve before publishing
- **🌍 Broader Reach via Federation:** Publish once to broadcast your events across participating federated platforms, with optional support for owning your event data directly on your personal data server (PDS via [AT Protocol & Bluesky Jetstream](https://islamu.gitbook.io/islamu-event/documentation/readme/federation-and-open-protocols/at-protocol-and-bluesky-jetstream))

### Platform Owners & Self-Hosters

- **🆓 100% Free & Open Source:** No feature paywalls, telemetry traps, or enterprise tiers. Licensed under AGPL-3.0-or-later
- **🛡️ Absolute Data Sovereignty:** Self-host on your own infrastructure (Docker, Coolify, Aspire, On-Prem) with total control over user and attendee data — see [Self-Hosting Overview](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting)
- **🐳 Flexible Deployment Topologies:** Deploy as a single lightweight container ([Docker Standalone](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/docker-standalone)) with embedded SQLite, a split [Docker Compose](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/docker-compose) topology, or across [Deployment Tiers](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/deployment-tiers)
- **💼 Multi-Tenancy:** Run as a dedicated single-tenant instance or a multi-tenant platform with isolated domains and branding at runtime without code changes — see [Multi-Tenancy](https://islamu.gitbook.io/islamu-event/documentation/readme/security-and-identity/multi-tenancy)
- **🛠️ White-Label Control:** Fully customizable branding, domains, logos, navigation links, and policies per tenant — see [White-Labeling & Branding](https://islamu.gitbook.io/islamu-event/documentation/readme/administration-and-branding/white-labeling)
- **💳 Zero-Custody Payments & Legal Protection:** Stripe Connect handles all payment processing, compliance, and payouts directly between attendees and organizers. The legal transaction is strictly between attendee and organizer—the operator never touches card data (zero PCI liability) nor holds attendee funds. Operators simply supply platform legal terms (Terms of Service, privacy policy) and can disable paid ticketing entirely at any time — see [Paid Events & Payouts](https://islamu.gitbook.io/islamu-event/documentation/readme/events-and-ticketing/paid-events-and-payouts)
- **🔧 Admin Hierarchy:** Instance admins, tenant admins, and organization admins with cascading settings — see [Admin Hierarchy](https://islamu.gitbook.io/islamu-event/documentation/readme/administration-and-branding/admin-hierarchy) and [Admin Guide](https://islamu.gitbook.io/islamu-event/documentation/readme/administration-and-branding/admin-guide)
- **🛡️ Built-in Moderation & Verification:** Moderation queues, organizer verification workflows, and structured appeal paths — see [Coop & Osprey Moderation](https://islamu.gitbook.io/islamu-event/documentation/readme/integrations-and-ai/coop-and-osprey) and [Authorization](https://islamu.gitbook.io/islamu-event/documentation/readme/security-and-identity/authorization)
- **🔌 Model Context Protocol (MCP) Server:** Built-in MCP adapter at `/mcp` enabling AI agents and IDEs to discover public events and propose actions through authorized confirmation flows — see [Model Context Protocol (MCP)](https://islamu.gitbook.io/islamu-event/documentation/readme/integrations-and-ai/mcp)
- **🧠 AI-Ready Foundation:** Provider-neutral AI assistant, RAG ingestion contracts, and proposal-first tooling wired through the same authorization and HAL affordance layer as the rest of the platform
- **🌍 Decentralization & Federation:** Optional event federation across remote platforms and AT-Protocol authentication for user-owned identities — see [AT Protocol & Bluesky Jetstream](https://islamu.gitbook.io/islamu-event/documentation/readme/federation-and-open-protocols/at-protocol-and-bluesky-jetstream)
- **🗄️ Privacy Erasure Authority:** GDPR-compliant user data erasure with choice of topology: co-located inside the main database, an isolated external PostgreSQL instance, or embedded SQLite — see [Privacy Erasure & Anti-Resurrection](https://islamu.gitbook.io/islamu-event/documentation/readme/security-and-identity/privacy-erasure)
- **📬 Mailing List Integration (Listmonk):** Connect a self-hosted [Listmonk](https://islamu.gitbook.io/islamu-event/documentation/readme/communications-and-notifications/listmonk) instance to sync attendee registrations, with independent privacy-erasure authority per tenant
- **📚 Comprehensive Docs:** [Official hosted documentation](https://islamu.gitbook.io/islamu-event) alongside in-repository specifications covering architecture, deployment, configuration, and APIs
- **🔐 Enterprise Security:** BFF architecture, local or Cerbos authorization, environment-first secrets (optional Infisical), and HATEOAS REST API — see [Authentication](https://islamu.gitbook.io/islamu-event/documentation/readme/security-and-identity/authentication), [Authorization](https://islamu.gitbook.io/islamu-event/documentation/readme/security-and-identity/authorization), and [Secrets Management](https://islamu.gitbook.io/islamu-event/documentation/readme/configuration-and-operations/secrets)
- **📜 Declarative Configuration Manifests & Portability:** Automated Day 0 bootstrap and Day 2 configuration portability via schema-validated JSON manifests with dry-run validation, preview diffs, and rollback — see [Configuration Manifests](https://islamu.gitbook.io/islamu-event/documentation/readme/configuration-and-operations/configuration-manifests)
- **🎛️ Multi-Instance & Fleet Orchestration Ready:** Complete programmatic control via management APIs (`/api/management/*`) and integration readiness for the upcoming ISLAMU Event Control Plane fleet orchestrator

---

## 🚀 Deployment & Hosting Options

ISLAMU Event is architected for sovereign self-hosting across diverse hardware requirements and scales:

| Topology Mode | Target Use Case | Step-by-Step Documentation |
|---|---|---|
| **[Tier 1: Standalone / Masjid](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/deployment-tiers#tier-1-standalone--masjid-community)** | Single container with embedded SQLite; ideal for 1 vCPU / 2 GB RAM servers. | 📖 **[Docker Standalone Runbook](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/docker-standalone)** |
| **[Tier 2: Production Multi-Service Split](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/deployment-tiers#tier-2-production-split-docker-compose)** | High-reliability split topology: API, Blazor UI, PostgreSQL, Keycloak, and Redis. | 📖 **[Docker Compose Runbook](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/docker-compose)** |
| **[PaaS Deployment (Coolify)](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/coolify-cerbos-traefik)** | Self-hosted PaaS deployment behind Traefik with an external Cerbos PDP container. | 📖 **[Coolify with Cerbos & Traefik](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/coolify-cerbos-traefik)** |
| **[Developer Orchestration](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/dotnet-aspire-and-cloud)** | Local development and cloud-native adaptation via .NET Aspire AppHost. | 📖 **[.NET Aspire & Cloud](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/dotnet-aspire-and-cloud)** |

For hardware sizing benchmarks, real-world Hetzner CPX32 reference metrics, and capacity planning, consult the **[Deployment Tiers & Sizing Guide](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting/deployment-tiers)** and the master **[Environment Variables Reference](https://islamu.gitbook.io/islamu-event/documentation/readme/configuration-and-operations/environment-variables)**.

---

## 🎨 Branding & Customization

ISLAMU Event is built from the ground up for sovereign white-label community use:

- **Instance Name & Domain:** Configure custom instance naming, base URLs, and canonical hosts.
- **Logos, Favicons & UI Themes:** Customize brand primary/secondary colors and typography — see [White-Labeling & Branding](https://islamu.gitbook.io/islamu-event/documentation/readme/administration-and-branding/white-labeling).
- **Custom Vanity Domains:** Map external domains to specific tenants with automatic TLS and routing — see [Custom Domains & SEO](https://islamu.gitbook.io/islamu-event/documentation/readme/administration-and-branding/custom-domains-and-seo).
- **Administrative Governance:** Manage organizations, verification badges, and event categories — see [Administration Guide](https://islamu.gitbook.io/islamu-event/documentation/readme/administration-and-branding/admin-guide).

---

## 🛡️ Enterprise Security & Compliance

Security and privacy fail closed at every layer:

- **🔐 Authentication:** OAuth 2.0 / OIDC user authentication powered by Keycloak — see [Keycloak Authentication](https://islamu.gitbook.io/islamu-event/documentation/readme/security-and-identity/authentication).
- **🛡️ Authorization:** Fine-grained policy evaluation with runtime switching between Local RBAC and Cerbos PDP — see [Authorization Architecture](https://islamu.gitbook.io/islamu-event/documentation/readme/security-and-identity/authorization).
- **🏢 Multi-Tenancy:** Fail-closed ambient query filters guaranteeing zero cross-tenant data leakage — see [Multi-Tenancy Architecture](https://islamu.gitbook.io/islamu-event/documentation/readme/security-and-identity/multi-tenancy).
- **🗄️ Privacy Erasure & GDPR:** Anti-resurrection tombstones preventing erased users from being restored from stale backups — see [Privacy Erasure Authority](https://islamu.gitbook.io/islamu-event/documentation/readme/security-and-identity/privacy-erasure).
- **🗝️ Secrets Management:** Centralized secret delivery via Environment or Infisical — see [Secrets Management](https://islamu.gitbook.io/islamu-event/documentation/readme/configuration-and-operations/secrets).

---

## Used in production

ISLAMU Event is used by:

<img src="assets/images/adopters/ISLAMU/islamu-logo-text-only-v2.png" alt="ISLAMU" width="200" />

---

Using ISLAMU Event and want to add your project/organization to this list? [Open a pull request!](https://github.com/islamu-ngo/Event/edit/develop/README.md)

## Roadmap

The roadmap is tracked publicly in the [Roadmap Kanban View][roadmap-link]. Use it to follow planned work, vote, and comment:

![Roadmap Kanban View][roadmap-image]

---

## 🤝 Contributing

There are many ways you can contribute to ISLAMU Event:

**Non-Technical:**
- 🐛 [Report bugs][github-issues-link]
- 💡 [Suggest features][github-issues-link]
- 📖 [Improve Public Documentation](https://islamu.gitbook.io/islamu-event/documentation/readme/contributing)
- 🌐 [Translate UI/docs via Weblate](https://hosted.weblate.org/)
- 📣 Spread the word in your community

**Technical:**
- 💻 Fix bugs & implement features
- 🧪 Author invariant-breaker test suites
- 📊 Profile & optimize EF Core queries
- 🎨 Enhance Blazor WebAssembly UI components

---

## Quick Start (For Developers & Contributors)

For local development and code contribution, launch the .NET Aspire developer loop:

```bash
git clone https://github.com/islamu-ngo/Event.git
cd Event
cp .env.example .env
curl -sSL https://aspire.dev/install.sh | bash # only needed when aspire CLI is not installed
aspire run --apphost Explore.AppHost/Explore.AppHost.csproj
```

This automatically orchestrates PostgreSQL, Keycloak, Mailpit, `Explore.API`, `Explore.Blazor`, and the Aspire dashboard (`http://localhost:18888`).

For Docker-only local development:

```bash
cp .env.example .env
docker compose config
docker compose up -d postgres redis keycloak-db keycloak keycloak-init islamu-event-api islamu-event-ui
```

### Contributor Onboarding Path
1. Review our [Contribution Guidelines](CONTRIBUTING.md) and [Local Getting Started](docs/internal/GETTING_STARTED.md).
2. Read the [Developer Guide](docs/internal/DEVELOPER_GUIDE.md) for the 5-minute mental model and coding patterns.
3. Check the authoritative [Quick Reference (Invariants)](docs/internal/QUICK_REFERENCE.md), [Clean Architecture Invariants](docs/internal/ARCHITECTURE.md), and [Governance Standards](docs/internal/GOVERNANCE.md).
4. AI-assisted coding agents must strictly follow [`AGENTS.md`](AGENTS.md) and [`.agents/contract/intents.yaml`](.agents/contract/intents.yaml).

### ✍️ Contributor License Agreement & Community Protection

All non-bot contributors must sign the [ISLAMU Contributor License Agreement][cla-link] before a pull request can be merged. Signing is handled automatically directly in your PR with a simple comment reply—see [`legal/CLA.md`][cla-link] for full terms.

> **🛡️ Why We Have a CLA & Why You Can Trust It:**
> * **Community-First Invariant:** Contributors retain ownership of their contributions. The CLA grants ISLAMU inbound rights so the non-profit can offer alternative licensing for enterprise compliance on private internal networks.
> * **The Anti-SaaS Covenant:** ISLAMU is bound by a strict governance commitment **never to grant an alternative license permitting a third party to operate a closed-source, proprietary SaaS or cloud service**. 
> * **Universal Parity:** Any company offering ISLAMU Event as a public SaaS must do so under `AGPL-3.0-or-later`, guaranteeing that all SaaS improvements are shared back with the community. Your contributions will never be locked behind a proprietary vendor wall.

---

## 📚 Documentation: Public Guides vs. Internal Specifications

ISLAMU Event maintains a deliberate separation between **Public Adopter Documentation** (operator/adopter perspective on GitBook) and **Internal Engineering Specifications** (developer/contributor perspective in GitHub Markdown):

### 🌐 1. Public Operator & Adopter Documentation (GitBook)

> 📖 **Official Hosted Portal:** Visit **[https://islamu.gitbook.io/islamu-event](https://islamu.gitbook.io/islamu-event)** for our complete public guides:

* **[Getting Started](https://islamu.gitbook.io/islamu-event/documentation/readme/getting-started/5-minute-quickstart)** — 5-minute quickstart and request flow sequence diagrams.
* **[Self-Hosting & Operations](https://islamu.gitbook.io/islamu-event/documentation/readme/self-hosting)** — Deployment tiers, Docker Standalone, Docker Compose, and Coolify.
* **[Configuration & Secrets](https://islamu.gitbook.io/islamu-event/documentation/readme/configuration-and-operations)** — Environment variables reference, secret authorities, and backups.
* **[Security & Identity](https://islamu.gitbook.io/islamu-event/documentation/readme/security-and-identity)** — Keycloak OIDC, Cerbos authorization, multi-tenancy, and privacy erasure.
* **[Administration & Branding](https://islamu.gitbook.io/islamu-event/documentation/readme/administration-and-branding)** — Web management consoles, white-labeling tokens, and custom domains.
* **[Events, Ticketing & Payments](https://islamu.gitbook.io/islamu-event/documentation/readme/events-and-ticketing)** — Modular aspects, custom properties, admissions, and Stripe Connect.
* **[Communications](https://islamu.gitbook.io/islamu-event/documentation/readme/communications-and-notifications)** — In-app notifications inbox, SMTP outbox delivery, and Listmonk sync.
* **[Integrations & AI](https://islamu.gitbook.io/islamu-event/documentation/readme/integrations-and-ai)** — Outgoing/incoming webhooks, Model Context Protocol (MCP), and S3 storage.
* **[Federation & Open Protocols](https://islamu.gitbook.io/islamu-event/documentation/readme/federation-and-open-protocols)** — AT Protocol event syndication and calendar lexicons.
* **[Contributing Guide](https://islamu.gitbook.io/islamu-event/documentation/readme/contributing)** — Public contributor guide and clean-room IP policies.

---

### 🛠️ 2. Internal Engineering Specifications (For Developers & Contributors)

For developers contributing code, architecture reviewers, and AI agents, use the authoritative in-repository specifications in [`docs/internal/`](docs/internal/index.md):

| Engineering Concern | Canonical In-Repository Markdown File | What You Will Learn |
|---|---|---|
| **Developer Mental Model** | [`docs/internal/DEVELOPER_GUIDE.md`](docs/internal/DEVELOPER_GUIDE.md) | 5-minute onboarding, project structure, and local dev loop. |
| **Global Project Invariants** | [`docs/internal/QUICK_REFERENCE.md`](docs/internal/QUICK_REFERENCE.md) | Single source of truth for architectural constraints and forbidden patterns. |
| **Clean Architecture** | [`docs/internal/ARCHITECTURE.md`](docs/internal/ARCHITECTURE.md) | Domain $\to$ Application $\to$ Persistence/Infrastructure $\to$ API boundaries. |
| **System Architecture Diagrams** | [`docs/internal/ARCHITECTURE_OVERVIEW.md`](docs/internal/ARCHITECTURE_OVERVIEW.md) | C4 container diagrams, component relationships, and data flows. |
| **Coding Patterns & Conventions** | [`docs/internal/GOVERNANCE.md`](docs/internal/GOVERNANCE.md) | MediatR slice patterns, manual validator instantiation, and HAL affordances. |
| **Testing Strategy** | [`docs/internal/TESTING.md`](docs/internal/TESTING.md) | TUnit conventions, test slicing (`--treenode-filter`), and Testcontainers lanes. |
| **Operations & Build Verification** | [`docs/internal/OPERATIONS.md`](docs/internal/OPERATIONS.md) | Verification policies, release gating, and build commands. |
| **REST & HAL API Contracts** | [`docs/internal/API.md`](docs/internal/API.md) & [`API_COOKBOOK.md`](docs/internal/API_COOKBOOK.md) | Endpoint contracts, ProblemDetails error formats, and curl examples. |
| **Blazor WebAssembly Frontend** | [`docs/internal/BLAZOR.md`](docs/internal/BLAZOR.md) & [`BLAZOR_DEV_WORKFLOW.md`](docs/internal/BLAZOR_DEV_WORKFLOW.md) | MudBlazor UI conventions, BFF session cookie proxying, and CSS isolation. |
| **Security & Threat Model** | [`docs/internal/SECURITY-MODEL.md`](docs/internal/SECURITY-MODEL.md) | Fail-closed auth boundaries, token handling, and multi-tenant isolation. |
| **Self-Hosting Technical Specs** | [`docs/internal/SELF_HOSTING.md`](docs/internal/SELF_HOSTING.md) | Low-level container wiring, ports, volume configurations, and profiles. |
| **AI Agent Canonical Contract** | [`AGENTS.md`](AGENTS.md) & [`.agents/contract/intents.yaml`](.agents/contract/intents.yaml) | Contribution contract, task intents, and strict agent execution guardrails. |
| **Full Internal Specs Index** | [`docs/internal/index.md`](docs/internal/index.md) | Master sitemap cataloging all 140+ internal technical specifications. |

---

## 🏗️ Technology Stack (v0.1.0)

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Architecture | Clean Architecture, CQRS, MediatR |
| UI | Blazor WebAssembly, MudBlazor |
| API | ASP.NET Core, REST/HAL, OpenAPI, Swagger, Scalar |
| Data | PostgreSQL, SQLite, EF Core |
| Auth | Keycloak OIDC/OAuth2 |
| Authorization | Cerbos PDP or local database-backed provider |
| Secrets | Environment variables and Infisical provider abstraction |
| Observability | Serilog, OpenTelemetry |
| Deployment | Docker Compose, Docker Standalone, .NET Aspire |
| Tests | TUnit, bUnit, integration and architecture tests |

## Community

Join the ISLAMU community on [Discord][discord-link] and our [GitHub Discussions][github-discussions-link]. We follow a [Code of Conduct][code-of-conduct] in all our community channels.

Feel free to ask questions, report bugs, participate in discussions, share ideas, request features, or showcase. We would love to hear from you.

> Give us a Star ⭐️

## 🛡️ Security Disclosure

If you discover a security vulnerability in ISLAMU Event, please report it responsibly instead of opening a public issue. We take all legitimate reports seriously and will investigate them promptly. See the [Security Policy][security-policy] for more info.

To disclose any security issues, please email us at [contact@openislamu.org][contact-email].

### Security fixes from forks

If you maintain a fork of ISLAMU Event and discover or fix a security vulnerability, please report it privately first using the process in our Security Policy instead of opening a public issue or public pull request before coordination.

We are grateful for security fixes from forks. To merge an exact patch into the official ISLAMU Event repository, every non-bot contributor must sign the ISLAMU Contributor License Agreement. This keeps the official codebase compatible with both the public AGPL-3.0-or-later release and ISLAMU’s alternative licensing path.

If you cannot or do not want to sign the CLA, you can still help by privately sharing the vulnerability details, affected versions, reproduction steps, and the general remediation approach. The ISLAMU maintainers may then implement an independent fix.

## Contributors

### Core Maintainer

|                                                                                                                                                                            Amir Akrari                                                                                                                                                                             |
| :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------: |
|                                                                                                                                         <img src="https://github.com/amirakrari.png" width="200px" alt="Amir Akrari" />                                                                                                                                          |
| <a href="https://github.com/amirakrari"><img src="https://api.iconify.design/devicon:github.svg" width="25px"></a> <a href="https://bsky.app/profile/amirakrari.bsky.social"><img src="https://api.iconify.design/simple-icons:bluesky.svg" width="25px"></a> |

I am deeply grateful to all our amazing contributors.

[![Contributors Image][contributors-image]][contributors-link]

## 💖 Sponsors

Support ISLAMU Event's ongoing development, infrastructure, and open-source mission by becoming a financial sponsor.

### 🥇 Gold Sponsors

*No active Gold sponsors yet.*

[👉 Become a Gold Sponsor →][contact-email]

### 🥈 Silver Sponsors

*No active Silver sponsors yet.*

[👉 Become a Silver Sponsor →][contact-email]

### 🥉 Bronze Sponsors

*No active Bronze sponsors yet.*

[👉 Become a Bronze Sponsor →][contact-email]

### ☕ Community Sponsors

*No active Community sponsors yet.*

[![Donate via Stripe][stripe-shield]][stripe-donation-link] &nbsp; [👉 Become a Community Sponsor →][stripe-donation-link]

## 🤝 In-Kind Supporters

Special thanks to the organizations providing free tooling, infrastructure, and licenses to support ISLAMU Event's open-source development:

| Organization | Contribution & Usage |
|:---:|---|
| <a href="https://www.gitbook.com"><img src="https://api.iconify.design/simple-icons:gitbook.svg?color=%233884FF" width="48" height="48" alt="GitBook Logo" /><br />**GitBook**</a> | Generously provides their **Community Plan** to host and power our [Official Public Documentation](https://islamu.gitbook.io/islamu-event). |

> If your organization would like to support ISLAMU Event with in-kind tooling, infrastructure, or software licenses, please reach out to us at [contact@openislamu.org][contact-email].

## ISLAMU Solutions

- [ISLAMU Event][github-repo-link]: Event Platform & Management System.
- [I-VSD][ivsd-github-repo-link]: Islamic Value Sensitive Design: A Framework for Provider-Mediated Software Solutions

## 🙏 Acknowledgement

- [Keycloak][keycloak-link]: An Open Source Identity and Access Management Provider.
- [Cerbos][cerbos-link]: An Open Source Policy Decision Point.
- [Infisical][infisical-link]: An Open Source Secret Management Platform.
- [MudBlazor][mudblazor-link]: An Open Source Blazor UI library that simplifies the creation of beautiful websites and webapps.
- [ROOST Coop][roost-coop-link]: An Open Source Review and Moderation Platform.
- [ROOST Osprey][roost-osprey-link]: An Open Source Investigation and Rules Engine.
- [Svix][svix-link]: An Open Source Webhooks Service.
- [Penpot][penpot-link]: An Open Source Design Tool.
- [Plane][plane-link]: An Open Source Project Management Platform that unifies projects, knowledge and agents with all-in-one workspace: projects, wiki, and AI.
- [Coolify][coolify-link]: An Open Source Platform as a Service, alternative to Vercel, Heroku, Netlify, and Railway for easy deploying to your own servers.
- [Weblate][weblate-link]: An Open Source Translation Management Platform.
- [Kener][kener-link]: An Open Source Status Page.

### Open-Source Libraries & Dependencies

Our codebase is enriched by dozens of community-crafted .NET libraries. For the complete, centrally managed dependency list and version pins, see [`Directory.Packages.props`](Directory.Packages.props).
*A heartfelt thank you to every open-source author and maintainer whose work (direct or transitive) helps make this project possible.*

## Inspiration (UI/...)

- [Luma][luma-link]: A Modern Event Management & Discovery Platform
- [Smoke Signals][smoke-signals-link]: An Event & RSVP Management and Discovery Web Application built on top of ATProtocol.
- [Mangadex][mangadex-link]: A Manga Discovery Platform with advanced filtering and multi-language support.
- [Plane][plane-link]: An Open Source Project Management Platform that unifies projects, knowledge and agents with all-in-one workspace: projects, wiki, and AI.
- [Hi.Events][hi.events-link]: An Open Source Event Ticketing and Management Platform

## 🌱 Sustainability

ISLAMU is built by one person — **Amir Akrari** — entirely on personal free time and personal funds.

**ISLAMU (ASBL en formation)** (Association Sans But Lucratif — a non-profit association in formation in Belgium) is being established as the operational and legal steward of all ISLAMU open-source projects and charitable activities. Once formally registered with legal personality, ISLAMU ASBL will assume full legal stewardship.

### How ISLAMU sustains itself

ISLAMU will not offer its open-source software as a hosted SaaS. Instead, sustainability is built on:

- **Fundraising & Grants:** Crowdfunding campaigns, foundation grants, and public-interest funding for open-source digital infrastructure
- **Sponsorships:** Ranked sponsorship tiers — sponsors receive recognition and visibility; every euro goes back into the non-profit
- **Official Partnerships & Fleet Tooling:** Organizations that want to offer ISLAMU software as a hosted service can become **Official ISLAMU Partners**. Partners are vetted, listed on the ISLAMU website, and actively recommended and marketed by ISLAMU. Official Partners receive access to **ISLAMU Event Control Plane** (our standalone fleet orchestrator that bridges multiple ISLAMU Event instances with PaaS engines like Coolify for automated provisioning, SLA management, and backups). ISLAMU does not compete with its partners by running a commercial SaaS; we empower our partners with tooling and referrals
- **Consultation & Support** — professional consulting around ISLAMU software deployment, customization, and integration

If you are a company or institution that wants to support this work, reach out at [contact@openislamu.org][contact-email] or start a conversation in our [Discord][discord-link].

## 📞 Contact

For any question or problem reporting, please consider opening a [new issue][github-issues-link] or send an email to [contact@openislamu.org][contact-email] or create a new post in our [Discord Server][discord-link].

## Privacy Policy

You can find details [here][privacy-policy].

## 📄 License & Ecosystem Architecture

This project is licensed under the terms of [GNU AGPL-3.0-or-later][license-link].

### 🌐 The Three-Pillar Ecosystem

ISLAMU Event operates on a transparent three-pillar model designed for universal community parity and sustainable open-source governance:

1. **Community Commons (`AGPL-3.0-or-later`):** 100% Free & Open Source for everyone. Any organization or hoster can run a SaaS or self-host under AGPLv3. All SaaS modifications must be published openly, ensuring complete community parity.
2. **Enterprise Internal-Use License (Anti-SaaS):** For enterprises whose internal compliance policies ban AGPL copyleft on private internal infrastructure. This license waives Section 13 network copyleft for private on-premises/VPC deployments and internal corporate events, while **strictly forbidding external SaaS or commercial cloud hosting**. Paid for commercial corporations (funding security audits and maintainers); gratis ($0) for verified non-profits and educational institutions.
3. **Official Partner Program:** For certified agencies, hosters, and integrators. Partners operate on the exact same 100% AGPLv3 codebase (no proprietary code privilege). Commercial value is generated through quality certification, trust branding, and official directory listings.

For the full ethical and strategic design analysis, see the [I-VSD Strategy Review](islamic-value-sensitive-design/i-vsd-licensing-and-commercial-strategy.md).

### Standalone Core And Optional Services

The minimum operational deployment is the single `Event.Standalone` image: the ISLAMU Event API and Blazor BFF/UI run in one process with SQLite persistence. Application, Data Protection, and embedded privacy-erasure migrations run in that process before it accepts traffic. PostgreSQL, SQL Server, MariaDB, MySQL, Redis, Keycloak, Cerbos, MinIO/S3, SMTP/Mailpit, Svix, Weblate, Formbricks, Coop, Osprey, AI providers, federation services, and external observability backends are optional capabilities, not requirements of the standalone core.

The AGPL-3.0-or-later license and any alternative license offered by ISLAMU apply only to material that ISLAMU owns or is authorized to license. Third-party libraries, container images, services, datasets, fonts, and other assets retain their respective licenses, public-domain status, and other applicable terms. Including an optional integration or deployment manifest in this repository does not relicense that third-party material. See the [Self-Hosting Technical Specs](docs/internal/SELF_HOSTING.md#third-party-software-and-license-boundary) and [release dependency policy](docs/internal/CI_CD_GOVERNANCE.md#standalone-and-optional-service-license-boundary).

<div align="right">

[![][back-to-top]][back-to-top-link]

</div>

<!-- LINK GROUP -->

[back-to-top]: https://img.shields.io/badge/-BACK_TO_TOP-151515?style=flat-square
[back-to-top-link]: #readme-top
[islamu-platform]: https://event.openislamu.org
[official-docs-link]: https://islamu.gitbook.io/islamu-event
[event-list-image]: assets/event-list-image.png
[roadmap-link]: https://sites.plane.so/views/b8b7d9fced694f5a9d9a546e9d40d988
[roadmap-image]: assets/islamu-event-roadmap-screenshot.png
[code-of-conduct]: CODE_OF_CONDUCT.md
[security-policy]: SECURITY.md
[privacy-policy]: https://openislamu.org/privacy
[license-link]: LICENSE
[contact-email]: mailto:contact@openislamu.org

[github-repo-link]: https://github.com/islamu-ngo/Event
[github-issues-link]: https://github.com/islamu-ngo/Event/issues/new
[github-discussions-link]: https://github.com/islamu-ngo/Event/discussions
[github-contributors-link]: https://github.com/islamu-ngo/Event/graphs/contributors

[github-workflow-status-shield]: https://img.shields.io/github/actions/workflow/status/islamu-ngo/Event/test.yml?branch=develop&logo=github&style=flat-square
[github-stars-shield]: https://img.shields.io/github/stars/islamu-ngo/Event?color=594ae2&style=flat-square&logo=github
[github-stars-link]: https://github.com/islamu-ngo/Event/stargazers
[github-license-shield]: https://img.shields.io/github/license/islamu-ngo/Event?color=594ae2&logo=github&style=flat-square
[github-license-link]: https://github.com/islamu-ngo/Event/blob/main/LICENSE
[github-last-commit-shield]: https://img.shields.io/github/last-commit/islamu-ngo/Event?color=594ae2&style=flat-square&logo=github
[github-last-commit-link]: https://github.com/islamu-ngo/Event
[github-contributors-shield]: https://img.shields.io/github/contributors/islamu-ngo/Event?color=594ae2&style=flat-square&logo=github
[github-discussions-shield]: https://img.shields.io/github/discussions/islamu-ngo/Event?color=594ae2&logo=github&style=flat-square
[discord-shield]: https://img.shields.io/discord/1357505436479131668?color=%237289da&label=Discord&logo=discord&logoColor=%237289da&style=flat-square
[discord-link]: https://discord.gg/wrkY824Yv5
[docs-shield]: https://img.shields.io/badge/Docs-GitBook-3884FF?style=flat-square&logo=gitbook&logoColor=white
[stripe-shield]: https://img.shields.io/badge/Donate-Stripe-635BFF?style=flat-square&logo=stripe&logoColor=white

[repobeats-image]: https://repobeats.axiom.co/api/embed/a0f11a3d9b80342b5f5965127c2c45871c9d3397.svg
[contributors-image]: https://contrib.rocks/image?repo=islamu-ngo/Event
[contributors-link]: https://github.com/islamu-ngo/Event/graphs/contributors

[keycloak-link]: https://www.keycloak.org/
[cerbos-link]: https://www.cerbos.dev/
[infisical-link]: https://infisical.com/
[roost-coop-link]: https://roost.tools/coop
[roost-osprey-link]: https://roost.tools/osprey
[svix-link]: https://www.svix.com/
[listmonk-link]: https://listmonk.app/
[mudblazor-link]: https://www.mudblazor.com/
[weblate-link]: https://weblate.org/
[penpot-link]: https://penpot.app/
[plane-link]: https://plane.so/
[coolify-link]: https://coolify.io/
[kener-link]: https://kener.ing/
[luma-link]: https://luma.com/
[smoke-signals-link]: https://smokesignal.events/
[mangadex-link]: https://mangadex.org/
[hi.events-link]: https://hi.events/
[gitbook-link]: https://www.gitbook.com/
[stripe-donation-link]: https://donate.stripe.com/14A6oIesc0Oc2KYg35aR200

[ivsd-github-repo-link]: https://github.com/islamu-ngo/Islamic-Value-Sensitive-Design

[cla-link]: legal/CLA.md
