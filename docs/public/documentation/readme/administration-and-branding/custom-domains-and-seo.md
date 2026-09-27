---
description: Bind tenant domains safely and operate the focused public-discovery SEO surface.
---

# Custom Domains & SEO

Custom vanity domains in ISLAMU Event are tenant routing and public accountability boundaries, not merely cosmetic branding aliases.

---

## Domain Setup & Tenant Routing

Custom domains are optional. The default browser URL is
`https://events.example.org/{slug}` and works with one normal DNS record and TLS
certificate; wildcard DNS and wildcard TLS are not required. Configure a vanity
domain only when a tenant needs its own host, such as `events.al-nour.org`.

Before enabling an external domain for a community tenant:

1. **Verify Tenant Ownership**: Ensure the domain binding is requested by an authorized Tenant Administrator (see [Admin Hierarchy](admin-hierarchy.md)).
2. **DNS & TLS Provisioning**: Point DNS A/AAAA or CNAME records to your server and verify valid TLS certificate issuance.
3. **Reverse-Proxy Header Forwarding**: Ensure your reverse proxy (Caddy, Traefik, or Nginx) forwards the client `Host` and `X-Forwarded-Proto` headers (see [Docker Compose Reverse Proxy](../self-hosting/docker-compose.md#5-reverse-proxy--tls-configuration)).
4. **Tenant Resolution Order**: In [Multi-Tenant Mode](../security-and-identity/multi-tenancy.md), API requests evaluate:
   $$\text{Trusted X-Tenant-Slug} \longrightarrow \text{Custom Domain} \longrightarrow \text{Subdomain} \longrightarrow 404$$
5. **Fail-Closed Verification**: An unmapped or unknown domain must immediately return `404 Not Found`. It will never route to a random tenant.

The ordering is security-significant. The BFF removes any browser-supplied
`X-Tenant-Slug` and creates the trusted internal value only from resolved route
context. When that trusted header is present, it wins over host matching. When it
is absent, the API checks the mapped custom domain before an eligible subdomain.
Do not configure an edge proxy to copy an untrusted client header through to the
API.

Path routing remains available when host routing is enabled. Its empty prefix
means the default `/{slug}` shape; a configured nonempty prefix is a supported
deployment choice, not a compatibility shim. Reserved system and application
segments cannot be assigned as tenant slugs.

---

## Implemented Public SEO Surface

Search engine optimization is intentionally focused on driving organic discovery of public community events:

* **Automated `/sitemap.xml`**: Dynamically renders indexable URLs for active public events and organizations.
* **Environment-Aware `/robots.txt`**: Automatically disables indexing (`Disallow: /`) in `Development` or `Staging` environments while permitting indexing in `Production`.
* **Social Sharing Cards**: Server-rendered Open Graph (`og:image`, `og:title`) and Twitter Cards for public event detail pages.
* **Structured Data (Schema.org JSON-LD)**: Injects rich `Event` schema markup (start/end time, venue location, organizer, ticket availability) to qualify for Google Event Search cards.
* **Anti-Crawl Protections**: Administrative consoles, checkout sessions, and non-public event drafts automatically emit `<meta name="robots" content="noindex, nofollow">`.

---

## Operational Scope & Limits

The platform automates structured metadata and search cards, but does not provide search engine ranking guarantees or Google Search Console API synchronizers. Operators remain responsible for domain reputation, DNS health, content quality, and search engine ownership verification.

---

## Related Guides & Next Steps

* **[Multi-Tenancy Architecture](../security-and-identity/multi-tenancy.md)** — Learn how browser paths and host headers resolve tenant boundaries.
* **[White-Labeling & Branding](white-labeling.md)** — Customize storefront appearance while preserving governance locks.
* **[Docker Compose Reverse Proxy Setup](../self-hosting/docker-compose.md#5-reverse-proxy--tls-configuration)** — Configure Caddy, Traefik, or Nginx for custom domains.
* **[Troubleshooting Tenant Routing](../configuration-and-operations/troubleshooting-and-health.md)** — Diagnose 404 unknown host and redirect issues.
