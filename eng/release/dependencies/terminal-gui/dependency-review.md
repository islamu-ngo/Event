# ISLAMU.Terminal.Gui Dependency Review

Date: 2026-09-01 Europe/Brussels  
Decision: Approved for the Setup Assistant Terminal target after exact-artifact
verification  
Approver: Project Steward

## Source Register

- Terminal.Gui official repository and `v2.4.17` release, accessed 2026-09-01:
  `https://github.com/tui-cs/Terminal.Gui.git`
- Upstream tag object:
  `58f3af1a4afe5d2772be134b2299a0f78f35c93c`
- Upstream commit:
  `d0a0ed9b150d3fc8aacf4ab07b7f7d91264fe6d6`
- Upstream license: MIT; the package retains `LICENSE`, upstream authorship,
  repository identity, README, and the ISLAMU modification notice.
- Project Steward authorization:
  [`setup-assistant-terminal-gui-steward-approval.md`](../../../../dev/active/setup-assistant-security-and-portability/setup-assistant-terminal-gui-steward-approval.md)

## Authorized Functional Delta

The patch removes the TextMateSharp and TextMateSharp.Grammars package
references, excludes the TextMate-backed implementation, and changes built-in
Markdown/code-view defaults to plain rendering. The public
`ISyntaxHighlighter` extension seam remains. Package metadata uses the distinct
`ISLAMU.Terminal.Gui` identity, fixed `2.4.17-islamu.1` version, and an explicit
downstream notice. The downstream build also disables the upstream
post-pack global-cache mutation and replaces GitVersion with the fixed approved
version.

No upstream source is vendored. The repository retains only the exact patch,
source identity, rebuilt binary package, lock closure, and generated evidence.

## Dependency And Security Decision

- Final runtime closure: 21 components in
  [`packages.lock.json`](probe/packages.lock.json).
- `TextMateSharp`, `TextMateSharp.Grammars`, grammar/theme assets, official
  `Terminal.Gui` package identity, and TextMate assembly/type references: absent.
- License policy: passed for the repository's 664 resolved package/version
  pairs; this graph adds no exception.
- NuGet vulnerability audit with transitives: no advisory reported for the
  exact probe graph.
- CycloneDX 1.6 evidence:
  [`terminal-gui.cdx.json`](generated/terminal-gui.cdx.json).
- Exact artifact/patch/assembly hashes:
  [`package-evidence.json`](generated/package-evidence.json).
- Steward-approved patch, assembly, and closure hashes are frozen separately in
  [`approval.json`](approval.json); regenerating package evidence cannot widen
  the authorized delta.

NuGet pack does not emit a byte-reproducible archive because it generates a
random OPC core-properties part name. The committed package therefore has one
authoritative SHA-256. Rebuild verification compares the pinned source and
patch, deterministic assembly hash, nuspec identity/dependencies, notices,
entry inventory, lock closure, and SBOM rather than falsely claiming identical
ZIP bytes.

## AFC / SSO Review

- Constrained elements: Terminal.Gui namespaces and public API are required for
  package compatibility; the package ID must differ; MIT attribution must remain.
- ISLAMU-owned design: machine CLI and human terminal are separate executables;
  CommunityToolkit owns value-free presentation state; the terminal target owns
  transient secret input and protected output; no editor or grammar feature is
  rebuilt.
- Discretionary similarity: none enters ISLAMU product source. The adapter uses
  project-native MVVM commands, messages, Core workflows, and lifecycle rules.
- Decision: pass for the temporary dependency/package boundary. Any upstream
  rebase, patch expansion, or new transitive requires a new review and Steward
  approval.
