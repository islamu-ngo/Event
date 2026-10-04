# Unified Administration Consolidation

Date: 2026-10-02

## Scope and Naming

The settings layout orchestrates both instance and tenant settings in
SingleTenant mode. `UnifiedAdminSettingsLayout` therefore belongs in the shared
administration component directory rather than under instance-only components.
Individual instance and tenant sections retain explicit scope names.

## Host Classification Is Not Authority

Dedicated administration-host classification selects a landing route through
the existing router. It neither needs a second Blazor shell nor supplies an
administrator grant. Backend authorization, tenant context and fresh HAL
affordances remain authoritative.

## Capability Parity Before Deletion

Removing a redundant shell requires comparing its overview, domains and
operations information and actions with the retained console. Tenant lifecycle
and tenant-plan pages remain independently reachable from unified navigation;
moving presentation must not silently remove their capabilities.

## Served HAL Navigation

A rendered component test that supplies a `tenants` relation cannot prove the
server emits it. Verify the overview through the real HTTP authorization and HAL
pipeline, assert the collection route and permission metadata, and test that
single-tenant mode does not advertise multi-tenant-only lifecycle navigation.
The console's tenant entry depends on that served relation, not local role checks.

## Configuration and Fleet Boundaries

An origin rename spans runtime readers, AppHost, Compose, launch profiles, the
setup catalogue and generated environment metadata. Refresh the catalogue
through `eng/setup-assistant/EnvironmentCatalogueGenerator`, not a handwritten
JSON replacement.

Internal `/api/admin/instance/*` administration is separate from external
`/api/management/*` fleet registration and credentials. Do not globally replace
every `ControlPlane` identifier: external fleet concepts keep their names.
