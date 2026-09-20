<!-- ABOUTME: I-VSD compliance-style evidence review for the current ISLAMU Event repository. -->
<!-- ABOUTME: Maps present implementation and documentation evidence to Islamic value-sensitive design concerns without issuing certification. -->

# ISLAMU Event I-VSD Compliance-Style Check

## Scope

This review checks the current ISLAMU Event repository evidence that was available locally at the time of review. It focuses on what is present in the product documentation, selected source anchors, tests/docs references, and repository governance files.

The user question was: "Only of the little thats present, is it 100% compliant?"

Short answer: no. The reviewed evidence contains several strong design and implementation patterns that support I-VSD traceability, especially around trust, privacy, security, tenant boundaries, consent, self-hosting, and accessibility. It does not support a 100% compliance conclusion because multiple categories are incomplete, externally unverified, not reviewed, or require qualified scholarly/legal/operational validation.

## Claim Boundary

This document is an Islamic Value Sensitive Design compliance-style review aid. It is not a fatwa, Sharia certification, halal certification, legal opinion, security audit, accessibility certification, privacy impact assessment, or empirical proof of user outcomes.

Finding levels are limited to `Pass`, `Concern`, `Fail`, `Not reviewed`, and `Requires scholarly review`. `Pass` means the reviewed repository evidence supports the design claim for this check; it does not mean final religious, legal, operational, or product certification.

No finding in this report should be represented as "Islamically compliant," "halal," "Sharia certified," or "guaranteed to prevent harm."

## Evidence Reviewed

- `README.md`
- `docs/PROJECT.md`
- `docs/ARCHITECTURE.md`
- `docs/API.md`
- `docs/AUTHORIZATION.md`
- `docs/SECURITY-MODEL.md`
- `docs/MULTI_TENANCY.md`
- `docs/SELF_HOSTING.md`
- `docs/BACKUP_RESTORE_UPGRADE.md`
- `docs/OPERATIONS.md`
- `docs/CI_CD_GOVERNANCE.md`
- `docs/SECRETS.md`
- `docs/ADMIN_HIERARCHY.md`
- `docs/ADMIN_GUIDE.md`
- `docs/ACCESSIBILITY.md`
- `docs/ACCESSIBILITY_ARTIFACTS.md`
- `docs/LOCALIZATION.md`
- `docs/FOOTER_MANAGEMENT.md`
- `docs/NOTIFICATIONS.md`
- `docs/EMAIL_NOTIFICATIONS.md`
- `docs/CONTACT_SHARING.md`
- `docs/FEDERATION.md`
- `docs/CUSTOM_PROPERTIES.md`
- `SECURITY.md`
- `CODE_OF_CONDUCT.md`
- Selected analytics source anchors: `Explore.API/Controllers/AnalyticsRelayController.cs`, `Explore.Application/Features/PublicExperience/Handlers/Commands/RelayAnalyticsEventCommandHandler.cs`, `Explore.Application/Analytics/AnalyticsRuntimeProfileResolver.cs`, and `Explore.Infrastructure/Analytics/RuntimeAnalyticsProvider.cs`
- Repository file inventory for projects, tests, and documentation surfaces
- I-VSD skill resources for compliance checks, principles/domains, evidence levels, context discovery, report templates, and scholarly consultation boundaries

## Missing Evidence

- Repository-local Terms of Service, acceptable-use policy, privacy policy text, cookie policy, data-retention schedule, user export/deletion policy, pricing/refund/cancellation policy, and public moderation/appeals policy.
- Production audit logs, support logs, incident records, vulnerability response metrics, penetration test results, operational telemetry review, privacy impact assessment, and release-specific accessibility audit output.
- Maintainer evidence for repository settings listed as `Not yet verified` in `docs/CI_CD_GOVERNANCE.md`, including branch protection, environment protection, secret scanning, push protection, Dependabot security updates, dependency graph, and CodeQL alert enablement.
- Stakeholder validation from event attendees, organizers, tenant administrators, accessibility users, Arabic/RTL users, vulnerable groups, Islamic scholars, or community moderators.
- Qualified Islamic scholarly review for finance/riba exposure, religious-content governance, contested moderation, public religious claims, or future monetization/sponsorship models.
- External public pages linked from the repository, including the hosted privacy policy, were not fetched or evaluated in this report.

