# Blazor UI Development Workflow

> **Category:** How-to (Diataxis)
> **Audience:** AI agents and developers modifying Blazor components, CSS, or MudBlazor layouts that require visual verification.
> **Last Updated:** 2026-09-26

When making Blazor UI / CSS changes that need visual verification, use the
AppHost session that you started and wait for its actual readiness signals. Do
not terminate unrelated `dotnet` processes or use a fixed delay as a readiness
substitute.

## 1. Safe AppHost Cycle

```bash
# Build before starting a new AppHost session.
dotnet build --configuration Release --verbosity quiet

# Start the selected AppHost profile in a terminal owned by this work.
dotnet run --project src/Explore.AppHost/Explore.AppHost.csproj --launch-profile local-default
```

Before navigating, subscribe to the AppHost resource-state/readiness events and
wait for the resources required by the selected profile. The AppHost already
models migration completion and API readiness before the BFF starts; observe
those transitions instead of sleeping. If a session must be restarted, send
`Ctrl+C` only to the terminal that owns that AppHost and wait for its children to
exit. Never use a machine-wide `stop-all-dotnet` command.

## 2. Local-Agent Browser Authentication

When the fixed local-agent ports belong to another workstream, use
`bash eng/scripts/run-local-agent.sh --isolated`. The same approved vault import
is retained; Aspire assigns isolated resource ports and user-secrets state.
Discover the endpoints for this worktree's AppHost and wait for its resources
and manifest readiness. Do not terminate the other host or assume port 5200
serves the current checkout. `--no-build` is optional and requires the matching
startup build to be current; otherwise let Aspire build the selected host.
Aspire isolation does not rename explicitly configured persistent volumes. Use
a task-private container-provider storage namespace when another workstream
owns the profile's named volumes; never reset those shared volumes to obtain a
fresh database.

Use this protocol only for the isolated Development `local-agent` profile. It
is Split topology with real BFF-to-API forwarding, not a Keycloak/Cerbos or
provider-parity proof.

### Launch and readiness

The selected secret authority must supply infrastructure credentials and Local
signing authority on every launch. Initial provisioning also requires the two
persona initialization passwords. Never print their values.

| Name | Role |
|---|---|
| `AGENT_BROWSER_SEED_ENABLED` | Non-secret opt-in; the profile sets it to `true`. |
| `AGENT_BROWSER_PERSONA_PASSWORD` | Final persona password; authoritative secret key `authentication.local.agent_browser_persona_password`. |
| `INSTANCE_BOOTSTRAP_LOCAL_PASSWORD` | Initial configured-administrator credential only. |
| `AUTHENTICATION_LOCAL_JWT_KEY` | Local signing authority; required on every launch. |
| `POSTGRESQL_USERNAME` | Username for the isolated PostgreSQL resource. |
| `POSTGRESQL_PASSWORD` | Password for the isolated PostgreSQL resource. |
| `AGENT_BROWSER_REDIS_PASSWORD` | Password for the isolated Redis resource. |
| `PRIVACY_ERASURE_IDENTITY_FENCE_KEY` | Retained external-identity fingerprint authority; Base64-encoded key material from the selected secret authority. |
| `PRIVACY_ERASURE_IDENTITY_FENCE_KEY_ID` | Stable identifier paired with the retained fingerprint key; preserve both across restart and restore. |

Do not put values for any of these names in commands, browser scripts, logs, or
screenshots. The profile does not choose Environment, User Secrets, or
Infisical for the developer. Explicitly select the configured authority in the
launching shell (or the ignored repository `.env`); a new shell does not inherit
a selection made in a previous terminal. When selecting Infisical, the chosen
project and environment must also contain the required `/postgresql` folder.
A missing folder fails closed during AppHost startup. Never rotate an existing
synthetic database password or reset its volume silently to work around that
failure. A separately authorized agent-only recovery must record the loss of
the original session and establish a new cold/warm baseline.

```bash
# Select SECRET_PROVIDER in this shell or the ignored repository .env first.
dotnet run --project src/Explore.AppHost/Explore.AppHost.csproj --configuration Release --no-build --launch-profile local-agent
```

