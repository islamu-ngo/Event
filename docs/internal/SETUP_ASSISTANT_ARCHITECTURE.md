# Setup Assistant Architecture

> **Audience:** Contributors | Operators | AI agents
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-09-29
> **Source Anchors:** `src/Event.SetupAssistant.Cli/`, `src/Event.Setup.Core/`, `src/Event.SetupAssistant.Terminal/`, `src/Event.SetupAssistant/SetupLive/`, `src/Explore.Application/Features/ConfigurationManifest/Importing/`, `eng/setup-assistant/SetupAssistant.Release.proj`, `.agents/skills/setup-assistant-cli/SKILL.md`, `tests/Event.Architecture.Tests/SetupAssistantReleaseTests.cs`

The Setup Assistant separates deterministic offline configuration work,
interactive operator presentation, and authenticated live-instance operations.
The release artifact in this phase is the non-interactive CLI only.

## Component boundaries

| Component | Responsibility | Outward dependencies |
|---|---|---|
| `Event.Setup.Core` | Bounded dotenv, composition, portability, readiness, and legal-document logic | BCL plus the approved syntax-only YAML dependency |
| `Event.SetupAssistant.Cli` | Deterministic command parsing, explicit I/O, machine JSON, and exit codes | `Event.Setup.Core` |
| `Event.SetupAssistant.Terminal` | Interactive operator workflow and protected secret entry | Shared presentation and Core |
| `Event.SetupAssistant` | Framework-neutral presentation state | Core |
| `Event.SetupAssistant.SetupLive` | Ephemeral transport adapter for server-issued live-control affordances | Generated client and Core contracts |
| Configuration import application services | Protected upload, preview binding, validation, atomic apply, and rollback evidence | Domain and repository contracts |

The machine CLI does not reference the terminal, shared MVVM presentation,
browser, desktop, persistence, live transport, telemetry, or hosting
frameworks. That inward-only graph keeps the standalone closure small and makes
headless automation independent from interactive UI dependencies.

## Executable command contract

`SetupCliParser` owns the executable grammar. Families are bare first
arguments:

- `catalogue`
- `manifest`
- `tenant-package`
- `portability`
- `env`
- `legal`
- `doctor`

There is no `setup` command family. Automation uses
`--machine`, which emits exactly one newline-terminated
`event-setup-command/v1` JSON object. `SetupCliCommandSchemaMetadata` generates
the checked schema at `schemas/event-setup-command-v1.schema.json`; the schema
generator, CLI tests, agent skill, and operator documentation must converge on
that compiled command metadata rather than plan prose.

The `portability` family owns
`export-operator-identity` and `import-operator-identity`. Export accepts an
authoritative operator-identity JSON document and emits digest-bound JSON or
YAML. Import verifies the artifact and emits a server request bound to an exact
lowercase SHA-256 revision or the explicit `absent` sentinel. Both outputs are
classified sensitive, and machine readiness remains `incomplete` with
`server-validation-required`; offline integrity never grants server mutation
authority.

## Standalone release model

`eng/setup-assistant/SetupAssistant.Release.proj` is the single release
configuration for:

| Runtime identifier | Platform |
|---|---|
| `linux-x64` | Linux x64 |
| `linux-arm64` | Linux ARM64 |
| `osx-arm64` | macOS Apple Silicon |
| `win-x64` | Windows x64 |

The project invokes the CLI project with these fixed properties:

- `Configuration=Release`
- `SelfContained=true`
- `PublishSingleFile=true`
- `PublishTrimmed=false`
- `EnableCompressionInSingleFile=true`
- `IncludeNativeLibrariesForSelfExtract=true`
- `UseAppHost=true`
- `DebugSymbols=false`

Trimming remains disabled because command serialization and reflection
contracts require their own explicit trimming evidence. The release process
renames the final apphost to `event-setup` (`event-setup.exe` on Windows)
without changing the managed assembly identity or propagating an assembly-name
override into project references.

