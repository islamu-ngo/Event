---
description: "Prepare local configuration with the offline Setup Assistant and keep restricted artifacts on protected native hosts."
---

# Setup Assistant

The Setup Assistant CLI is a non-interactive tool for inspecting the public
configuration catalogue, validating or rendering dotenv input, checking legal
drafts, and producing deterministic machine-readable diagnostics. Use the
Terminal UI for interactive human workflows.

The CLI does not store credentials, connect to a live instance, or replace the
instance administration surface.

Run `event-setup` or `event-setup --help` to list the available commands; neither
starts the interactive Terminal UI.

## Choose a release target

| Host | Runtime identifier | Release executable | File export |
|---|---|---|---|
| Linux x64 | `linux-x64` | `event-setup` | Protected local filesystem required |
| Linux ARM64 | `linux-arm64` | `event-setup` | Requires Linux host verification |
| macOS Apple Silicon | `osx-arm64` | `event-setup` | Disabled pending native protection evidence |
| Windows x64 | `win-x64` | `event-setup.exe` | Disabled pending native protection evidence |

Each target is self-contained and published as one executable. The host does
not need a separately installed .NET runtime.

Windows and macOS packages can inspect public catalogue data and validate
local input, but cannot save files. The Terminal's protected Save is
unavailable on those hosts. Neither package currently promises protected
dotenv or identity export. The browser provides a smaller public-only
workspace. The separate desktop release currently supports Linux x64 only.

## Publish from source

Run the release project from the repository root. Keep build temporary files
on a filesystem with enough space:

```bash
export TMPDIR="$HOME/.cache/agent-tmp"
dotnet msbuild eng/setup-assistant/SetupAssistant.Release.proj \
  -target:PublishSetupAssistant \
  -property:SetupAssistantRid=linux-x64 \
  -property:SetupAssistantOutputRoot="$PWD/artifacts/setup-assistant"
```

Replace `linux-x64` with one of the runtime identifiers in the table. The
executable is written under `artifacts/setup-assistant/<runtime-identifier>/`.

On Linux or macOS, install it into a directory on `PATH`:

```bash
chmod 0755 artifacts/setup-assistant/linux-x64/event-setup
install -m 0755 artifacts/setup-assistant/linux-x64/event-setup "$HOME/.local/bin/event-setup"
event-setup doctor --machine
```

## Interactive terminal

Publish the Terminal.Gui application separately:

```bash
dotnet msbuild eng/setup-assistant/SetupAssistant.Release.proj \
  -target:PublishSetupAssistant \
  -property:SetupAssistantSurface=terminal \
  -property:SetupAssistantRid=linux-x64 \
  -property:SetupAssistantOutputRoot="$PWD/artifacts/setup-assistant"
artifacts/setup-assistant/terminal/linux-x64/event-setup-terminal
```

Run it in a real terminal at least 80 columns by 17 rows. Redirected input
or output is rejected. Navigation separates environment editing, the
catalogue, configuration manifests, tenant packages, legal previews and
operator-identity drafts. Changing a form invalidates its prepared output;
changing workspace clears private inputs and previews and cancels a pending
environment write before publication. Identity input is masked; clipboard,
context-menu and undo/redo history commands are disabled. Native exports use
the protected, create-only file rules above. Exported drafts do not attest
identity or apply anything to an instance.

## Native desktop workspace

Publish the independent Avalonia application for its verified host:

```bash
dotnet msbuild eng/setup-assistant/SetupAssistant.Release.proj \
  -target:PublishSetupAssistant \
  -property:SetupAssistantSurface=desktop \
  -property:SetupAssistantRid=linux-x64 \
  -property:SetupAssistantOutputRoot="$PWD/artifacts/setup-assistant"
```

Run `artifacts/setup-assistant/desktop/linux-x64/event-setup-desktop` from a
private, owner-controlled working directory in an X11 or XWayland graphical
session. Files are created in that launch directory and are never overwritten.
The desktop release target refuses other RIDs until their native runtime and
protected-output behavior have been verified.

The three tabs prepare a standalone SQLite environment template, a local
configuration manifest, or an operator-identity draft. Environment placeholders
still require the operator's selected secret authority; preparation is not
deployment readiness. Identity input is bounded to the shared 64 KiB UTF-8
contract and is not copied into shared observable state.

Prepare or validate before Save. Editing inputs invalidates prepared output;
every save attempt consumes the preparation, including permission failures.
Use **Clear private input** to clear an identity draft. Status text exposes
codes and digests, not private content. The right-to-left layout toggle changes
layout direction; it does not claim complete Arabic translation or
assistive-technology certification.

The desktop does not discover, authenticate to, or apply anything to an Event
instance. Exported identities are local drafts, not verified identity assertions.

## Public browser workspace

Publish the independent static Blazor application:

```bash
dotnet msbuild eng/setup-assistant/SetupAssistant.Release.proj \
  -target:PublishSetupAssistantBrowser \
  -property:SetupAssistantOutputRoot="$PWD/artifacts/setup-assistant"
```

Serve `artifacts/setup-assistant/browser/wwwroot/` from one static origin.
Serve WebAssembly files with `application/wasm`, preserve the `_framework/`
directory, and route application paths such as `/manifest` to `index.html`.
The initial load requires the static host. There is no service worker and
no promise of relaunching without network access.

