# Setup Assistant Architecture

> **Audience:** Contributors | Operators | AI agents
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-09-30
> **Source Anchors:** `src/Event.SetupAssistant.Cli/`, `src/Event.Setup.Core/`, `src/Event.SetupAssistant.Terminal/`, `src/Event.SetupAssistant/SetupLive/`, `src/Explore.Application/Features/ConfigurationManifest/Importing/`, `eng/setup-assistant/SetupAssistant.Release.proj`, `.agents/skills/setup-assistant-cli/SKILL.md`, `tests/Event.Architecture.Tests/SetupAssistantReleaseTests.cs`

The Setup Assistant targets perform local configuration work only. Core
validation, presentation and protected native output are separate boundaries.
The existing SetupLive adapter and platform import services remain separate
backend capabilities; none is referenced by the offline product targets.

## Component boundaries

| Component | Responsibility | Outward dependencies |
|---|---|---|
| `Event.Setup.Core` | Bounded dotenv, composition, portability, readiness, and legal-document logic | BCL plus the approved syntax-only YAML dependency |
| `Event.Setup.Artifacts` | Classified public output and native create-only protected files | Core and OS APIs; no UI or network client |
| `Event.SetupAssistant.Cli` | Deterministic command parsing, explicit I/O, machine JSON, and exit codes | Core and Artifacts |
| `Event.SetupAssistant.Terminal` | Interactive operator workflow and protected secret entry | Shared presentation, Core and Artifacts |
| `Event.SetupAssistant.Desktop` | Disabled contract shell; no separate writer implementation | Shared presentation and Artifacts |
| `Event.SetupAssistant` | Framework-neutral presentation state | Core |
| `Event.SetupAssistant.SetupLive` | Ephemeral transport adapter for server-issued live-control affordances | Generated client and Core contracts |
| Configuration import application services | Protected upload, preview binding, validation, atomic apply, and rollback evidence | Domain and repository contracts |

The machine CLI does not reference the terminal, shared MVVM presentation,
browser, desktop, persistence, live transport, telemetry, or hosting
frameworks. That inward-only graph keeps the standalone closure small and makes
headless automation independent from interactive UI dependencies.

## Classified protected artifacts

`SetupArtifactPolicy` in Core owns a closed kind-to-sensitivity mapping.
Only `PublicCatalogue` and `PublicTemplate` permit a public byte projection;
environment, configuration, operator identity, unknown and undefined kinds
are restricted. The classifier does not inspect arbitrary payloads to guess
their safety. Trusted producers select kinds; input-derived configuration
must never be relabelled as a public template.

CLI artifact producers call the classified `ISetupCliWriter.WriteArtifact`
port. Restricted `--output -` fails before the first byte. Machine envelopes
and human status are separate, value-free output; sensitive artifacts are
represented only by metadata, never embedded payloads. The existing wire
spelling `sensitive` maps to Core's restricted classification.

All native file writes use `ProtectedArtifactWriter`. Linux prepares an
anonymous `O_TMPFILE` inode in the selected directory with verified current
UID and exact `0600` mode before staging bytes. Descriptor-relative
`openat` traversal refuses symlinks and untrusted/writable directory chains
(root-owned sticky ancestors such as `/tmp` are allowed, not final
directories). After bounded write and flush, commit reopens and validates
the directory chain, compares its device/inode identity, and uses `linkat`
from the open descriptor through `/proc/self/fd` for atomic no-replace
publication. `statx` verifies the installed inode, owner, mode and length.
There is no named staging file to replace and no backup sidecar.

An existing file is never overwritten, including the disabled Desktop
shell's former overwrite option. Competing preparations have one winner;
the loser cannot remove the winner. Cancellation or disposal closes the
anonymous inode, leaving the prior output intact. Results are closed status
codes, not OS exception messages or paths. Root, elevated administrators,
and processes acting as the same OS account are outside the ordinary
other-account confidentiality promise. Filesystems without anonymous-file
and hard-link support, unavailable procfs, or unprovable permissions fail
closed without a pathname-based fallback.

Windows and macOS restricted Save are **disabled**. Windows must eventually
prove a protected, non-inherited DACL established before staging, limited
to the current user and justified OS principals, plus reparse, race and
cleanup invariants. macOS requires its own native host evidence; Linux
mode tests do not prove either platform. The host CI jobs currently test
refusal and public output only; they are not permission-support evidence.
Browser and Desktop activation remain separate work.

The shared adapter's implementation is repository-native. Its externally
constrained elements are only the Linux `openat`, `open` (`O_TMPFILE`),
`statx`, `fchmod`, `geteuid` and `linkat` ABI identifiers and semantics
(Linux man-pages API references: `man7.org/linux/man-pages/man2/`).
No third-party implementation, snippet, package or source-derived structure
was incorporated. The anonymous-inode/create-only design was selected over
pathname snapshot-and-replace because a path comparison cannot make the
subsequent overwrite atomic against a competing writer.

## Offline presentation boundaries

Terminal navigation owns separate environment, catalogue, manifest, tenant
package, legal and identity draft views. Core remains the authority for
composition, portability and legal substitution. Input changes invalidate
prepared bytes; navigation clears restricted inputs and outputs. Status
labels carry outcomes and approved metadata, not identity content.
`SetupAssistantSurface=terminal` selects its independent apphost and
`terminal/<rid>/` publish directory. It never falls back to the CLI.

## Executable command contract

`SetupCliCommandRegistry` owns one Spectre.Console.Cli command graph.
Typed settings provide both binding and reflected schema option metadata;
the handwritten parser is removed. Families are bare first arguments:

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

Bounded argument preflight rejects hostile values before framework
diagnostics can echo them. Help does not require operational files,
destinations or revision values, but still observes the confidentiality
boundary. Human help and results use Spectre.Console without interactive
prompts; machine serialization bypasses rich rendering entirely. The
executable reads environment names only. The approved runtime package
closure is Spectre.Console 0.57.2, Spectre.Console.Ansi 0.57.2,
Spectre.Console.Cli 0.56.1, and Core's existing YamlDotNet dependency.

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

| Runtime identifier | Platform | Protected file output |
|---|---|---|
| `linux-x64` | Linux x64 | Linux native writer; filesystem protections required |
| `linux-arm64` | Linux ARM64 | Same fixed Linux ABI; host verification required |
| `osx-arm64` | macOS Apple Silicon | Disabled; no restricted-save package claim |
| `win-x64` | Windows x64 | Disabled; no restricted-save package claim |

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

Phase 7 additionally runs `SetupArtifactSensitivityTests`, executable
`SetupCliProgramTests`, the real-OS `Event.Setup.Artifacts.Tests` project,
and migrated Terminal/Desktop integration checks. `_build-test.yml`
includes the shared artifact project in the Linux setup gate and separate
Windows/macOS refusal gates. Returning early from Linux-only cases on
another host must not be reported as proof of native write protection.

## Related documentation

- [Public Setup Assistant guide](../public/self-hosting/setup-assistant.md)
- [Configuration architecture](CONFIGURATION.md)
- [Operations](OPERATIONS.md)
- [Testing](TESTING.md)
