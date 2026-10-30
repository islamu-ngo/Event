# BFF Session Authority Redis Dependency Decision

Reviewed: 2026-10-23. Owner: Platform/Security and distribution maintainers.
Scope: the approved shared, fail-closed per-browser-session BFF authority and the
same admitted image pin for canonical local/Compose cache resources.

## Selected Components And Provenance

| Component | Exact identity and role | Authoritative terms and decision |
| --- | --- | --- |
| Redis server | `docker.io/library/redis:7.2.16-alpine@sha256:f8548b29a1f1cb358b5fcfec1e0126beab57ad15776c1e87623f865579f16c5a`; separately obtained dedicated session-authority service in local hosting, Compose and owned runtime tests | Redis 7.2.16 [versioned COPYING](https://raw.githubusercontent.com/redis/redis/7.2.16/COPYING), BSD-3-Clause; accepted under the ordinary dependency policy. The [vendor licensing table](https://redis.io/legal/licenses/) independently identifies the 7.2.x line as BSD licensed. |
| Official image identity | Exact tag and manifest digest above | [Docker Hub tag metadata](https://hub.docker.com/v2/repositories/library/redis/tags/7.2.16-alpine); independently fetched metadata matched the selected digest on the review date. Tag-only selection is insufficient for this authority. |
| StackExchange.Redis | Existing installed `2.13.1`, now explicitly referenced by the BFF for its native Redis interface; no version upgrade or new SDK family | Installed NUSPEC declares MIT and upstream revision `981ba5241002db9758c656f1020a045690a1ec32`; [NuGet identity](https://www.nuget.org/packages/StackExchange.Redis/2.13.1), [MIT terms](https://licenses.nuget.org/MIT). Central version and generated locks retain the same selected version. |

The exact server artifact controls this decision. Generic image-page wording
about earlier patch versions does not override the selected version's legal
artifact. Redis 7.4/7.8 and Redis 8 are outside this selection. The separate k6
external-tool exception does not authorize a different Redis release or image.
Canonical AppHost and Compose cache declarations now use this same exact admitted
pin. Native annotation evidence found Aspire's previous default selected Redis
8.6; Compose used mutable `redis:7-alpine`. Neither previous declaration supplies
license admission for a different server release. The session and cache services
remain separate authorities with unchanged credential, network and volume roles.

This declaration does not downgrade, convert or delete an existing Redis volume.
Use a fresh explicitly disposable development store or a separately reviewed,
version-compatible export/restore when an existing store contains newer-format
data. Do not start the older server over that data merely because configuration
now names the admitted pin. Optional Svix 1.96.1, Weblate and Co-op compatibility
is not established by image metadata or the native Microsoft cache-provider
contract; do not claim those integrations passed without their own execution.

The vendor's [version support table](https://redis.io/docs/latest/operate/oss_and_stack/install/version-mgmt/)
lists the 7.2 line's end of support as 2029-12-01. Review security/support and
licensing together before replacing this pin; do not silently follow a mutable
major tag.

## Outbound Licensing And Notices

BSD-3-Clause permits the selected server use while requiring retained copyright,
conditions and disclaimer when redistributed, and prohibits unauthorized
endorsement. MIT requires the client's copyright and permission notice. These
component terms preserve the intended public AGPL and policy-approved
alternative offerings for ISLAMU-owned work. This is an ordinary compatible
dependency decision, not a new Project Steward license exception.

The Linux image contains Alpine and other bundled components with their own
terms. Redis's license does **not** establish a BSD license for the assembled
image. Preserve the image's component notices and applicable source obligations;
exporting, repackaging or bundling its layers requires the normal assembled-image
SBOM/license review. Declaring a digest-pinned, separately obtained service does
not waive those obligations or transfer third-party ownership through the CLA.

## Independent Implementation And Verification

The implementation receives repository-native behavior, public interface facts
and this component decision. No Redis/server/client implementation, snippets,
internal structure or upstream tests are copied into implementation context.
ISLAMU independently owns its session binding, authority admission, cookie and
circuit integration, expiry, race handling and refusal tests.

One earlier worker unexpectedly encountered an installed Testcontainers example
after authoring the native contract/test and fail-closed stub checkpoint. It
stopped implementation without further source edits. A fresh source-free worker
assumed the same bounded paths using only the repository checkpoint, functional
packet and sanitized public signatures; the example was excluded from the
handoff. Provenance review must bind the pre-exposure native-file hashes and the
successor's independently authored changes. The exposed worker is research-only.

The authority must remain separate from availability-cache fallback, primary
and identity stores, and Data Protection ownership. Runtime credentials come
from the selected approved secret authority; no password belongs in this record
or source. Namespace and peer/restart identity requirements belong to the
paired configuration/operator guides.

After native lock regeneration, run the existing dependency-license policy
validator against the complete graph and retain its result. Package admission
does not prove Redis availability, safe session revocation, race correctness,
restart survival or cross-host isolation; those require their named native and
real-provider results. No such execution is claimed by this decision.

Native solution restore with `--force-evaluate --disable-parallel -m:1` and the
following `--locked-mode` restore both exited 0. Comparing the twelve regenerated
lockfiles by framework/package/version/content hash found no changed resolved
package bytes; the BFF dependency and downstream project-reference metadata
changed. `dotnet run .ci/scripts/validate-dependency-license-policy.cs -- .`
passed for 471 unique package/version pairs. The four pre-existing visible
exceptions listed in the [testing dependency record](testing-assurance.md#verification)
remain unchanged; this decision adds no exception or warning suppression.
