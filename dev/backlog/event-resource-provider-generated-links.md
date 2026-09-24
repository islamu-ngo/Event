# Provider-generated event-resource links

- Owner: Integrations and security.
- Activate when: A selected external provider can enforce expiring, revocable destinations.

## Acceptance

- Approve a source-free provider API contract and compatible dependency terms before implementation.
- Clamp provider TTL to a fixed upper bound and the resource's remaining access window; fail closed when the provider is unavailable or the schedule drifts.
- Test destination leakage, revoked rights, replay, rotation and subsequent navigation separately. Do not confuse a platform redirect's expiry with revocation of a previously revealed provider URL.
