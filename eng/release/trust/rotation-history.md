# Release Signer Rotation History

## Current state

No signer entry is currently bundled. Activation is blocked until independent
reviewers approve real principals, unique public keys, custody owners, validity
windows, revocation contacts, and the promoted artifact store. Private keys never
belong in this repository or a trusted bundle.

Activation itself is tooled and no longer hand-assembled: run `activate-trust` with the two
reviewed **public** keys, as documented in
[RELEASE_RUNBOOK.md](../../../docs/RELEASE_RUNBOOK.md). The command enforces separation of duty —
distinct principals, distinct key material, distinct fingerprints — refuses private key files and
non-`ssh-ed25519` algorithms, and refuses to overwrite an existing root without `--replace`. It is
deliberately impossible for one person to satisfy, because one key that can both promote the tooling
bundle and sign the release it attests would forge the whole chain on its own.

## Rotation and revocation contract

- A new key receives a new unique principal and fingerprint. An overlap window is
  allowed only for distinct keys and recorded validity periods.
- Revocation is effective on and after its recorded policy date. Remove the key from
  `allowed-signers`, append the reason and replacement here without restricted
  details, promote a new trusted bundle, and retain the prior bundle as evidence.
- A tag is an immutable Git object. Replacing or recreating a tag fails even when its
  commit target and signer are unchanged; operators must investigate and issue a new
  version rather than accepting the moved name.

## Entries

| Principal | Role | Validity | Status | Disposition |
|---|---|---|---|---|
| _none_ | _none_ | _none_ | blocked | `allowed-signers` remains comment-only until reviewed production keys are promoted |