## Context Inventory

ISLAMU Event is documented as an open-source, self-hostable event discovery and management platform for communities, organizations, and platform operators. The repository distinguishes the purpose-agnostic software from the ISLAMU-hosted Islamic-focused instance. It supports event/session lifecycle flows, organization and membership management, lookup-driven filtering, multi-tenant runtime behavior, Blazor BFF authentication, runtime-selectable authorization, HAL/HATEOAS API affordances, modular event aspects, contact-sharing consent, in-app notifications, SMTP sending, localization, accessibility conventions, and federation foundations.

The technical architecture uses .NET 10, Clean Architecture, CQRS/MediatR, PostgreSQL/EF Core, Keycloak OIDC/OAuth2, Blazor Server/BFF, REST/HAL/OpenAPI, Docker Compose/Aspire, transactional outbox, OpenTelemetry/Serilog, and optional Cerbos policy authorization. Tenant isolation is primarily implemented through tenant resolution and EF Core filters, with row-level security documented as prototype support rather than production enforcement.

Important current boundaries are clearly documented: full ActivityPub/ATProto public interoperability is not implemented; email notification fanout is not implemented; some admin surfaces are mixed or backend-pending; accessibility artifacts are templates until release-specific evidence is filled in; repository governance settings still require external verification.

## Stakeholders

- Event attendees and registrants.
- Event organizers, organizations, and groups.
- Tenant administrators and organization/group administrators.
- Instance operators and maintainers.
- People whose contact information may be shared with approved organizations.
- Users affected by moderation, suspension, banning, tenant-local status, or public event visibility decisions.
- Self-hosting operators and downstream white-label tenants.
- Community members affected by Islamic-focused event classification, gender segregation metadata, madhab targeting, prayer-relative timing, and culturally sensitive discovery filters.
- Users requiring accessibility support, RTL layout, localization, and assistive technology compatibility.
- Non-users whose data, reputation, or community participation may be affected by public event content, exports, analytics, federation, or contact sharing.

## I-VSD Principles And Domains

The review maps evidence against these principles: Trust/Amanah, Truthfulness/Sidq, Justice/Adl, Non-Harm/La Darar, Rights of People, Avoiding Riba, Avoiding Gharar, Avoiding Deception, Promise-Keeping, Excellence/Ihsan, Modesty/Haya, and Avoiding Spying/Tajassus.

The provider-responsibility domains reviewed were Strategic, Design, Technical, Operational, Governance, and Evaluation.

## Findings

