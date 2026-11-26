# Release Signer Rotation History

## Current state

Production release signing is governed by the public-key trust root in
[`eng/release/trust/allowed-signers`](allowed-signers) as documented in
[RELEASE_RUNBOOK.md](../../../docs/internal/RELEASE_RUNBOOK.md). Each enrolled
operator public key (`ssh-ed25519` or hardware-backed `sk-ssh-ed25519@openssh.com`)
is scoped to the domain-separated OpenSSH namespaces `git` (signed annotated
release tags) and `islamu.event.release-checksums` (offline `SHA256SUMS.sig`
countersignatures for post-tag sealed archives). Private keys never belong in
this repository, in `.env` files, or in GitHub Actions secrets.

## Rotation and revocation contract

- A new key receives a distinct public key and recorded SHA-256 fingerprint.
  An overlap window is allowed only for distinct keys and recorded validity
  periods.
- Revocation is effective on and after its recorded policy date. Remove the key
  from `allowed-signers` (or bound it with `valid-before`), append the reason
  and replacement below without restricted details, and merge through protected
  review.
- A tag is an immutable Git object. Replacing or recreating a published tag
  fails verification even when its commit target and signer are unchanged;
  operators must investigate and issue a new version rather than moving a tag.

## Entries

| Principal | Role | Namespaces | Fingerprint (SHA-256) | Effective Date | Status | Disposition |
|---|---|---|---|---|---|---|
| `amirakrari@openislamu.org` | Project Steward / Release Operator | `git,islamu.event.release-checksums` | `SHA256:/+dDHXg2xUiZlIZgYV2gC6/UzxcrdCxC6b5egPbfJEw` (ED25519, `islamu-release-operator`) | 2026-11-27 | active | Initial production release operator public key enrolled in `eng/release/trust/allowed-signers` |
