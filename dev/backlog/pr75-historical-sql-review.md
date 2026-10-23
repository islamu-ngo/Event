> Preservation snapshot: 2026-10-22, PR75 handoff at 84fc67c7. This is dated supporting evidence, not a current passing analysis or executable commit plan. The main report is [PR75 deferred implementation](pr75-remaining-implementation-handoff.md). Resolve stale paths and compare current Git source before acting. Local ignored notes and logs are not required to retain the contracts below.

# PR75 SQL construction security audit

Date: 2026-10-18
Worktree: /home/amir/ISLAMU/Github/Event/.worktrees/event-api-security-remediation
Reference HEAD: 23c637859cd00eb4b1d142b335bad93fe1d62954
Input: dev/active/event-api-security-remediation/pr75-review-findings.json, sonar entries whose rule is csharpsquid:S2077.

## Additional verified bootstrap literal defect — 2026-10-19

Current working source revealed a separate apostrophe-only Text helper in
`PostgresConfigurationManifestBootstrapContract.Sql.cs`. Accepted Global login
names containing `$body$` or `$roles$` terminate generated outer SQL blocks.
Two real native installer tests reproduced42601 before the fix, not an earlier
authority failure. Complete E-string encoding (backslashes first, apostrophes,
then dollar as `\044`) now passes both cases with unchanged original connection,
transaction/backend and rollback-restored role/function/policy catalogs. Genuine
Global positive/body-drift/restoration control also passes. This is an installer
parsing defect; no arbitrary execution or privilege escalation was demonstrated,
and separately connected renamed-Global execution is not tested.

This finding is additional to the dated138-item scanner inventory. No scanner-key
association or external disposition was established; do not silently reclassify
one of its136 FP recommendations or claim all remaining current SQL is safe.
The independent five-image commit excludes canonical revision/generic-lock changes
and body-control positives requiring unstaged grant/status corrections.
Receipts: `pr75-persistence-verification-current.md`; native3 GREEN in
`pr75-bootstrap-deployment-literal-corrected.mQ3yAu.log`.

## Dated PR reconciliation — 2026-10-19

The original audit below describes its dated reference, not an assertion that its
old file locations or proposed fixes are still current. GitHub's Sonar check is
attached to HEAD `0992e28c794b7e0b68d90f03be16d295f7cdc269`, starting
2026-10-19T00:14:38Z and completing00:19:27Z. The public PR endpoint reports that
same analysis start time. The detailed compute-task endpoint returns401, so no
separate compute-task revision receipt is claimed.

Read-only current issues/search returned138 unresolved OPEN S2077 findings,
one complete page of138. The old/current key-set comparison has exactly two
removed keys and two new keys; all other136 keys match. The changed keys follow
the approved direct-ADO ownership extraction, not removal of interpolation:

| Current key and sink | Previous key and sink | Current source evidence and recommendation |
| --- | --- | --- |
| AaFP8gvupr3bo2gBvsEx; Database/ProviderPrimitives/UserTenantNavigationQuery.cs:25 | AaFLPmVlidhKYWVJGFfw; Repositories/UserTenantNavigationRepository.cs:29 | FP recommendation. Only the contract's fixed, delimited FunctionSql identifier is interpolated. The exact subject is separately bound as NpgsqlDbType.Uuid on line26; no request supplies SQL structure. Existing enrollment/body readiness still runs before the call. |
| AaFP8be1pr3bo2gBvsEu; Security/ProviderPrimitives/HumanTenantPreferenceDataOperations.cs:50 | AaFLPlbkidhKYWVJGFdX; Security/HumanTenantPreferenceStore.cs:88 | FP recommendation. Private ExecuteNativeAsync receives only the contract's fixed write/image identifiers from its two private call sites. Subject/tenant are typed UUID parameters and updated_at is TimestampTz on lines51–53. The original store transaction is retained; this classification is SQL-construction evidence, not a proof of the entire mutation contract. |

Paths above are relative to src/Explore.Persistence. The two relocated methods and
their fixed callers were inspected after graph lookup reported stale756aa4c metadata. No query was renamed,
hidden, obfuscated or suppressed to evade S2077. Request values remain parameterized;
SQL identifiers must remain separately delimited rather than value-parameterized.

The two real deployment-literal defects were corrected in committed
`1e68690d3` with complete escaped E literals. Their original finding keys
AaFLPk18idhKYWVJGE-q and AaFLPlDaidhKYWVJGFI4 nevertheless remain OPEN in the live
analysis. Native punctuation tests establish parsing before immutable installed-body
attestation rejects renamed enrollment; they do not authorize changed enrollment.
The navigation installer key ending GE-p remains a distinct FP recommendation.

The old catalogue key AaFLPlO6idhKYWVJGFRV is also still OPEN on analyzed HEAD;
the working-tree catalogue deletion is a pending cutover, not remote closure.
Thus current analyzed findings retain the original136 FP recommendations and two
source-corrected real-defect histories, with the two FP key replacements above.
No external disposition has been submitted; maintainer authorization/access and
fresh analysis remain necessary. Current receipt is outside the repository at
`/home/amir/.cache/agent-tmp/pr75-sonar-s2077-current.08h4z7.json`.

### Bounded independent current-source refresh

Two independent reviewers refreshed nine current findings, including the two
relocated sinks above. They inspected the six originally flagged files changed
in committed source since the original audit, together with the extracted data
operations. Their source-only conclusions are:

| Current finding | Rechecked boundary | Recommendation |
| --- | --- | --- |
| AaFLPk18idhKYWVJGE-p | Navigation installer compiles fixed source declarations and the validated namespace; no request or deployment-login value reaches this flagged assignment. | False positive |
| AaFLPk18idhKYWVJGE-q | Navigation readiness now uses complete escaped E literals; exact enrolled owner, signature and installed-body attestation remain. | Real defect corrected in source; live finding still OPEN |
| AaFLPlDaidhKYWVJGFI4 | Preference readiness uses the same complete literal encoding and retains both exact routine-body and owner/signature checks. | Real defect corrected in source; live finding still OPEN |
| AaFLPlISidhKYWVJGFLn | Localization status invokes a fixed delimited routine; the tenant is separately bound as UUID. Committed changes at this sink are formatting only. | False positive |
| AaFLPlISidhKYWVJGFLo | Localization readiness compiles a closed source catalogue with complete E literals; comparing a SHA256 with pg_proc.prosrc does not execute catalogue text. | False positive |
| AaFLPk0WidhKYWVJGE9Z | Publisher readiness compiles owner-declared bodies and fixed caller expressions, provider-quoted identifiers, encoded deployment strings and expected body hashes. | False positive |
| AaFLPk0WidhKYWVJGE9a | Publisher data read invokes its fixed delimited routine with separately bound tenant/principal UUID parameters. | False positive |

Together with the two relocated rows above this is nine findings, not a fresh
audit of all138. Transitive compiler/helper changes have separate extraction
reviews; an unchanged sink file alone does not prove unchanged behavior. Publisher
attestation currently consumes the pending ActorPublisherCatalogProof helper, so
its working-tree behavior must not be represented as already committed HEAD.
Neither reviewer changed source, ran native tests or wrote external dispositions.
Their evidence supports SQL-construction recommendations, not blanket approval of
all routine authorization semantics or a passing Sonar gate.

## Result and scope

All **138 distinct finding keys across 75 files** are accounted for below. **136** are false-positive recommendations with individual source evidence (133 ordinary FP, two explicitly boundary-dependent FP-BOUNDARY, one static native installation FP-DDL). **Two** are FIX-DOLLAR: real configuration-literal serialization defects in the tenant-navigation and tenant-preference readiness guards. These two are not demonstrated HTTP SQL injection or demonstrated arbitrary SQL execution; the concrete established consequence is accepted deployment login text breaking native SQL parsing/readiness. Do not close them as blanket safe interpolation.

No request-controlled SQL execution was established at any of the 138 sinks. This is a source audit, not an executed exploit test or a proof of every stored procedure's authorization semantics. The finding key identifies the precise flagged assignment; a defect elsewhere in its flow is called out separately rather than misclassifying every parameterized SELECT using that flow.

Only this ignored report was created. No product edits, suppression attributes/comments, tests, builds, commits, pushes or external Sonar writes were performed. Existing worktree changes belong to the parent track and were left untouched. Graph was unavailable by instruction. Repository source sufficed; no external documentation/search was needed, and the quota-blocked Tavily path was not retried. A helper delegation attempt was rejected by the nesting limit; all dispositions were completed in this track.

