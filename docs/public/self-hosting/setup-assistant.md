---
description: "Publish and operate the standalone Setup Assistant CLI without exposing secrets or bypassing server-issued configuration-import affordances."
---

# Setup Assistant

The Setup Assistant CLI is a non-interactive tool for inspecting the public
configuration catalogue, validating or rendering dotenv input, checking legal
drafts, and producing deterministic machine-readable diagnostics. Use the
Terminal UI for interactive human workflows.

The CLI does not store credentials, connect to a live instance, or replace the
instance administration surface.

## Choose a release target

| Host | Runtime identifier | Release executable |
|---|---|---|
| Linux x64 | `linux-x64` | `event-setup` |
| Linux ARM64 | `linux-arm64` | `event-setup` |
| macOS Apple Silicon | `osx-arm64` | `event-setup` |
| Windows x64 | `win-x64` | `event-setup.exe` |

Each target is self-contained and published as one executable. The host does
not need a separately installed .NET runtime.

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

## Use the implemented command grammar

Command families are bare first arguments. Do not prefix them with `setup`.
For example, `event-setup catalogue ...` is valid; `setup catalogue ...` is
not an executable command.

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
