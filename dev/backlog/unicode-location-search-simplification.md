<!-- ABOUTME: Scoped follow-up design replacing location scalar-token storage with strict normalized Unicode text. -->
<!-- ABOUTME: Preserves privacy and validation while defining provider, runtime-upgrade, migration, and release proof gates. -->

# Unicode Location Search Simplification

Last Updated: 2026-09-06 Europe/Brussels

## 1. Ownership And Delivery

- Owner: active Unicode implementation workstream, with independent privacy/security and persistence reviews.
- Status: implemented and checkpointed locally on `develop` in scoped Conventional Commits; the user-authorized upstream merge and combined-schema regeneration are in progress. Pre-merge five-provider proof, scoped reviews and repaired Architecture passed, but integrated verification and final delivery remain incomplete. Nothing has been published or approved as fully verified. The sections below preserve the original accepted design and intake evidence; they are not a claim that every delivery gate passed.
- Parent: `dev/active/prevent-ci-failures-and-unicode-simplification/`; this backlog item is the permanent source-free handoff when that ignored working memory is removed.
- Repository evidence: `3f9b796b4a0aea6fff40baaaf88e07add663e863`, 2026-09-06.
- Intents: `add-ef-migration` (security), `update-repository-query` (domain state), focused `test-suite-rationalization`; protect Tier 2 privacy lifecycle invariants.
- Admission: revalidated in the [Unicode I-VSD report](../../islamic-value-sensitive-design/i-vsd-unicode-location-search-simplification.md), current / plan-aligned for the original design digest `1c30d905825826910a8f563db9916be3855478b4978233b09f3375b97792fb78`. Status-only edits do not change that reviewed design or inherit final approval from the parent report.
- Read before implementation: migration/persistence/domain/privacy rules, `DOMAIN.md`, `PRIVACY_ERASURE.md`, `TESTING.md` multi-provider contracts, `OPERATIONS.md` location migration/reset section, and governed release documentation. Older migration IDs were an intake finding; the local runbook has been updated. Actual generated artifacts remain authoritative, and upstream integration requires combined-model regeneration and another documentation reconciliation.
- User independently authorized full implementation directly on develop. Exact phase-owned commit packets and final delivery remain gated; no remote publication authority is inferred. Active execution evidence resides in `dev/active/unicode-location-search-simplification/` and `.omo/evidence/20260906-unicode-location-search-simplification/`; neither ignored directory substitutes for committed operator documentation.

### Pre-Merge Execution Evidence — 2026-09-06

The encoder/wrappers/digest are deleted; the standard-library helper, atomic validation, location-only Unicode storage and four generated application initials are present. Local IDs: PostgreSQL `20260906173823_Init`, SQLite `20260906173858_Init`, SQLServer `20260906173913_Init`, MySQL/MariaDB `20260906173951_Init`. Five-engine corpus/probes, clean/repeat MigrationService, pending-model checks and migration lifecycle slices pass for the recorded local source. Domain1134/1134 and Application2061/2061 pass; the recorded Release build has zero errors. These results do not certify a future integrated model.

Do not mark this backlog delivered: pre-merge Architecture is577passed/0failed/1existing skip and the repaired combined Persistence cohort is313/313 without skips, but full Persistence is1,554passed/4failed/5existing skips. The earlier aborted90-failure run is historical. The later three-builder diagnostic correction passed20/20 locally; upstream's assembly-wide test-isolation policy now supersedes those local logging exceptions. The user approved separate repairs, standard EF-generated headers, checkpoint commits and merging origin. All43 initial merge conflicts have been reconciled; combined-schema generation and integrated verification remain required. The new checkpoint range passes release validation; broader historical validation still flags an unrelated prior commit scope. Final release validation and CI evidence remain open. No original requirement is deferred or waived by this status update.

### Integration Disposition — 2026-09-07

