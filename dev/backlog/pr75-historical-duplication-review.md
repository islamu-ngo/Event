> Preservation snapshot: 2026-10-22, PR75 handoff at 84fc67c7. This is dated supporting evidence, not a current passing analysis or executable commit plan. The main report is [PR75 deferred implementation](pr75-remaining-implementation-handoff.md). Resolve stale paths and compare current Git source before acting. Local ignored notes and logs are not required to retain the contracts below.

# PR 75 duplication gate investigation

Date: 2026-10-18.
Scope: read-only investigation; this ignored report is the only authored file.
Analyzed PR head and local HEAD: `23c637859cd00eb4b1d142b335bad93fe1d62954`.
Project: `islamu-ngo_Explore`; pull request: `75`.

## Latest live gate receipt — 2026-10-19

The original investigation below remains historical. Current public measures for
PR75 are **15,054 duplicated new lines / 327,453 new lines =4.59730098670649%**.
The required maximum remains3%; quality gate remains ERROR, with reliability4/D
and security5/E independently failing. No source-reduction estimate or local
cohort result establishes a passing remote gate.

GitHub attaches SonarCloud Code Analysis to HEAD
`0992e28c794b7e0b68d90f03be16d295f7cdc269`, start00:14:38Z/completion00:19:27Z.
The public PR-analysis endpoint reports analysisDate2026-10-19T00:14:38+0000.
This supersedes the earlier326,276 denominator; the numerator remains15,054.
The compute-task endpoint returns401, not a separately verified revision receipt.

Exact read-only checks executed with curl -fsS --max-time30 and jq:

- `/api/measures/component`, component=islamu-ngo_Explore, pullRequest=75,
  metricKeys=new_lines,new_duplicated_lines,new_duplicated_lines_density,
  new_security_rating,new_reliability_rating: response received/parsed, exit0.
- `/api/qualitygates/project_status`, projectKey=islamu-ngo_Explore,
  pullRequest=75: response received/parsed, exit0; duplication actual4.6,
  comparatorGT, threshold3.
- `/api/project_pull_requests/list`, project=islamu-ngo_Explore: response/parse0.
- `gh api repos/islamu-ngo/Event/commits/0992e28c794b7e0b68d90f03be16d295f7cdc269/check-runs`:
  Sonar head_sha and analysis timestamps above, exit0.

Coordination observation: GitHub's PR base is develop, but Sonar's PR metadata
reports base=main. This is an observed metadata mismatch, not established causation
for the duplicate percentage or permission to change the gate/baseline. CI/analysis
configuration ownership must investigate it separately; no configuration or external
disposition was changed here. The catalogue/federation working-tree cutovers and
their uncommitted helper changes are not included in this remote receipt.

## Result

The 5.1% result is primarily handwritten PostgreSQL security implementation
duplication, not generated snapshots. Sonar reports **16,722 duplicated new
lines / 325,488 new lines = 5.137516590473382%**, against a maximum of 3%.
Of the numerator, **11,432 lines (68.36%)** are in
`src/Explore.Persistence/Security/ProviderPrimitives/`. Another **3,175 lines
(18.99%)** are generated migration bodies. The remaining **2,115 lines
(12.65%)** are outside those two categories.

The appropriate refactor is to share PostgreSQL catalog/installation mechanics
inside Persistence, while each owning contract retains its exact source
inventory, principals, policies, SQL business body, signatures, fingerprint
namespace, and admission/transaction requirements. Moving security authorities
into one generic executor would trade a measurable code smell for a security
regression.

There is no evidence that a tiny patch can clear this gate. The eight largest
configuration-import installers contribute 2,628 duplicate-line flags in total;
even eliminating all of those flags would leave 4.33% before denominator shrink.
The smallest useful first patch is much smaller than the total gate remediation.
The bounded gate-capable candidate envelope below has 8,500 current duplicated
lines across 68 contributing files, but this is **capacity, not a prediction that
all 8,500 are safely removable**. A later implementation must measure the actual
result; no passing analysis is claimed here.

## Evidence identity and limitations

- `gh pr view 75 --json url,title,headRefOid,baseRefName,statusCheckRollup`
  returned PR title `fix(access)!: retain original authority across Event operations`.
