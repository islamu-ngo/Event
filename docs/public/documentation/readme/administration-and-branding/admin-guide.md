---
description: Walkthrough of administrative consoles and management workflows for instance and tenant admins.
---
<!-- ABOUTME: Operator walkthrough of instance, tenant and organization administration. -->
<!-- ABOUTME: Explains console capabilities and safe Local account credential handover and recovery. -->

# Administration Guide

This guide walks administrators through the web consoles in the Blazor management interface, covering instance-level controls, tenant provisioning, monetization, branding, and organization governance.

---

## 1. Administrative Consoles & Routes

| Administration Scope | Typical Role | UI Entry Points | Capabilities |
|---|---|---|---|
| **Instance Control Plane** | Instance Administrator | `/admin/instance`<br>`/admin/instance/tenants`<br>`/admin/instance/domains` | [Multi-Tenant Governance](../security-and-identity/multi-tenancy.md), provisioning tenants, domain approvals, global quotas, platform settings. |
| **Instance Settings** | Instance Administrator | `/settings/instance` | Default system policies, storage configurations, SMTP defaults, [platform monetization policies](../events-and-ticketing/paid-events-and-payouts.md). |
| **Tenant Administration** | Tenant Administrator | `/settings/admin` | [Tenant Branding](white-labeling.md), lookups, navigation, custom footers, event templates, and [custom registration properties](../events-and-ticketing/custom-properties.md). |
| **Organization Management**| Organization Admin | `/settings/organization/{id}` | Organization profile, membership approvals, verified organizer status, and API keys. |
| **Group Management** | Group Admin | `/settings/group/{id}` | Group profile, public event listings, group branding, and members. |

---

## 2. Instance Administration

In multi-tenant deployments, the **Instance Console** (`/admin/instance`) manages
tenants and platform operations. Instance settings are available in both deployment modes.

### Local Accounts

Open `/settings/instance?section=local-accounts`. The Local accounts entry appears
only when the server advertises the capability for your current instance access.
Tenant administration does not grant permission to manage these shared credentials.

Use **Create local account** to enter the person's email and name. A successful
creation reveals a generated temporary password once. Record the operation ID,
hand over the password privately, then select **Dismiss credential**. The recipient
must replace that temporary password before ordinary sign-in; no email delivery is
required for this supervised handover. Do not save credential-bearing responses in
logs, scripts or support tickets.

For an eligible existing account, **Issue temporary credential** requires a reason.
Reset invalidates its previous credential and sessions, preserves verification,
and requires another private password replacement. Missing or invalid credential
metadata is shown as unknown; the screen does not invent a reset action for it.

If an issuance response is lost, keep its operation ID and use **Check operation**.
Status reads never repeat issuance or reveal the password. Use **Reconcile** only
when offered to complete interrupted account linking. If handover is no longer
possible, inspect the resulting account and perform a separately authorized reset
with a new operation ID. Refreshing or reopening the section cannot recover a
previously dismissed password. See [Authentication](../security-and-identity/authentication.md)
for the sign-in and recovery boundaries.

An access-denied or conflict response can arrive after the credential change was
saved. Keep the original operation reference until its status is known; cancelling
its form or checking another operation does not resolve it. These references stay
only in the open component, so record them before leaving the section.

Successful issuance and recovery refresh the account details on your current
page without dismissing the password handover. Accounts are ordered oldest-first:
a newly created account may be on a later page rather than page one.
If refreshing account details fails, further resets remain unavailable until a
refresh succeeds; the current password handover stays visible.

### Tenant Lifecycle Management
- **Create Tenant**: Provision a new community tenant with a unique slug and primary administrator.
- **Tenant States**:
  - *Active*: Fully operational; events can be published and registered.
  - *Suspended*: Public routes return inactive notices; administrative reads remain accessible.
  - *Archived*: Read-only state prior to scheduled purge.
- **Destructive Purge**: Scheduling a tenant purge requires explicit reason confirmation and typing the tenant slug.

### Platform Monetization
Navigate to `/settings/instance` $\to$ **Monetization** (see [Paid Events & Payouts](../events-and-ticketing/paid-events-and-payouts.md)):
- **Platform Fee Policy**: Set platform fees across paid ticket sales (configured in basis points and optional fixed charges per currency).
- **Platform Contribution**: Enable optional voluntary contributions during checkout with customizable heading and body text.
- *Note*: Changes use optimistic concurrency revisions; concurrent edits fail safely to prevent accidental overwrites.

---

## 3. Tenant Administration & White-Labeling

Tenant administrators manage their community experience via `/settings/admin`:

### Branding & Appearance
- **Themes & Colors**: Configure brand primary and secondary colors (see [White-Labeling](white-labeling.md)).
- **Logos & Favicons**: Upload high-resolution community assets.
- **Navigation & Links**: Customize top navigation links and header menus.

### Custom Footers
- Choose footer layouts, social links, and copyright notices.
- Built-in governance locks prevent tenants from removing legally required disclosures (such as Terms of Service and Privacy Policy).

### Custom Registration Properties
- Define custom questions and fields for events and attendee registrations (see [Custom Properties Governance](../events-and-ticketing/custom-properties.md)).
- Set exposure levels: `Public` (visible on listing), `Private` (visible to organizers only), or `System`.

---

## 4. Organization & Group Governance

Organizers create Organizations and Groups to co-host events:
- **Verification Badges**: Instance and tenant admins can mark verified organizations to give attendees trust.
- **Membership Management**: Assign Owner, Admin, and Member roles within an organization (see [Admin Hierarchy](admin-hierarchy.md)).
- **Payment Connections**: Organizers onboard their own [Stripe Connect](../events-and-ticketing/paid-events-and-payouts.md) accounts directly through organization settings to receive ticket payouts.

---

## Related Guides & Next Steps

* **[Admin Hierarchy & Scopes](admin-hierarchy.md)** — Review permission sets across Instance, Tenant, and Event roles.
* **[White-Labeling & Branding](white-labeling.md)** — Detailed token customization and design tokens.
* **[Custom Domains & SEO](custom-domains-and-seo.md)** — Route custom domains to tenant storefronts.
* **[Configuration Manifests](../configuration-and-operations/configuration-manifests.md)** — Export and import declarative tenant settings.