Preserve upstream relational ATProto storage and local Unicode semantics in one EF-generated initial per application migration assembly. Independent migration authorities stay unchanged. Fresh, empty task-owned generation catalogs were verified before native EF removal; no existing proof or developer database was reset. Reuse the upstream canonical test-options helper and its strict diagnostic regression. The initial merged Persistence compilation passes with zero errors; this is not migration or runtime certification. Exact final generated IDs belong in the canonical operations runbook after generation. Immutable prior release fragments remain present; a new combined-rebaseline fragment records the corrected deployment contract.

## 2. Verified Problem

`src/Explore.Domain/ValueObjects/UnicodeScalarKeyV1.cs` contains one encoder plus `LocationAddressSubstringKeyV1` and `LocationDisplaySortKeyV1`. It already applies NFC → invariant uppercase → NFC with strict Rune decoding and noncharacter segmentation, then stores seven ASCII characters per scalar. Both wrappers cap encoded text at 14,000 characters.

`LocationPiiConfiguration` and `LocationConfiguration` cap original address/name columns at 500 characters. SQL column maximums are not preallocated storage: claim reduced actual encoded payload, not “14KB allocated for every address.” `LocalAddressSuggestionQuery` already uses `.Contains()` and a bounded SQL query. Simplify encoding and schema; do not reimplement the query engine.

The whole-scalar hash test currently accepts two ICU-related hashes. Its removal eliminates a broad mapping snapshot test, not operating-system dependence in persisted normalization. Other tests cover useful invariants and must survive as behavior tests.

## 3. Chosen Contract

This is a clean breaking replacement. No V1 readers, format autodetection, dual-write, compatibility adapters, locale-dependent SQL lowercasing, in-memory filtering before authority checks, or new Unicode/FTS packages.

### 3.1 Text semantics