- A narrowed `gh pr view 75 --json headRefOid,statusCheckRollup --jq ...`
  confirmed the head above and `SonarCloud Code Analysis = FAILURE`, completed
  `2026-10-17T19:03:31Z`; details:
  <https://sonarcloud.io/dashboard?id=islamu-ngo_Explore&pullRequest=75>.
- PR: <https://github.com/islamu-ngo/Event/pull/75>.
- `GET /api/qualitygates/project_status?projectKey=islamu-ngo_Explore&pullRequest=75`
  returned duplication `actualValue=5.1`, `errorThreshold=3`, comparator `GT`.
  Reliability and security ratings also fail independently, at `4` and `5`.
  Fixing duplication alone will not make the overall quality gate green.
- `GET /api/measures/component` returned the exact numerator, denominator and
  unrounded density above.
- Graph lookup was attempted first. Its metadata says it was built at
  `0808b697c6dd1f01245820d65dd285acb05b412e`, not this PR head. A qualified
  `ApplyAsync` lookup failed and the partial class lookup was ambiguous.
  Current literal caller searches and bounded source reads were used instead.
- Plain Git status initially reported clean because of stale fsmonitor state.
  This is **not** a clean worktree. Reliable subsequent commands used
  `git -c core.fsmonitor=false ...`; no Git configuration was changed.
- The worktree had nine existing modified files, 62 insertions and 17 deletions
  at the status observation. These include response capture, application
  handlers, hierarchical settings, the data-protection operator, its response
  capture tests, and the terminal-gui probe lockfile. They are not this
  investigation's edits. Sonar measurements describe the analyzed PR head,
  not those uncommitted changes.

## Complete measure reconciliation

Fetched all five pages of `qualifiers=FIL`, `ps=500`: **2,014 files**.
Fetched both pages of `qualifiers=UTS`, `ps=500`: **890 test files**.
The reconciliation is exact:

| Analyzed category | New lines | Duplicated new lines |
| --- | ---: | ---: |
| FIL, all five pages | 160,789 | 16,722 |
| UTS, page 1 | 82,858 | 0 |
| UTS, page 2 | 81,841 | 0 |
| Project total | **325,488** | **16,722** |

Do not substitute just the FIL denominator or just production C# lines for the
project's measured denominator when calculating this gate.

The sorted FIL query has **285 positive contributing files**, all within its
first 500 results. Their summed numerator is exactly the project numerator.
All remaining FIL entries and all UTS entries contribute zero.

| Positive-file category | Files | Duplicated new lines |
| --- | ---: | ---: |
| Persistence security ProviderPrimitives | 149 | 11,432 |
| Generated migration bodies | 12 | 3,175 |
| Application | 54 | 784 |
| Infrastructure | 29 | 640 |
| Other Persistence | 33 | 513 |
| Architecture tests analyzed as FIL | 2 | 92 |
| Release engineering | 2 | 40 |
| Event.Setup.Core | 1 | 18 |
| Standalone integration test analyzed as FIL | 1 | 18 |
| API | 1 | 6 |
| Event.Wire.Contracts | 1 | 4 |
| Total | **285** | **16,722** |

The tests with positive contributions have FIL measures; merely living below
`tests/` does not make them UTS in this analysis. No reclassification is proposed.

## Complete clone acquisition

For every one of the 285 positive files, fetched
`GET /api/duplications/show?key=<file-key>&pullRequest=75`.
Resolved each block's `_ref` through that response's `files` map. This yielded
**1,017 file-referenced clone groups**, or **429 distinct block-group arrays**
after sorting their `(path, from, size)` entries and removing exact repeated
arrays. This distinct-group count is the investigation's normalization, not a
separate Sonar metric. Some arrays are overlapping or nested subclones.

Clones include unchanged counterpart files, such as the MySQL init migration.
Their block sizes are not necessarily their *new*-line contributions. Do not add
raw clone sizes or all counterpart sizes to obtain the numerator: overlapping
ranges and pre-existing lines would be counted repeatedly.

The native, read-only acquisition command is reproducible without a token:

