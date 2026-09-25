# Protected event-resource destination assurance

- Owner: Security, BFF and hosting maintainers.
- Activate when: Closing the protected-destination plan's unverified transport and host-composition gates before a release.
- Shipped baseline: PR #54 contains tenant/resource-bound encryption, a write-only input contract, safe-origin disclosure, current-authority redirect, targeted protector cases and a coordinated fresh-host keyring restore.

## Acceptance

- Exercise the production Split and Combined browser/BFF path against a controlled external origin through 301, 302, 303, 307 and 308 responses, including every supported hop. Observe that neither the API nor BFF auto-follows the external redirect and that Authorization, cookies, tenant, antiforgery, setup/admin, correlation and privileged forwarding headers never reach that origin.
- Use synthetic dynamically generated destinations and approved environment/secret-provider bindings. Prove hostile schemes, userinfo and origins, wrong tenant/resource/purpose, ciphertext tampering, missing keys, retained retired keys and changed application identity fail without echoing values in responses, serialized models, logs or traces.
- Verify production and Combined Data Protection registration with a stopped-host coordinated database/bytes/keyring restore, rotation and key-retirement boundaries; a test-only durable provider is not production-composition evidence. Keep keys until every encrypted destination that needs them is gone.
- Retain the existing current-authority check before headers. A permitted redirect exposes only its validated Location with `Referrer-Policy: no-referrer` and `Cache-Control: no-store`; an unavailable or revoked destination yields no Location. An already disclosed provider URL cannot be recalled by this platform.
- Record per-hop receipts and redacted telemetry. The browser navigation portions also close **S22/S34** in [browser acceptance](event-resource-browser-acceptance.md); targeted HTTP and Split BFF tests already passing do not count as this multi-hop proof.