When the shared Development Infisical `/api` folder selects Keycloak, direct
Infisical selection correctly rejects the Local-only agent API. The
repository-native `bash eng/scripts/run-local-agent.sh --no-build` instead
reads approved Development Universal Auth bootstrap values from the shared
User Secrets store, imports only allowlisted agent credentials from the
selected `/api`, `/postgresql` and `/privacy` vault folders into this process,
and selects Environment plus the agent's compiled Local topology. It does not
rewrite the shared vault, the ignored `.env`, or any credential. The operator
needs `curl`, `jq`, `base64`, and `dotnet`; omit `--no-build` after source edits
until a Release build has produced the intended binaries. The implement-tasks
workflow copies any repository-root `.env` into a new or resumed worktree
without overwriting a task-specific copy. An empty source `.env` does not
supply missing secrets.
The `/privacy` folder must contain the retained identity-fence key and its stable
identifier. Missing authority blocks startup replay even on a new Local-only
database; the agent must not invent replacement key material or bypass replay.

The profile binds loopback-only HTTP endpoints. Register the AppHost
resource-state observer before launch, then wait for the migration resource to
complete and both `explore-api` and `explore-blazor` to report ready/running.
Only then issue the first browser request. A port conflict is a failure to
resolve, not permission to choose another port or stop another developer's
process.

If the AppHost was launched in Release with `--no-build`, rebuild the touched
API or BFF project with `dotnet build --configuration Release` before restarting
that resource. Aspire's resource rebuild can compile Debug while the
`--no-build` process still launches an older Release binary. Check the actual
running configuration instead of treating a successful rebuild command as
evidence that the browser is exercising the edited code.

| Surface | Address |
|---|---|
| API | `http://localhost:5100` |
| BFF, discovery/anonymous host | `http://localhost:5200` |
| Dedicated instance-admin host | `http://admin.localhost:5200` |
| Default tenant host | `http://default.localhost:5200` |
| Negative-control tenant host | `http://agent-negative.localhost:5200` |
| Mailpit UI / SMTP | `http://localhost:58025` / `localhost:51025` |

The six provisioned identifiers are deliberately synthetic. They name the
persona to select but do not disclose a password:

| Persona | Address | Browser host |
|---|---|---|
| Instance administrator | `admin@agent.example.test` | `admin.localhost:5200` |
| Tenant administrator | `tenant-admin@agent.example.test` | `default.localhost:5200` |
| Organizer | `organizer@agent.example.test` | `default.localhost:5200` |
| Manager | `manager@agent.example.test` | `default.localhost:5200` |
| Attendee | `user@agent.example.test` | `default.localhost:5200` |
| Tenant moderator | `moderator@agent.example.test` | `default.localhost:5200` |

Use a separate unauthenticated browser context for anonymous behavior. Use a
fresh browser context for every persona switch; signing in over a previous
administrator context cannot prove the new persona's authority.

### Real browser login

For the selected host and fresh context, open `/login` and use its visible Local
Identity form. Observe the same-origin `GET /auth/status` and
`POST /bff/auth/local/login` responses before triggering the corresponding
actions. The shipped BFF login module obtains the ordinary antiforgery cookie
and sends `X-CSRF-TOKEN` with the form request. Do not read, inject, or replay
cookie values, construct a BFF session, or send a bearer token from browser
automation.

The shipped form follows the login response's local `redirectUrl` with a full
navigation. Wait for that navigation to settle before registering observation
of and issuing another `GET /auth/status`; a fetch issued while the previous
document unloads can fail without meaning the login failed. Treat login as
successful only when the post-auth status is authenticated and its identity is
the selected persona; an HTTP `2xx`, a rendered user menu, or a `redirectUrl`
alone is insufficient.

The browser session is an HttpOnly BFF cookie. Access/refresh tokens remain
server-held for BFF forwarding; browser automation must neither read nor attach
an `Authorization: Bearer` header.

### Authority checks