```bash
curl -fsS --get https://sonarcloud.io/api/measures/component_tree \
  --data-urlencode component=islamu-ngo_Explore \
  --data-urlencode pullRequest=75 \
  --data-urlencode metricKeys=new_duplicated_lines,new_lines \
  --data-urlencode qualifiers=FIL --data-urlencode ps=500 \
  --data-urlencode s=metricPeriod \
  --data-urlencode metricSort=new_duplicated_lines \
  --data-urlencode metricPeriodSort=1 --data-urlencode asc=false |
jq -r '.components[] |
  select(any(.measures[];
    .metric=="new_duplicated_lines" and (.periods[0].value|tonumber)>0)) |
  .key' |
xargs -P 8 -I '{}' sh -c \
  'curl -fsS --get https://sonarcloud.io/api/duplications/show \
    --data-urlencode "key=$1" --data-urlencode pullRequest=75' sh '{}' |
jq -s '{
  files:length,
  groupsReferenced:(map(.duplications|length)|add),
  groups:([
    .[] | . as $r | .duplications[] |
    [.blocks[] | {path:$r.files[._ref].name,from,size}] |
    sort_by(.path,.from,.size)
  ] | unique | sort_by(-(map(.size)|add)))
}'
```

This command was executed, including all 285 clone requests. Its initially
oversized terminal output was recovered with a compact summary rerun. No
analysis, configuration change, or Sonar write endpoint was invoked.

## Generated migrations versus handwritten source

These are the twelve positive generated migration body contributions:

| Path below `src/` | Duplicated new lines |
| --- | ---: |
| Explore.Persistence/Migrations/20261013110705_AllowRepeatedFailedConfigurationImports.cs | 761 |
| Explore.Persistence.Migrations.SqlServer/Migrations/20261013113433_AllowRepeatedFailedConfigurationImports.cs | 761 |
| Explore.Persistence.Migrations.Sqlite/Migrations/20261013111447_AllowRepeatedFailedConfigurationImports.cs | 483 |
| Explore.Persistence.Migrations.MySql/Migrations/20261013113654_AllowRepeatedFailedConfigurationImports.cs | 462 |
| Explore.Persistence/Migrations/20261017035216_ReconcileProgrammeModel.cs | 122 |
| Explore.Persistence/Migrations/20261009002501_PdsExactLineageActiveUniqueness.cs | 117 |
| Explore.Persistence.Migrations.SqlServer/Migrations/20261009002502_PdsExactLineageActiveUniqueness.cs | 117 |
| Explore.Persistence.Migrations.MySql/Migrations/20261009002501_PdsExactLineageActiveUniqueness.cs | 82 |
| Explore.Persistence/Migrations/20261007131053_AuthenticationCredentialCutoverReceipts.cs | 79 |
| Explore.Persistence.Migrations.SqlServer/Migrations/20261007131645_AuthenticationCredentialCutoverReceipts.cs | 79 |
| Explore.Persistence.Migrations.Sqlite/Migrations/20261009002500_PdsExactLineageActiveUniqueness.cs | 70 |
| Explore.Persistence.Migrations.Sqlite/Migrations/20261007131355_AuthenticationCredentialCutoverReceipts.cs | 42 |

The PostgreSQL and SQL Server import migrations were inspected: their generated
`Migration.Up` sequences repeat model changes across providers, including refund
dispatch fields, outbox payload changes and provider-sync lineage fields. Sonar
ignores string literal differences, so provider-specific SQL type names do not
necessarily distinguish these sequences.

Concrete clone examples:

- PostgreSQL import migration `110:336` and SQL Server `100:336`.
- PostgreSQL import migration `448:300` and SQL Server `439:300`.
- MySQL import migration `462:216` and SQLite `420:216`.
- PostgreSQL exact-lineage migration `6:117` and SQL Server `6:117`.

Notation is `start:size`, not an inclusive ending line.
`components/tree` queries for `ModelSnapshot` and `Designer` returned zero
components for this PR. None appears among the positive contributors. That is
an observation about the analyzed tree, not a recommendation to add exclusions.
The `*.Installer.cs` and `*.Attestation.cs` contracts below are handwritten C# and
SQL generators, not EF migration output.