The catalogue contains public Core definitions only. The manifest page
accepts the bounded `event-setup-public-manifest/v1` template format:
`schema`, `kind`, `name`, `topology`, `capabilities` and `providers`.
It is not the native configuration-portability or identity format.
Unknown fields and kinds, identity fields, malformed input and files above
64 KiB are rejected without a preview or download. A new selection clears
the previous result; a failed or superseded read cannot restore it.

Processing stays in browser memory after static boot. There is no application
API, login, instance address, secret input, persistent storage or telemetry.
Use a native target for restricted files. Browser validation never applies
configuration or establishes platform authorization.

## Use the implemented command grammar

Command families are bare first arguments. Do not prefix them with `setup`.
For example, `event-setup catalogue ...` is valid; `setup catalogue ...` is
not an executable command.

Human help and command results use Spectre.Console. Request help for a
family or operation without supplying input files, output paths or revisions:
`event-setup catalogue --help` or `event-setup manifest validate --help`.
Machine mode remains a separate JSON contract rather than formatted text.

Commands that produce an artifact require an explicit output destination.
Use `--output -` to write public catalogue data to standard output. To try the
three common commands from a terminal:

```bash
event-setup catalogue list --output -
event-setup catalogue show --key API_HTTP_PORT --output -
event-setup manifest create --output instance-manifest.json
```

The manifest command creates a new file and rejects an existing destination.
Use `--dry-run` to preview a write without producing the file.

Only public catalogue output may use `--output -`. Environment files,
configuration manifests, tenant packages and operator identity outputs
require a protected native file, even when a particular template contains
no secret. Redirecting restricted output to stdout fails with exit `74`
without emitting artifact bytes.

On Linux, choose a local directory owned by your account or root, with no
group/other write access and no symlink components. The writer creates
owner-only (`0600`) files atomically and never overwrites an existing file.
Choose a fresh destination for each export; move or retire the previous
file yourself after review. Cancellation discards private staging without
leaving temporary files. Anonymous-file and hard-link support and mounted
procfs are required; unsupported filesystems fail rather than fall back to
an unprotected write. This protects against other ordinary local accounts,
not root, elevated administrators, or processes running as your own account.

Use machine mode for scripts and CI:

```bash
event-setup catalogue list --machine --dry-run
event-setup env validate --input deployment.env --machine
event-setup env render --topology standalone --provider sqlite --output deployment.env --machine
event-setup manifest validate --input instance-manifest.json --machine
event-setup tenant-package validate --input tenant-package.json --machine
event-setup legal validate --input legal-draft.json --machine
event-setup doctor --machine
```

Machine mode emits one newline-terminated `event-setup-command/v1` JSON object.
Treat `exitCode` as authoritative and retain only value-safe diagnostics,
coverage, readiness, and artifact metadata.

### Operator identity portability

Export an operator-identity document as a digest-bound JSON or YAML artifact:

```bash
event-setup portability export-operator-identity \
  --input operator-identity.json \
  --output operator-identity.yaml \
  --format yaml \
  --machine
```

Prepare an import request against the exact current target revision:

```bash
event-setup portability import-operator-identity \
  --input operator-identity.yaml \
  --output operator-identity-import.json \
  --expected-revision "$EXPECTED_REVISION_SHA256" \
  --machine
```

Use `--expected-revision absent` only when the target has no current identity
document. Export and import outputs are sensitive. A successful command proves
offline format and digest integrity only; it does not prove server readiness,
authorization, or apply permission.

## Configuration import sessions on a live instance

Live configuration import is an established server-controlled workflow,
separate from the offline CLI:

1. An authorized administrator uploads one bounded configuration artifact.
   The server protects the artifact and creates a short-lived import session.
2. The administrator requests a preview. The preview is bound to the exact
   artifact digest, target revision, selected sections, mappings, apply mode,
   required approvals, and expiry.
3. The client displays the preview and follows only the actions present in the
   server-authored HAL links.
4. Apply consumes the matching preview once. A stale revision, changed
   selection, expired or cancelled session, wrong target, invalid session
   token, or replay fails closed.

The authenticated Setup Live API exposes this flow at:

- `POST /api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/configuration-import/sessions`
  to create a protected session;
- `POST .../sessions/{sessionId}/preview` to generate the bound preview;
- `POST .../sessions/{sessionId}/apply` to consume an apply-ready preview.

Use the enrollment capability returned by the server. Preview and apply also
require the session access token. Follow the preview link from the created
resource and the apply link only when the preview response advertises it; do
not construct or enable the apply action locally. An enrollment-bound session
cannot be applied through the ordinary tenant import route, even by a tenant
administrator who has its session token.

## Security rules

- Never place passwords, tokens, credentials, private keys, API keys, or
  connection strings in command arguments, file names, environment names,
  logs, or retained command output.
- Review generated files before deployment. `--dry-run` reports planned work
  but does not write the output artifact.
- Keep configuration artifacts separate from runtime secret authorities.
- Protect operator-identity artifacts because they contain public legal and
  contact identity data even though they contain no credentials.
- Gate live preview and apply actions by server-issued HAL links, not local
  roles or claims.
- Back up affected stores and test imports in staging before applying reviewed
  configuration to a production instance.

## Related documentation

- [Operator identity export and import](operator-identity-portability.md)
- [Configuration Manifests](../documentation/readme/configuration-and-operations/configuration-manifests.md)
- [Environment Variables](../documentation/readme/configuration-and-operations/environment-variables.md)
- [Secrets](../documentation/readme/configuration-and-operations/secrets.md)
- [Backup, Restore & Upgrade](../documentation/readme/configuration-and-operations/backup-restore-upgrade.md)