The admin host selects the instance-settings landing route, not an authority
grant. Test the instance administrator at `admin.localhost`; test tenant,
organization, event, and attendee behavior at `default.localhost`. Use
`agent-negative.localhost` for the wrong-tenant negative control. Host choice
does not replace API authorization. On the admin host, `/login` must remain
the ordinary Local form after Blazor becomes interactive; authentication
pages remain on the shared application router. Local credential
verification itself has no tenant context, while subsequent tenant-scoped API
requests still require a resolved tenant. After a successful instance
administrator login with the default return URL, the BFF selects
`/settings/instance` on the admin host and
`/settings/instance?section=getting-started` on a tenant host. Cookie
validation reads the authenticated current user and persisted administrator
authority without inventing a tenant for the admin host.

For each action under test, confirm the server-provided HAL relation is present
before exercising the allowed action. Separately attempt the corresponding
direct mutation when the relation is absent and require its denial. Check the
new protected request after every persona switch; do not infer authority from
claims, a prior context, or a rendered menu.

### Restart, recovery, and teardown

If AppHost exits before provisioning completes, relaunch the same `local-agent`
command and wait on the same readiness events. Startup resumes only its recorded
owned operation. A completed persona is not reset, regranted, profile-synced,
or forced to resolve initialization passwords on restart. Ownership, identity,
or port conflicts fail closed; preserve the value-free failure reason and
investigate the selected authority/configuration rather than changing data.

Stop only the AppHost process/session that this work launched (normally
`Ctrl+C` in its terminal). Its dedicated data survives ordinary restart. Do not
kill all `dotnet` processes, stop unrelated containers, or delete the agent
database/volumes as recovery. A fresh-volume reset is destructive and requires
explicit developer approval.

For an intentionally new synthetic database baseline, use the
[agent database reset procedure](OPERATIONS.md#reset-only-the-agent-database)
against the **running agent API**, not a container or filesystem. The
maintenance owner first returns 503/no-store on new API requests and drains
active request/worker units. It preserves migrations, approved lookups, Redis,
Mailpit, file storage, and the erasure authority while regenerating six native
Local credentials in separate recoverable transactions. Once the tool reports
ready, discard old browser sessions and sign all six personas in again.
If it reports a native failure, admission remains closed until same-owner
retry or pre-traffic restart recovery succeeds. A client timeout or lost
response has an unknown outcome: inspect `/health` and the new credential
baseline before assuming the API is either closed or ready.
Measure request-to-ready rather than claiming the 2000 ms target by design.

## 3. Visual Inspection

After the site is up, inspect the affected page in a browser at the relevant desktop and mobile widths. Verify focus behavior, keyboard navigation, RTL, dark mode, responsive layout, and reduced motion when those surfaces changed.

---

## 4. Key Notes

| Concern | What to Know |
|---|---|
| App URL | Profile-dependent; see the local-agent addresses above or the selected AppHost resource metadata. |
| Process management | Stop only the AppHost session you own; AppHost manages its child processes. |
| Enhanced navigation | Blazor enhanced navigation interferes with `page.goto()` — use `page.reload()` instead |
| Scoped CSS | Changes to `*.razor.css` require a full rebuild (not hot-reload) |
| MudBlazor version | v9 — match existing component API; see [`blazor-ui-conventions`](../../.agents/skills/blazor-ui-conventions/SKILL.md) |
| CSS isolation + BEM | See [`blazor-css-isolation`](../../.agents/skills/blazor-css-isolation/SKILL.md) |
| Purchase contract generation | Build `Explore.API` in Release to refresh `schemas/openapi_islamu-event.json`, then build `Explore.Blazor.Client` to run pinned NSwag and the record transformer |
| Purchase generated-contract gate | Run `TicketPurchaseGeneratedContractTests`; never edit `EventApiClient.g.cs` directly |

---

## 5. Cross-References

- Component / render-mode conventions → [`docs/BLAZOR.md`](BLAZOR.md)
- BFF auth / YARP / token forwarding → [`docs/SECURITY-MODEL.md`](SECURITY-MODEL.md), [`blazor-bff-patterns`](../../.agents/skills/blazor-bff-patterns/SKILL.md)
- UI conventions (MudBlazor, BEM, theming) → [`blazor-ui-conventions`](../../.agents/skills/blazor-ui-conventions/SKILL.md)
- Accessibility requirements → [`docs/ACCESSIBILITY.md`](ACCESSIBILITY.md)
- Design tokens, CSS layers, wrappers → [`design-system`](../../.agents/skills/design-system/SKILL.md)