## Trust and syntax boundaries actually inspected

- **Identifiers:** HumanPrincipalSourceSql.cs:47-64 obtains schema from RelationalNamespaceOptionsExtension or the EF model, resolves model tables/columns, and calls ISqlGenerationHelper.DelimitIdentifier. IdempotencyReceiptSql.cs:16-17 and the import Installer partials use the same native helper. Delimited identifiers are structure, not values: do not replace them with value parameters or remove their quoting to satisfy the analyzer.
- **Deployment namespace:** Explore.Secrets/Database/PrimaryDatabaseConfiguration.cs:113-136,693-694 validates Schema against ^[A-Za-z_][A-Za-z0-9_]{0,62}$. PrimaryDatabaseProviderComposition.cs:111-112,129-133 carries the selected namespace into RelationalNamespaceOptionsExtension. EF table/column names are application-owned mapping metadata, not request fields. These boundaries matter for the two FP-BOUNDARY rows; quoted identifiers alone are NOT safe SQL string literals. Custom direct DbContext construction that bypasses the composition contract is not treated as adversarial request input.
- **Deployment login names:** PostgresNativeQueueEnrollment.cs:18-36 binds explicit role usernames and rejects blank/control/trim/length/duplicate names, but does NOT exclude dollar signs, backslashes or apostrophes. PrimaryDatabaseConfiguration.BindUsername:37-43 does not impose a portable-identifier regex on usernames. Therefore safe login serialization cannot be assumed from schema validation.
- **Readiness and DDL:** PostgresPrivilegeGuard.cs builds source-declared role/privilege matrices; its Text helper emits an E literal with escaped backslash/quote and octal dollar escaping. Contract-specific ExactBody/ExactBodies compilers compare encoded routine signatures and expected SHA-256 source-body hashes. A hash comparison against pg_proc.prosrc is not execution of catalog-provided SQL. Data is absent from the compiler inputs; request values are bound on the subsequent SELECT.
- **Dollar quoting is another parser layer:** an inner single-quoted login literal does not protect an enclosing DO $guard$ body from a raw matching dollar tag. The usual project-native Text helper removes raw dollar signs from embedded literal source using \044. The two FIX-DOLLAR helpers do not. Identifier quoting and string-literal escaping solve different problems; neither justifies ignoring enclosing delimiters.
- **Portable queries:** local Type/property inventories, private fixed-operation callers, generated placeholder names, and provider-only fixed suffixes were traced explicitly. A helper accepting string SQL is not automatically unsafe: HumanPrincipalSourceQueries.ReadRowsAsync is private and receives source-generated Queries/FallbackQueries/LocalQueries, while data is bound separately. Conversely, an internal method name is not used as evidence without inspecting its actual caller values.
- **Native static compilation:** native source builders translate source-owned query placeholders to PL/pgSQL parameter symbols. That is not text substitution into a supplied SQL query. At the flagged installation sink PostgresHumanAiModerationProposalTargetContract.cs:261, the routines, signatures, arguments, source-column vocabulary and grants are locally declared; runtime IDs/JSON are not compiled as SQL. Detailed evidence is in its per-key row.

Paths in the preceding bullets are under src/Explore.Persistence unless another project is explicitly named. Per-file headings below give full repository-relative paths; row line numbers point to that exact file. References to an Installer, Attestation or other partial mean the same class prefix in the same directory, not a test double.

## Bounded fixes to carry into product implementation

### Required: correct literal serialization in two native contracts

Allowed future product scope for this issue is exactly:

1. src/Explore.Persistence/Security/ProviderPrimitives/PostgresUserTenantNavigationContract.cs: the apostrophe-only Literal helper at191 and its embedded-login callers at141-165,173; readiness sink81 and installer body53-64.
2. src/Explore.Persistence/Security/ProviderPrimitives/PostgresTenantPreferenceContract.cs: the apostrophe-only Literal helper at227 and embedded-login callers169,262-279; readiness sink109 and installer bodies72-92.

Use the existing project-native full SQL-literal pattern, not regex filtering, renamed wrappers or splitting command text to hide S2077:

~~~csharp
private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
    .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
~~~

The snippet describes the desired C# helper (ordinary C# escapes, not JSON); apply it as real C# in the future product patch. Convert surrounding quoted interpolations from e.g. '{Literal(enrollment.RuntimeLogin)}' to the complete literal expression {Text(enrollment.RuntimeLogin)}. Convert the other Literal callsites in those two files consistently, including signatures and source-body generation, then remove the obsolete helper. Do not put an E prefix on a value without also escaping backslashes. Keep DelimitIdentifier/Quote for identifiers and keep all subject/tenant/timestamp values parameterized on ordinary calls.

Concrete supported input to queue for later verification: a distinct deployment RuntimeLogin containing the literal characters $guard$ (for example runtime$guard$). Enrollment accepts it, but the generated readiness command contains that tag inside a single-quoted login and therefore terminates its outer body early. A GlobalLogin containing $body$ or $ddl$ similarly challenges generated installed bodies; apostrophe/backslash names must preserve their exact string identity. No malicious SQL payload needs to execute to establish this serialization bug. Changing standard_conforming_strings must not be required for correctness.

Do not attempt to bind @login inside a DO body: command parameters there are inside the body string, not top-level bind positions. The native literal compiler is the minimal source-native fix. If readiness is redesigned as top-level parameterized catalog SELECTs instead, that is a separate larger change and is not needed for this finding.

Native body identity changes when its generated text changes. Keep current exact-body and fail-closed enrollment checks; do not weaken hashes, silently normalize a drifted installation, or hand-edit generated migrations/snapshots to accommodate the fix. Use the task's controlled development enrollment/reinstallation path when implementing and verifying regenerated source bodies. No deployment was attempted here.

### Optional only: parameterize PDS privilege-check values

PostgresPdsSettlementContract.cs:77,125-131 wraps delimited signatures/tables and mapped column names in raw single quotes. With today's application-owned model names and validated schema, no untrusted string crosses this boundary: this is FP-BOUNDARY, not established exploitable injection. For clearer value/identifier separation, a bounded optional improvement is to generate command parameter names for signature/table/column arguments to has_function_privilege, has_table_privilege and has_column_privilege and bind those strings. Do not replace actual DDL identifiers with parameters; Permissions also supplies identifier-bearing GRANT SQL. Do not invent a generalized SQL-fragment API or ratchet model-name restrictions merely to clear Sonar.

### Deliberate Sonar review, not obfuscation

For each FP/FP-BOUNDARY/FP-DDL row, an authorized reviewer should record the concrete origin and bind/quote boundary from that row in Sonar's review workflow. This report makes recommendations only; it does not mutate Sonar or claim that local refactoring closes remote issues. FIX-DOLLAR rows stay open pending the bounded product fix and later execution evidence. Preserve the closed native routines, body attestation, source allowlists, retained transactions and SQLite write-reservation fences. Do not add NOSONAR, suppressions, analyzer exclusions, test-only SQL wrappers or string rearrangements that obscure the source-native construction.

## Per-key dispositions

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresAiActorContextReadContract.cs

FunctionSql:30 uses HumanPrincipalSourceSql.Name; Define:135-154 builds fixed model privilege/source/policy assertions. ExactBody is in the .Attestation.cs partial:11-28. Text:261-262 escapes backslash, quote and dollar. Runtime actor reader requires its original tenant Serializable transaction:64-68.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlORidhKYWVJGFQP | 52 | FP | Readiness DO inserts generated guards, source-body hash, fixed session_user expression and typed Guid tenant (50); enrollment names use Text, not request SQL. |
| AaFLPlORidhKYWVJGFQR | 70 | FP | Only quoted ai_actor_context_read identifier enters SQL; tenant and principal are Npgsql UUID parameters:71-72. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresAiConversationOwnerFenceContract.cs

FunctionSql/PrivacyFunctionSql:31-32 quote constant names; Define:149-175 builds source/model assertions; .Attestation.cs:11-29 quotes signatures and compares body hashes. Text:234-235 is dollar-safe. Fence:64-70 checks tenant, Serializable transaction and original connection.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlSGidhKYWVJGFUZ | 75 | FP | DO guard/body/caller fragments are native compiler output; privacyFence chooses two fixed calls:80-81; tenant/principal/actor/admitted/privacy/organizer are typed parameters:83-91. |
| AaFLPlSGidhKYWVJGFUY | 116 | FP | Fresh verification connection reads only metadata; RuntimeLogin is Text encoded:118; definition guard/exact-body fragments are source-native, not data supplied by caller. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresAtprotoModerationCompensationContract.cs

FunctionSql:32-34 DelimitIdentifier-quotes compensation prefix plus source-selected operation; RequireTransaction:94-98 requires an existing Serializable owner.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk0CidhKYWVJGE82 | 67 | FP | ReadPageAsync fixes operation to read; target is serialized JSONB and offset BIGINT:68-70. |
| AaFLPk0CidhKYWVJGE81 | 86 | FP | WriteAsync fixes operation to write; target and write payloads are JSONB parameters:87-90. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresPdsQueueAdmissionContract.cs

Private FunctionSql:88-93 derives the PdsSyncOutbox model schema and DelimitIdentifier-quotes the fixed claim function; owner is worker data, not an identifier.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlHgidhKYWVJGFKz | 70 | FP | Function identifier only is interpolated; batch, owner, timestamps and UUID token array are bound with explicit Npgsql types:71-75. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresPdsSettlementContract.cs

Permissions:84-132 has a source-declared Type/property/privilege matrix; table names are EF metadata delimited at117, columns are mapped at118. Functions:62-68 are fixed disclosure operations. Schema:136-138 is deployment namespace, normally validated by PrimaryDatabaseConfiguration:134-136,693-694.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlNfidhKYWVJGFPU | 78 | FP-BOUNDARY | Checks:125-131 and77 put delimited table/signature strings and raw mapped column names inside single-quoted literals without separate literal encoding. Current source-owned model names and validated ASCII schema contain no quote/backslash, and no request supplies them. Not general-purpose safe quoting; parameterizing these privilege-function arguments is optional hardening, not proof of current request injection. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresPublicationFanoutExecutionContract.cs

Define in .Sql.cs:14-23 takes only context/enrollment; Guard and ExactBody in .Installer.cs:92-114 use privilege declarations, encoded signatures and body hashes; Text:127-128 is dollar-safe. Execution retains claim, connection and transaction:98-102.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlLoidhKYWVJGFN8 | 89 | FP | Fixed native readiness block plus quoted publication_fanout_execution name; claim/request JSON and even operation are data parameters:93-95, not SQL fragments. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresOutboxExecutionContract.cs

Routine:23 is constant. Guard in .Sql.cs:202-218 builds fixed privilege/nested-routine assertions. ExactBody:158-163 compares an encoded signature and generated body hash; Text:155-156 escapes dollar quoting.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlD2idhKYWVJGFJY | 117 | FP | Only guard/exact-body output and quoted constant routine enter SQL. Claim, notification, operation, status, error, retryable, delay, failed time, expected and replacement are bound through Add:124-133. |

### src/Explore.Persistence/Database/ProviderPrimitives/LocationGovernanceCorrectionNativeOperations.cs

PostgresLocationPrivacyGovernanceContract.RetainReceiptFunctionSql:31 calls Names.Function; Names:195-203 uses ISqlGenerationHelper.DelimitIdentifier with fixed location_privacy_governance_ prefix. Operations.For:15-16 selects PostgreSQL.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPmLWidhKYWVJGFeo | 22 | FP | Fixed retain_receipt function identifier; @tenant/@message/@audit are DbType.Guid parameters:24-35 on the current transaction. |

### src/Explore.Persistence/Privacy/ErasureAuthority/ProviderPrimitives/PostgresSubjectPrivacyErasureOperations.cs

PostgresSubjectPrivacyErasureContract.FunctionSql:28-30 quotes prefix+operation. Actual callers PostgresSubjectPrivacyErasureSettlement.cs:34,56 pass prepare/dispose literals; operation is not a subject field.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPnt0idhKYWVJGFjP | 18 | FP | Only quoted routine name and HasValue-selected fixed argument suffix enter SQL. Intent, subject, sequence, policy, reason, recorded/applied timestamps are explicit typed parameters:21-28. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresLocalStorageQuarantineContract.cs

RequireReadyAsync:38-185 locally builds predicates from fixed roles, read-column inventory, functions and EF metadata. Table/Column helpers use DelimitIdentifier; Text at file end doubles quotes. This sink is top-level SELECT, not an enclosing dollar-quoted DO.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlFkidhKYWVJGFKm | 187 | FP | Every predicate is generated inside this method. Function signatures and expected bodies use parameters:116-117; catalog relation/column names use Text; function volatility/public booleans are fixed definition metadata:97-115. No caller-supplied predicate string. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresStorageProbeSourceContract.Owner.cs

FunctionSql is in PostgresStorageProbeSourceContract.cs:32-33, using HumanPrincipalSourceSql.Name. Owner method:21-25 binds retained connection/transaction and Serializable acquisition.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlWmidhKYWVJGFY- | 30 | FP | Acquire bool selects literal acquire or observe identifiers; selected tenant is UUID parameter:31-34. |

### src/Explore.Persistence/Database/ProviderPrimitives/PublicEventDiscoveryProviderOperations.cs

ReadAsync is private:135. Its callers pass fixed discovery operation names (e.g. page_local:36, seek_local:56). PostgresPublicEventDiscoveryContract.FunctionSql:31-35 also validates the closed Operations list before Name quoting; AdmissionSql:62-79 compiles model/enrollment guards and typed tenant.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPmOmidhKYWVJGFfO | 143 | FP | Admission block is native generated SQL, not specification text. SELECT uses a validated quoted routine; request (including compiled clauses/sort/cursor) is JSONB and tenant UUID:145-146. |

### src/Explore.Persistence/Database/ProviderPrimitives/PublicEventResponseClockProviderOperations.cs

Private local ReadAsync:60 is on binding.RequireSerializableOwner and retained transaction:63-66,80-84. PostgresPublicEventResponseClockContract.FunctionSql:24 quotes its constant; AdmissionSql:49 onward builds fixed guards/encoded enrollment and Guid tenant.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPmL5idhKYWVJGFeu | 67 | FP | Readiness block and quoted fixed function are structural. operation, now, threshold, address and coordinates are parameterized along with tenant:69-74; operation is not a routine suffix. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresPrivateEventManagementContract.cs

FunctionSql:49 uses Name of constant private_event_management_read; Define:98-120 builds fixed privilege/model/policy guards; ExactBody:158 onward checks generated body hash. Text:172-173 is dollar-safe. Read requires exact Runtime tenant Serializable owner:64-68.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk9JidhKYWVJGFES | 73 | FP | DO includes generated guard, hashed source body and encoded Guid caller tenant. Principal, kind, event/session IDs, page, size and type remain Npgsql parameters:81-88. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresPublicEventDetailContract.cs

FunctionSql:30 quotes fixed public_event_detail. AdmissionSql compiles readiness from context/enrollment; ExactBody:200 onward hashes generated expected body. Text:214-215 escapes dollar. binding.RequireSerializableOwner:75,86 retains owner.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk1NidhKYWVJGE9_ | 80 | FP | Admission SQL plus quoted fixed detail function; tenant/id/code bound:82-84. Publicly supplied code never enters SQL syntax. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresRegistrationPrivateReadContract.cs

FunctionSql:71 quotes fixed registration_private_read. Define:129-150 builds closed privilege/source guards; ExactBody:263 onward and Text:278-279 protect native readiness. Original tenant Serializable transaction checked:88-92.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlDtidhKYWVJGFJO | 97 | FP | DO uses generated assertions and typed tenant text. All request selectors, IDs, token hash, management flag and timestamp use typed Add parameters:105 onward; no ticket/hash concatenation. |

### src/Explore.Persistence/Security/ProviderPrimitives/ConfigurationManifestBootstrapData.cs

Relations:39-56 is generated from fixed Types:22-30 and EF property/key metadata, with DelimitIdentifier for every table/column. SqlitePredicate:170-213 selects fixed relation-tag branches and binds all scope values using Param:172-178.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk8YidhKYWVJGFDk | 144 | FP | Projection/order columns and table are model-derived quoted identifiers. Slugs, IDs, keys and outbox data in predicates become generated @pN parameters; caller JSON is never used as a predicate fragment. |