Leave all migration/snapshot output out of the proposed source refactor.
Hand-editing migrations or introducing a cross-provider migration helper solely
to make their generated output less repetitive is not justified. Even pretending
all 3,175 migration flags disappeared would leave 13,547 / 325,488 = 4.16%.
They cannot explain or solve the gate on their own.

## Root cause and ownership

The security programme correctly split independent capabilities into dedicated
owners, but duplicated the mechanics for proving each owner's installation:
role graph membership, effective privileges, relation/column shape, source RLS,
function metadata and body hashes, trigger properties, and install sequencing.
Sonar sees the repeated syntax even when role names, diagnostics and fingerprint
prefixes differ.

This mechanism is visible at two levels above the reported duplicate blocks:

1. `src/Explore.Persistence/Schema/ProviderPrimitives/ExploreDatabaseMigrator.cs:151`
   installs actor/publisher contracts separately; `:209-217` installs the
   independent import owners. Installation composition is already centralized.
2. Runtime operations remain capability-specific. `AiRepositoryNativeOperations.cs:19`
   calls actor-context reads, and `EventCreationPublisherNativeOperations.cs:19`
   calls publisher reads. `EventResourceSettingsWriter.cs:161,231,238,378`
   validates, observes originals and writes through the resource import owner.

Share emitted proof mechanics, not runtime owners. Existing
`PostgresPrivilegeGuard.cs` is the local precedent: its `Contract` contains
explicit role, routine, relation/column and membership declarations; its methods
emit assertion SQL without granting access or discovering an allowlist from live
ACLs. Its documentation explicitly leaves caller enrollment, body fingerprints,
RLS policies and business fences to the owning contract. Extend or compose this
style only where semantics actually match; do not replace a stronger local guard
with a weaker existing helper merely because both query `pg_catalog`.

## Concrete handwritten clone groups and refactors

All abbreviated filenames below live in
`src/Explore.Persistence/Security/ProviderPrimitives/`.
Contribution figures are Sonar's per-file duplicated *new* lines, not sums of
the possibly overlapping block sizes.

### Configuration-import installation

| Filename | Contribution |
| --- | ---: |
| PostgresConfigurationImportEventResourceContract.Installer.cs | 378 |
| PostgresConfigurationImportTenantSettingContract.Installer.cs | 372 |
| PostgresConfigurationImportLegalMutationContract.Installer.cs | 355 |
| PostgresConfigurationImportPaidPolicyContract.Installer.cs | 355 |
| PostgresConfigurationImportTenantIdentityContract.Installer.cs | 326 |
| PostgresConfigurationImportBrandingContract.Installer.cs | 321 |
| PostgresConfigurationImportPublicationPolicyContract.Installer.cs | 319 |
| PostgresConfigurationImportReceiptContract.Installer.cs | 202 |
| Other nine contributing configuration-import files | 172 |
| Family total, 17 files | **2,800** |

Resource/TenantSetting clones include `14:71`/`14:71`, `117:84`/`120:84`,
`242:59`/`246:59`, and `351:60`/`354:60`.
LegalMutation/PaidPolicy include `332:163`/`269:163` and
`129:100`/`111:100`. Branding/TenantIdentity include `246:135`/`245:135`.

Recommended first bounded patch: the Resource and TenantSetting installer pair
plus one internal configuration-import-specific SQL installation collaborator.
Their current numerator capacity is **750 lines**, not 750 guaranteed savings.
Retain both public contracts and their `ApplyAsync` guards. Share common role
graph, fixed-function ownership/ACL proof, fingerprint and policy-install
assembly through explicit owner-local declarations.

The important difference is visible at Resource installer `:85` and TenantSetting
installer `:85`: the resource owner can read/insert full SystemSetting rows and
update selected fields, whereas TenantSetting gets only `SettingKey,IsLocked`
SELECT there and no SystemSetting insert/update. TenantSetting additionally
asserts exact mapped TenantSetting properties. A common helper must not union
these grants, infer missing declarations, or erase that inventory assertion.

If expanded to the other import owners, keep legal decisions, paid-policy
ceilings, branding keys, identity fields, publication keys, receipt state and
read/write function bodies in their respective files. Do not turn those
independent mutation capabilities into one generic import authority.

### Actor, publisher and conversation catalog proof

