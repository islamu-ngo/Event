# Inter Typography Asset Admission

> **Audience:** Maintainers and distributors
> **Status:** Admitted for unmodified, bundled typography; published delivery and local fallback verified
> **Reviewed:** 2026-10-06
> **Source anchor:** `src/Explore.Blazor/wwwroot/fonts/inter/`

## Component and provenance

Inter 4.1 is a shipped-runtime font, not a NuGet dependency or application
implementation. The admitted component is the unmodified roman variable
`web/InterVariable.woff2` from the author's
[4.1 release](https://github.com/rsms/inter/releases/tag/v4.1).
The [official archive](https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip)
contains that face and its `LICENSE.txt`. No upstream stylesheet, script, source,
or application design is imported.

| Artifact | SHA-256 |
|---|---|
| `Inter-4.1.zip` | `9883fdd4a49d4fb66bd8177ba6625ef9a64aa45899767dde3d36aa425756b11e` |
| `InterVariable.woff2` (352,240 bytes) | `693b77d4f32ee9b8bfc995589b5fad5e99adf2832738661f5402f9978429a8e3` |
| Upstream `LICENSE.txt` | `262481e844521b326f5ecd053e59b98c8b2da78c8ee1bdbb6e8174305e54935a` |

Fontconfig inspection identifies the variable roman face and named instances
including Light, Regular, Medium, SemiBold, Bold, and ExtraBold. The application
declares the required CSS weight interval 300-800. Inter does not supply every
supported language's glyphs; existing local system-font fallback remains required.

## Distribution decision

The authoritative terms are **SIL Open Font License 1.1 (OFL-1.1)**, retained
with the asset, including the Inter Project Authors' copyright.
The [official license](https://openfontlicense.org/open-font-license-official-text/)
permits bundling, embedding, redistribution, and sale with software. Its conditions
attach to the font, not the independently owned application. This permits the
public AGPL distribution and alternative application licenses, including commercial
distribution and hosted use, while the font itself always retains OFL-1.1.
The project's CLA does not relicense the font.

Distributors must retain the copyright and complete license with every font copy.
Do not sell the font by itself or represent it as application-owned material.
Do not use author names to imply endorsement. This admission covers unchanged
bytes only: subsetting, format conversion, derivative naming, and font updates
require fresh admission. The shipped header declares no Reserved Font Name.
No seat, hosting, source-offer, or application relicensing obligation is introduced
by this unmodified bundled face.

The compatibility decision follows the repository's
[dependency gate](../IP_GOVERNANCE.md#dependency-and-license-gate): third-party
material keeps its own terms without constraining the selected license for
ISLAMU-owned application material. The automated dependency validator remains a
separate required check; it cannot substitute for the asset review above.

## Implementation separation and recovery

Research used Tavily to identify authoritative license facts and Context7 for
Microsoft's static-web-asset interface documentation. Application linkage and
tests are independently designed from the existing host, tokens, and HTTP test
seam. No third-party application structure, CSS, or tests are copied.

The host owns the local stylesheet and static asset publication. Existing
typography tokens retain their system stack; unavailable fonts must never
trigger an external font request. Restore an admitted local version or use
the existing system stack for recovery. This boundary concerns typography,
not whole-application offline operation.
