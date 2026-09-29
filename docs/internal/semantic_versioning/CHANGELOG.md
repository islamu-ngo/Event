ABOUTME: Semantic version index for implemented and planned platform versions.
ABOUTME: v0.1.0 implemented scope mirrors API WORK ITEMS.md and v1.0.0 planned scope mirrors API WORK ITEMS TODO.md.

# Changelog — ISLAMU Event Platform

> **Frozen pre-automation planning/history:** This file predates the governed
> release engine. It is preserved as planning and historical classification only,
> not generated release history, not governed release evidence, and not proof that
> any SemVer tag or public version has been published.

> All notable changes are documented in version files.
> Future governed releases will follow [Semantic Versioning 2.0.0](https://semver.org/)
> after explicit steward approval and signed-tag activation.

**Last Updated:** 2026-08-28

---

## [Unreleased]

Planning/history notes for features merged to `develop` branch, not yet released:

- **Outbox pattern** — transactional outbox with `OutboxProcessor` background service, exponential backoff retry, dead-letter queue, optimistic concurrency. Specialized variants: `PdsSyncOutbox`, `PolicyChangeOutbox`.
- **Footer management** — tenant-configurable footer with link groups, social links, 4 templates (standard-3-col, standard-2-col, minimal, community), instance governance locking. 11 API endpoints + admin UI.
- **Design system** — CSS `@layer` architecture (6 layers), 3-tier design token system, MudBlazor wrapper components (AppButton, AppCard, AppTextField, AppIconButton, AppDialogShell), `DialogOptionsFactory` presets, MudBlazor override whitelist policy.
- **Accessibility services** — `IAccessibilityAnnouncerService` (ARIA live regions), `IAccessibilityFocusService` (focus management with save/restore), JS interop module, `MainLayout` page shell with skip-link and landmarks, architecture convention tests (8 tests).
- **Secrets library** — `Explore.Secrets` multi-provider secret management (Environment, Infisical; Vault/Azure/AWS planned). Background refresh with exponential backoff, AES-256-GCM encryption, health checks, Prometheus metrics.
- **Actor appearance** — BackgroundColor, BackgroundEffect, BannerColor, BannerPictureId, BackgroundImageId fields. `AppearanceStyleBuilder` for inline CSS generation with overlay effects.
- **Analytics relay rate limiting** — dedicated `AnalyticsRelay` rate limit policy for `POST /api/a/t`.
- **Authorization parity tests** — architecture tests ensuring resource kinds map to Cerbos policies with fallback cases.
- **EF Core persistence hardening** — provider-neutral repository boundaries, metadata-owned physical identifiers, isolated lock/queue primitives, and generated initial migration histories for PostgreSQL, SQLite, SQL Server, MariaDB, and MySQL. This pre-v1 rebaseline requires recreation of databases using the removed development application histories; Data Protection and retained privacy-erasure authority histories remain independent. No public API contract changed.

---

## Version History

| Version | Title | Released | Status |
|---------|-------|----------|--------|
| [v0.1.0](v0.1.0.md) | First Public Release (Beta) | TBD | Frozen planning/history |
| [v1.0.0](v1.0.0.md) | Planned Stable Release | TBD | Frozen planning/history |

---

## Versioning Policy

### Current Phase: Frozen Pre-Automation Planning

- The repository has no governed release tag yet.
- **Implemented planning baseline for v0.1.0** is tracked in `v0.1.0.md` under **Implemented (v0.1.0)**.
- **Planned scope for v1.0.0** is tracked in `v1.0.0.md` under **Planned (v1.0.0)**.

### Progression to 1.0.0

The first governed version will be selected only after explicit Project Steward approval,
signed baseline activation, and the governed release runbook. Do not reclassify this
frozen planning file into generated release history.

---

## Feature Checklist Mapping

### ✅ Implemented in v0.1.0

- Source of truth: [v0.1.0.md](v0.1.0.md)
- Implemented section mirrors: `API WORK ITEMS.md`

### ⏳ Planned for v1.0.0

- Source of truth: [v1.0.0.md](v1.0.0.md)
- Planned section mirrors: `API WORK ITEMS TODO.md`

---

## Notes

- All currently implemented baseline work remains historically classified under the frozen `v0.1.0` planning note.
- All enterprise-next backlog items remain historically classified under the frozen planned `v1.0.0` note.
