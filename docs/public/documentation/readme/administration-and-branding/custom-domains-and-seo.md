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

1. **Verify the requester and domain control**: Check the tenant administrator's authority, then independently verify control of the exact domain with a fresh challenge. A tenant entering a name, DNS resolving to your ingress, or a successful certificate lookup is not proof that this tenant controls it.
2. **Record the proof and provision DNS/TLS**: Retain an operator proof receipt tying the exact host to the tenant UUID, then configure DNS and valid TLS at the ingress. Install the short-lived deployment attestation described below.
3. **Reverse-Proxy Header Forwarding**: Configure exact trusted proxy IPs or bounded CIDRs and send a complete `X-Forwarded-For`, `X-Forwarded-Host`, `X-Forwarded-Proto` chain. Strip caller-supplied forwarding fields before creating the trusted chain.
4. **Tenant Resolution Order**: In [Multi-Tenant Mode](../security-and-identity/multi-tenancy.md), API requests evaluate:
   $$\text{Trusted X-Tenant-Slug} \longrightarrow \text{Custom Domain} \longrightarrow \text{Subdomain} \longrightarrow 404$$
5. **Fail-Closed Verification**: Unknown/unproved hosts and malformed or untrusted forwarding return an empty, non-cacheable `400 Bad Request` before redirects or tenant routing. An accepted instance host with no resolvable tenant still returns the ordinary tenant `404`.

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

### Operator ownership attestations

Configure `PUBLIC_BASE_URL` as the canonical public HTTPS URL. Add only actual
instance/backend/admin aliases to `EffectiveHostAuthority:InstanceHosts`; no
wildcards are accepted. The default accepts localhost only and is not a public
deployment configuration. Sensitive onboarding callback URLs use the canonical
authority even when a legitimate tenant vanity host made the request.

Tenant domain reservations remain managed through normal tenant administration.
They do not activate the domain by themselves. After verifying ownership,
install a deployment-owned `EffectiveHostAuthority:TenantDomains` entry with:

| Field | Operator responsibility |
|---|---|
| `Host` | Exact normalized host, without scheme, port, wildcard or trailing dot. |
| `TenantId` | UUID of the tenant whose administrator demonstrated control. |
| `VerifiedAt` | UTC time the external ownership proof was verified. |
| `ExpiresAt` | UTC expiry, after verification and no more than 24 hours later. |
| `EvidenceReference` | Nonsecret receipt identifier for the operator's retained proof, at most 256 characters. |

Use the native .NET deployment configuration section, or equivalent indexed
environment keys such as `EffectiveHostAuthority__TenantDomains__0__Host`.
For Compose, explicitly project each attestation field into the API service;
these records are not automatically copied from `.env` or tenant settings.
Keep proof evidence itself outside tenant-visible configuration. A missing,
expired or future-dated attestation denies routing; an inactive tenant or changed
reservation denies routing even if a previous request succeeded. Duplicate host
attestations and overlong validity periods reject configuration.

Renew only after re-verifying control. To revoke, remove the attestation and
reload the deployment configuration on every replica, or restart all replicas
when using environment variables. Also remove the ingress binding/certificate
when retiring a domain. Expiry is checked per request without a cache grace
period; a replica that has not received revocation can still honor its existing
receipt until it expires, so fleet convergence is an operator duty. Event does
not automatically perform DNS challenges or renew ownership proofs.

For instance-owned subdomains, configure an exact
`EffectiveHostAuthority:SubdomainBaseDomains` base that is also an approved
instance host, enable the subdomain resolver, and reserve one tenant label.
The resolver's instance base-domain setting must match that approved base.
Nested labels and unreserved/inactive tenants are rejected. Tenant routing
requires a fresh reservation lookup rather than a positive cache hit.

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
* **[Troubleshooting Tenant Routing](../configuration-and-operations/troubleshooting-and-health.md)** — Diagnose host rejection, unresolved tenants and redirect issues.
