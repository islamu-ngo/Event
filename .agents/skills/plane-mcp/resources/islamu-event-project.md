# ISLAMU Event Plane Reference (`EVENT`)

> **Audience:** AI agents | Maintainers
> **Status:** Implemented
> **Owner:** Agent Context
> **Last Verified:** 2026-11-12
> **Source Anchors:** `event/schema/states.yaml`, `event/schema/labels.yaml`, `event/schema/types.yaml`, `event/work/modules.yaml` (gitignored local Plane Compose project), [ADR](../../../../docs/internal/adr/ADR-plane-project-management-standardization.md)

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
| Desktop (Avalonia) | Surface | pending schema push |
| TUI | Surface | pending schema push |
| CLI | Surface | pending schema push |
| Payments | Domain | pending schema push |
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

Label UUIDs marked "pending schema push" are filled in after `plane schema push` mints them; until then, add those labels by name through YAML, not by MCP UUID.
