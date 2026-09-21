# Onboarding Findings

## 2026-09-21 - Guided administration and browser evidence

The guided UI projects existing journey identity capabilities and preflight
categories; it does not own requirements or authorization. Form options are
value-free vocabulary. The identity document's update relation and revision, not
metadata links, remain the edit and concurrency authorities. The new generated
metadata HAL wrapper needs explicit AOT serialization registration, including its
unknown extension data.

### Browser-proven integration repair

The shared BFF request enricher already gave authenticated status/journey reads
precedence over old setup cookies. The separate server-side
`SetupSecretForwardingHandler` still restored a setup header after bearer
forwarding. Browser direct GETs returned authenticated 200 while interactive
journey reads returned 403. The smallest two handler regressions failed on the
restored header. Exact authenticated GET status/journey now omit it; all 56
forwarding-handler cases pass. Anonymous setup and completion writes retain their
existing requirements. A subsequent live Local journey rendered the categorized
administration surface after password replacement and fresh sign-in.

### Rendered checks and bounded conclusions

- Real HTTPS Standalone with an isolated SQLite database, explicit Environment
  authority and dynamically injected secrets; no dependency or backend workflow
  added. Internal API HTTPS redirection was disabled while the public listener
  remained HTTPS-only. Setup-secret request quota was a QA-only override.
- Local selection, keyboard completion, temporary-password replacement and fresh
  sign-in returned success. The explicit Cerbos path retained separate runtime,
  policy-package and credential states and a disabled Continue action when not
  configured; there was no silent Local fallback. This is not an external-provider
  login or configured Cerbos integration certification.
- Wizard/advanced-provider captures covered desktop, 390px mobile, equivalent
  200/400% reflow widths (720/360px), light/dark, forced/high contrast, reduced
  motion and RTL. Native disclosure and keyboard activation worked. These are
  reflow-equivalent viewport checks, not browser-chrome zoom certification.
- Dark sidebar labels were corrected with the existing semantic text token.
  Measured composited contrast was 11.72:1 for the current-step background and
  14.87:1 for the plain sidebar. Fresh 390/720/360px captures showed no overflow.
- The live checklist exposed long reason-code overflow and three action targets
  below 24px. A browser assertion failed with `overflow=true, smallTargets=3`.
  Scoped wrapping/minimum-target rules were added; no global redesign. Compilation
  and focused tests pass, but post-fix browser reflow was not rerun before the
  operator explicitly froze browser commands. Do not describe this as rendered
  verification of the final CSS.
- Identity-editor real-surface interactions, error-state captures and its full
  matrix remain incomplete. Orca was unavailable; espeak-ng alone is not a browser
  screen reader. No screen-reader or independent visual-review pass is claimed.
- Browser-ready setup at 01:56:16.823Z to first administration landing at
  02:01:13.589Z was 296.766 seconds, including QA/rebuild work. The landing later
  encountered a startup reroute; direct navigation subsequently remained settled.
  This is neither a healthy-container benchmark nor a broad under-five-minute
  usability result.
- Notification-stream 401, a status-read 429/startup reroute, and optional SQLite
  background processor errors were observed. They were not repaired or declared
  clean-parent baseline failures without differential runtime proof. The public
  bounded SQLite profile remains the operational reference.

### Verification and exact-parent quarantine

Parent `3cf82b0284ea3153617bdfa1fec39b5a91bd1b40` was checked out clean in a temporary
task-namespaced detached worktree. The same six client test failures reproduced:

1. `GeneratedTagClients_AreDiscoverable`: expected 170 pairs, actual 171.
2. `LocalSetupRendersWithoutOrdinaryUserHydration`: expected one username input,
   actual zero.
3. `RecoveredLocalOperationSendsExplicitLegalIdentityAndNoCredentialEmail`:
   username input absent.
4. `FailureClearsPasswordWithoutReplacingReservedOperation`: username input absent.
5. `CancelIgnoresLateCompletionAndClearsTransientPassword`: username input absent.
6. `GeneratedBrandingReadbackRestoresUpdatesAndClearsExistingSupportField`:
   expected support address, actual empty value.

No failing test was removed, relaxed or skipped. Final full client suite: 2,802
cases, 2,795 passed, six failed, one existing skip. Parent targeted reproduction:
18 cases, 12 passed, the same six failed. Focused component/serialization/workspace
slice: 67/67; accessibility conventions: 8/8; forwarding handler: 56/56. Final
Release solution build: zero warnings/errors. Language-server verification was
unavailable (C# timeout; CSS server not installed); compiler results are retained.
The workstream browser/assistive-technology gates remain partially open despite
shipping the requested atomic commit.

Evidence is retained locally under
`.omo/evidence/20260920-progressive-instance-onboarding/browser-phase3/`, including
`circuit-authority-red.txt`, `circuit-authority-green.txt`,
`36-sidebar-composited-contrast.txt`, `52-settled-checklist-matrix.txt`,
`55-checklist-regression-red.txt`, `parent-client.txt`, `freeze-focused.txt`,
`freeze-full-client.txt`, and `freeze-release-build.txt`.

### Provenance

Implementation uses repository-native contracts and independent component/test
composition. No third-party implementation source, asset or new dependency was
introduced. Structure/sequence/organization review preserves the existing service,
HAL, document-revision and scoped-design-token boundaries.
