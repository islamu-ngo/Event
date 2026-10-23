---
description: Supply and operate the Local password blocklist and native hash work factor.
---

# Local password policy

New Local credentials, including bootstrap, administrator creation, password
change, recovery and first-use replacement, require 15–128 Unicode scalar values
after NFC normalization. Supplementary characters count once, not as two UTF-16
units. NFC makes canonically equivalent spellings of a character the same
credential. Event preserves whitespace and case: it never trims or case-folds
passwords. No digit, uppercase, punctuation or unique-character rule is added.
Long passphrases remain allowed unless their entire normalized value is blocked.

Login and current-password verification accept a nonempty normalized credential
of up to 128 scalars; they do not impose the new-credential minimum or run the
blocklist. A short existing credential can therefore be verified for controlled
replacement without weakening the rules for its replacement. An input envelope
of 1,024 UTF-16 units bounds normalization work. Malformed UTF-16 is rejected.
Canonical normalization applies before both hashing and verification, not only
at creation.

## Supply the snapshot before selecting Local

Event ships no password corpus. The operator must obtain or author an appropriate
common/compromised-password corpus with provenance and terms compatible with
their intended use and distribution. Do not copy a third-party corpus into this
repository. Record the source, license approval, acquisition date and revision
in the deployment's custody records. Runtime validation checks the manifest's
shape and pinned bytes, not legal approval or dataset quality.

Publish one UTF-8 JSON object with these exact, case-sensitive members:

| Member | Contract |
| --- | --- |
| `Version` | Integer `1`. |
| `Revision` | Nonblank revision identifier, at most 128 characters, no controls. |
| `Source` | Nonblank provenance reference, at most 2,048 characters, no controls. |
| `License` | Nonblank approved-terms reference, at most 256 characters, no controls. |
| `Passwords` | Array of 1–100,000 nonempty complete password strings, each at most 128 normalized scalars. |

The file must be at most 16 MiB. Unknown or duplicate object members, malformed
JSON, missing metadata, and duplicate NFC-equivalent entries are invalid.
The short entries in a blocklist need not satisfy the creation minimum. Matching
is ordinal equality of the whole NFC-normalized string, never a substring ban.
All replicas must receive the same approved bytes and their SHA-256 digest.

Bind the following native .NET configuration keys:

| Key | Meaning |
| --- | --- |
| `Authentication:Local:PasswordBlocklistPath` | Absolute path to the read-only mounted snapshot. No bundled default. |
| `Authentication:Local:PasswordBlocklistSha256` | Exactly 64 hexadecimal characters pinning the snapshot bytes. No default. |
| `Authentication:Local:PasswordHashIterations` | Integer 100,000–2,000,000; default 210,000 pending deployment calibration. |

The environment equivalents are
`Authentication__Local__PasswordBlocklistPath`,
`Authentication__Local__PasswordBlocklistSha256`, and
`Authentication__Local__PasswordHashIterations`. These are policy configuration,
not account credentials. The deployment catalogue and API/Combined projection
also accept `AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_PATH`,
`AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_SHA256` and
`AUTHENTICATION_LOCAL_PASSWORD_HASH_ITERATIONS`. Canonical configuration takes
precedence over a friendly alias. Compose passes these aliases to the native
configuration keys; mount the snapshot read-only using a deployment-owned
Compose override, and set the path to its location inside the container.
The base topology does not publish a corpus or invent a default mount.

Prepare the complete file and digest offline. Publish by atomic file replacement
on the mounted filesystem, update the deployment's digest pin, then restart each
consumer deliberately. Do not edit a live file in place. Event captures and
validates one immutable snapshot during persistence composition; there is no
live file watcher or partially applied dataset. Removing or changing its pathname
after startup does not replace that process's captured policy.

`local-password-policy` participates in readiness, not liveness. A missing,
unreadable, oversized, incorrectly pinned or invalid snapshot makes readiness
unhealthy when Local is the configured primary provider or the default provider.
Every Local credential mutation fails closed on an unusable snapshot, including
Local accounts linked under another primary provider; supply the snapshot whenever
those mutations are needed. A blocklist outage does not reset credentials or
switch providers. No password-dependent third-party network query is made.

## Hash cost and offline calibration

Event uses ASP.NET Core Identity's salted adaptive IdentityV3 format:
PBKDF2-HMAC-SHA512, a fresh 128-bit random salt per hash and a 256-bit subkey.
Readiness reports only the effective algorithm, format, iteration count, salt
and subkey sizes, normalization and snapshot usability/count. It never reports a
password, credential hash, corpus value, manifest provenance or filesystem path.

The default iteration count is not a deployment benchmark result. Before release,
calibrate on the actual deployment hardware and runtime under representative
load, including concurrent login capacity and denial-of-service budgets. The
offline tool generates its own temporary synthetic input, touches no Identity
database, sends no network request and prints only parameters and timings:

```bash
dotnet run --project eng/tools/local-password-calibration/LocalPasswordCalibration.csproj --configuration Release -- 210000 100
```

The arguments are an allowed iteration count and 20–1,000 samples. Record hardware,
OS, runtime version, release revision, workload and the reported hash/verification
median and 95th percentile. Repeat with candidate work factors outside fast
tests, then set the approved deployment value. No automatic tuning or timing-based
test assertion chooses security policy. This command is authored but has not
been executed as part of the source-only security programme.

A successful login with an obsolete native hash work factor uses Identity's
rehash path and persists a newly salted hash. Do not reduce an approved work
factor to avoid capacity planning. Blocklist publication does not force arbitrary
periodic expiry; replace credentials on evidence of compromise or an authorized
recovery/reset decision.

## Controlled pre-release credential replacement

There is no raw-password normalization fallback. Credentials created under an
older non-NFC convention may need authorized recovery or supervised reset before
they can be used under this policy. Coordinate the cutover with administrator
access and replica restarts; do not silently reset all users. The existing
restricted first-use flow produces a new compliant password, then requires fresh
login. Rejected changes preserve the old hash, security/concurrency stamps,
credential state and recovery or replacement proof.

See [Authentication](../security-and-identity/authentication.md) for Local
account authority, lockout, email recovery and private temporary-password handover.
