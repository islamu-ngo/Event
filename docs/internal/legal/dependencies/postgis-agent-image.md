# Agent Browser PostGIS Image Dependency Decision

> **Audience:** Contributors | Maintainers | Distribution reviewers
> **Owner:** Contributor Experience
> **Reviewed:** 2026-09-27
> **Scope:** Development-only `AgentBrowser` Aspire PostgreSQL resource

## Component And Boundary

The isolated agent profile uses the externally pulled
`docker.io/postgis/postgis:18-3.6-alpine` image, pinned to manifest digest
`sha256:ffcf0c4b904e41b9779f8098007fb5a9484025319c18c70cf8e1bcebb742b9b7`.
The inspected Linux amd64 image digest is
`sha256:c6383fbb981442981420d9c1c87cfd8543e8d70545f7c87ea247a72c04185ee8`.
This is an optional development/test service, not a NuGet dependency, a
bundled image, a production deployment image, or a grant to implement public
proximity discovery. `PostGIS` does not enter an ISLAMU assembly or source
tree. Normal Aspire profiles retain their existing PostgreSQL image.

The pinned image reports PostgreSQL 18.6, installed `postgis.control`, and
`PGDATA=/var/lib/postgresql/18/docker`. A network-isolated, temporary
PostgreSQL 18 database created the extension and returned true for a
`ST_DWithin` spatial expression on 2026-09-27; no agent data was mounted.
Do not fall back to PostgreSQL 17 against a PostgreSQL 18 data volume.

## Provenance And License

| Component | Authoritative source | Terms and decision |
| --- | --- | --- |
| Docker PostGIS image packaging | [Upstream Docker PostGIS LICENSE](https://raw.githubusercontent.com/postgis/docker-postgis/master/LICENSE) | MIT. Preserve the copyright and permission notice if the image or packaging is redistributed. |
| PostGIS extension | [Upstream PostGIS COPYING](https://raw.githubusercontent.com/postgis/postgis/master/COPYING) | GNU GPL version 2 text. The native extension remains inside its separate database service; this decision does not license ISLAMU-owned code under GPL or authorize redistribution of the image. |
| Pinned image/tag | [Docker Hub tag metadata](https://hub.docker.com/v2/repositories/postgis/postgis/tags?page_size=50) | Tag and digest recorded above; use the digest for reproducible development runs. The image also contains native dependencies, including GEOS and PROJ; their image-level notices and obligations require separate review before any redistribution. |

The image is pulled by a contributor from upstream for local development. No
image layers, extension binaries, third-party SQL, or vendor implementation
source are copied into the repository or included in an outbound artifact.
Public AGPL and any alternative outbound license remain choices for
ISLAMU-owned work because this runtime is a separately obtained service.
This approval is limited to local execution; publishing, bundling or shipping
the image needs an image SBOM, native dependency/license review, notices and
source-offer assessment, and separate Project Steward approval.

## Verification And Rollback

- Confirm the selected digest is pullable for the target architecture and run
  `CREATE EXTENSION postgis` plus a spatial expression in the agent database.
- Run the repository dependency-license policy check before committing the
  image reference; automated approval does not replace the distribution review.
- If the agent-only image is withdrawn, return its AppHost resource to the
  existing PostgreSQL 18 image only after confirming the database contains no
  PostGIS extension objects, or recreate only the disposable agent database
  under explicit operator authorization. Never point PostgreSQL 17 at its
  PostgreSQL 18 data directory.
