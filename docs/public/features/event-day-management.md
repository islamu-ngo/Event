# Event day management

Event owners can create and manage days for their events. Day writes are authorized against the stored parent event in the current tenant; a submitted event identifier cannot grant access to an existing day. Missing, deleted and foreign-tenant parents do not confer write authority.

PATCH requires the current concurrency stamp as a quoted strong `If-Match` value. Weak, unquoted, missing and malformed values are rejected. A stale stamp, including two competing updates to the same version, returns `409`; reload the day before retrying.

A day cannot move to another event: its parent forms part of the persisted key used by sessions and ticket entitlements. A different parent in PATCH returns validation `400`, rather than a server error. An unchanged parent remains accepted. This does not change request or response shapes.

Banner references must be active public raster images in the same tenant. Omitted PATCH fields retain their values; explicit clear removes the reference, not the stored image. Storage metadata lookup failures do not create a day. Public day reads retain parent publication and visibility filtering; management reads require event-management authorization.