### src/Explore.Persistence/Security/ProviderPrimitives/ConfigurationManifestBootstrapProviderOperations.cs

ApplySqliteAsync builds relation from the bootstrap vocabulary, not imported SQL names. Param:68-86 converts row values using EF mapping and creates generated aN/bN placeholders. Relations quoting is ConfigurationManifestBootstrapData:39-56.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPkyNidhKYWVJGE7q | 89 | FP | INSERT uses quoted relation.Table/column.Sql and generated parameter placeholders. Every after value is bound by Param; ON CONFLICT is fixed SQL. |
| AaFLPkyNidhKYWVJGE7r | 92 | FP | UPDATE SET and null-safe preimage WHERE use quoted model columns and generated aN/bN parameters; before/after row strings are data. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanImportPublicationQueries.cs

ReadCommand and WriteRowAsync are private. Read callers:31,38 fix generic types and source-declared SystemProperties/TenantProperties; write callers:70-85 do likewise. Table/Column:109-120 use DelimitIdentifier; Parameter:122 onward binds values.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlK1idhKYWVJGFNR | 54 | FP | Projection from fixed property arrays; allowed keys become keyN parameters:53; optional tenant is separately parameterized:56. |
| AaFLPlK1idhKYWVJGFNU | 95 | FP | INSERT identifiers are model-derived; after values become generated afterN parameters:93. |
| AaFLPlK1idhKYWVJGFNV | 100 | FP | UPDATE identifiers are model-derived; beforeN/afterN values are bound. equal:99 is fixed provider choice IS NOT DISTINCT FROM or IS, not input. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportBrandingContract.cs

FunctionSql in .Installer.cs:49-51 quotes configuration_import_branding_ prefix plus source operation. WriteOriginalAsync constructs only write and validates retained source ownership; current branding payload is application data.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk-WidhKYWVJGFFZ | 102 | FP | Only fixed write routine identifier enters SQL; tenant/id/stamps/key/schema/defaults/payload/actor/time are typed parameters:106-116. JSON is not native DDL. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportEventResourceContract.cs

Private ExecuteAsync callers:59,143 select read/write and construct parameter names as literal current/original/actor/at:144-147. FunctionSql in .Installer.cs:48-50 quotes the fixed resource prefix. ValidateOwner:177,188 retains original transaction.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlJNidhKYWVJGFMR | 181 | FP | Interpolated parameter names are internal literals from the two callers, not manifest fields. All parameter values are added:183-184; operation is quoted as a single identifier. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportLegalMutationContract.cs

FunctionSql in .Installer.cs:93-95 quotes configuration_import_legal_mutation_ plus source-selected operation. Write validates original tracked mutation and retains origin.Connection/transaction:99-109.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlLLidhKYWVJGFNl | 110 | FP | Fixed write routine identifier; scope, tenant, kind, mapped document/version/source JSON and expected stamp/version/state are Npgsql parameters:115-127. Markdown/source content is serialized data. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportLegalSourceContract.cs

FunctionSql in .Installer.cs:38-39 quotes constant function name using model schema. ValidateOwner:130 onward requires sealed PostgreSQL instance-import owner, not arbitrary SQL caller.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk8EidhKYWVJGFDS | 43 | FP | Function identifier is the only dynamic syntax; scope enum is Integer and tenant UUID:50-52; returned markdown never becomes SQL here. |
| AaFLPk8EidhKYWVJGFDQ | 117 | FP | Count query interpolates quoted constant function and compile-time LegalDocumentScope.Instance integer cast; NULL::uuid is fixed SQL. No runtime string data. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportPaidPolicyContract.cs

Private Command:235 callers:36,55,119 select read/page/write. Parameter names are literal tenant, offset, limit, revision, expected_id, expected_version, actor, at. FunctionSql in .Installer.cs:78-80 quotes fixed prefix and selected operation.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk_lidhKYWVJGFGW | 240 | FP | Argument syntax comes only from source-created NpgsqlParameter names; policy payload, money-policy values and cursors are bound:36-57,119-125,242-243. Caller cannot supply an argument-name list. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportPublicationPolicyContract.cs

Private ExecuteAsync callers:46-49 and131-135 select read/write, constructing names tenant/after/limit/mutations/actor/at. FunctionSql in .Installer.cs:48-50 quotes fixed publication prefix; ValidateOwner:166 onward seals instance-import ownership.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk-gidhKYWVJGFFc | 185 | FP | Internal literal parameter names form placeholders; tenant/cursor/mutation JSON and time remain bound Npgsql values:187-188. No setting key or value becomes SQL syntax. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportReceiptContract.cs

Private ExecuteAsync is reached from store_operation:54 and store_effect:93 only. FunctionSql in .Installer.cs:82-84 quotes receipt prefix; mapped dictionaries:272 onward contain EF-column-keyed values serialized as JSON, not SQL.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk8PidhKYWVJGFDW | 262 | FP | Quoted source-selected function plus fixed @value; full receipt dictionary is JSONB parameter:263. Retained transaction/connection checked after execution:267-269. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportScalarSettingContract.cs

FunctionSql in .Installer.cs:58-60 quotes fixed scalar prefix. Upsert caller checks registry/value and metadata before dispatch:35-51; external scalar content stays data.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlJDidhKYWVJGFMO | 56 | FP | Only upsert identifier is interpolated. Key/value/type/description/category/lock/allowed-values/order/id/actor/timestamps are typed parameters:61-72. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportSnapshotContract.cs

Private ReadAsync:116-125 seals instance-import owner. Callers in this file:38,58,78 use settings/documents/policy; Bootstrap and Tenants partials add fixed bootstrap/tenant_slugs operations. FunctionSql .Installer.cs:28-30 quotes identifier. BootstrapFunctionAuthorityPredicate .Bootstrap.cs:56-68 has only constant role substitutions.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlT1idhKYWVJGFWL | 135 | FP | Optional predicate is source-native fixed catalog SQL selected by operation pattern, not expectedBody text. Signature is @signature:137-138; expected body is compared in C#:142, never executed. |
| AaFLPlT1idhKYWVJGFWM | 148 | FP | Operation becomes quoted routine name; optional keys controls a fixed suffix. Tenant UUID and text-array keys are bound:149-152, validated against catalog:104-113. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportTenantIdentityContract.cs

Private ExecuteAsync callers:38-42,61-62,73-82 select write/fence/finalize and literal parameter names. FunctionSql .Installer.cs:48-50 quotes tenant-identity prefix. ValidateOwner:110 requires original transaction.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk6-idhKYWVJGFCq | 113 | FP | Tenant displayName.Trim is a text parameter, not command text; tenant/actor/time and finalization UUID/bool/BIGINT arrays are bound. Generated placeholders come only from internal literal names. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationImportTenantSettingContract.cs

FunctionSql .Installer.cs:48-50 quotes tenant-setting prefix. Batch:before lines74 is constructed from retained original settings; ValidateOwner and reference checks:83-85 enforce original connector.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk7bidhKYWVJGFC- | 74 | FP | Only upsert routine identifier is dynamic; tenant, serialized writes, actor and represented time are UUID/JSONB/TimestampTz parameters:75-79. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationManifestBootstrapContract.cs

Private CallAsync:357 callers:111,119,128-130,141-142 use source/validate_source/write/write_failed and literal names scope/before/changes/after. Function:336-338 quotes bootstrap prefix in fixed Relations[0].Schema. RequireOwner:346-353 requires original nonhuman Global Serializable transaction.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlEBidhKYWVJGFJh | 362 | FP | Parameter-name interpolation is from internal tuple literals only. Scope and image JSON strings are all JSONB parameters:363-364; no arbitrary SQL compilation of scope input. |

### src/Explore.Persistence/Database/ProviderPrimitives/TypedSettingsSqliteWriter.cs

TypedSettingsSqliteWriter.WriteAsync gets Type and JsonObject, not a table-name string. HumanPrincipalSourceSql.Table/Column:50-64 resolve EF mappings and quote them; after/before payload does not supply identifiers.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPmG2idhKYWVJGFeU | 31 | FP | DELETE interpolates only mapped table and Id column; target Guid is @id added:29. Dynamic Type can select only mapped metadata, not escape SQL grammar. |

### src/Explore.Persistence/Security/ProviderPrimitives/MixedSettingsDataImage.cs