- Normalize accepted text with NFC → `ToUpperInvariant()` → NFC. The final NFC pass remains required. This is invariant uppercase matching, **not full Unicode case folding, transliteration, accent removal, or locale-aware Turkish search**.
- Do not promise `Straße` equals `STRASSE` or dotted-I equals ASCII-I. Add explicit positive and negative examples reflecting the chosen uppercase contract. Arabic combining marks, ZWJ/ZWNJ, emoji modifiers, and variation selectors are preserved, not stripped as generic “invisible characters.”
- A query means literal substring of the normalized address. Matching is not grapheme/word segmentation: normalized `é` matches `e` plus combining acute, but accent-free `e` need not match normalized `é`. `%`, `_`, backslash, `[` and `]` are literals, even when a provider implements `.Contains()` through escaped LIKE rather than a substring function.
- Original display/address text remains the authoritative user-facing value. Derived keys are never substituted into UI text, external provider requests, logs, or diagnostic exports.
- Source name/address contract: non-null, non-whitespace, at most 500 UTF-16 code units. Existing caller-specific query trimming and `LocalAddressSuggestionBounds` remain authoritative; the normalization helper does not silently trim source text. Validate the source limit consistently before storing, including SQLite where max-length metadata alone is not enforcement.
- Reject malformed UTF-16, embedded NUL, and Unicode noncharacters (`FDD0–FDEF`, each plane's `FFFE/FFFF`) at the domain/query input boundary. This intentionally removes the noncharacter-preservation path. These are distinct from legitimate supplementary characters, private-use characters, and Arabic format controls.
- Normalize the **whole** accepted value, then enforce a separate maximum of **2,000 UTF-16 code units** for the derived key. Never substring, split a surrogate pair, or silently truncate. A value violating either raw or derived bound fails validation before any aggregate mutation. This is an explicit accepted-input contract, not a claim that every conceivable 500-unit value has been mathematically proven to fit.
- Required expansion example: repeated `U+0344`, whose NFC form expands, and a 500-unit address with a searchable suffix after position 300. These must fit and retain complete output. Arbitrary runtime-expansion cases beyond the declared bound fail explicitly.

### 3.2 Matching and ordering scope

Require identical authorized match membership across the five supported engines for the accepted corpus. Keep SQL `OrderBy(DisplaySortKey).ThenBy(Id).Take(limit)` and stable repeat ordering within a provider. Do **not** promise byte-identical alphabetical or GUID tie ordering across providers: UTF-16/UTF-8, padding, supplementary characters, and native UUID comparison are separate concerns.

This explicitly narrows the previous draft's unsupported universal-order claim. It does not preserve the old scalar-hex order as a compatibility requirement. Product discovery needs bounded, stable suggestions, not a new universal multilingual collation engine. Test prefixes, supplementary/BMP pairs, trailing spaces, and equal-name IDs; for limited pages assert each provider's declared deterministic order. If cross-provider identical ranking later becomes a product requirement, specify it before choosing a separate binary sort representation; do not infer that requirement from the old encoding.

## 4. WHEN / THEN Acceptance Matrix

| ID | WHEN | THEN |
| --- | --- | --- |
| UNI-01 | NFC and canonically equivalent NFD addresses/queries are compared | Same normalized key and authorized match membership. |
| UNI-02 | Accented Latin, Arabic with marks/joiners, Greek, supplementary letters, emoji, or private-use characters are supplied | Preserve accepted text and selected casing semantics; no ASCII fallback or lossy replacement. |
| UNI-03 | Null/whitespace, malformed UTF-16, NUL/noncharacters, raw overflow, or derived overflow is supplied | Existing validation response style; no partial address/name/key/version/stamp mutation and no unhandled SQL error. |
| UNI-04 | A 500-unit address contains a suffix after character 300, or NFC expands input | Entire supported value remains searchable; no truncation. |
| UNI-05 | Search includes `%`, `_`, backslash or brackets | Literal matching on each real engine; no unintended wildcard matches. |
| UNI-06 | Same normalized address is owned by another tenant, quarantined, private-home, unauthorized organization, deleted, or erased | No suggestion from that row; authority filtering happens before materialization and limit. |
| UNI-07 | PII is erased, then stale-key repair, repeated erasure, promotion, or a stale writer runs | No derived address resurrection; persistence concurrency/erasure rules decide the write and keys never appear in logs. |
| UNI-08 | A current address/name write or explicit repair succeeds | Original value and complete current keys change atomically; rejected changes leave the previous state intact. |
| UNI-09 | Duplicate names, prefixes, supplementary characters, or trailing spaces occur | Repeatable provider-local order and bounded result count; no claimed universal .NET ordinal order. |
| UNI-10 | Stored key has an unsupported revision | Fail closed in suggestions until an authorized rebuild; no V1 read fallback. |
| UNI-11 | Runtime/globalization profile changes | Complete documented compatibility check and coordinated rebuild/reset before writers/readers with the changed profile resume. No rolling mixed-normalizer operation is promised. |

## 5. Smallest Correct Architecture

### Domain and lifecycle

Use one internal static `LocationTextNormalization` helper in `Explore.Domain`, not a value-object class hierarchy or injected normalizer. It validates decoded Runes and returns a normalized string. Keep the existing `DisplaySortKey` / `AddressSubstringKey` property names unless a public requirement demands renaming; avoiding an unnecessary rename is not compatibility baggage.

Delete `UnicodeScalarKeyV1`, both wrappers, token-width/sentinel construction, and noncharacter segmentation. Replace them with explicit source/key bounds and one current normalization revision consumed by both fields. Keep revision checks only for fail-closed stale-derived-state handling; this is a current operational invariant, not support for an old reader. Current-only constraints replace `% 7 = 0` encoding checks and obsolete version-zero migration scaffolding where no current lifecycle requires it.

Preserve validate-before-mutate ordering in `Location.SetFullName`, manual/provider address changes, promotion, repair, and `LocationPii.SetAddress`. Keep erasure guards. Trace `HasCurrentDerivedKeys` / `EnsureCurrentDerivedKeys` and all persistence repair consumers before changing their signatures. Do not silently assume deleting a version property cannot affect privacy repository SQL.

### Persistence

Add a small location-specific Unicode collation annotation alongside `PortableOrdinalAsciiPropertyExtensions`, handled by the existing `PortableRelationalModelPolicy`. Do not globally change `UsePortableOrdinalAscii` or its actor/bootstrap/setup callers.

Map Unicode text explicitly, with `HasMaxLength(2000)` on complete derived values: PostgreSQL Unicode text/varchar with `C`; SQLite text with `BINARY`; SQL Server **nvarchar**, not non-Unicode varchar, with `Latin1_General_100_BIN2`; MySQL and MariaDB `utf8mb4` with an engine-supported binary text collation, initially `utf8mb4_bin`. Actual provider DDL/translation is subject to the real-engine gates below; do not assume max-length semantics or trailing-space behavior agree.

Database collation controls the selected operators, not normalization. Continue normalizing query parameters in Domain code before the existing EF predicate. Keep parameterization, no-tracking, disabled auto-includes, tenant/membership/governance predicates, narrow projection, and SQL limit. Do not add `AsEnumerable()` or query-time lowercase/collation wrappers to conceal provider failures.

`.Contains()` is generally a filtered scan, not a B-tree substring seek. Existing tenant/authority predicates narrow candidates; `Take` bounds returned rows, not scanned rows. Record representative query plans and timings using the existing provider test harness. Add an index/FTS dependency only for a measured unmet requirement in a separate change.

### Runtime drift

Use the repository's approved SDK/runtime and tested deployment globalization profile. Focused tests cover documented semantics; do not freeze every Unicode scalar or expand a list of host-specific hashes. A normalization revision does not automatically detect a new ICU mapping: runtime/globalization updates must be reviewed as persisted-key changes. Before deployment, compare focused corpus behavior and run an authorized current-key rebuild or disposable development reset while writes are stopped. Mixed old/new runtime writers remain unsupported. If immutable cross-runtime Unicode tables become mandatory, that is a separate dependency/provenance and deployment decision, not an implicit property of `ToUpperInvariant`.

## 6. Invariant-First Execution

1. **Readiness and Red:** refresh callers/migrations, revalidate I-VSD and threat/erasure assumptions; establish the Release baseline once. Add failing `LocationTextNormalizationTests` for UNI-01–UNI-04 and extend `LocationDerivedKeyLifecycleTests` for rejection atomicity and UNI-07. Record the intended failures against the old format/contract before deletion.
2. **Provider/authority Red:** extend the existing query corpus to UNI-05–UNI-10 through `PrimaryDatabaseProviderBehaviorFixture`; add a dedicated `LocalAddressSuggestionProviderUnicodeTests` entrypoint if the existing PostgreSQL/SQLite class cannot run with that fixture. Use the production provider composition and authorized injected credentials. The worst break is erased/out-of-scope address PII becoming searchable; assert rows and persisted state, never internal method calls.
3. **Green vertical change:** helper, aggregate lifecycle, scoped collation, query current-revision checks, generated migrations, and matching tests travel together. Delete the old encoder and whole-scalar digest after targeted replacements pass. Update directly affected architecture/portable model tests, not unrelated framework tests.
4. **Provider proof and release:** run the shared corpus against PostgreSQL, SQLite, SQL Server, MariaDB, and MySQL. Generate/apply each affected application chain on clean disposable DBs, run MigrationService a second time, check pending model changes and provider metadata, and verify error/PII boundaries. No skipped provider is counted as passed.
5. **Refactor and graduation:** delete obsolete aliases/format cases/tests, publish public/internal reset and text-semantics docs plus DBML and governed release fragment; record measured trade-offs in an unused ADR path and a reusable journal finding. End with required CI evidence for the implementation head.

Red checks are run before Green, not forbidden until phase end. Target a class during development with `dotnet test --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --configuration Release --treenode-filter '/*/*/*LocationTextNormalizationTests/*'`. Use the analogous persistence project/class filter for provider checks. At PR completion run the minimum Domain, Persistence, and Architecture project gates because all three are affected; layer economy is not permission to omit required suites.

One schema-changing vertical commit may contain multiple layers and generated files because splitting it would leave broken callers/model contracts. Independent tooling and durable-only documentation changes still receive separate atomic commits. Suggested semantic outcome: `refactor(discovery)!: store validated normalized location search text`, with a precise breaking footer, governed change fragment/Change-Id, exact refreshed paths, and no internal-skip claim that hides operator impact.

## 7. Exact Migration Families And Recovery

Verified current application artifacts to replace through EF generation:

| Engine | Application migration project / initial ID |
| --- | --- |
| PostgreSQL | `src/Explore.Persistence/Migrations/20260904200209_Init.cs` |
| SQLite | `src/Explore.Persistence.Migrations.Sqlite/Migrations/20260904200427_Init.cs` |
| SQL Server | `src/Explore.Persistence.Migrations.SqlServer/Migrations/20260904200600_Init.cs` |
| MySQL / MariaDB | `src/Explore.Persistence.Migrations.MySql/Migrations/20260904212444_Init.cs` |

Include each generated designer and application snapshot. Determine the correct configured design-time context/provider/namespace command from the current migration tooling before executing; `--startup-project src/Explore.API` alone is not a five-provider strategy. Never hand-edit generated files or predeclare a new timestamp. Inspect the actual model delta, not timestamp churn in a 1.7MB Init.

Do not touch nested Identity, Data Protection, or PrivacyErasureAuthority migrations without a model dependency. Pre-release breaking freedom authorizes the new design, not deletion of arbitrary local data. Remove/regenerate only verified unapplied development artifacts; otherwise explicitly select the disposable application database to recreate. Record prior backup or disposability, exact database target, and new migration IDs. Never erase a shared server volume or invent migration-history rows. Recovery is matching backup restoration or recreation with matching binaries; Git revert alone does not restore data.

## 8. Risks, Documentation, And Done

| Risk | Minimum acceptable fix |
| --- | --- |
| Critical — erasure resurrection/authority leak | Dedicated failing invariant first; real persisted-state and five-provider membership evidence. |
| Critical — silently narrowed text | Explicit raw/derived validation and mutation atomicity; no slicing. |
| Critical — wrong migration family/reset target | Verified four application assemblies, five-engine clean apply/idempotency, exact disposable target. |
| Major — ICU drift | Coordinated runtime check/rebuild policy; current-revision gating alone is insufficient. |
| Major — padding/encoding/order mismatch | Real literal-match corpus; provider-local order contract, no false ordinal portability claim. |
| Major — changing all ordinal ASCII fields | New scoped Unicode marker and unchanged identity/setup behavior verification. |

Documentation in the same PR: `schemas/islamu-event.md`, internal location/domain and operations anchors, relevant `docs/public/` self-hosting reset/search guidance, and `docs/internal/releases/changes/` fragment. Choose existing public pages by content rather than inventing paths. Preserve dual-doc separation: operator steps in public docs, implementation invariants internally.

Done means no scalar-token encoder/digest, complete accepted values retained, invalid input rejected before mutation/SQL, provider membership/privacy evidence passing, migrations generated for every application assembly, no unsupported old-format path, and operator/runtime-upgrade instructions verified. Smaller source and payload are benefits; never trade away validation, privacy, or truthful support boundaries.

## 9. Research Provenance And I-VSD Mapping

Official sources accessed 2026-09-06: [Unicode normalization](https://www.unicode.org/reports/tr15/) through Firecrawl CLI; [.NET globalization](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/globalization) through Context7 `/dotnet/docs`; [EF collations](https://learn.microsoft.com/en-us/ef/core/miscellaneous/collations-and-case-sensitivity), [CHARINDEX](https://learn.microsoft.com/en-us/sql/t-sql/functions/charindex-transact-sql), and [MySQL binary collations](https://dev.mysql.com/doc/refman/8.4/en/charset-binary-collations.html) through Tavily MCP. Firecrawl MCP was unavailable. No implementation source/assets are retained; no dependency added. The helper decomposition and retained domain/persistence lifecycle are independently chosen from repository needs.

- IVSD-F002 / IVSD-M002 → runtime drift policy and focused tests, UNI-11.
- IVSD-F003 / IVSD-M003 → complete bounded text/storage, UNI-03–UNI-04.
- IVSD-F004 / IVSD-M004 → accepted scripts, literal membership, derived PII and erasure, UNI-01–UNI-10.
- IVSD-F001/M001 and F005/M005 remain owned by the CI PR. Revalidate changed ownership and narrowed ordering claims before implementation readiness; do not inherit the old report's approval claim.
