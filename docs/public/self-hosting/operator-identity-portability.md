# Export and import the instance operator identity

Operator identity manifests move the legal identity document independently of a
database backup. They contain legal names and public contact details, so treat the
files as sensitive even though the machine CLI output contains only metadata.
They do not include users, tenant identities, credentials, or payment history.

## Export

Use an authenticated **instance administrator** HTTP client to download
`GET /api/instance-operator-identity/manifest` as `source-identity.json`.
The stored identity must be complete for public disclosure and paid commerce.
Setup-secret authentication alone does not authorize these portability routes.

The JSON manifest includes `apiVersion`, `kind`, `settingKey`, `contentDigest`,
`revisionHash`, and `document`. To produce YAML:

```bash
event-setup portability export-operator-identity \
  --input source-identity.json --output source-identity.yaml \
  --format yaml --machine
```

JSON is the default output format. Export also accepts a raw persisted identity
document in JSON, including its server-generated UUIDv7 `operatorId` and `revision`.
Offline export verifies the contract, not the identity's legal completeness.
Do not confuse it with the ordinary management API response, which also contains
readiness metadata.

## Prepare and apply an import

First download the **target's** current manifest and retain its `revisionHash`.
This token is separate from the source manifest's revision and prevents overwriting
someone else's update. Then prepare the import request:

```bash
event-setup portability import-operator-identity \
  --input source-identity.yaml --output import.json \
  --expected-revision "$TARGET_REVISION_HASH" --machine
```

For a target with no identity document, use `--expected-revision absent`.
An incomplete existing draft is not absent: read its `revision` from
`GET /api/instance-operator-identity` and hash its lowercase canonical UUID string
with SHA-256, without a trailing newline.

The CLI does not perform an HTTP request or write to a database. Submit `import.json`
as `application/json` to `POST /api/instance-operator-identity/manifest/import`
using your authenticated instance-administrator client. The request has exactly
two properties, `manifest` and `expectedRevisionHash`.

The server validates the SHA-256 content digest, checks complete legal identity
readiness, requires a registration identifier for registered organizations, and
rejects unrecognized country jurisdictions. It preserves the target's operator ID
and assigns a new revision. A manifest is not proof of official status: importing
an official claim or replacing an already official identity is rejected because
registry attestation verification is not available.

## Results and recovery

- HTTP `200`: the identity and durable audit event committed together.
- HTTP `400`: malformed, tampered, incomplete, or disallowed identity; no update.
- HTTP `401` or `403`: authentication or instance-administrator authority is missing.
- HTTP `409`: the target revision changed. Read the new target state, review the
  difference, and prepare a new request with the current target hash.
- A persistence or audit-write failure rolls back the transaction. Audit log
  delivery occurs afterward through the existing retryable outbox worker.

SHA-256 digests detect changes; they do not authenticate the source. Review the
source and target before submitting. To restore a previously exported identity,
import it against the target's **current** revision rather than reusing an old
request.

Both CLI commands support `--dry-run`, which validates and reports planned output
without creating an artifact. In `--machine` mode, stdout is one JSON metadata
object; artifact output requires a separate file. Successful offline preparation
reports `server-validation-required`, not permission to modify the target.
The CLI creates files atomically, uses owner-only `0600` permissions on Unix,
and refuses to overwrite an existing output file. Manifests are bounded to 64 KiB;
unknown members, duplicate keys, YAML aliases/tags, and multiple YAML documents
are rejected.

For installation and the other command families, see the [Setup Assistant guide](setup-assistant.md).