Read types:24-36 and typed plan/governance type arrays are source-owned. HumanPrincipalSourceSql quotes identifiers. Predicate:60-74 receives only @tenant/generated @keyN at115; PlanPredicate receives literal @assignment:112. Governance partial:14-23 embeds only code constants.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlXVidhKYWVJGFZ4 | 116 | FP | SELECT projection/order metadata quoted; keys, tenant and plan assignment are bound:98-110. Neither original.Values.Keys nor request predicates are raw SQL. |
| AaFLPlXVidhKYWVJGFZz | 170 | FP | ReadEffect first checks SQLite and WriteTypes:164-165; model property columns quoted; row identity is DbType.Guid parameter:172-176. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresEmailDeliveryPolicyContract.Controls.cs

ControlsFunction:25-27 DelimitIdentifier-quotes fixed control name plus literal suffix; RequireControlTransaction:74-78 requires existing mutation transaction.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlOcidhKYWVJGFQZ | 35 | FP | Literal _require_mutation suffix; tenant UUID parameter:36-37. |
| AaFLPlOcidhKYWVJGFQb | 47 | FP | Fixed controls routine; tenants UUID-array parameter:48. |
| AaFLPlOcidhKYWVJGFQa | 67 | FP | Literal _apply suffix; changes JSONB, now TimestampTz and actor UUID parameters:68-70. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresEventResourceProviderContract.cs

FunctionSql:38-39 calls HumanPrincipalSourceSql.Name. Readiness Guard and ExactBodies are in .Installer.cs:201 onward,336-342; Text:124-125 is dollar-safe. Private ExecuteAsync callers:78,82,86,90 fix acquire/apply/verify/complete and before/expected placeholder names.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPkycidhKYWVJGE70 | 57 | FP | DO contains locally generated privilege and body-hash assertions, with encoded enrollment/model literals; no original request/image text. |
| AaFLPkycidhKYWVJGE71 | 72 | FP | Only quoted fixed read function, no arguments or caller strings. |
| AaFLPkycidhKYWVJGE72 | 101 | FP | action and placeholder parameter are private source literals; original request and before/expected image are JSONB parameters:102-103. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresMixedSettingsContract.Typed.cs

FunctionSql in main .cs:55-56 quotes mixed_settings_ prefix. All typed operation choices are private literals/boolean branches; Text there:172-173 is dollar-safe. Readiness verifies generated native bodies before each call.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlFPidhKYWVJGFKe | 69 | FP | before presence selects typed_read/typed_acquire and fixed optional placeholder. Actor/provider/account/tenant/stamp/session/resolved/request/before are typed parameters:71-81. |
| AaFLPlFPidhKYWVJGFKd | 92 | FP | Fixed typed_data identifier; tenant UUID and serialized original JSONB bound:93-94. |
| AaFLPlFPidhKYWVJGFKf | 105 | FP | Fixed typed_compile identifier; original/effects JSON and tenant are parameters:106-108. Compile refers to domain effect compilation, not execution of caller SQL. |
| AaFLPlFPidhKYWVJGFKi | 127 | FP | Private callers:115,119 pass typed_write/typed_complete. payload/tenant bound:129-132. Conditional typed_compile clause is fixed source text (not reachable from those callers), not caller SQL. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresMixedSettingsContract.cs

FunctionSql:55-56 quotes mixed prefix; .Attestation.cs:169-174,307-312 builds fixed guard/body hashes. Text:172-173 encodes dollar. Native compiler inputs are context/enrollment; requests/effects are separate data parameters.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlRNidhKYWVJGFTj | 81 | FP | Ready routine gets tenant UUID parameter:91-92 outside DO (DO cannot receive it); migrator login uses Text:83,85; guard and body hash fragments are generated code. |
| AaFLPlRNidhKYWVJGFTk | 103 | FP | before presence picks read/acquire only; all human/account/session/request values parameterized:105-115. |
| AaFLPlRNidhKYWVJGFTo | 127 | FP | Literal data routine; tenant and serialized original JSONB parameters:128-129. |
| AaFLPlRNidhKYWVJGFTn | 139 | FP | Literal compile routine; request and accepted dictionary JSONB plus tenant bound:140-142. No input SQL string. |
| AaFLPlRNidhKYWVJGFTp | 153 | FP | Literal write routine; even slice selector is a Text parameter along with JSONB changes and tenant:154-156. |
| AaFLPlRNidhKYWVJGFTq | 166 | FP | Literal complete routine; tenant and expected JSONB bound:167-168. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresOperatorIdentityEditorContract.cs

FunctionSql:49-50 uses Name. .Attestation.cs:95-109,154-161 builds fixed catalog/body-hash assertions; Text:155-156 protects dollar quoting. Private ReadAsync callers:83,90,95 fix read/pending/audit with literal parameter names.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlSPidhKYWVJGFUj | 76 | FP | Admission DO is generated guard/exact-body code, not identity settings payload. |
| AaFLPlSPidhKYWVJGFUl | 114 | FP | Fixed write identifier; actor, before/after, audit, payload, occurred time and import boolean are parameters:115-123. |
| AaFLPlSPidhKYWVJGFUm | 138 | FP | Operation and parameter names are private literal callers, not stored/request names; Parameters.AddRange:139 binds every actual value. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresResolverConfigurationContract.cs

FunctionSql:38-39 uses Name with fixed resolver prefix; Sentinel is source constant. .Attestation.cs:81 onward,186-193 constructs catalog/body hashes; Text:146-147 is dollar-safe. RequireContext:138-143 binds ordinary tenant-null Global owner.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk9nidhKYWVJGFE4 | 66 | FP | Admission uses encoded Global/Migrator login names and generated assertions. Sentinel and read identifiers are quoted model/source names:73,76; no resolver address interpolated. |
| AaFLPk9nidhKYWVJGFE5 | 98 | FP | Fixed quoted read routine with empty argument list. |
| AaFLPk9nidhKYWVJGFE6 | 127 | FP | Fixed quoted write routine; before/effects JSONB, actor UUID and occurred time are parameters:128-131. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanProvisioningDataQueries.cs

Model.Inventory in ManagedProviderProvisioningDataModel.Inventory.cs:17-64 is fixed Type/key/column/update vocabulary. ManagedProviderProvisioningSql.cs:16-22 delegates identifier quoting to HumanPrincipalSourceSql and reads request fields via json_extract(@request, fixed path); Predicate:47-77 uses only code-selected expressions. Effect.Relation/columns:145-150 originate in the internal compiler, not JSON property names.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlS3idhKYWVJGFVi | 74 | FP | SELECT remainder projection/order uses fixed inventory and quoted mapped columns; NOT COALESCE predicate is internal SQL; entire serialized request bound as @request:77-80. |
| AaFLPlS3idhKYWVJGFVg | 169 | FP | SQLite INSERT uses relation-declared columns, mapped/quoted identifiers and generated @vN placeholders; every effect scalar is parameter.Value:163-166. |
| AaFLPlS3idhKYWVJGFVh | 177 | FP | SQLite UPDATE uses fixed relation.Update columns and quoted key; @vN values and @key are bound:163-178. |

### src/Explore.Persistence/Security/ProviderPrimitives/MachineMutationAuthoritySql.cs

Portable branch only after PostgreSQL dispatch:30-42. Table/C:58-62 use EF mapping and DelimitIdentifier. Predicate:63-64 consists of quoted fixed columns, @key/@key_id/@owner, NULL test and source enum integer; proof string fields never interpolate.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlELidhKYWVJGFJk | 74 | FP | Exact-row no-op UPDATE uses internally constructed predicate; three proof identifiers bound:67-72 on original Serializable transaction. |
| AaFLPlELidhKYWVJGFJl | 79 | FP | SELECT reuses same fixed predicate and existing bound proof parameters after footer fences; scope string is read as data, not parsed SQL. |

### src/Explore.Persistence/Security/ProviderPrimitives/MachineRoomMutationAuthoritySql.cs

Portable branch after PostgreSQL dispatch:29-41. Table/C:52-56 use EF mapping and DelimitIdentifier. Predicate:57-58 is fixed grammar with @key/@key_id/@owner and InstanceAdmin enum integer.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlWvidhKYWVJGFZA | 68 | FP | No-op UPDATE uses quoted mapped identifiers and bound proof identifiers:61-66. Original tenant Serializable owner checked:25-28. |
| AaFLPlWvidhKYWVJGFZB | 72 | FP | SELECT reuses fixed predicate and bound values after room fence; returned scopes/expires are data, not command construction. |