| Filename | Contribution |
| --- | ---: |
| PostgresAiActorContextReadContract.Attestation.cs | 176 |
| PostgresEventCreationPublisherReadContract.Attestation.cs | 176 |
| PostgresAiConversationOwnerFenceContract.Attestation.cs | 163 |
| PostgresEventCreationPublisherReadContract.cs | 126 |
| PostgresAiActorContextReadContract.cs | 125 |
| PostgresAiActorContextReadContract.Installer.cs | 77 |
| PostgresEventCreationPublisherReadContract.Installer.cs | 77 |
| PostgresAiConversationOwnerFenceContract.cs | 73 |
| PostgresAiConversationOwnerFenceContract.Installer.cs | 23 |
| Family total | **1,016** |

The first two attestation files have a `10:176`/`10:176` clone; all three have
`49:137`/`49:137`/`53:137`. The smallest mechanical extraction is the
relation/column/index/FK catalog proof and caller-private-source privilege proof.
Keep each function's exact output argument names, modes and PostgreSQL types:
actor and publisher `ExactBody` methods have different record layouts despite
Sonar treating their syntax as a clone.

Preserve design-time identity metadata, C/default collation rules, key ordering,
btree/opclass checks, FK delete actions and enabled FK triggers. Preserve each
owner's source set and diagnostics. Leave Actor versus membership result
materialization and `MaximumActors` overflow behavior untouched.

### Administration attempt installation and seals

| Filename | Contribution |
| --- | ---: |
| PostgresLocalAdministrationContract.Attestation.cs | 260 |
| PostgresSupportAdministrationContract.Attestation.cs | 260 |
| PostgresLocalAdministrationContract.Sql.cs | 171 |
| PostgresSupportAdministrationContract.Sql.cs | 169 |
| PostgresLocalAdministrationContract.Installer.cs | 106 |
| PostgresSupportAdministrationContract.Installer.cs | 106 |
| PostgresLocalAdministrationContract.cs | 95 |
| PostgresSupportAdministrationContract.cs | 85 |
| PostgresLocalAdministrationContract.Compiler.cs | 10 |
| PostgresSupportAdministrationContract.Compiler.cs | 10 |
| Family total | **1,272** |

Installers clone `6:106`/`6:106`; attestation clones include
`127:163`/`172:163`; Sql files include `245:120`/`206:118`.
A narrowly named administration-attempt SQL collaborator can share bootstrap,
private table shape, completion tombstone erasure and deferred settlement proof.
Each owner must still supply its own roles, source/update inventory, original
source assertions, RequestGuard, Caller and compiler.

Do not extract their compilers for a 20-line contribution. Their effect behavior
is not identical: local apply returns `Outcome` plus `Image`; support apply
returns the image, and support enforces append-only audit writes. Preserve the
request/preimage comparison, revision increment, no-write-after-completion
checks, backend/transaction binding and commit-time deferred seal. No shared
runtime attempt table or cross-owner authority is proposed.

### Human original-source contracts

| Filename | Contribution |
| --- | ---: |
| PostgresHumanEventAssignmentSourceContract.Installer.cs | 248 |
| PostgresHumanSupportAccessSourceContract.Installer.cs | 242 |
| PostgresHumanWebhookResourceSourceContract.Installer.cs | 226 |
| PostgresHumanSupportAccessSourceContract.cs | 103 |
| PostgresHumanWebhookResourceSourceContract.cs | 101 |
| PostgresHumanEventAssignmentSourceContract.cs | 59 |
| PostgresHumanEmailSourceContract.Attestation.cs | 53 |
| PostgresHumanEmailSourceContract.cs | 53 |
| PostgresHumanPrincipalSourceContract.Installer.cs | 41 |
| PostgresHumanEmailSourceContract.Installer.cs | 20 |
| PostgresHumanPrincipalSourceContract.cs | 20 |
| Family total | **1,166** |

Assignment/Webhook installers clone `70:111`/`66:108`.
Assignment/Support/Webhook clone their policy/catalog region around
`115:66`/`114:63`/`111:63` and installation tail `198:64`/`194:63`/`191:63`.
Share source-column proof, fixed routine metadata proof and anchor/trigger
installation within the original-source protocol family. Retain purpose checks,
source-specific observation/acquisition/write SQL, separate revision anchors and
original caller connection/transaction requirements.

