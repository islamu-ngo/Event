# Browser testing

The Browser profile checks real sign-in, mandatory first password replacement, logout, browser session expiry, keyboard navigation, Arabic/English direction, tenant routing and free-ticket registration. It starts a disposable local Event installation and uses generated test identities. It does not connect to an existing deployment or use your personal account.

Use a Docker-compatible runtime and the repository's .NET SDK. The test fixture selects its Environment secret authority and owns its temporary resources. Run from the repository checkout, with temporary files on a disk-backed directory:

```bash
export TMPDIR="$HOME/.cache/agent-tmp"
dotnet build tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj --configuration Release --verbosity quiet
```

Install the matching Chromium binary explicitly with the native .NET installer. The same cache path must be available when running the journeys:

```bash
export PLAYWRIGHT_BROWSERS_PATH="$TMPDIR/islamu-assurance-headless-browsers"
dotnet run eng/tools/InstallAssuranceBrowser.cs
```

The installer pins Microsoft.Playwright 1.64.1, accepts no arguments and returns the upstream install exit code. Its fixed `--only-shell` action installs the Chromium headless shell and matching browser-cache dependencies, avoiding full Chrome and its Widevine component. It does not install operating-system packages or launch a server. Test discovery never downloads or installs a browser. Install any required operating-system browser libraries using your runner's ordinary provisioning process.

These binaries are separate developer tools in an external cache. They are not included in Event source, product images or release bundles. Microsoft.Playwright's MIT license does not cover the browser's assembled components: preserve the headless shell's `LICENSE.headless_shell`/`ABOUT` and FFmpeg's `COPYING.LGPLv2.1` in their cache. Browser or FFmpeg redistribution needs a separate license review and distribution decision; this setup does not authorize it.

The artifact-safety checks do not launch a browser or containers:

```bash
dotnet test --project tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj --configuration Release --no-build -- --treenode-filter "/*/*/BrowserArtifactPolicyTests/*" --minimum-expected-tests 17 --no-progress --maximum-parallel-tests 1
```

Run the actual browser journeys separately:

```bash
dotnet test --project tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj --configuration Release --no-build -- --treenode-filter "/*/*/BrowserJourneyTests/*" --minimum-expected-tests 6 --no-progress --maximum-parallel-tests 1
```

The runtime must finish migration and become healthy before the browser can proceed. Startup failure is a failed run, not proof that the journeys passed. Report the candidate revision, named outcomes and any missing checks. Cookie expiry and server token expiry are separate scenarios. The latter deliberately waits for the product's valid five-minute minimum token lifetime and observes endpoint rejection through bounded polling; allow that duration in runtime jobs.

Failure reporting deliberately excludes traces, HAR files, screenshots, browser storage, raw page content and raw exception messages because these can contain passwords, cookies and personal data. Do not enable `DEBUG`/`PWDEBUG` or publish raw service logs while investigating. The test output identifies the journey using a fixed safe code. Preserve the fixture's cleanup ownership checks; never replace cleanup with broad container or volume deletion.
