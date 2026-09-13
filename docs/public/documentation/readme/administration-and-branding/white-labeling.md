---
description: Customize presentation while preserving legal and operator accountability disclosures.
---

# White-Labeling & Branding

White-labeling customizes the visual identity and presentation of tenant storefronts and community hubs. It does not alter platform, payment, tax, provider, or legal liability boundaries.

---

## Customizable Presentation Layers

Depending on delegated administrative scope (see [Admin Hierarchy](admin-hierarchy.md)), operators can customize:

* **Theme Tokens**: Brand primary and secondary colors, typography, and dark/light mode defaults.
* **Header & Navigation**: Logos, favicons, custom menus, and external links.
* **Footer Layouts**: Social links, copyright notices, and supplementary link groups.
* **Email Templates**: Branded headers and footers for [SMTP Outbox Delivery](../communications-and-notifications/email-smtp.md).

> [!IMPORTANT]
> **Governance Locks Retain Legal Disclosures!**  
> Instance administrators can enforce **Governance Locks** on footer components. This prevents individual tenants from hiding or deleting legally required terms of service, privacy notices, or operator identity disclosures.

---

## Visual Branding vs. Legal Accountability Identity

Visual branding is not legal identity:
* Public pages preserve **Operator Legal Identity** as a mandatory, non-overrideable disclosure resource (configured via `INSTANCE__OPERATORIDENTITY__*` in [Environment Variables](../configuration-and-operations/environment-variables.md#9-operator-legal-identity-production-gate)).
* If required public disclosure identity is unavailable, the footer fails closed with `503 tenant_identity_unavailable`. A brand logo or display name will never be substituted as a legal fallback.

---

## First Access to Tenant Branding

Opening tenant branding initializes a missing default branding document. Two
signed-in callers opening it simultaneously receive the same saved document and
revision; the later insert does not replace the winner's name or assets.
Repeated reads do not reset customized branding. Editing still requires the
existing tenant permissions, governance checks, and current concurrency stamp.
This initialization does not create a missing tenant or supply legal identity.

If database cleanup also fails during initialization, the original failure is
preserved for existing recovery handling. Cleanup failure never turns an
uncertain initialization into a successful response or resets saved branding.

## Standard Operational Workflow

1. Establish tenant ownership and bind approved domains (see [Custom Domains & SEO](custom-domains-and-seo.md)).
2. Configure required accountability identities in `.env` before customizing visual branding.
3. Apply theme, navigation, and footer settings through the [Tenant Admin Console](admin-guide.md) or import via [Configuration Manifests](../configuration-and-operations/configuration-manifests.md).
4. Verify that governance-locked disclosures remain visible and immutable.
5. Test public pages across desktop, tablet, and mobile viewport breakpoints.

---

## Related Guides & Next Steps

* **[Administration Web Walkthrough](admin-guide.md)** — Step-by-step branding customization in the UI.
* **[Custom Domains & SEO](custom-domains-and-seo.md)** — Bind custom vanity domains to individual tenants.
* **[Configuration Manifests](../configuration-and-operations/configuration-manifests.md)** — Export and deploy declarative branding bundles.
* **[Operator Legal Identity Reference](../configuration-and-operations/environment-variables.md#9-operator-legal-identity-production-gate)** — Required production disclosures.
