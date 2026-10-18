# Future Configurator live integration

Status: deferred by the extraction's explicit offline scope.
Owner: Event backend configuration maintainers, jointly with Configurator runtime
maintainers for any future cross-repository contract.

## Trigger and dependencies

Start only when operators request a supported live workflow beyond local
artifact preparation, and after the external Configurator cutover and packaging
gates are complete. This is not a dependency for source deletion. Preserve the
existing Event SetupLive transport, wire contracts and backend security coverage
until a separately reviewed live contract replaces them.

## Acceptance

Specify target discovery, enrollment scopes, HAL-driven affordances, ephemeral
capability custody, secret authority and revision preconditions before coding.
Prove tenant isolation, expiration/revocation, fail-closed writes and absence of
secret-bearing telemetry with Event-owned backend/API invariants. Configurator
must not gain implicit credential custody, arbitrary network access or a claim
of host acceptance from its offline result envelope.

A future design needs explicit user authorization and coordinated Event and
Configurator changes. Publisher indexes, network definition distribution,
digest-pinned caches and new protected-save operating systems belong to
Configurator's separate backlog, not this Event integration item.

No required extraction behavior, command migration or source-cleanup work is
deferred here.
