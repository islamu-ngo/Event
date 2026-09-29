ABOUTME: Documents implemented sitemap, robots, render-policy, and public-discovery SEO behavior.
ABOUTME: Separates source-backed SEO features from planned or unsupported site-wide SEO automation claims.

# SEO And Public Discovery

> **Audience:** Operators | Admins | Integrators | Contributors
> **Status:** Mixed
> **Owner:** Frontend
> **Last Verified:** 2026-07-16
> **Source Anchors:** `Explore.API/Controllers/SitemapController.cs`, `Explore.Application/Features/Seo/Handlers/Queries/GetSitemapEventsQueryHandler.cs`, `Explore.Infrastructure/Services/PublicUrlBuilder.cs`, `Explore.Blazor/Controllers/RobotsController.cs`, `Explore.Blazor/Components/App.razor`, `Explore.Blazor/Extensions/BffManifestEndpoints.cs`, `Explore.Blazor.Client/Routes.razor`, `Explore.Blazor.Client/Services/RuntimeRenderPolicyService.cs`, `Explore.Blazor.Client/Pages/Events/EventDetail.razor`, `Explore.Blazor.Client/Pages/Events/EventDetail.razor.cs`, `Explore.Application/Features/PublicExperience/`, `Explore.Application/Services/TenantPolicySettingService.Read.cs`, `Explore.Application/Services/TenantPolicySettingService.Apply.cs`, `docs/RENDER_POLICIES.md`, `docs/ADMIN_GUIDE.md`

SEO support is implemented as a focused set of public-discovery primitives: sitemap generation, robots output, render-policy decisions for public routes, and tenant public-experience settings. Do not describe this as full site-wide SEO automation.

## Sitemap Behavior

`GET /sitemap.xml` is public XML output. It combines static public routes with published public event URLs.

| Sitemap Input | Behavior |
|---|---|
| Static routes | Includes public static paths such as `/`, `/events`, `/about`, `/contact`, `/privacy`, `/terms`, and `/community-guidelines`. |
| Event routes | Includes published public events returned by the sitemap query handler. |
| URL base | Uses the public request base URL, including forwarded host/proto and path base handling. |
| Size boundary | Event projection is clamped for sitemap safety; do not document unlimited event output. |

Repository-level sitemap coverage verifies that the event set is tenant-filtered and limited to published public events. Controller output still combines those event entries with the static public routes listed above.

## Robots Behavior

`GET /robots.txt` is public from the Blazor host:

| Environment | Behavior |
|---|---|
| Production | Allows crawling and advertises `/sitemap.xml`. |
| Non-production | Returns `Disallow: /`. |

Robots output uses forwarded host/proto context for the sitemap URL. Integration coverage verifies that non-production hosts disallow crawlers and production robots output uses forwarded proto/host for the authoritative sitemap URL. Do not document non-production environments as indexable.

## Render Policy And Public Routes

Runtime render policy groups routes into onboarding, admin, public SEO, and operational buckets. Public SEO route classification influences prerender behavior, especially under the `SeoBalanced` preset.

Important boundaries:

- Onboarding routes render as `InteractiveServer` regardless of stored render-mode settings.
- Public SEO routes default toward prerendering when advanced overrides do not override the preset.
- Defaults fall back safely when tenant settings are missing.
- `Explore.Blazor.Client/Routes.razor` registers `/home`; keep route documentation tied to that router mapping and the runtime classifier instead of inventing separate page-level SEO behavior.

See [RENDER_POLICIES.md](RENDER_POLICIES.md) for the authoritative render-policy model.

## Public Experience And Tenant Controls

Public discovery settings are tenant-scoped and managed through admin settings rather than a standalone SEO admin page.

| Area | Implemented Control |
|---|---|
| Instance render policy | Instance admin controls presets, advanced overrides, route groups, and tenant delegation locks. |
| Tenant render policy | Tenant admin can override allowed render-policy settings when not locked by instance governance. |
| Tenant public experience | Tenant admin controls public-experience mode, catalog labels, primary organization, home blocks JSON, CTA JSON, and event-section presets. |
| Tenant domains | Tenant admin controls preferred home-page/domain-facing settings through tenant domain/public experience surfaces. |

Tenant resolution and public URL generation are domain-aware. See [MULTI_TENANCY.md](MULTI_TENANCY.md) and [ADMIN_GUIDE.md](ADMIN_GUIDE.md) for the broader administration context.

## Event Detail Metadata

Public event detail pages emit crawler and preview metadata from the same primary event URL helper used by the share and calendar flows.

| Metadata | Behavior |
|---|---|
| Preferred URL | Built from the public event slug/code through `AbsoluteUrlBuilder`. |
| Open Graph and Twitter | Uses event title, normalized description, preferred URL, and featured image/public storage image URL. |
| Structured data | Emits schema.org `Event` JSON-LD for crawlable public events only. JSON is generated through `System.Text.Json`; no raw HTML rendering helper is used. |
| Noindex | Emits `robots noindex, nofollow` for non-public visibility or non-crawlable event statuses such as draft, cancelled, or moderated states. |

Do not claim structured data for every route. Current structured data automation is scoped to public event detail pages.

## Web Manifest

The public Blazor shell links a minimal web app manifest for launch install metadata. The BFF serves the manifest dynamically from public-experience branding, using the configured brand display name, logo URL, and favicon URL when available, with generic fallback values when branding cannot be resolved. No service worker, offline cache, push, or background sync behavior is registered by the manifest work.

## Implemented Vs Planned

Implemented:

- Public sitemap endpoint.
- Public robots endpoint with environment-sensitive indexing behavior.
- Public route render-policy classification.
- Event detail page metadata/authoritative behavior in the Blazor client.
- Event detail schema.org `Event` JSON-LD for crawlable public event pages.
- Event detail noindex metadata for non-public or non-crawlable event states.
- Minimal public web app manifest and app-shell manifest link.
- Error pages with page-level noindex behavior.
- Tenant public-experience controls for discovery-oriented presentation.

Not proven by inspected source:

- Site-wide dynamic metadata on every route.
- Structured data / JSON-LD automation outside public event detail pages.
- Preferred URL management for every possible page.
- SEO score auditing, keyword tools, or search-console integrations.
- A standalone SEO admin page.

## Related Documentation

- [RENDER_POLICIES.md](RENDER_POLICIES.md) - render-policy governance.
- [ADMIN_GUIDE.md](ADMIN_GUIDE.md) - public discovery and admin workflow context.
- [MULTI_TENANCY.md](MULTI_TENANCY.md) - tenant and domain resolution.
- [BLAZOR.md](BLAZOR.md) - Blazor render-mode architecture.
