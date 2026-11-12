# ISLAMU Event Plane Reference (`EVENT`)

> **Audience:** AI agents | Maintainers
> **Status:** Implemented
> **Owner:** Agent Context
> **Last Verified:** 2026-11-12
> **Source Anchors:** `event/schema/states.yaml`, `event/schema/labels.yaml`, `event/schema/types.yaml`, `event/work/modules.yaml`, `event/work/workitems.yaml` (Epics) (gitignored local Plane Compose project), [ADR](../../../../docs/internal/adr/ADR-plane-project-management-standardization.md)

Stable identifiers for Plane MCP calls against ISLAMU Event. The gitignored `event/schema/*.yaml` files are authoritative. If an ID here disagrees with them, trust the YAML and correct this file.

## Project

| Field | Value |
|---|---|
| Workspace slug | `islamu` |
| Project | `ISLAMU Event`, identifier `EVENT` |
| Project UUID | `284d0bcc-083a-4af0-a909-6b1480dec7e2` |

## Version Modules (release scope; `EVENT` uses no Cycles)

| Module | UUID | Status | Holds |
|---|---|---|---|
| `v0.1.0` | `e8e48050-d259-4808-a13b-5de8f9b68d70` | in-progress | Shipped foundation and remaining first-release scope (no release has shipped yet) |
| `v1.0.0` | `638fb867-208e-4d50-a006-1bb6ee66cb75` | backlog | GA-horizon scope |

## Work-Item Types

| Type | UUID | Notes |
|---|---|---|
| Epic | `1ca65f63-a3ad-450c-bef7-ef502e4f7d85` | `is_epic: true`; bounded program, never has a `parent` |
| Feature | `dbb56269-30af-475d-bae4-b158dbc41e03` | Default type (`defaults.type: Feature` in `event/plane.yaml`) |
| Bug | `34f1c263-7382-43c0-bdf6-80ee2367a893` | |
| Security | `67c75932-1eae-488c-9a09-ab7cb41dd3b7` | Bounded, completable security deliverable |
| Refactor | `132d9004-dd6c-42f1-b4ff-d32692746f78` | |
| Test | `d1517651-93c6-4451-b49a-839758ca5f34` | Cross-feature test infrastructure only; feature tests are DoD |
| Docs | `4de3a47d-c12f-44e2-86db-b845c27fd796` | Cross-feature documentation only |
| CI/CD | `9bee74b8-875f-4233-9aaa-d5a643bf38a6` | |
| Config | `b3f637e4-76ba-4071-a280-42cb006835b9` | |
| Chore | `552e968a-406f-4e4a-b26d-b05f795bbcdd` | |

Hierarchy: `Epic → Work Item → optional Sub-item`; longest `parent` chain is 2.

## States

Items in `Candidate`, `Backlog`, `Waiting Dependency`, or `Todo` carry no assignees; assign someone when the item moves to `Ready` or `In Progress`.

| State | UUID | Group |
|---|---|---|
| Backlog (default) | `51fc7303-1224-4aae-ae89-3d3fcabe3905` | backlog |
| Candidate | `5b913d8f-6344-4597-84be-6a2733d1c9a3` | backlog |
| Todo | `3ad62172-eb5f-4e03-884f-0fa3392211ce` | unstarted |
| Scheduled | `9f559470-5fca-4636-bfae-303ae6b0af35` | unstarted |
| Ready | `49c11eb9-4a06-4776-8260-1431cec9a21c` | unstarted |
| Waiting Dependency | `c72e6e47-e578-4199-ad21-046597da06b2` | unstarted |
| In Progress | `06895d7e-4494-4b6b-b16a-30d2ca259c65` | started |
| Review Needed | `b2b6bda9-36e2-44b4-a9ea-d3664352c1b6` | started |
| Changes Requested | `d4931873-45d7-496b-bd5a-6405be7f6ac2` | started |
| Testing | `c207dd60-4ff0-498b-bb41-88dc1c755137` | started |
| Ready for Release | `a4b87a78-305b-4296-a05d-8f817241334e` | started |
| Released | `0a2eacca-8523-4857-8c82-658dea34b669` | completed |
| Monitoring | `520fc0e9-d920-40e5-9cf0-3d6d437d27b4` | completed |
| Done | `836060da-ba98-43a5-b6ab-f05cd9c2769b` | completed |
| Cancelled | `bbbe851e-ca9d-47c4-9015-ff436232ba45` | cancelled |
| Won't Do | `63e87952-e120-4563-84b8-6138f6c5304d` | cancelled |
| Replaced | `242303d8-fb84-4338-a1d7-a2de6bdb8e93` | cancelled |

## Labels

Surface labels say which codebase surface a deliverable touches (multi-select). Domain labels classify the capability.

