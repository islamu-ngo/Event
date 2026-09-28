# Development-only agent browser persona provisioning

> **Status:** Implementation under verification; not approved as a usable profile until the real login gates pass.
> **Audience:** Maintainers and local operators
> **Source anchors:** `AgentBrowserPersonaStartup`, `AgentBrowserPersonaBindingSeeder`, `AgentBrowserProvisioningOptions`

## Boundary

This is an explicit Development-only fixture, not an administrative bootstrap
mechanism for deployed instances. Admission requires AgentBrowser mode, opt-in,
Split hosting, Local authentication and authorization, colocated Identity,
PostgreSQL database `islamu_event_agent`, and configuration-manifest mode Off.
Standalone rejects the mode using its actual combined-host identity, before
service composition or external Identity migration.

The migration service and API migration owner perform schema and lookup work
only. They call `LookupTableSeeder` directly: `DatabaseSeeder.SeedAsync(false)`
also backfills tenant branding documents and therefore is not schema-only on a
foreign database. Ordinary development seeding remains unchanged.

## Lifecycle and ownership

The API holds a dedicated PostgreSQL session advisory lock across ownership
preflight, configured bootstrap, fixture transactions, and credential transitions.
Contention fails immediately; the entire operation has a 120-second cancellation
deadline. Each lifecycle call uses a fresh scope, never a caller-owned transaction.
The connection is closed even when lock release fails.

An absent configured bootstrap marker requires an empty business and Local
Identity database. An existing marker must match the configured Local subject and
generation. Foreign privileged bindings, tenant PDP overrides, reserved profile
collisions, partial foundation records, and mismatched receipt bindings fail closed.
The Pending marker is persisted before the atomic tenant/organization/event
foundation. Tenant activation uses the native tenant creation service and typed
branding and synthetic legal-identity documents.

The configured administrator runner owns platform authority. Other personas use
the existing Local credential creation and activation contracts. Application user,
personal actor, and Local login IDs are taken from the native creation receipt.
Each application binding transaction includes its initial membership and scoped
grants. Organizer eligibility comes from `OrganizationTenant`, not an Identity
role claim. Event grants are native `EventRoleAssignment` entities.

First-use completion proves the original temporary password through the normal
login handler, authenticates the signed challenge with the named replacement
scheme, and invokes the native replacement command. A lost replacement response
is resolved by rereading the receipt, not by repeating a completed transition.
The first successful transition also checks ordinary signed authentication.

## Secret and replay finality

Initialization secrets come only from `ISecretResolver`. All required values are
validated before bootstrap or grant writes. Non-admin temporary passwords use
HMAC-SHA256 over a versioned operation identifier, keyed by the final persona
password; they are never persisted or logged. Existing pending subjects must
still accept their derived temporary password. Changing that authority mid-run
fails closed.

Every launch resolves the Local JWT signing authority and compares it to both API
validation schemes and bound configuration. Missing, malformed, short, or
mismatched keys fail closed; the ordinary random validation-key fallback cannot
admit this profile.

Ready metadata plus the Replaced receipt and exact application binding is final.
Completed replay resolves neither initialization password, never logs in to
compare the original final password, and never synchronizes profile fields.
An already committed exact application graph never receives grant writes.
Revoked or deleted grants remain revoked or absent; damaged identity graphs fail
instead of being repaired.

## Fixtures and recovery

The six fixed synthetic accounts are administrator, tenant administrator,
organizer, event/registration manager, attendee, and tenant moderator. There is
no global Moderator or GroupAdmin grant. A second tenant and organization/event
are negative controls. Event dates are persisted once at UTC date plus seven days
with a two-hour session; startup never refreshes expired dates.

Keep initialization secrets stable until provisioning finishes. For superseded
operations, missing completed actors, or conflicting ownership, stop the agent
profile and inspect the native credential/bootstrap state. Do not reset or
regrant through startup. Recreate only the explicitly isolated agent database
when discarding its data is intended; never point this mode at an existing
development or deployment database. No schema migration or public endpoint is
introduced by this provisioning path.