### Event read-projection catalog and tenant-policy compilation

| Filename | Contribution |
| --- | ---: |
| PostgresPublicEventDetailContract.Installer.cs | 275 |
| PostgresPrivateEventManagementContract.Installer.cs | 210 |
| PostgresRegistrationPrivateReadContract.Installer.cs | 210 |
| PostgresPublicEventResponseClockContract.Installer.cs | 169 |
| PostgresPublicEventDiscoveryContract.cs | 168 |
| PostgresPublicEventResponseClockContract.Attestation.cs | 105 |
| PostgresPrivateEventManagementContract.Graph.cs | 80 |
| PostgresRegistrationPrivateReadContract.Graph.cs | 78 |
| PostgresPublicEventDetailContract.cs | 69 |
| PostgresPublicEventResponseClockContract.cs | 69 |
| PostgresPublicEventDetailContract.Projection.cs | 56 |
| PostgresPublicEventResponseClockContract.Queries.cs | 49 |
| PostgresRegistrationPrivateReadContract.cs | 43 |
| PostgresPublicEventDiscoveryContract.Queries.cs | 38 |
| PostgresPrivateEventManagementContract.cs | 30 |
| PostgresPublicEventDiscoveryContract.Presentation.cs | 27 |
| PostgresPublicEventDiscoveryContract.Installer.cs | 25 |
| Family total | **1,701** |

PrivateManagement/Registration installers clone `78:159`/`76:159`.
Detail installer/ResponseClock attestation clone `137:104`/`11:104`.
Detail/Discovery/ResponseClock clone `173:68`/`185:68`/`47:68`.

Share model-derived tenant-parent predicate compilation, catalog shape and
read-policy fingerprint assertions. Retain source registration callbacks so
recursive parent traversal registers the exact required source set. Keep
null-tenant semantics, explicit public/private projections, principal visibility
and response-clock behavior owner-local. An abstract generic graph reader or
shared public/private result mapper is not warranted.

### Execute-only fleet-health installation

| Filename | Contribution |
| --- | ---: |
| PostgresTicketingRecoveryHealthContract.cs | 194 |
| PostgresWebhookLocalDeliveryHealthContract.cs | 187 |
| PostgresRefundReconciliationHealthContract.cs | 137 |
| PostgresQueueDrainHealthContract.cs | 27 |
| Family total | **545** |

TicketingRecovery and WebhookLocalDelivery source/observation guards were
inspected. Share execute-only health role/policy enrollment and source privilege
proof, not business predicates or aggregate output types. The webhook routine
takes an observation timestamp; ticketing recovery does not. Preserve the narrow
predicate-column grants, prohibition on Global ambient source reads, and
rejection of restrictive policies that could make an unhealthy fleet look empty.
Refund and queue-drain are clone-corpus candidates; their full business bodies
were not inspected and must not be silently folded into an identical contract.

## Gate arithmetic and bounded implementation scope

Let `R` be duplicate new-line flags removed and `delta` be net new lines removed
after adding shared helpers. The relevant condition is:

```text
(16,722 - R) / (325,488 - delta) <= 0.03
R >= ceil(6,957.36 + 0.03 * delta)
```

At an unchanged denominator the exact minimum is **6,958**, because 3% of
325,488 is 9,764.64 and an integer numerator must be at most 9,764.

| Net new lines removed | Required duplicate flags removed |
| --- | ---: |
| 0 | 6,958 |
| 3,000 | 7,048 |
| 6,000 | 7,138 |
| 10,000 | 7,258 |

The six families above contain 68 positive files with 8,500 duplicate flags and
16,272 measured new lines. If a future refactor removes **7,500** flags while
removing no more than those 16,272 new lines net, its worst-case modeled result
would be **9,222 / 309,216 = 2.9824%**. This is a conservative arithmetic
acceptance budget, not evidence that those exact savings can be implemented.

Recommended bounded scope:

1. Start with the Resource/TenantSetting importer installer pair, actor/publisher
   catalog assertions, and Local/Support administration bootstrap/seal helpers.
   These have directly inspected repeated mechanisms and clear independent
   owner-local declaration seams. Do not claim that this first slice clears 3%.