| Label | Kind | UUID |
|---|---|---|
| API | Surface | `6aa64816-e481-4815-9dd8-e7f2e5efbc04` |
| Web App | Surface | `113b3ec2-f1ee-442b-a8c2-8fd54222904e` |
| Database | Surface | `246a0805-4d13-4240-a1db-9c30700303cb` |
| KMP (mobile, watch & TV) | Surface | `1dbbfddd-801d-499f-b82d-076962df5fdd` |
| Desktop (Avalonia) | Surface | `a377b45f-7c6d-49d4-838d-354fb6d7e1c8` |
| TUI | Surface | `d73ffc16-fbcf-45ef-838b-a73a01bb6584` |
| CLI | Surface | `61fe05bf-cb48-488e-b27f-3103055d5856` |
| Payments | Domain | `c13278ff-ef9f-49b6-bf66-c63644dcac2d` |
| Finance | Domain | `6d72f6e7-a520-485c-add6-f91bdaff45ce` |
| Federation | Domain | `980e1183-60e4-43f5-a3ff-2e4b8385ecca` |
| Decentralization | Domain | `e8010bda-52ff-49c0-a6c7-4dd2f2a87785` |
| Integration | Domain | `462822b0-1615-445d-8b82-8e51cec71511` |
| Compliance | Domain | `7c524715-6d58-4203-a776-c9634a5a377c` |
| Legal | Domain | `bae5ec2c-1d9e-436f-bb13-55dcd0899409` |
| Accessibility | Domain | `6ce471a7-c4b7-44f2-be96-0d4bda54c190` |
| AI | Domain | `5d68c978-6d0d-4994-89ef-3b57f8a79839` |
| Automation | Domain | `6a1d5a15-612e-4657-b6ac-b5e3835901c4` |
| Research | Domain | `2bcaf737-8adb-4aa1-9970-87e7a24ca40f` |
| Prototype | Domain | `f84e2e50-1325-48d9-992a-7c1da80e5ac7` |
| Marketing | Domain | `cafa9b50-3a80-4b6c-bd3e-54c1c8960708` |
| Docs | Retiring | `de71e194-4605-4de4-a1c5-305eaac19e19` (redundant with type `Docs`; do not apply to new items) |


## Capability Epics

Bounded capability programs created by the 2026-11 backlog standardization. Parent new Work Items under the matching Epic; an Epic never has a parent. Each Epic holds one `Delivered foundation: <Epic> (v0.1.0 rollup)` Feature that records the commit-level history it replaced.

| Epic | ID | UUID |
|---|---|---|
| Authentication Provider Matrix v1 | `EVENT-6850` | `80e440cc-e5c9-47e2-9bad-8099ce4c7880` |
| Event Lifecycle, Sessions & Publishing | `EVENT-6851` | `cf1cca25-5f90-48e4-93b8-b85a845206d3` |
| Registration, Ticketing & Admission | `EVENT-6852` | `4945c8aa-4bae-45d6-a447-67ac0935f5cd` |
| Payments & Commerce (Stripe Connect first) | `EVENT-6853` | `c42a470d-4b27-4169-820a-8458adb5c072` |
| Notifications & Email Delivery | `EVENT-6854` | `ea903ff8-088f-4181-9296-1664b53ad99b` |
| Webhooks, Public API & Integrations | `EVENT-6855` | `ad8d0e71-3760-4664-ab62-12dc7fef7e78` |
| Storage & Managed Files | `EVENT-6856` | `57815b2e-1714-47f7-a7ba-632cf232dc99` |
| Multi-Tenancy, Onboarding & Control Plane | `EVENT-6857` | `7c44fd5b-c56d-4189-816a-4263933cccac` |
| Moderation, Reporting & Support Access | `EVENT-6858` | `7cb13651-5e24-48b5-b77d-7483cb278be5` |
| AI Assistant & RAG | `EVENT-6859` | `a114d2d9-a299-4bb6-bdc0-350306f80020` |
| Federation & ATProto v1 | `EVENT-6860` | `a42dded4-5c82-4752-8cae-fb589dd1d9bb` |
| Self-Hosting, Configuration & Release Governance | `EVENT-6861` | `47ebe021-7f53-4788-bc7b-fd8a7ccd8b32` |
| Accessibility & Localization v1 | `EVENT-6862` | `873478e1-83f9-40cc-a38d-ab505562242e` |
| Organizer Analytics & Dashboards | `EVENT-6863` | `b532111a-8b28-4e6c-9918-7cdf35b5712f` |
| Additional Client Surfaces (KMP, Avalonia, TUI, CLI) | `EVENT-6864` | `b3a51db9-71b5-4408-aac4-94317ff00247` |
