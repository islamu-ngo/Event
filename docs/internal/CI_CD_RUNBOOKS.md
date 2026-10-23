<!-- ABOUTME: Maintainer runbooks for rerunning CI/CD gates without bypassing controls. -->
<!-- ABOUTME: Documents evidence-first triage, approved rerun paths, and override requirements. -->

# CI/CD Runbooks

> **Audience:** Maintainers | Operators | AI agents
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-06-01
> **Source Anchors:** `docs/CI_CD_GOVERNANCE.md`, `docs/RELEASE_CHECKLIST.md`, `.github/workflows/`

Use these runbooks when a CI/CD gate fails. The default response is to preserve evidence, fix the cause, and rerun the same gate. Do not bypass branch protection, environment approval, workflow-security checks, or deployment evidence requirements to make progress.

## General Rerun Rules

1. Open the failed job summary and retained artifacts before rerunning locally.
2. Confirm whether the failure is deterministic, infrastructure-related, or caused by the change.
3. Rerun the same GitHub Actions job once if the failure is likely transient.
4. If the second run fails, assign an owner and record the retained artifact or log path in the PR/release notes.
5. Do not weaken a required workflow, delete evidence steps, or mark an advisory lane as ignored unless the exception has owner, date, scope, compensating control, and removal condition.

## Gate Runbooks

| Gate | First evidence | Approved rerun path | Bypass policy |
|---|---|---|---|
| `Build & Test` | `test-results-fast`, build log, TRX files, NuGet/license audit output | Rerun the failed job, then reproduce with the affected per-project `dotnet test` command if needed | Do not remove NuGet vulnerability, dependency license, cache-policy, or TRX upload steps. |
| `OpenAPI Contract Guard` | `openapi-contract-guard`, job summary, OpenAPI/client diff | Regenerate OpenAPI and NSwag client through documented commands, commit deterministic artifacts, rerun | Do not hand-edit generated OpenAPI or NSwag client output. |
| `Workflow Security` | `workflow-security-evidence`, action pin output, `actionlint`, `zizmor`, cache/Dockerfile/Dependabot validators | Fix workflow YAML or C# validator policy, rerun workflow security | Do not merge unpinned external actions, medium-or-higher `zizmor` findings, or privileged cache-policy failures without documented exception. |
| `Contributor License Agreement` | PR body checkbox/signature lines, validator output | Ask contributor to update PR body; rerun the CLA job after edit | Do not bypass CLA for non-bot contributors without explicit maintainer/legal approval. |
| `Secret Scanning` | `secret-scanning-evidence` SARIF/text | Rotate/remove new secret, rewrite commit if needed, rerun range scan | Do not mark newly introduced secrets as accepted; legacy scheduled findings need triage/baseline before blocking. |
| `Security Integration Tests` / `Cerbos Policy Validation` | `security-test-evidence`, `cerbos-policy-evidence`, TRX and logs | Rerun once for container startup flake; otherwise fix code/policy fixture and rerun | Do not bypass authorization/policy failures for deployment. |
| `OpenSSF Scorecard` | `scorecard-evidence`, code-scanning SARIF | Advisory only; triage repeated findings before promotion | Do not make Scorecard required until signal quality and repo permissions are proven. |
| Container build | `container-build-*`, digest JSON, promotion evidence, Trivy text/SARIF, attestation verification JSON, checksum manifest | Fix Dockerfile/dependency/scan issue and rebuild the same component | Do not deploy if scan, attestation verification, immutable promotion verification, or checksum generation fails. |
| Coolify deploy | `deployment-production-evidence` / `deployment-staging-evidence` | Fix webhook/secrets/smoke URL/configuration, rerun environment-gated deploy | Do not bypass production environment approval, deployment freeze, required smoke checks, or expected digest evidence. |

## Reviewed exact secret-scan false positives

`.gitleaksignore` contains individual reviewed finding fingerprints, not rules,
paths or credential values. Every entry requires retained evidence and explicit
maintainer approval. Never use it to accept an actual secret or hide a new finding.

### PR75 tenant-setting row identifier

- Owner and approval: Project Steward, explicitly approved on 2026-10-19.
- Scope: the single `generic-api-key` finding at
  `tests/Event.Persistence.IntegrationTests/Repositories/PersistedResourceAuthorityTests.cs:344`
  in commit `e2937d6ef122d7441e1165b1113a549c3602ef6c`.
- Classification: the literal names a seeded `TenantSetting.SettingKey` row in
  tenant-isolation tests. It is not a credential, token, setting value or connection
  secret. No matched content is reproduced here and no source is obfuscated.
- Evidence: [Secret Scanning run 39287193181](https://github.com/islamu-ngo/Event/actions/runs/39287193181),
  artifact `secret-scanning-evidence` (ID `12202619443`), gitleaks 8.30.1, one result;
  retained SARIF SHA-256
  `6fc768f19e3807b8775a3c018ee078c48c67d670a0b6333ce7c3ed93ca8ec646`.
- Compensating control: all rules and files remain scanned; the ignore matches
  only that historical commit/path/rule/line fingerprint. A changed location or
  future occurrence is not accepted by this entry. Validate the original merge
  range with the pinned scanner and redacted output before recording a pass.
- Removal condition: the CI owner removes the entry after demonstrating that no
  supported scanned range can include that historical finding, or that the pinned
  rule no longer flags it. Do not rewrite shared history merely to remove it.

This is a reviewed nonsecret classification, not an emergency gate override.

## Emergency Override Requirements

Emergency overrides are reserved for urgent security or operator-impact incidents. An override must include:

- owner approving the override;
- date and workflow/gate affected;
- reason the normal gate cannot be fixed first;
- compensating control used before release or deploy;
- follow-up issue or task to remove the exception;
- link to retained evidence showing what was skipped or manually verified.

Production deployment freezes use `DEPLOYMENT_FREEZE=true`. A manual `workflow_dispatch` production deploy can proceed only when `override_reason` is provided and retained in deployment evidence.