| Category | Level | Evidence-based assessment |
|---|---|---|
| Value source and claim boundaries | `Pass` | The repository generally distinguishes implemented features from planned ones and separates purpose-agnostic software from the ISLAMU-hosted Islamic-focused instance. Federation, email, accessibility, and admin docs frequently mark mixed or future work instead of overclaiming completion. This supports Truthfulness and Avoiding Deception. It does not certify the Islamic-focused instance or any public policy claim. |
| Data governance and purpose limitation | `Concern` | Tenant isolation, contact-sharing consent, secret ownership, API authorization, and custom-property exposure metadata support purpose limitation. Missing evidence remains for full data inventory, retention schedule, data subject export/deletion policy, privacy impact assessment, and production access review. |
| Privacy and avoiding spying | `Concern` | Strong positives include BFF token control, HAL-gated actions, tenant-scoped data, minimal SSE notification hints, PII-excluding operational metrics, permissions policy disabling camera/microphone/geolocation/payment, and explicit contact-sharing opt-in/withdrawal. The analytics relay and provider model introduce tracking risk that is partially mitigated by kill switches, consent profiles, cookieless modes, event-name/property restrictions, rate limiting, and Null provider fallback. Full compliance cannot be concluded without hosted privacy/cookie policy review, analytics data-flow evidence, retention evidence, and operator configuration evidence. |
| Security and resilience | `Pass` | Reviewed evidence supports strong implementation traceability: Keycloak OIDC/JWT validation, BFF session model, antiforgery for unsafe cookie-authenticated BFF endpoints, upload destination binding to reduce proxy/SSRF risk, fail-closed authorization, Cerbos/local provider boundaries, rate limiting, security headers, idempotency, health checks, backup/restore runbooks, secret-provider abstraction, AES-256-GCM encryption, CI supply-chain governance, pinned actions/base images, vulnerability audit policy, and responsible vulnerability reporting. Remaining validation gaps prevent certification: penetration testing, repository-settings proof, production audit evidence, and incident response metrics are missing. |
| AI or algorithmic behavior | `Not reviewed` | The operational docs mention an AI provider health surface and metrics redaction rules, but this review found no active AI product behavior sufficient to evaluate recommendations, religious guidance, ranking automation, or high-impact decisions. Any future AI-generated religious content, personalization, ranking, or moderation assistance requires a separate I-VSD review and likely scholarly/expert escalation. |
| Marketing and public claims | `Concern` | Repository docs are comparatively careful about current-versus-planned boundaries, including federation and accessibility artifacts. The README uses broad security/compliance-style positioning and links an external privacy policy that was not reviewed here. Public website copy, hosted legal pages, deployment claims, and screenshots would need review before making stronger claims. |
| Pricing, cancellation, renewals, and terms | `Not reviewed` | The reviewed repository does not contain an implemented pricing, billing, subscription, refund, cancellation, or renewal policy. Some extensibility/admin docs mention future tiered pricing, payment processing, or usage logs as configurable/business-model-dependent concepts. Because there is no complete present evidence, this category cannot pass. Future monetization, credit, late fees, sponsorship, investment, or payment terms require legal and qualified Islamic scholarly review for riba/gharar concerns. |
| Business model, funding, and partner incentives | `Concern` | AGPL licensing, self-hosting, Docker Compose deployment, backup/restore guidance, and federation foundations reduce lock-in risk. However, funding, sponsorship, investor influence, ad policy, partner data-sharing incentives, hosted-service commitments, and conflict-of-interest governance are not evidenced. |
| Moderation, appeals, and community protection | `Concern` | The product has tenant-local participation/moderation state, role boundaries, admin hierarchy, and a repository `CODE_OF_CONDUCT.md` with reporting and enforcement concepts. The evidence does not include a complete product moderation policy, appeals workflow, abuse-report handling, religious-content governance, moderator audit evidence, or community consultation. Because an Islamic-focused event platform may involve contested religious/community decisions, some cases require qualified scholarly and governance review. |
| Accessibility, localization, RTL, and cultural fit | `Concern` | Strong design traceability exists: WCAG 2.2 AA target, skip links, landmarks, heading rules, live regions, focus management, logical CSS properties, Arabic/RTL support, localization providers, and culturally aware event metadata. `docs/ACCESSIBILITY_ARTIFACTS.md` explicitly says release-specific evidence is still a template and browser scans are not automated, so this cannot be treated as a completed accessibility validation. |
| Portability, self-hosting, interoperability, and lock-in | `Pass` | The repository provides self-hosting docs, Docker Compose deployment, backup/restore/upgrade runbooks, open-source AGPL licensing, runtime provider choices, BYO Cerbos support, and federation groundwork. The docs truthfully mark full ActivityPub/ATProto gateway behavior as not implemented. Remaining gaps include complete tenant/user data portability tooling, production restore evidence, and full federation protocol validation. |
| Operations, support, vulnerability, incident, and EOL | `Concern` | There are strong operational runbooks for health, startup, backups, release/deployment evidence, secrets, and vulnerability reporting. Concerns remain because response-time commitments are undefined or not release-grade, incident records and support metrics are unavailable, EOL/lifecycle policy is missing, and repository settings/evidence are not fully verified. |
| Governance, accountability, admin powers, and sponsor influence | `Concern` | Admin hierarchy docs define instance/tenant/org/group boundaries, lock/delegation concepts, dangerous-operation recovery notes, and restrictions on instance-admin access to tenant business data. However, complete audit retention policy, production audit evidence, sponsor/partner influence policy, legal contributor gate, and religious escalation governance are incomplete or missing. |

