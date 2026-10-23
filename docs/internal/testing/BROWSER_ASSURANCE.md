# Owned browser assurance

> Audience: contributors maintaining real runtime/browser verification.
> Owner: `Event.Distributed.IntegrationTests`, `Browser` profile.

`BrowserJourneyTests` drives Chromium through Microsoft.Playwright .NET under TUnit. Each journey creates an `OwnedAppHostFixture` in the non-Testing Assurance environment, starts the real Split topology, and waits for its migration and health contract. The fixture owns ports, stores, volumes, bootstrap identity and environment-generated credentials. A new nonpersistent browser context owns cookies and cache for each journey. Disposal closes the context before the browser and driver, then disposes the AppHost owner. Discovery does not install Chromium or start services.

The local configured administrator starts with a temporary password. Each journey first submits that password through the real login form, verifies the password-replacement page is still anonymous, changes the password through that form, and verifies replacement itself did not authenticate the browser. Subsequent sign-in uses the new fixture-owned password. No authentication service, database seed, WebApplicationFactory or local-agent persona substitutes for this lifecycle.

| Journey | Observable invariant |
| --- | --- |
| Login/logout | Successful credentials grant the protected create-event page; logout clears authentication and the same page challenges again. |
| Browser cookie expiry | Expiring the real HttpOnly session cookies in the browser removes access; fresh credentials restore it. This does not prove server-side JWT expiry. |
| Server token expiry | A real five-minute token first accesses the protected ticket-management endpoint. Bounded condition-driven polling observes its rejection, then confirms the browser session is anonymous and challenges again. |
| Keyboard and languages | Tab reaches the password field, Enter submits, the language menu works by keyboard, Arabic sets RTL, refresh retains the choice, English restores LTR and the skip link focuses the main landmark. |
| Tenant boundary | Two real tenants are created through HTTP. A public ticket catalog is visible under its owning route; the other route cannot expose it even with a forged browser tenant header. |
| Free registration | Public API setup creates an event and published free catalog. Browser reservation and finalization produce a confirmed order that survives reload; both the public HAL result and UI remove the finalize action. |

Setup HTTP calls perform the ordinary create/draft/publish commands and use the current event concurrency stamp. Registration itself occurs through the rendered UI. Assertions use labels, roles, visible status and public JSON contracts. There are no arbitrary readiness sleeps or intercepted/fabricated responses. The selected free ticket collects no participant details and requires no external payment provider. These checks complement existing domain, persistence, BFF, authorization and accessibility suites; they are not a WCAG certification or the complete payment/tenant threat model.

## Artifact boundary

`BrowserArtifactPolicy` admits only fixed journey/outcome codes. Browser traces, HAR, storage state, videos, screenshots, console output, arbitrary filenames, response bodies and raw exceptions are not admitted. The owner configures no recording or storage export, and the journey boundary replaces exceptions with a code that has no inner exception, data payload or raw message. The Harness tests include a generated sensitive canary and prove it cannot survive exception sanitization. No screenshot is currently admitted, so there is no unmasked screenshot path.

Nonempty `DEBUG` or `PWDEBUG` is rejected before host/browser startup because driver logging can bypass exception sanitization. Do not add raw failure attachments in CI. A future visual artifact requires a separate reviewed allowlist, masking of every sensitive surface, canary tests and an explicit retention policy before use. Runtime-side logging has its own secret/redaction contract; absence of browser artifacts does not certify every service log.

## Execution and evidence

See the [public browser testing guide](../../public/documentation/readme/contributing/browser-testing.md) for install and run commands. Run `BrowserArtifactPolicyTests` in the Harness profile without browser/container startup. Execute the Browser profile only with the admitted package, installed Chromium and owned runtime prerequisites. Record named pass/fail/skip counts and candidate commit; a build, discovery or Harness pass does not close the real browser profile.

`eng/tools/InstallAssuranceBrowser.cs` is the explicit native .NET installation entrypoint. It pins the admitted Microsoft.Playwright 1.64.1 package, rejects all arguments and invokes only the public `Microsoft.Playwright.Program.Main` Chromium-install action with `--only-shell`. The returned status is preserved. This avoids downloading unused full Chrome and Widevine. Set `PLAYWRIGHT_BROWSERS_PATH` to a task-owned headless-only cache and use that same path for the tests. The tool has no test/discovery hook, Node helper, PowerShell dependency, system-package installation or arbitrary CLI forwarding.

The initial implementation must be validated against a healthy configured-administrator fixture. Server JWT expiry is separate from browser-cookie expiry and takes at least five minutes of real server time. The fixture selects the existing five-minute minimum through `Authentication:Local:AccessTokenLifetimeMinutes`; a six-minute deadline bounds condition-driven endpoint polling. No product validator or clock is substituted.

## Source and license provenance

The hub supplied source-free public API facts for Microsoft.Playwright 1.64.1: browser creation, isolated contexts, pages, semantic locators, assertions and ordered disposal. Its NuGet metadata declares MIT, upstream `microsoft/playwright-dotnet` revision `8cc0f7b4e67f7a2ceb91a6cb0a5a8e6de6141ae1`. No third-party implementation, test, snippet or asset was read or copied. The test design derives from Event's own routes, request contracts, bootstrap and HAL behavior. Package/lock admission is hub-owned.

The browser downloads have independent terms. Headless shell 156.0.8078.4 (Playwright revision 1248) includes Chromium BSD terms and aggregate third-party notices in `LICENSE.headless_shell`; its `ABOUT` also identifies Chrome terms. FFmpeg `n7.0.1-playwright-build-1013` includes `COPYING.LGPLv2.1`. These are external developer-cache executables only, never Event source, product-image or release-bundle content. Their notices remain with the downloaded tools. The package MIT license is not a blanket license for these binaries, and this implementation does not approve redistribution. The hub-owned [dependency record](../legal/dependencies/testing-assurance.md) owns the component metadata and distribution boundary.
