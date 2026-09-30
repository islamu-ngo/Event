# ADR-035: Offline Setup Assistant Artifact Boundaries

> **Status:** Accepted
> **Date:** 2026-09-30
> **Scope:** Setup Core, native artifact output and the four operator surfaces

## Context

The approved Setup Assistant workstream prepares local configuration and
identity drafts without connecting to an Event instance. CLI, terminal,
browser and desktop have different output capabilities, but must not define
conflicting confidentiality policy or imply platform authorization.

## Decision

- Keep deterministic parsing, composition and artifact classification in
  `Event.Setup.Core`. Native filesystem capabilities belong to the
  UI-independent `Event.Setup.Artifacts` adapter.
- Treat unknown or undefined artifact kinds as restricted. Only closed
  public catalogue/template projections may reach public output.
- Use one native protected writer for CLI, terminal and desktop. Publication
  is create-only, and no permission or target failure permits stdout or
  ordinary file output as a fallback.
- Ship a smaller standalone browser with a closed public-template format,
  no application client, private identity input, persistent storage or
  service worker. Release selected upload files after processing and prevent
  stale reads from restoring an earlier export.
- Keep prepared native bytes private and single-use. Input changes and
  workspace generations invalidate stale preparations and completions.
- Preserve existing SetupLive/backend implementations as separate services.
  Offline targets neither reference them nor treat local validation as live
  application, authoritative readiness or verified identity.
- Advertise only demonstrated host behavior. Linux x64 native protection is
  verified; Windows/macOS export remains disabled. The desktop package
  currently admits only the host-proven `linux-x64` RID.

## Consequences

Restricted stdout exports and implicit native overwrites are intentionally
removed during greenfield development. Browser files are not interchangeable
with private native portability manifests. Each target has an independent
package and dependency boundary, including test-only UI harness exclusion.

Cross-publication, schema metadata and another surface's test results do not
establish runtime or confidentiality guarantees. Release gates include actual
native/static behavior, generated-contract checks, dependency policy and
target-specific tests. New OS support requires its own permission evidence.

## Evidence

- [Setup architecture](../SETUP_ASSISTANT_ARCHITECTURE.md)
- [Operator guide](../../public/self-hosting/setup-assistant.md)
- [Dependency record](../legal/dependencies/setup-assistant-ui.md)
- `tests/Event.Setup.Artifacts.Tests/ProtectedArtifactWriterTests.cs`
- `tests/Event.SetupAssistant.Browser.Tests/BrowserManifestReadLifecycleTests.cs`
- `tests/Event.SetupAssistant.Desktop.Tests/DesktopApplicationTests.cs`
- `tests/Event.Architecture.Tests/SetupAssistantReleaseTests.cs`