No repository-evidence finding was classified as `Fail` in this pass. That does not mean the platform is fully compliant; it means the reviewed evidence showed many incomplete or unvalidated areas rather than a clearly evidenced contradiction severe enough to classify as fail.

## Recommendations

1. Create repository-local public policy documents for privacy, cookies/analytics, terms of service, acceptable use, moderation/appeals, data retention, user export/deletion, and pricing/refund/cancellation if the hosted service will charge users.
2. Add a data inventory that maps personal data categories, purposes, legal/ethical basis, retention, deletion behavior, export paths, admin access, third-party processors, and tenant boundaries.
3. Make analytics governance explicit in product-facing policy: default provider, cookieless/consent behavior, relay behavior, property allowlist, retention, third-party destination, opt-out/kill-switch behavior, and tenant override responsibilities.
4. Promote accessibility from design traceability to release validation by filling `docs/ACCESSIBILITY_ARTIFACTS.md` with dated automated and manual evidence, including keyboard-only, screen-reader, RTL, reduced-motion, and axe-core or equivalent scans.
5. Document moderation and appeals as a product policy, not only admin architecture: report intake, investigation rules, evidence retention, appeal path, conflict-of-interest handling, vulnerable-group protections, and escalation for religiously contested cases.
6. Add an Islamic-scholar escalation policy for finance/riba, religious-content claims, contested religious event classification, public religious guidance, and any future AI features that could shape religious decisions.
7. Verify and store evidence for repository and deployment controls listed as `Not yet verified` in `docs/CI_CD_GOVERNANCE.md`.
8. Define support, vulnerability, incident, and EOL expectations with response windows, ownership, evidence retention, and user notification duties.
9. Add sponsor/partner/funding governance before introducing ads, paid tiers, sponsorship placement, partner sync, or usage-based billing.
10. Keep public marketing copy aligned with the current implementation boundaries, especially for federation, accessibility, security, privacy, analytics, and compliance wording.

## Validation Gaps

- This report mostly validates design and implementation traceability from repository evidence. It does not validate production behavior.
- Operational validation would require logs, audits, deployment settings, incident records, support records, security settings exports, and release evidence.
- Stakeholder validation would require feedback from attendees, organizers, admins, accessibility users, RTL/Arabic users, self-hosters, and communities affected by moderation or Islamic-specific categorization.
- Security validation would require independent security review, penetration testing, dependency/vulnerability evidence, secret-scanning proof, and production configuration review.
- Accessibility validation would require release-specific automated and manual test results.
- Legal/privacy validation would require counsel review and complete public policies.
- Islamic validation would require qualified scholarly review for triggered religious-legal domains.

## Escalation Needed

- `Requires scholarly review`: any claim that the product, hosted instance, pricing model, sponsorship model, payment terms, event classification rules, moderation decisions, or religious-content features are halal, haram, Sharia-compliant, or Islamically certified.
- `Requires legal/privacy review`: terms, privacy policy, cookies/analytics, data retention, deletion/export rights, accessibility statement, vulnerability policy, incident notification, and hosted-service billing/refund/cancellation commitments.
- `Requires security expert review`: production security posture, secret management, repository security settings, penetration testing, incident response, and deployment hardening.
- `Requires accessibility expert/user review`: release conformance, assistive technology compatibility, manual keyboard/screen-reader flows, RTL/localization experience, and known limitations.
- `Requires community governance review`: moderation/appeals, abuse handling, community safety, religiously contested event inclusion, and operator/admin power boundaries.
