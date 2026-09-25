# Event-resource release verification and formatter repair

- Owner: Event-resource maintainers and CI/release engineering.
- Activate when: Making merged event-resource code eligible for a green `develop` Build & Test run or claiming full workstream verification.
- Current status: The final merged `develop` tree is byte-identical to published PR #55 and passed a Release build at that published head. Broad checks below are not green.

## Formatter regression on merged develop

Build & Test [run 36125672970](https://github.com/islamu-ngo/Event/actions/runs/36125672970) at `c4586607da1947f56646a70f535e19972f5c2027` stopped at `dotnet format whitespace` with **1,254 WHITESPACE diagnostics** across event-resource source and tests; the step exited 2. The integration and database-provider jobs were skipped. Do not mistake this for the earlier two onboarding assertion failures: those tests were not reached on this run.

- Correct the actual formatting of the affected resource files in a separately reviewable formatting-only change without altering assertions, suppressing diagnostics or changing generated migrations. Use native file edits and the repository's formatting convention; verify whole-solution `dotnet format whitespace --verify-no-changes` and a Release build before treating the formatter gate as closed.
- Rerun `run-tests / Build & Test` once on the corrected develop tree and inspect the actual case report. The earlier PR #49 run failed two `NativeInstanceOnboardingOperationTests` assertions (48 expected, 43 present) identically on untouched pre-feature `develop`; the static inventory omits real native requests. Correct that upstream test contract on evidence, not by lowering the count or guessing additions.

## Unclassified and inherited suite outcomes

- Two full API attempts exited 143 with 124/128 reported failures but no complete case report; only six sampled failures reproduced on untouched base. Obtain a fresh machine-readable case report, classify every feature-owned failure, and quarantine independently reproduced unrelated failures.
- The full Infrastructure suite timed out after S3 credential and named-lock errors. Supply its approved dynamic secret authority and provider prerequisites, then rerun the owning tests without embedding secrets in fixtures.
- The full Client suite had two failures and Architecture had eleven matching pre-feature `develop`; full Application had two inherited native onboarding-count failures. Preserve these failures as explicit independent repairs. Do not delete, skip or weaken their tests to make this feature look green.
- Scoped resource API 64/64, Infrastructure 64/64, Blazor Integration 770/770, five-engine clean/idempotent migrations and provider behavior 50/50 (resource cases 30/30), and MinIO private-bucket runtime 1/1 passed on documented production-equivalent source. Reuse these checkpoints unless a relevant source change warrants rerunning them; they do not turn broad red suites green.
- Sonar flagged generated multi-provider migration duplication on #50/#53 (7.7%/11.5%) and S4158 on #53's necessary duplicate-default rejection. The latter has a passing 56-case domain suite; do not hand-edit generated migrations or remove the fail-closed guard. Resolve any remaining quality-gate policy decision on evidence rather than hiding a new warning.
- Confirm any demonstrated architecture or recovery lesson not already captured in [ADR-033](../../docs/internal/adr/ADR-033-governed-event-resource-delivery.md) is graduated to the appropriate domain journal before declaring the original knowledge-closure task complete.

The eight real-browser gaps have their own [acceptance entry](event-resource-browser-acceptance.md). Record a final scenario-by-scenario disposition and anonymized security/privacy review before calling the resource workstream release-ready.
