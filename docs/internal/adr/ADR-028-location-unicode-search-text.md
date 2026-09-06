<!-- ABOUTME: Records the complete Unicode location-search contract and provider-specific ordering boundary. -->
<!-- ABOUTME: Explains validation, privacy, normalization drift, and generated migration recovery without compatibility shims. -->

# ADR-028: Complete Unicode Location Search Text

Date: 2026-09-06
Status: Accepted design; runtime evidence belongs to the implementation verification record.

## Context

Location search already filters authority in one bounded EF Core query. Its old
seven-character scalar tokens amplified stored payload and coupled tests to an
entire host Unicode mapping digest. Column capacity was not preallocated storage.

## Decision

Use one internal Domain helper with strict Rune validation and whole-value
NFC → invariant uppercase → NFC normalization. Source text is nonblank and bounded
at 500 UTF-16 code units; derived text is bounded independently at 2,000, with no
truncation. Reject malformed UTF-16, NUL, and Unicode noncharacters. Preserve original
display text and meaningful marks, format controls, and supplementary characters.

Both derived fields use revision 2. Current-only constraints and query predicates
fail closed; authorized repair uses existing aggregate promotion/governance paths.
Address-derived text remains in removable `LocationPii`. Validate an entire grouped
write before any tracked mutation, and retain erasure/concurrency authority.

Use targeted Unicode binary collation metadata alongside the independent ASCII
marker: PostgreSQL C, SQLite BINARY, SQL Server nvarchar/BIN2, MySQL/MariaDB utf8mb4_bin.
Require identical authorized match membership for the accepted corpus and stable
provider-local ordering. Do not promise cross-provider alphabetical or UUID ordering.
The existing `.Contains()` scan is retained: authority reduces candidates and `Take`
bounds results, not examined rows. Additional indexes or full-text infrastructure
require a measured unmet product requirement.

## Alternatives and Consequences

The token encoder, two wrappers, old readers, segmentation, and scalar digest are
removed. Full Unicode case folding, transliteration, accent stripping, and a universal
sort-key engine would change product semantics and are excluded. No dependency is added.
Focused multilingual tests and five real-engine behavior checks replace the digest;
they do not freeze ICU/NLS mappings across runtime upgrades.

The four application migration catalogs are regenerated through EF for a breaking
development reset. Identity, Data Protection, and privacy-authority catalogs retain
independent histories. Runtime/globalization changes require a stopped-traffic
compatibility comparison and authorized rebuild or disposable reset. Mixed normalizer
profiles are unsupported. Recovery requires matching data and binaries/profile.

The Project Steward explicitly approved retaining standard EF-generated migration,
designer and snapshot headers for this regeneration as an exception to the ABOUTME
banner rule. Generated artifacts remain tool-owned; never prepend banners by hand.

## References

- [Domain semantics](../DOMAIN.md#location-unicode-search-text)
- [Runtime and migration operations](../OPERATIONS.md#location-unicode-runtime-compatibility)
- [Public operator guidance](../../public/documentation/readme/configuration-and-operations/backup-restore-upgrade.md#location-search-upgrade-and-runtime-changes)
- [I-VSD review](../../../islamic-value-sensitive-design/i-vsd-unicode-location-search-simplification.md)

Research handoff used official Unicode normalization, .NET globalization, EF collation,
SQL Server CHARINDEX, and MySQL binary-collation behavior as source-free requirements.
All implementation structure was independently derived from repository lifecycle and
provider composition; no third-party source, SQL, tests, assets, or new dependencies
were retained.