2. Expand those helpers only to the six named families and only for catalog,
   fixed-installation and original-connection plumbing that is actually
   equivalent. Main contract files may change mechanically; result mapping,
   business SQL and authorization decisions remain local. The 68-file count is
   a contributor envelope, not a mandatory list of files to edit.
3. Do not broaden to Application custom-property CRUD, AI transport providers,
   generated migrations, or the residual 81 provider-primitive contributors
   merely to accumulate savings. The six families have sufficient theoretical
   capacity; if equivalent-mechanism extraction cannot achieve the acceptance
   budget, report the measured shortfall before expanding scope.

The 81 residual provider-primitive files contribute 2,932 lines. Their existence
is part of the complete inventory, not authorization to refactor them. The
Application and Infrastructure groups are also real duplicates, but contribute
only 784 and 640 lines respectively and are not the root cause of this gate.

Architectural tradeoff: one shared proof builder means one defect could affect
several owners. Counter this by immutable source-declared contract inputs,
family-scoped helpers, no live-catalog allowlist discovery, no role unions and no
implicit optional guards. Leave the migrator's separate installation order and
each runtime admission path intact. Keep generated body hashes and fingerprint
checks consistent when the emitted SQL changes. Shared code does not justify
runtime re-enrollment, self-repair, reconnects, retries or changed isolation.

No source-only inspection can promise an exact post-refactor Sonar percentage:
overlapping clone groups can persist, shared helpers can remain clones of other
contracts, and changed/new-line attribution and denominator both move. A later
authorized analysis is the observable acceptance check. No analysis was started
or written by this task.

## Commands, outcomes and omissions

Executed read-only commands:

- `gh pr view 75 --json url,title,headRefOid,baseRefName,statusCheckRollup`
  and the narrowed Sonar check query: exit 0.
- `git rev-parse HEAD`: exit 0; matched the PR head.
- `git -c core.fsmonitor=false status --short` and
  `git -c core.fsmonitor=false diff --stat`: exit 0; existing changes recorded
  above. Earlier plain status was misleading and is not used as evidence.
- `git check-ignore dev/active/event-api-security-remediation/pr75-duplication-review.md`:
  exit 0; report is ignored.
- `git -c core.fsmonitor=false diff --check`: exit 0 at the observation.
- `git -c core.fsmonitor=false diff --no-index --check /dev/null dev/active/event-api-security-remediation/pr75-duplication-review.md`:
  exit 1 with no whitespace diagnostics; the no-index comparison detects the
  new report's content against an empty file. This is not reported as exit 0.
  The final status/check-ignore command sequence exited 0. Final status showed
  13 concurrently modified tracked files; the additional release-impact
  script/workflow/tests and deployment-configuration test are other work, not
  investigation edits.
- Native `curl -fsS --get` calls to Sonar's `qualitygates/project_status`,
  `measures/component`, fully paginated `measures/component_tree`,
  `duplications/show`, `components/tree`, and `webservices/list`:
  successful final requests returned exit 0.
- Initial attempts to sort new-code metrics using `s=new_duplicated_lines` or
  `s=metric` failed with HTTP 400. Sonar's error body requires `s=metricPeriod`
  plus `metricPeriodSort=1`; the corrected queries succeeded. No missing page
  was treated as a zero.

No product edits, exclusions, suppressions, quality-gate changes, configuration
changes, commits, builds, test runs, or external Sonar writes were performed.
No build or test result is claimed. This investigation intentionally stops at
evidence and proposed patch scope, as requested.

Official documentation was retrieved through Context7 for SonarQube Cloud's
Web API and duplication definitions. No Tavily request was made.

- <https://docs.sonarsource.com/sonarqube-cloud/appendices/web-api>
- <https://docs.sonarsource.com/sonarqube-cloud/managing-your-projects/metric-definitions>
- <https://sonarcloud.io/web_api/api/duplications/show>

The official duplication definition explains why renamed strings do not remove
clones: for non-Java code the default detection requires at least 100 successive
duplicated tokens over at least 10 lines; indentation and string literal
differences are ignored. The repair must remove repeated implementation, not
reformat or rename it.