### src/Explore.Persistence/Security/ProviderPrimitives/PlatformApiKeySourceProjection.cs

SQLite projection uses HumanPrincipalSourceSql metadata identifiers and its private fixed query vocabulary. Both flagged statements are no-row write-reservation fences, not copies of external query strings.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlVqidhKYWVJGFYZ | 90 | FP | RolePermission table and RoleId column are mapped/quoted:89-90; WHERE 0 is constant; no data values interpolated. |
| AaFLPlVqidhKYWVJGFYa | 174 | FP | SupportAccessSession table and Id are mapped/quoted:173-174; WHERE 0 is constant; no user/session string in command. |

### src/Explore.Persistence/Security/ProviderPrimitives/PlatformApiKeySourceProviderQueries.cs

PostgresPlatformApiKeySourceContract.FunctionSql:31-32 maps bool acquire to two constants and HumanPrincipalSourceSql.Name. ExecutePostgresAsync uses captured connection and transaction:26-28.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk5iidhKYWVJGFA1 | 29 | FP | Transaction presence picks observe/acquire name only. Actor/provider/account/purpose/stamp/session/resolved are typed parameters:31-39; key/account values cannot become function names. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresLocalAdministrationContract.cs

FunctionSql:99-100 uses Name. .Attestation.cs:131 onward,282-288 compiles fixed privilege/body-hash checks; Text:199-200 is dollar-safe. Private InvokeAsync callers:153-180 select read/acquire/compile/apply/complete; operation is never a Request field.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlRXidhKYWVJGFTv | 144 | FP | Readiness DO uses generated guard and exact-body fragments; request data is absent from this SQL. |
| AaFLPlRXidhKYWVJGFTx | 191 | FP | Quoted routine from private operation literal; optional argument affects only fixed @argument suffix. Request/argument are JSONB:192-194; owner remains original Serializable transaction:186-188. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresManagedProviderProvisioningContract.cs

FunctionSql:18-19 quotes provisioning prefix. .Attestation.cs:118 onward,256-264 builds source catalog and body hashes; native Text helper encodes dollar. InvokeAsync explicitly checks Routines/public operation and before-shape:50-52.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlKPidhKYWVJGFM8 | 43 | FP | DO generated from context/enrollment and fixed native declarations; no provisioning request in ready SQL. |
| AaFLPlKPidhKYWVJGFM- | 61 | FP | Closed validated operation becomes quoted identifier; serialized request and before are JSONB parameters:62-63. Tenant-null Global owner checks:54-58. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresSupportAdministrationContract.cs

FunctionSql:64-65 quotes support prefix. .Attestation.cs:176 onward,327-333 compiles fixed assertions; Text:154-155 is dollar-safe. Private InvokeAsync callers:111-134 fix read/acquire/compile/apply/complete.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk1zidhKYWVJGE-j | 106 | FP | Readiness DO contains only generated guard and hashed expected body assertions. |
| AaFLPk1zidhKYWVJGE-n | 146 | FP | Quoted source operation, fixed optional placeholder; request and argument JSONB bound:147-149. No support reason/text enters syntax. |

### src/Explore.Persistence/Security/ProviderPrimitives/AccountMembershipNativeDefinitions.cs

PostgresHumanPrincipalSourceContract.Observe/AcquireFunctionSql:36-37 use Name of constants. AdmissionSql:54-55 builds fixed guard/hash output. This definitions adapter selects based on actual current transaction.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlTqidhKYWVJGFWF | 36 | FP | Only two quoted function identifiers are selected; actor/provider/account/purpose/tenant/stamp are typed parameters:40-46. |

### src/Explore.Persistence/Security/ProviderPrimitives/EventTeamNativeDefinitions.cs

PostgresHumanEventAssignmentSourceContract.Observe/AcquireFunctionSql:32-34 use HumanPrincipalSourceSql.Name. Adapter admits installation before reading:29-30 and after:42-44.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk2FidhKYWVJGE-z | 31 | FP | Transaction presence selects fixed observe/acquire; actor/purpose/tenant/event UUID array are parameters:34-39. No event name/list is joined into SQL. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanEmailSourceQueries.cs

PostgresHumanEmailSourceContract.Observe/AcquireFunctionSql:32-33 use Name; AdmissionSql:52-53 compiles fixed privilege/body assertions. HumanEmailSourceQueries chooses SQLite projection or native PostgreSQL, retaining connection owner.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlKridhKYWVJGFNO | 42 | FP | Fixed routine selection by transaction; actor/provider/account/purpose/tenant/stamp/session/resolved/route are all explicitly typed parameters:45-55. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanEmailSourceSqlite.cs

HumanPrincipalSourceSql.Table/Column quote EF identifiers; no-row UPDATE reserves SQLite write transaction and does not inject subject data. Subsequent source reads use separate fixed query builders and parameters.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk32idhKYWVJGE_2 | 34 | FP | RolePermission/RoleId mapped and quoted at33-34; WHERE 0 is constant with no external values. |
| AaFLPk32idhKYWVJGE_3 | 124 | FP | SupportAccessSession/Id mapped and quoted at123-124; WHERE 0 is constant with no external values. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanEventProviderSourceQueries.cs

PostgresHumanEventProviderSourceContract.Observe/Acquire/ValidateFunctionSql:32-35 all use Name of constants. The three-way branch depends on retained transaction and final flag, not request content. Portable EnsureProvider path:178-185 chooses SQLite fence.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk03idhKYWVJGE94 | 70 | FP | Only three fixed quoted routines; Parameters(command):75 binds event/provider operation/binding/connection/effect/submission/form/version/secret and actor/tenant. No secret or operation string is appended to SQL. |
| AaFLPk03idhKYWVJGE90 | 184 | FP | SQLite RegistrationForm/Id model identifiers quoted through sql.Names:183-184; WHERE 0 constant, no registration input. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresEventCreationPublisherReadContract.cs

FunctionSql:26 quotes constant event_creation_publisher_read; Define/SourceSql use fixed EF model and role source vocabulary. ExactBody .Attestation.cs:11 onward compares body hash; Text:249-250 is dollar-safe. ReadAsync requires exact tenant Serializable owner:62-65.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk0WidhKYWVJGE9Z | 49 | FP | DO generated from definition guard and exact-body plus encoded Guid caller tenant:47-52; no principal/data query string. |
| AaFLPk0WidhKYWVJGE9a | 68 | FP | Quoted constant routine; tenant/principal explicitly bound UUIDs:69-70. |

### src/Explore.Persistence/Repositories/UserTenantNavigationRepository.cs

PostgresUserTenantNavigationContract.FunctionSql:71-72 DelimitIdentifier-quotes fixed user_tenant_navigation_read and deployment schema. Repository native branch requires readiness:26-27; userId is method Guid, never identifier.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPmVlidhKYWVJGFfw | 29 | FP | Only fixed quoted navigation routine identifier enters command; @subject bound UUID:30. Separate readiness serialization defect is captured at navigation contract:81 below, not in this parameterized SELECT. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresUserTenantNavigationContract.cs

FunctionSql:71-72 safely quotes constant function. Build:113-180 creates guard from PostgresPrivilegeGuard plus a second string using Literal:191 (apostrophe doubling only). PostgresNativeQueueEnrollment.Validate:29-36 permits dollar/backslash in login names; those names come from explicit deployment configuration, not HTTP.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk18idhKYWVJGE-p | 28 | FP-BOUNDARY | Existence SELECT uses fixed role/function constants and Literal(model.SchemaName):33. Validated deployment schema is ASCII identifier (PrimaryDatabaseConfiguration:134-136,693-694); no login text or request enters this command. |
| AaFLPk18idhKYWVJGE-q | 81 | FIX-DOLLAR | DO $guard$ embeds model.Guard; additional guard at141-165 puts enrollment login names into apostrophe-only Literal. Accepted login containing $guard$ closes the outer body delimiter; readiness is syntactically unsafe for supported configuration. This is configuration-driven parse/availability failure; no HTTP injection or successfully executing arbitrary SQL is demonstrated. |

### src/Explore.Persistence/Security/HumanTenantPreferenceStore.cs

