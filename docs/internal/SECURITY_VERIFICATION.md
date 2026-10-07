# Security Verification and Evidence Boundaries

The security workstream distinguishes source inventory, executed test evidence
and release admission. None can substitute for another.

## Source inventory

`eng/security/security-control-register.json` retains all 209 reference
identities, 53 functional families, 50 scenarios and 22 bug/vulnerability
classes. Initial source-assessment dispositions are:

| Initial disposition | References |
| --- | ---: |
| Protected | 95 |
| Vulnerable | 5 |
| Improvable | 25 |
| Unverified | 43 |
| Not applicable | 41 |

These are qualified findings from the maintained assessment at its recorded
source revision. They are not counts of passing security tests. Every runtime
final disposition remains explicitly unset until its owning boundary produces
reviewable evidence. Historical API-key issuance is a separate delivered
receipt, not closure of every integration-credential reference.

The catalogue maps references to current executable phases and scenarios.
Preserved historical task aliases are provenance, not a runtime compatibility
layer. Profiles, code/artifact identity, concrete execution, non-applicability
and operator evidence require their actual inventory and receipts.

Verify catalogue identity completeness and refusal of unearned closure:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- verify-security-register eng/security/security-control-register.json
```

This command verifies the source-inventory contract. It cannot certify a
candidate's security.

## Runtime applicability dimensions

The catalogue records three deployed compositions: dedicated API, split
browser/BFF with YARP, and combined Standalone with its in-process API bridge.
OpenAPI generation is a fourth composition case, not a deployed host.
Contract, RealRuntime, Stress and Security are independent test profiles;
their names do not remove Testing environment exclusions.

API schemes include the MultiAuth selector, Keycloak Bearer, Local identity
and replacement, API key, managed control plane, setup, ATProto transient /
bootstrap / session, privacy receipt and admission scanner authorities.
Browser Cookies and dynamically configured Keycloak, Google and ATProto
schemes have different consumers. Registration alone proves neither endpoint
acceptance nor current resource authority. Authorization-provider selection
(`local` / `cerbos`) is a separate dimension.

The five actual database compositions are PostgreSQL, SQLite, SQL Server,
MySQL and MariaDB. EF InMemory is a test substitution. Sharing a migration
assembly does not make MySQL evidence a MariaDB receipt; SQLite cannot prove
ordinary PostgreSQL-role isolation.

`EndpointSecurityInventory` captures the compiled graph after the real host
starts, retaining unnamed, non-controller and API-description-excluded
endpoints as well as ordered declared authorization metadata. API tests
capture both enabled and disabled MCP transport. The transport's anonymous
metadata does not grant anonymous tool execution. Split-BFF and combined
capture retain browser/circuit surfaces. Scheduler and dashboard require a
non-Testing host; their omission in Testing is not proof of runtime omission
or successful worker activation.

PC-qualified provider finding/mitigation pairs and RESOURCE-qualified browser
scenarios remain separate from identically numbered API identities. Historical
provider PR references are provenance, not current phase numbers or closure.

## Selected security execution

`SecurityChangeSelection` is the native predicate used by
`.github/workflows/security-tests.yml`. It includes dedicated API
hosting/authentication, Standalone and shared BFF ingress. Missing or empty
changed-path input selects execution fail-safe. An unrelated-change no-op
remains distinct from a successful test run.

Two required lanes currently produce execution evidence:

| Lane | Project | Selector |
| --- | --- | --- |
| `security-integration` | `Event.API.IntegrationTests` | `/*/*/*/*[Category=Security]` |
| `cerbos-policy-contract` | `Event.API.IntegrationTests` | `/*/*/*/*[Category=PolicyContract]` |

Each selected lane requires at least one test. The shared native producer reads
the actual TRX, rather than trusting a step conclusion or declared JSON totals.
It rejects ambiguous/missing reports, zero execution, skipped/failed outcomes,
duplicate executions and inconsistent counters. Successful production retains
report bytes, portable paths and SHA-256 digests under the uploaded artifact.
Existing tee logs are retained when present.

`SecurityExecutionEvidence` owns validation and its partial production
companion owns the CLI/CI acquisition flow. The producer contract is:

```text
produce-security-execution <output-directory> <candidate-oid> <run-id> <run-attempt> <event-name>
```

CI supplies its actual `GITHUB_SHA`, `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT` and
`GITHUB_EVENT_NAME`. A PR merge revision cannot be renamed to a release
candidate's revision. No-op or unsuccessful selected execution produces no
admissible packet. Diagnostic uploads remain available after failures.

The packet's context is **Testing dedicated API test-host execution**.
Provider, database role and authentication scheme remain `not-attested`;
these fields do not establish ordinary-role isolation or live deployment
behavior. Synthetic admission fixtures test this protocol, not API security.

## Exact-candidate sidecar

Candidate verification consumes:

```text
docs/internal/releases/<version>/security-execution.v1.json
```

This post-commit sidecar and its referenced reports are retained execution
artifacts, not part of candidate commit B. Including B's own identity in B
would create a circular identity requirement.

After existing release-source, trusted-tooling and recomposed-notes checks,
`verify-candidate` requires an `executed` packet naming the exact supplied full
candidate OID. Both lanes must belong to the same invocation, occur exactly
once, and satisfy:

```text
selected = executed = passed > 0
failed = skipped = 0
```

The verifier rejects duplicate JSON properties, invalid contexts, missing
lanes/reports, changed artifact digests, traversal and symbolic-link paths.
JSON depth/size and report size are bounded; XML DTD/entity expansion is
prohibited. The current packet limit is 256 KiB and each retained report/log
limit is 16 MiB. Exceeding a limit rejects evidence; it does not truncate a
report into a passing result.

The candidate manifest records `securityExecutionSha256` from the exact
validated packet bytes. Same-input verification remains deterministic;
changing the retained packet makes an already generated manifest stale.
Archive the packet and every referenced artifact together for replay.

This boundary establishes attributable structured execution integrity.
Hashes alone do not authenticate the producer or establish implemented
security. Complete provider/migration/architecture, non-Testing host,
operator/browser and trusted-provenance admission remains separately required
at the programme's final gate.

## Pinned standards and framework provenance

The register pins **ASVS 5.0.0**, targets L2 with explicitly selected L3
requirements, and retains 67 independently verified identifiers with their
normative sources. Source applicability is recorded; runtime verification
remains unverified until the owning boundary executes. Host prerequisites describe source-qualified
participation, including browser flows that depend on API validation; they
do not assert that each host independently implements every requirement.
This is a selected applicability register,
not a complete ASVS assessment or certificate.

The minimum evidence union includes native catalogue/selection/admission
tests, actual enabled host graphs and transport tests, the owning Domain and
Application invariants, real affected relational-role/provider behavior,
worker lifecycle and middleware execution, and required operator/browser
receipts. Run the affected boundary before delivering its slice; aggregate
the complete supported-provider/migration/architecture union at programme
exit. A passing catalogue or a two-lane CI packet cannot substitute for that
union. A no-op and a zero-test run supply no selected assurance.

Research used Tavily MCP and Context7 MCP on 2026-10-07. The retained handoff
contains functional requirements, necessary public interface identifiers and
URLs, not external implementation code or copied expressive structure.

- [OWASP ASVS and version-qualified identifiers](https://owasp.org/www-project-application-security-verification-standard/)
- [Microsoft endpoint collection](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.routing.endpointdatasource)
- [Endpoint metadata](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.endpoint.metadata?view=aspnetcore-10.0)
- [OpenAPI exclusions](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata)
- [JWT issuer, signature, audience and expiry validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication)
- [Protected-payload lifetime](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/limited-lifetime-payloads)
- [Data Protection key expiry and revocation](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0)

Endpoint routing metadata includes surfaces excluded from OpenAPI, but does
not automatically enumerate middleware-only or upstream infrastructure.
Inventory those boundaries separately. A valid token signature does not
establish receiving-service or object/tenant authority. Expired decryption
keys and revoked keys have different semantics; rotation alone is not
credential revocation.
