# I-VSD Planning Assessment: Offline Four-Surface Setup Assistant

Last Updated: 2026-09-30

## Review Metadata

- Mode: planning
- Workstream: setup-assistant
- Subject: offline CLI, Terminal.Gui, standalone Blazor and Avalonia
- Report kind: implementation-planning assessment
- Report status: current / plan-aligned (planning only)
- Disposition: offline plan aligned; connected-flow review superseded
- Evidence cutoff: 2026-09-30
- Reviewed input: repository at `59eafea41` plus the final offline
  triad in `dev/active/setup-assistant/`, SHA-256:
  - plan `4fe3927124e202275a7d1550477a40ca78f36d389c435e554da66321353e8d09`
  - tasks `41d70832e0f1db07b8eb877bc926f70690cc9f90a749ec64f4c6f9fd833533d4`
  - context `cd4ca17bf15847af8b07b8c76d20e214a945a529dbbf1606c7acd7e255a29513`
- Historical input: the 2026-09-29 connected-flow report
  previously mapped IVSD-F101–F107/M101–M107; those findings
  are **not** approval to add a network surface

## Scope And Claim Boundary

The developer explicitly removed all instance connections
from the Setup Assistant, even read-only queries. This
review concerns local artifact handling, public-only
browser inputs, protected native output, localization,
accessibility, honest release claims and third-party
dependencies. It does not assess connected authorization,
OAuth, API-key scope, HAL writes, provider credential
handoffs, tenant-enforced administration or live imports;
those belong to a separate future ISLAMU Event TUI
and the existing platform backend.

This report offers provider-responsibility design
reasoning, not a religious-legal ruling, legal identity
certification, privacy/security certification, accessibility
certification, dependency approval or product-release
authorization. A manifest preview is not evidence that
the platform has accepted or applied it.

## Findings

| ID | Lifecycle / severity / claim | Stakeholder, principle and provider-controlled risk | Evidence / confidence | Mitigation / owner |
|---|---|---|---|---|
| IVSD-F201 | open / critical / private output | Amanah and non-harm: self-hosters' environment or identity data can leave through CLI stdout, browser download, terminal diagnostics or a world-readable Windows file. The provider controls artifact classification and every output surface. | `src/Event.SetupAssistant.Cli/Program.cs::SystemWriter` permits `-` and does not set Windows ACL; `WindowsProtectedFileWriter` is unsupported; source-confirmed, proposed shared writer unbuilt. | IVSD-M201; Core/artifact/CLI/browser owners. |
| IVSD-F202 | open / high / truthful readiness | Truthfulness and operator agency: local validation can be confused with a successful platform import or legal approval. The provider controls product language, preview labels and error states. | Existing CLI offline contract and public setup guide; proposed new UIs unbuilt. | IVSD-M202; all presentation owners. |
| IVSD-F203 | open / high / equitable access | Justice and ihsan: keyboard-only, screen-reader and Arabic/RTL operators may receive less usable terminal, browser or desktop workflows than the CLI. | `docs/internal/ACCESSIBILITY.md`, existing localized terminal tests; no enabled browser/desktop evidence. | IVSD-M203; presentation/accessibility owners. |
| IVSD-F204 | open / high / dependency claims | Amanah and truthfulness: Spectre, Avalonia and WASM change the published assembly/native-asset graph before transitive licenses, notices, vulnerabilities and target behavior have been verified. | `Directory.Packages.props`, target csprojs, disabled-target architecture tests; primary MIT package metadata is only preliminary. | IVSD-M204; dependency/release owners. |
| IVSD-F205 | open / high / identity representation | Justice and truthfulness: a locally prepared operator identity manifest may contain private personal data or appear to certify official status, although no authority has verified it. | `OfflinePortabilityWorkflow` and server-side identity readiness are distinct; native UI/browser workflows remain proposals. | IVSD-M205; identity/privacy and UI owners. |

## Recommendations

- **IVSD-M201:** First prove the existing CLI's restricted
  `env render --output -` disclosure with a process-level
  behavioral assertion. Give Core a closed public/restricted
  artifact contract with unknown kinds restricted. Reject
  restricted stdout and browser output. Use one bounded
  native prepare/commit writer with protected directory,
  symlink/reparse and race checks, owner-only Unix mode,
  a non-inherited Windows DACL for the current user and
  required OS principals, atomic install and post-check.
  Require real Windows and macOS host evidence or disable
  restricted saves on the unverified target. Elevated OS
  principals remain outside the ordinary-account guarantee.
  Clear transient private buffers and keep diagnostics
  value-free. The tests use dynamically generated data,
  never source-inline credentials.
- **IVSD-M202:** Label every result as a **local draft,
  validation or export**; never call it an applied
  deployment, live preview or legal certification.
  Explain how separate authorized platform
  administration would consume a file without the
  assistant opening a connection. Explain that the static
  browser downloads boot assets on first load and cannot
  promise offline relaunch; no Event-instance API is used.
  Make cancel/back and failure recovery explicit.
- **IVSD-M203:** Give terminal navigation, the
  public Blazor page and Avalonia window target-specific
  keyboard/focus, programmatic labels, error
  association, contrast, Arabic/RTL and assistive
  technology checks before claiming parity. Unsupported
  OS save behavior must be visible, not silently degraded.