Private ExecuteNativeAsync:84 has exactly two source callers:33 passes PostgresTenantPreferenceContract.FunctionSql;59 passes ImageFunctionSql. Those use DelimitIdentifier:223-224. RequireOwner:70-81 checks genuine prepared mutation and original tenant-null Serializable lease.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlbkidhKYWVJGFdX | 88 | FP | function argument is not caller SQL: it is one of quoted write/image routines selected by private callers. Subject, tenant and updated time are typed parameters:89-91. Separate readiness defect is contract:109. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresTenantPreferenceContract.cs

Function/Image identifiers:98-99,223-224 are safely quoted. Guard:229-282 combines safe PostgresPrivilegeGuard output with additional raw single-quoted Literal(enrollment.*), where Literal:227 only doubles apostrophes. Enrollment permits dollar/backslash characters.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlDaidhKYWVJGFI4 | 109 | FIX-DOLLAR | DO $guard$ interpolates Guard, whose login literals:262-270 retain raw $guard$ if present in accepted deployment login. Fix literal serialization and native expected body generation; do not change SQL function names or parameterized preference writes. Parse/availability defect proved structurally; arbitrary execution not demonstrated. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanAdministratorCommitAuthoritySql.cs

PostgresHumanAdministratorCommitAuthorityContract:49-51 chooses three constant names; Function:80-81 DelimitIdentifier-quotes each. subjectOnly and tenant are booleans; scope supplies only data parameters.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlFEidhKYWVJGFKW | 30 | FP | Three source-selected quoted routines only; actor/provider/account/purpose/tenant are Npgsql parameters:36-43. AdmissionSql before call and RequireOwner after preserve source ownership, not raw SQL trust. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresGlobalLookupCatalogueContract.cs

FunctionSql:58 quotes global_lookup_catalogue. Source attestation in .Attestation.cs uses closed Lookup type/property inventory; Guard/body hashes and Text:147-148 encode enrollment, not catalog-provided executable text. RequireReady:100-106 requires original open backend and validates lease.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlO6idhKYWVJGFRV | 107 | FP | DO interpolates generated definition/ExactBody, fixed RestrictedCallerSql and Text(RuntimeLogin):112. Isolation/replication/tenant checks are fixed statements. No lookup label/data string becomes SQL. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresLocalizationSecretStatusContract.cs

FunctionSql:30-31 quotes localization_secret_status. Guard/ExactBody use context/enrollment, fixed Fields:26 onward and native body hash. Text:297-298 is dollar-safe; original lease/backend retained:75-84.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlISidhKYWVJGFLn | 62 | FP | Quoted fixed status function; selected tenant UUID parameter:63-64. No secret value or setting key interpolates. |
| AaFLPlISidhKYWVJGFLo | 92 | FP | Readiness DO contains only generated catalog/privilege guard and exact-body assertions; input enrollment literals use Text, with dollar escaping. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresOrdinarySettingsContract.Reader.cs

Main PostgresOrdinarySettingsContract.FunctionSql:52-53 quotes fixed prefix. Reader captures current tenant selection and calls readiness before query; request keys are serialized data, not property/SQL names.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk4VidhKYWVJGFAB | 38 | FP | Fixed read suffix, NULL::uuid fixed grammar. tenant UUID and distinct keys JSONB bound through Add:39-41. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresOrdinarySettingsContract.cs

FunctionSql:52-53 quotes ordinary prefix; .Attestation.cs:171 onward,307-314 compiles fixed guard/expected-body hashes; Text:318-319 is dollar-safe. Source observations/attempts enforce retained original ownership before every operation.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlZGidhKYWVJGFbW | 75 | FP | DO inserts generated guards/hashes, Text-encoded enrolled logins and typed Guid tenant:74-83. Anchor name is a quoted source constant, not a setting key. |
| AaFLPlZGidhKYWVJGFbX | 116 | FP | Fixed read identifier; source tenant/user UUID and keys JSONB bound:117-119. |
| AaFLPlZGidhKYWVJGFbY | 142 | FP | Fixed acquire identifier; tenant/user/request/before are typed parameters:143-154. Effects and new IDs are serialized JSON, not compiled SQL. |
| AaFLPlZGidhKYWVJGFba | 184 | FP | Fixed write identifier; even group/key/operation selectors are parameters, with before/after JSONB:185-189. |
| AaFLPlZGidhKYWVJGFbb | 217 | FP | terminal boolean selects literal complete/verify names; expected image JSONB bound:218. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresProviderConfigurationContract.cs

FunctionSql:47-48 quotes provider prefix. .Attestation.cs:88 onward,195-202 builds fixed guard/hashes. Text:168-169 is dollar-safe. RequireAttempt:151 onward demands original Serializable connector; setup/auth choices are source booleans/types.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlRqidhKYWVJGFT- | 78 | FP | DO contains source-native guard/hash checks, encoded Global/Migrator login and quoted source anchor; no provider configuration value. |
| AaFLPlRqidhKYWVJGFUA | 101 | FP | Literal read function; actor UUID parameter:102. |
| AaFLPlRqidhKYWVJGFUB | 119 | FP | Literal acquire function; actor UUID and retained before JSONB parameters:120-122. |
| AaFLPlRqidhKYWVJGFUD | 140 | FP | operation:136-137 is a closed concatenation of authentication_/authorization_ and setup/human, not input text. Actor/before/effects/time are typed parameters:141-146. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresStorageConfigurationContract.cs

FunctionSql:39-40 quotes storage prefix. .Attestation.cs:118 onward,232-239 compiles fixed guard/hashes; Text:131-132 is dollar-safe. RequireContext:123 onward and original represented Serializable attempt:100-106 constrain callers.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlYWidhKYWVJGFaq | 62 | FP | DO interpolates generated assertions, encoded MigratorLogin, quoted fixed anchor and Text(Guid ambientTenant):71; no storage root/provider address enters syntax. |
| AaFLPlYWidhKYWVJGFar | 85 | FP | Literal read routine; ambient UUID parameter:86. |
| AaFLPlYWidhKYWVJGFat | 110 | FP | Literal write routine; ambient/actor UUID, before/effects JSONB and time parameterized:111-115. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanAiModerationProposalTargetQueries.cs

PostgresHumanAiModerationProposalTargetContract.Observe/AcquireFunctionSql:39-40 use Name. AdministratorPredicate:45-58 receives source expression @actor from this sink, not subject text; it quotes EF tables/columns. Portable event/fence statements likewise use HumanPrincipalSourceSql.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlBjidhKYWVJGFHg | 57 | FP | Owner presence chooses two quoted routine constants; actor/selected/event are UUID parameters:59-61. |
| AaFLPlBjidhKYWVJGFHh | 72 | FP | Predicate helper actor argument is literal @actor; value is bound before query:71. Administrator role code/platform enum are source constants, not input concatenation. |
| AaFLPlBjidhKYWVJGFHi | 76 | FP | Event table/TenantId/Id/IsDeleted mapped and quoted; event Guid bound:78. Actor parameter retained from previous check is data only. |
| AaFLPlBjidhKYWVJGFHe | 138 | FP | SQLite Event/Id no-row reservation; identifiers mapped/quoted:133-138, WHERE 0 constant. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanWebhookResourceSourceQueries.cs

PostgresHumanWebhookResourceSourceContract:28-30 quotes constant observe/acquire through Name. Portable names use HumanPrincipalSourceSql; target webhook consumer/endpoint fields go only to Parameters, not function names.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlIIidhKYWVJGFLi | 65 | FP | Fixed branch-selected native routine and fixed placeholder list; Parameters:69 binds actor/purpose/tenant/operation/id/kind/owner/consumer/endpoint. |
| AaFLPlIIidhKYWVJGFLk | 139 | FP | SQLite no-row write reservation of WebhookConsumer/Id; metadata-quoted identifiers:134-139; no endpoint URI or webhook payload. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanEventAssignmentSourceQueries.cs

PostgresHumanEventAssignmentSourceContract:32-34 quotes constant observe/acquire. Portable HumanEventAssignmentSourceSql.Names delegates metadata quoting; event-list length affects separately generated placeholders, not this native command.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlYNidhKYWVJGFak | 63 | FP | Transaction presence selects quoted constant routine. Parameters:66 binds actor/purpose/tenant and UUID event array; no joined event data. |
| AaFLPlYNidhKYWVJGFam | 129 | FP | SQLite EventRoleAssignment/Id metadata-quoted no-row UPDATE:124-129; no role/user string in command. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanSupportAccessSourceQueries.cs

