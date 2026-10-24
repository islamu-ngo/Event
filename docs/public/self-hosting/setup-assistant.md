---
description: "Prepare Event configuration with separately installed ISLAMU Configurator and Event-owned local definitions."
---

# External Configurator for Event

Install [ISLAMU Configurator](https://github.com/islamu-ngo/Configurator) separately.
Event no longer bundles the offline CLI, terminal, desktop, browser or shared
preparation engine. The extraction changes tooling, not Event's environment,
database schema, authentication, bootstrap or backend configuration authority.

## External Configurator handoff

Supply `configurator/definitions/islamu-event/definition.json` from the matching
Event release. Check its family, revision and digest against `release.json`.
The stable Event family is `a4bcfaa6-9979-43e5-b1f5-00d552618cdf`; native and
browser definitions have the same identity. Reference answers are fictional
interoperability vectors, not production credentials or approved legal templates.

Event maintains one handwritten public `configurator.yaml`, reviewed against its
catalogue, secret policy and host contracts. Maintainers use Configurator's portable
`configurator-integrate` skill and standalone CLI to check, revise and build
canonical JSON; clients consume the selected compiled `definition.json`.
The YAML is not automatically generated or kept fresh. `release.json` records
the selected release metadata; retained receipts do not prove edited source was
compiled. Digests enable comparison, not publisher authentication, legal approval
or release promotion. Selected revision 8 includes the optional application and
external Identity LocalIdentityWriter username/password fields and removes the
obsolete shared Identity connection-string field. External Identity uses discrete
endpoint and purpose-specific credentials. It retains revision 6's
semantic paths in place of traversal field numbers; real answer workspaces are
not migrated implicitly. The delivery directory contains
only this YAML source, `definition.json` and `release.json`. Synthetic inputs,
answers and expected artifacts live under
`tests/Event.Wire.Contracts.UnitTests/Fixtures/Configurator/islamu-event/`;
they are test data, not deployment defaults. Event still accepts JSON/dotenv
artifacts; YAML is only the authoring format.

Configurator's [packaging guide](https://github.com/islamu-ngo/Configurator/blob/HEAD/docs/public/packaging.md)
owns install/build instructions and host support. Copy the complete package
directory, including license and third-party notices. CLI, terminal, desktop
and static browser are separately packaged surfaces. A packaged RID is not
proof of protected-save support: native restricted output currently requires
Linux and a filesystem where owner-only, create-only protection can be proved.
Windows/macOS output fails closed; desktop packaging supports Linux x64 only.

The [CLI guide](https://github.com/islamu-ngo/Configurator/blob/HEAD/docs/public/cli.md)
owns executable grammar and the `islamu-configurator-command/v2` result envelope.
Authoring and runtime definition schemas remain v1.
For native interactive preparation:

```bash
islamu-configurator-terminal --definition configurator/definitions/islamu-event/definition.json
islamu-configurator-desktop --definition configurator/definitions/islamu-event/definition.json
```

On the origin where you deploy Configurator, open
`/launch/a4bcfaa6-9979-43e5-b1f5-00d552618cdf`. This selects installed metadata
without contacting Event. Optional `appVersion`, `entry`, `locale` and `digest`
selectors must match the installed definition. No public origin is prescribed.
The browser transfers only declared public enum/boolean choices. Never upload
secrets, environment files, manifests, tenant packages or personal identity data.

## Command migration

Old `event-setup` families are not aliases. Replace bundled tooling commands with
the generic CLI, an explicit local definition and a declared artifact ID.

| Previous workflow | External preparation |
|---|---|
| Catalogue inspection | `catalogue list/show/describe` with `--definition` |
| Dotenv preparation | `env validate/render` with `--artifact environment` |
| Configuration manifest | `artifact` operations with `--artifact configuration-manifest` |
| Tenant package | `artifact` operations with `--artifact tenant-package` |
| Operator identity portability | `operator-identity` artifact; Event still owns integrity and target import acceptance |
| Legal draft preparation | `legal-drafts` artifact; host review/publication remains separate |
| Bundled release targets | Separately packaged Configurator surfaces |

```bash
islamu-configurator doctor --machine
islamu-configurator catalogue list --definition configurator/definitions/islamu-event/definition.json --machine --dry-run
islamu-configurator env render --definition configurator/definitions/islamu-event/definition.json --artifact environment --input environment-answers.json --output deployment.env --machine
islamu-configurator artifact validate --definition configurator/definitions/islamu-event/definition.json --artifact configuration-manifest --input manifest-answers.json --machine
```

`--input` is a JSON answer workspace keyed by declared field IDs, not an arbitrary
existing host artifact. Existing projected artifacts require their answer proof
via `--answers`; lost/transformed answers cannot be inferred. Choose an artifact's
declared format, rather than adding an unsupported YAML conversion switch.
`--dry-run` publishes nothing. All answer-derived bytes are restricted, including
public-field answers; they never go to stdout. Output is create-only, and an
existing file is left unchanged.

Machine success describes preparation, not permission to apply to Event;
target acceptance remains separate. `doctor` checks local writer/schema
availability, not target readiness.
Keep artifacts separate from runtime secret authority and use Event's existing
authorized import workflow for promotion.

## Event shapes, identity and legal drafts

The checked definition declares five generation-only artifacts and a bounded
reference shape. Tenant row edits within its declared list change answers, not
the definition revision. Changed document vocabulary, bounds, nullability or
output policy requires a reviewed source edit and a new canonical revision.
Trusted issuer fields are finite declarations, not wildcard authority.
See [Event definition ownership](../../internal/CONFIGURATOR_DEFINITION_EXPORT.md).

Maintainers install the standalone CLI and copy the complete
`skills/configurator-integrate/` directory from its package or
[repository](https://github.com/islamu-ngo/Configurator/blob/HEAD/skills/configurator-integrate/SKILL.md)
into their agent's supported skill location. The agent reads Event's public
configuration code/contracts and authors the supported compact source; it does
not inspect private answers or launch Event. Confirm the installed v2 command
result contract and v1 authoring/runtime schemas, then use native commands:

```bash
islamu-configurator --help
islamu-configurator definition --help
islamu-configurator definition check --source configurator/definitions/islamu-event/configurator.yaml --machine
```

For a new integration, `definition init` allocates identity once and requires an
exact application version. For an existing release, retain its canonical JSON
and run `definition revise --source PATH --against PRIOR_CANONICAL_JSON` after
semantic source edits, then `definition build --source PATH`. Build creates an
immutable revision under `dist/configurator/revisions/` relative to the source.
Review actual results/digests and exercise complete and incomplete synthetic
workspaces through the documented artifact commands before distributing a release.
Fixtures remain test-owned; no current compilation is implied by these recipes.

Export an integrity-protected operator identity through Event's authenticated
manifest API, not an offline hashing helper.
The checked generation-only definition is not an identity import request.
`artifact import` requires a separately admitted import-semantic definition
that explicitly projects the required target revision field. Do not invent a
request wrapper or substitute the source revision for the target's current one.
See [identity portability](operator-identity-portability.md).

Legal output remains a draft for host review. Local validation does not publish
documents, establish accountable identity or certify legal validity.

## Configuration import sessions on a live instance

PostgreSQL setup enrollment and operation receipts require their fixed Runtime
storage permissions, installed through Event's normal migrator. The setup
descriptor is separate from API-key storage permissions. It retains tenant row
isolation, immutable issuance claims and operation identity, and grants no DELETE
access. Partial or widened grants are rejected, not repaired automatically.
Do not switch request handling to Migrator/Global credentials or bypass these
checks to resolve an enrollment error. This correction adds no configuration
setting or schema migration and does not enable live control in Configurator.
Its native permission regressions remain subject to remote CI acceptance.

Live configuration import remains a server-controlled workflow, separate from
offline Configurator:

1. An authorized administrator uploads a bounded artifact. The server protects
   it and creates a short-lived import session.
2. Preview binds its digest, target revision, selected sections, mappings,
   apply mode, approvals and expiry.
3. The client enables only actions advertised by server-authored HAL links.
4. Apply consumes the matching preview once. Changed revisions/selections,
   expiry, cancellation, wrong targets, invalid tokens and replay fail closed.

The authenticated SetupLive API remains:

- `POST /api/tenants/{tenantId}/setup/enrollments/{enrollmentId}/configuration-import/sessions`
- `POST .../sessions/{sessionId}/preview`
- `POST .../sessions/{sessionId}/apply`

Use the server-issued enrollment capability throughout; preview/apply also need
the session access token. An enrolled session cannot be applied through ordinary
tenant import routes, even by a tenant administrator holding its token.
Configurator has no live sign-in, target apply or administration operation.

## Security and recovery

Never put credentials or personal artifact values in arguments, file names,
environment names, logs or retained command output. Review output before use.
Protect identity artifacts despite their public legal/contact content. Back up
affected stores and test imports in staging before applying to production.

For tooling rollback, use a previously recorded Configurator package and its
matching definition revision; keep licenses/notices with it. Do not bypass
Event validation or change database state to compensate for a tooling rollback.
For a stale backend preview/import, read current target state and prepare a new
request rather than replaying an old one.

## Related documentation

- [Operator identity export and import](operator-identity-portability.md)
- [Configuration Manifests](../documentation/readme/configuration-and-operations/configuration-manifests.md)
- [Environment Variables](../documentation/readme/configuration-and-operations/environment-variables.md)
- [Secrets](../documentation/readme/configuration-and-operations/secrets.md)
- [Backup, Restore & Upgrade](../documentation/readme/configuration-and-operations/backup-restore-upgrade.md)
