# Export and import the instance operator identity

Operator identity manifests move the legal identity document independently of a
database backup. They contain legal names and public contact details; treat
them as sensitive. They exclude users, tenant identities, credentials and
payment history.

Offline preparation uses separately installed
[Configurator](https://github.com/ISLAMU/Configurator/blob/HEAD/docs/public/cli.md)
with Event's `operator-identity` artifact and a matching answer workspace.
The generic runtime does not own Event identity hashes, import wrappers or
administrator authority. The public browser accepts public choices only; never
upload identity documents there. See [the handoff](setup-assistant.md#external-configurator-handoff).

## Export and integrity

Use an authenticated **instance administrator** HTTP client to download
`GET /api/instance-operator-identity/manifest` as `source-identity.json`.
The stored identity must be complete for public disclosure and paid commerce.
Setup-secret authentication alone does not authorize portability routes.

The JSON manifest contains `apiVersion`, `kind`, `settingKey`, `contentDigest`,
`revisionHash` and `document`. The authenticated export route supplies Event's
integrity-protected wire shape with its server-owned identity and revision.
Configurator does not compute Event identity hashes or allocate server revisions.
Do not substitute a locally assembled document for the authenticated export.
Integrity alone does not authorize a write or establish legal completeness.
The ordinary management API response also contains readiness metadata and is
not interchangeable with the portable manifest.

Configurator's checked reference definition projects an integrity-protected
generation-only identity artifact. Changed document structure requires a
maintainer-authored source edit, a native CLI-built definition revision and a
corresponding answer workspace; arbitrary
exports cannot be reverse-mapped into that fixed shape. Generic `--answers`
proof does not replace Event's wire or semantic checks.
Maintainers use the portable `configurator-integrate` skill and standalone
`definition check/revise/build` workflow described in
[Event definition ownership](../../internal/CONFIGURATOR_DEFINITION_EXPORT.md);
the YAML remains author-maintained and synthetic fixtures remain test-owned.

## Prepare and apply an import

First download the **target's** current manifest and retain its `revisionHash`.
This differs from the source manifest's revision and prevents overwriting
someone else's update. The backend import request has exactly `manifest` and
`expectedRevisionHash`.

For a target without an identity document, the expected revision is `absent`.
An incomplete existing draft is not absent: read its `revision` from
`GET /api/instance-operator-identity` and hash its lowercase canonical UUID
string using SHA-256, without a trailing newline.

Submit the reviewed request as `application/json` to
`POST /api/instance-operator-identity/manifest/import` using an authenticated
instance-administrator client. Configurator performs no HTTP request.
Its `artifact import` command requires an admitted import-semantic definition
and an explicitly projected revision field. Do not invoke it against the checked
generation-only reference, invent an implicit host wrapper or use the source
revision as the target precondition.

The server verifies content integrity and complete legal identity readiness,
requires a registration identifier for registered organizations, and rejects
unrecognized jurisdictions. It preserves the target operator ID and assigns a
new revision. A manifest does not prove official status: importing an official
claim or replacing an already official identity is rejected because registry
attestation verification is unavailable.

## Results and recovery

- HTTP `200`: identity and durable audit event committed together.
- HTTP `400`: malformed, tampered, incomplete or disallowed identity; no update.
- HTTP `401`/`403`: missing authentication or instance-administrator authority.
- HTTP `409`: target revision changed; read current state and prepare a new request.
- Persistence/audit-write failure: transaction rolls back. Audit delivery follows
  through the existing retryable outbox worker.

SHA-256 detects changes, not source authenticity. Review source and target.
Restore a previous identity against the target's **current** revision rather
than reusing an old request.

Event bounds identity manifests to 64 KiB and rejects unknown members, duplicate
keys, YAML aliases/tags and multiple YAML documents. Configurator's general
input limits do not relax these host-specific limits. Its machine result is
value-free metadata with `host-validation-required` readiness; output requires
a separate protected, create-only native file. Local success does not grant
target acceptance or administrator authority.