PostgresHumanSupportAccessSourceContract:27-29 quotes constant observe/acquire. Portable HumanSupportAccessSourceSql.Names delegates metadata quoting. Forwarding/session IDs are data parameters.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlE7idhKYWVJGFKQ | 62 | FP | Native observe/acquire branch only; fixed placeholder list; Parameters:65 binds actor/purpose/tenant/session/resolved. |
| AaFLPlE7idhKYWVJGFKS | 129 | FP | SQLite SupportAccessSession/Id metadata-quoted no-row UPDATE:124-129; no support reason/session token concatenation. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanPrincipalSourceQueries.cs

PostgresHumanPrincipalSourceContract:36-37 quotes constant routines. ReadRowsAsync is private:133; its caller:78-83 takes each element from new HumanPrincipalSourceSql(context,currentReads).Queries, then line130 closes that scope. Queries are fixed property/Type vocabulary using quoted mapping, enums/constants and placeholders. Local/fallback reads use the same internal builder family, not HTTP SQL.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk2-idhKYWVJGE_Y | 66 | FP | Transaction presence selects quoted observe/acquire; Parameters plus purpose Integer bound:70-71. |
| AaFLPk2-idhKYWVJGE_W | 136 | FP | sql argument is generated from the fixed builder at78-83, not a caller/query parameter. Only suffix is literal FOR UPDATE under MySQL transaction check. Parameters(command):138 binds actor/account/provider/tenant/stamp. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresStorageProbeSourceContract.cs

FunctionSql:32-33 uses Name with fixed storage_probe prefix; .Attestation.cs:105 onward builds guard, and exact-body checks are compiler-generated. Text:74-75 encodes dollar; RequireReady:53-57 requires already-open original backend.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlWKidhKYWVJGFYy | 58 | FP | DO contains generated guard/body assertions, encoded migrator login and fixed observe function; tenant expression is fixed current_setting cast, not a request SQL fragment. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresHumanAiModerationProposalTargetContract.cs

Observe/Acquire:24-25 are constants and selectors:39-40 use Name. Text:41-42 escapes dollar. Privileges:61-70, Guard, Body:189-210 and ExactBodies:214-223 use fixed model source vocabulary. Native replacement of @actor/@tenant/@event is source-to-source compilation of HumanEventAssignmentSourceSql.Queries, not replacement in supplied request text.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPlEVidhKYWVJGFJw | 235 | FP | Fresh metadata connection gets encoded RuntimeLogin plus AdmissionSql built from source guard/hash assertions:237-241; no private data string enters command. |
| AaFLPlEVidhKYWVJGFJx | 261 | FP-DDL | Actual installation SQL, not a request query: two constant routines with fixed Arguments/Signature:250-256; names/table/columns quoted, login/membership values Text-encoded, CREATE ROLE/GRANT uses format %I/%L:278-283, EXECUTE $ddl$ contains locally compiled definitions:302. No request payload or database-returned SQL body is executed. |

### src/Explore.Persistence/Security/ProviderPrimitives/HumanAuthorizationRouteQueries.cs

PostgresPolicySourceSetContract.AdmissionSql:51-52 and PostgresHumanAuthorizationRouteContract.AdmissionSql:221-222 generate guards/hash checks from context/enrollment, using dollar-safe Text and fixed role/model vocabularies. Route selectors:27-28 delegate HumanAuthorizationRouteSql.Name -> HumanPrincipalSourceSql.Name.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk2hidhKYWVJGE_L | 37 | FP | AttestAsync concatenates two source-native readiness blocks only; command.Parameters.Clear:36 proves no payload masquerading as a fragment. |
| AaFLPk2hidhKYWVJGE_M | 45 | FP | routine:42-44 is one of two quoted native constants; purpose Integer and tenant UUID bound:46-47. |

### src/Explore.Persistence/Security/ProviderPrimitives/PolicySourceSetProviderLock.cs

AcquireFunctionSql in PostgresPolicySourceSetContract:32,61-62 quotes constant routine. Table/Column helpers near end of this adapter resolve EF metadata and DelimitIdentifier; Sqlite path is explicit:44.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk0MidhKYWVJGE9K | 31 | FP | Quoted fixed acquire identifier; purpose enum and tenant UUID are Npgsql parameters:32-33. |
| AaFLPk0MidhKYWVJGE9M | 49 | FP | SQLite RolePermission/RoleId mapped identifiers with constant WHERE 0; neither purpose nor tenant is syntax. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresHumanPrincipalSourceContract.Session.cs

Session is a private constant in PostgresHumanPrincipalSourceContract partial family; HumanPrincipalSourceSql.Name:50 quotes it. Session owner method admits before/after call:53-54,63-64 and checks original connector via RequireOwner.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk_3idhKYWVJGFGY | 56 | FP | Only constant Session identifier is interpolated; authority.LocalSubjectId, SecurityStamp and tenant are typed UUID/Text/UUID parameters:57-59. Session stamp is never SQL. |

### src/Explore.Persistence/Security/ProviderPrimitives/PostgresIdempotencyReceiptContract.cs

Routines:35-45 is the closed receipt vocabulary. FunctionSql:50-51 delegates IdempotencyReceiptSql.Name:16-17 for metadata/schema quoting. .Attestation.cs:201 onward,265-272 builds fixed guards and body hashes; Text:239-240 is dollar-safe. CleanupAsync private callers:174,178 fix count/scrub.

| Finding key | Line | Disposition | Concrete evidence / action |
| --- | ---: | --- | --- |
| AaFLPk7xidhKYWVJGFDH | 72 | FP | Readiness DO inserts generated guard/hash and Caller fragments; login names use Text and tenant is formatted Guid or NULL:71. No receipt body participates. |
| AaFLPk7xidhKYWVJGFDJ | 99 | FP | Fixed claim function; tenant, proposal JSONB, nonce bytea and bound flag parameterized:100-105. |
| AaFLPk7xidhKYWVJGFDI | 116 | FP | Fixed read function; tenant, serialized request descriptor JSONB and timestamp parameters:117-120. |
| AaFLPk7xidhKYWVJGFDK | 132 | FP | Fixed complete function; tenant/id/nonce/status/body/content-type/time all typed parameters:133-139. Response body cannot enter SQL grammar. |
| AaFLPk7xidhKYWVJGFDM | 149 | FP | Fixed consume function; JTI Text and tenant/created/expires parameters:150-153. |
| AaFLPk7xidhKYWVJGFDN | 164 | FP | Fixed tenants function; cutoff/take parameters:165-166, maximum rows validated:161. |
| AaFLPk7xidhKYWVJGFDO | 186 | FP | Private count/scrub choices only; tenant/cutoff/take typed parameters:187-189, limit checked:183. |

## Accounting and execution evidence

- Input selection: exactly 138 S2077 records, 138 unique keys, 75 distinct source paths.
- Ledger: exactly one disposition row per selected key; 133 FP + 2 FP-BOUNDARY + 1 FP-DDL + 2 FIX-DOLLAR = 138. No omitted or invented key.
- Read-only commands executed: git status --short and git diff --stat were initially consulted with the repository's default fsmonitor and proved unreliable; they are NOT accepted as clean-tree evidence. After the parent's correction, git -c core.fsmonitor=false status --short and git -c core.fsmonitor=false diff --stat reported nine pre-existing modified paths (9 files, 62 insertions, 17 deletions at that observation). No repository configuration was changed.
- git check-ignore dev/active/event-api-security-remediation/pr75-sql-review.md succeeded and printed that exact path; git rev-parse HEAD returned the reference above. Native read/grep tools and read-only find navigation supplied source evidence. Input selection and report key-set accounting used the resident eval tool; no helper scripts were written or executed in the repository.
- The only file mutation in this track is creation of this report via apply_patch. The parent may continue editing other paths concurrently; a changing status receipt is not attributed to this audit.
- Tests/builds/database probes/restore, commits/pushes and external writes: NOT RUN by instruction. Parser consequences are static source reasoning, not a live PostgreSQL exploit receipt. Final assurance must run only after the parent lifts its deferred-verification instruction; useful checks then include original enrollment/readiness with delimiter-bearing role names, exact native body attestation after controlled regeneration, and the affected preference/navigation behavior. No new tests or product code were added here.