- **IVSD-M204:** Verify exact direct/transitive/build/publish
  closures, license and NOTICE obligations, vulnerability
  results, target RID assets and independently published
  artifacts. Any WASM runtime-pack lock exception remains
  limited to that project with equivalent restored/published
  closure evidence. A top-level MIT package label does
  not approve the full distribution.
- **IVSD-M205:** Handle offline identity manifests only
  on protected native targets, mark their files private,
  never emit them into the public browser or machine
  diagnostics, and state that portability does not
  establish paid-commerce readiness or official/legal
  status. A tenant or identity ID in an artifact is data,
  not local authorization.

Rejected alternatives: an optional Setup Assistant
read-only API mode; browser-held API key or OAuth token;
normal file writes as a Windows fallback; placing
private identity into a static-hosted public browser;
using a passing CLI build to declare other surfaces
available. Keeping existing SetupLive backend code
elsewhere does not change this product boundary.

## Stakeholders And Principles

Self-hosters need sovereign, locally reviewable
configuration and honest recovery. Operators whose
identity data appears in artifacts need minimization
and protected output. Keyboard-only, screen-reader
and Arabic/RTL operators need equivalent navigation.
Maintainers and distributors need reproducible
dependency and platform support claims. Amanah
governs private artifacts and software custody;
truthfulness governs preview/readiness/identity
language; justice and ihsan govern accessibility
and product parity; non-harm and avoidance of
spying govern private output and telemetry.

Domains reviewed: UX, privacy, local security,
portability, self-hosting, accessibility,
supply chain, operations and governance.
Live provider authentication, server tenancy,
monetization, ranking and religious-content
adjudication are not changed by this workstream.

## Common Overlooked Failures

- CLI `--output -` may print restricted bytes even
  while the JSON status remains value-free.
- A Windows temporary file can briefly inherit
  broad ACLs before an atomic move; the ACL must
  be established before content is staged.
- A public browser fetches same-origin boot
  assets but must not fetch an application API
  or off-origin assets after load; a static
  host can change served code, so this is not
  a confidentiality guarantee or an offline-relaunch
  promise. "Offline" here means no Event-instance
  connection, not network-free first launch.
- A value-bearing filename, exception or retry
  diagnostic can expose private data even if
  the editor visually masks it.
- A local identity draft may be mistaken for
  platform-verified legal identity.

## Validation Gaps And Escalation

The new shared artifact writer, browser and desktop
are not yet built. Windows/macOS protected-output
and target-host UI/package evidence are absent;
NuGet full-closure licensing and accessibility
evidence remain implementation gates. The developer
owns product approval; repository dependency,
CI, IP and accessibility owners review actual
artifacts before target activation. Claims of
legal, official or religious endorsement require
the relevant external authority, not this
assessment. No user decision about live
authentication is pending for this workstream.

## Evidence Reviewed And Missing Evidence

- `src/Event.Setup.Core/Portability/OfflinePortabilityWorkflow.cs`,
  `src/Event.SetupAssistant.Cli/{Program,SetupCliParser,SetupCliMachineOutput}.cs`,
  `src/Event.SetupAssistant.Terminal/{SetupTerminalApplication,SetupTerminalProtectedWriter}.cs`,
  `src/Event.SetupAssistant.Desktop/Files/{UnixProtectedFileWriter,WindowsProtectedFileWriter}.cs`,
  Browser/Desktop project files and their focused tests.
- `docs/internal/{SETUP_ASSISTANT_ARCHITECTURE,SECRETS,CI_CD_GOVERNANCE,ACCESSIBILITY}.md`,
  `docs/public/self-hosting/setup-assistant.md`, archived
  `dev/zarchive/setup-assistant/` triad and the active
  plan/context/tasks under this re-baseline.
- Missing until implementation: actual static/published
  target closure, Windows/macOS host ACL evidence,
  operator accessibility feedback and package audit.

The code-review-graph tools were searched for and
were unavailable; bounded source reads and exact
project-reference checks supplied the evidence.
No third-party implementation source or assets
entered this assessment.

## Review Lifecycle

| Date | Previous status | New status | Trigger / disposition |
|---|---|---|---|
| 2026-09-29 | none | current / plan-aligned (old scope) | Connected Setup Assistant proposal, now superseded |
| 2026-09-30 | current | stale / revalidation-required | Developer forbade any instance connection |
| 2026-09-30 | stale | current / plan-aligned | Final triad fingerprints above; offline F201–F205/M201–M205 mapped to scenarios A–F and Tasks 7.1–11.3 in plan Section 9 |

The prior F101/F107 connected authority risks
are retired **as out of scope**, not resolved
through implementation. Prior F102/F103/F104/F105/F106
are superseded by separately numbered offline
findings. Do not reuse old IDs for new meanings.

## Planning Handoff

- Workstream: setup-assistant
- Status: current / plan-aligned for planning; implementation
  and target-host release evidence remain outstanding
- Current finding/mitigation set: IVSD-F201/M201
  through IVSD-F205/M205
- Required mappings: plan Section 9 scenarios and
  numbered Tasks 7.1–11.3.
- Refresh triggers: any proposed network/identity
  authority, public-browser private-file handling,
  changed restricted artifact contract, file
  permission downgrade, target dependency, telemetry
  or accessibility claim.
