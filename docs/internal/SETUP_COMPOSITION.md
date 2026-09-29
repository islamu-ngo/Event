<!-- ABOUTME: Documents bounded JSON, YAML, and directory authoring for Setup portability artifacts. -->
<!-- ABOUTME: Defines deterministic output parity, fail-closed grammar, filesystem support, and recovery. -->

# Setup Composition

Setup Core accepts three source forms for `ConfigurationManifestV1Alpha2` and
`TenantConfigurationPackageV1Alpha2`:

- normalized JSON bytes;
- YAML bytes using the restricted grammar below; and
- a Linux directory containing `.json`, `.yaml`, or `.yml` fragments.

The source form and fragment names are authoring details only. They do not enter
the wire contract. Every successful compilation is reparsed and serialized by
`ConfigurationPortabilityJsonCodec`; equivalent sources therefore return the
same normalized bytes and `ArtifactDigest`.

## Public Core API

Create a `SetupCompositionCompiler` and pass a typed
`SetupCompositionJsonSource`, `SetupCompositionYamlSource`, or
`SetupCompositionDirectorySource` to `CompileAsync`. A successful
`SetupCompositionResult` contains one exact typed Wire object reference,
normalized bytes, and its digest. A failure contains only a closed
`SetupCompositionFailureCode`; it contains no source path, key, supplied value,
exception text, provider coordinate, or tenant/user identifier.

`ISetupCompositionPublicationCommitBarrier` is the deterministic test and host
seam immediately before publication. Production callers normally use the
overload backed by `SetupCompositionImmediatePublicationCommitBarrier`.

## Restricted YAML

YamlDotNet supplies parser events only. Setup Core owns conversion to the
normalized tree. Mappings, sequences, and scalars are supported, but the root
must be one mapping and the stream must contain exactly one non-empty document.
Aliases, anchors, tags, merge keys, directives, duplicate keys, non-scalar
keys, and null keys are rejected.

Quoted scalars always remain strings. Plain `true`, `false`, and `null` use
only those exact lower-case spellings. Integers use invariant authoritative decimal
notation without a plus sign, leading zero, fraction, exponent, locale
separator, hexadecimal/octal form, or negative zero. Ambiguous implicit YAML
spellings fail rather than being guessed.

## Directory Policy

Directory composition is enabled only on Linux, where Setup Core uses real
filesystem identity and no-link handle resolution. Windows directory mode is
disabled until equivalent reparse/junction and handle-safety semantics are
proved on a Windows runner. Normalized JSON and YAML byte sources remain
portable across supported .NET platforms.

A directory may contain only visible, non-temporary JSON/YAML fragments and
ordinary directories. Symbolic links, hard links, special files, mount escape,
unknown extensions, hidden files, backup files, traversal, absolute paths,
reserved names, and case or Unicode-normalization path collisions fail closed.
Fragments are ordered by ordinal relative path and only root mappings merge.
Mappings merge recursively; duplicate or conflicting leaves fail.

Setup Core captures and safely reads a complete snapshot, waits at the
publication barrier, then captures and compares the complete snapshot again.
Add, remove, rename, replacement, resize, retarget, identity, or content drift
returns `SourceChanged`. No partial result or retained file handle is returned.

## Default Limits

| Resource | Limit |
|---|---:|
| Aggregate source bytes | 4,194,304 |
| YAML documents | 1 |
| Parser events | 131,072 |
| Normalized nodes | 65,536 |
| Nesting depth | 32 |
| Mapping entries | 4,096 |
| Sequence entries | 4,096 |
| Scalar characters | 65,536 |
| Aggregate scalar characters | 1,048,576 |
| Directories | 256 |
| Files | 1,024 |
| Entries per directory | 256 |
| Relative path characters | 512 |
| Path depth | 16 |
| Per-file bytes | 524,288 |
| Aggregate directory bytes | 4,194,304 |
| Aggregate directory nodes | 65,536 |

Limits use checked arithmetic. The exact limit is accepted when all other
contracts are valid; `limit + 1` fails with `LimitExceeded`. Larger named scale
profiles are not part of this phase and remain disabled.

## Failure And Recovery

Composition performs no network, persistence, logging, telemetry, target
publication, or secret-provider operation. Cancellation and every invalid or
uncertain source state produce no object, bytes, digest, or partial file.
Operators can recover from YAML or directory rejection by supplying the same
non-secret artifact as bounded normalized JSON. An unsupported filesystem never
falls back to path-only checks.