`ValidateSetupAssistantReleaseMatrix` evaluates all four runtime identifiers
without publishing them. `PublishSetupAssistant` restores and publishes one
selected RID beneath the supplied output root. `PublishAllSetupAssistantRids`
is the release-lane target when every runtime pack is available.

## Live configuration import API

Phase 4 exposes the established staged workflow through the authenticated
Setup Live API. Phase 6 packages the offline CLI but does not duplicate that
HTTP authority.

### Session state

`ConfigurationImportSession` owns the server-side state machine:

1. `Uploaded` — a protected bounded artifact, target authority, lowercase
   SHA-256 digest, byte length, expiry, and access-token digest are bound to a
   new session.
2. `PreviewReady` — the preview binds the exact artifact digest, target
   revision digest, selected-section digest, mapping digest, apply mode,
   required-approval digest, and preview expiry.
3. `Consumed` — an apply operation presented the same target, token digest,
   and preview binding and consumed the session once.
4. `Cancelled` or `Expired` — no subsequent preview or apply operation is
   admitted.

Default session lifetime is 30 minutes and the maximum is one hour. Artifact
expiry cannot precede session expiry. Access-token digest comparison uses
fixed-time comparison. Wrong target, invalid token, stale preview, expiry,
cancellation, and replay fail closed.

SetupLive-created sessions persist a distinct 64-character session authority
key derived from the tenant, enrollment, current generation, and actor. The
tenant mutation lock still uses its canonical tenant key. Ordinary tenant
preview/apply routes cannot match the enrolled session key, even with its
access token; the SetupLive bridge rechecks enrollment authority under its
generation fence before using the bound key. This uses the existing
`TargetAuthorityKey` column and requires no migration.

### HAL and transaction boundary

The live client follows server-authored HAL relations and methods. The presence
of a relation is the action affordance; local role or claim inspection is not
an authorization substitute. The preview is not authorization by itself.
Apply must revalidate the bound preview and current target revision inside the
server-owned mutation boundary before committing an atomic result.

`SetupConfigurationImportsController` now owns three authenticated operations
under
`/api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/configuration-import/sessions`:

- `POST` the collection to create a protected import session;
- `POST {sessionId}/preview` to bind and return the preview;
- `POST {sessionId}/apply` to consume an apply-ready preview.

The enrollment capability header is required throughout. Preview and apply also
require the import access-token header. The created resource advertises the
preview relation; an apply relation is emitted only when the preview result is
apply-ready. Clients follow those server-authored HAL links rather than
constructing mutation authority locally.

## Agent skill boundary

`.agents/skills/setup-assistant-cli/SKILL.md` is an operational router for the
packaged CLI. Its frontmatter follows `_SKILL_SCHEMA.md`; its bash examples use
`event-setup` plus parser-owned families. It must never:

- route to the interactive TUI or browser setup;
- claim that offline CLI validation grants live-instance authority;
- place secrets in arguments, paths, environment names, or retained output;
- translate planning shorthand into nonexistent commands;
- infer API routes from application-layer import classes.

## Verification contract

`SetupAssistantReleaseTests` provides the executable release guard:

- exact four-RID matrix and standalone publish properties through the
  evaluated `WriteSetupAssistantReleaseContract` MSBuild target;
- real host-RID publish with no managed DLL sidecars and no UI or telemetry
  assemblies in the release directory.

Skill metadata is intentionally outside product tests. Validate its structured
frontmatter and header policy explicitly with:

```bash
dotnet run --file eng/setup-assistant/ValidateSetupAssistantSkill.cs -- \
  .agents/skills/setup-assistant-cli/SKILL.md
```

Phase verification also runs the release matrix evaluation, publishes and
executes the host artifact, and inspects each non-host RID through MSBuild
property evaluation. Cross-platform execution is not claimed from a different
host.

## Related documentation

- [Public Setup Assistant guide](../public/self-hosting/setup-assistant.md)
- [Configuration architecture](CONFIGURATION.md)
- [Operations](OPERATIONS.md)
- [Testing](TESTING.md)
