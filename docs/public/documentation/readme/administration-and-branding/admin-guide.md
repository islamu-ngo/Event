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

### Getting Started After Setup

Open `/settings/instance?section=getting-started` after fresh sign-in. The checklist
separates **Required before public disclosure**, **Required before paid events**,
and **Recommended**. Each server-reported check retains its current state,
requirement category, reason and remediation authority. An optional capability
shown under Recommended does not become a setup prerequisite.

Use the offered operator-identity and provider actions; missing actions are not
permissions you can recover by changing a browser role or URL. The identity editor
uses the server's operator-kind and country choices, with labeled controls and
visible guidance. If choices are unavailable, refresh rather than inventing a
country or legal form. Registration identifiers are optional; complete only facts
that apply to your operator. Saving uses the document's current revision and edit
permission. Instance and directory identities remain independent.

### Prepare A Private Default Directory

A directory in **Provisioning** is private even when its operator identity is
complete. The completed setup administrator can manage its branding and operator
identity with the existing default-tenant permissions. Saving those documents
never publishes the directory.

Local administrators can replace the temporary password and sign in afresh while
the directory remains private. Replacement requires the protected, short-lived
challenge from temporary-password sign-in, not the completed setup secret. The
private administrator session does not grant public directory access.

In SingleTenant mode, authenticated instance administrators can read the default
directory through the existing control-plane tenant-detail API and follow its
`activate` link when offered. Only the fixed default directory is accepted; this
does not enable the multi-tenant fleet console. Document editing still requires
that directory's tenant-admin grant, not merely a platform role.

Activation is explicit and rechecks current identity and configured capacity. If
another administrator changes the identity first, activation uses that latest
revision. Reload a conflicting edit instead of overwriting it. Repeating a
successful activation does not create duplicate lifecycle history. Public pages
remain unavailable until activation succeeds. No migration or new setting is
needed; setup-completion and getting-started screens are unchanged by this API
capability.

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

### Resource-policy deployment activation (operator API)

Remote event-resource policies use a deployment-level activation fence. This
operator API is separate from resource authoring or publication; activating a
provider does not enable a resource or bypass instance governance.

Use `/api/event-resource-provider-activation` with current instance administrator
authority. Responses are private and non-cacheable.
`Idempotency-Key` does not replay an earlier activation receipt: each retry
rechecks current administrator authority and deployment state.

1. Read `GET /bindings` for the current binding revision. Register or update a
   deployment through `PUT /bindings`, supplying its UUIDv7 deployment ID,
   normalized gRPC endpoint aliases, explicit scope, policy version and the
   expected binding revision. Empty scope selects Cerbos root policy. Keep every
   alias for the same physical policy deployment under the same ID; aliases
   cannot later be removed or transferred to another deployment.
2. Before an external policy writer changes a bound deployment, call
   `POST /begin` with its deployment ID. Resource authority closes immediately.
   Known application-managed publishers fence their own writes, but every
   external writer must participate too.
3. Stop previous writers and verify the declared scope/version across all
   reachable replicas. Call `POST /activate` with the operation ID and epoch from
   the current operation receipt, the scope/version, confirmation that previous
   writers have stopped, the positive reachable-replica count, and the number
   running the declared policy. Both counts must agree. Set
   `frozenParentPolicyContractConfirmed` to `true` only after verifying the
   parent event moderation policies and their derived-role dependencies against
   the frozen principal contract. Custom rules that use the legacy raw `nowUtc`
   attribute must be migrated to derived-time predicates before activation.
   Omitting or declining this confirmation keeps the operation closed.
4. If publication is uncertain, cancelled, incomplete or failed, keep access
   closed. Start a new operation, repair the deployment, verify convergence and
   activate that new operation. An older receipt cannot activate a newer
   operation. After a binding conflict, reload the binding revision before
   making another change.

The application cannot discover undeclared aliases or unannounced remote edits.
Replica counts and convergence are operator attestations, not measurements
inferred from an upload response. Do not treat the local epoch as a remote policy
revision. Manage these bindings only through this protocol, not generic settings
editing or configuration-manifest import.

### Background Scheduler

When enabled, **Background Scheduler** in Instance Settings shows the current
scheduler and job state. If a read is rate-limited or temporarily unavailable,
the section shows an error instead of stale job controls. Wait for the service
to recover, then select **Refresh**; refreshing does not run, pause or resume jobs.

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

### Moving Rooms Between Locations

A room can move to another location in the same tenant only when no session,
session group, or agenda item still references it. This includes any retained
reference on a deleted record: moving a room never rewrites historical schedule
links. Non-location edits remain available for scheduled rooms. Create a replacement
room at the destination if the original room must remain linked to a schedule.

A successful move preserves the room ID and applies the final name and other
submitted edits together. Room names must be unique within the destination
location. If the room or its schedule references change concurrently, reload before
retrying; a rejected move leaves the original placement intact.

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
