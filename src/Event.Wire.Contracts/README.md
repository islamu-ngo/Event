# Event.Wire.Contracts

`Event.Wire.Contracts` contains versioned, machine-consumed representations that must behave
identically in server and isolated client runtimes.

The assembly and project name are exactly `Event.Wire.Contracts`. Public namespaces use the
`ISLAMU.Wire.Contracts.*` root to avoid colliding with the existing `Explore.Domain.Event`
aggregate type and the cross-language reserved keyword `event`.

## Include

- Strict, versioned payload and deep-link codecs
- Opaque bearer, cursor, continuation, ETag, and idempotency value types
- Stable media types, header names, and ProblemDetails extension keys
- Cross-runtime JSON converters and wire enums
- Capability and bootstrap envelopes shared by API/BFF and client runtimes

Group each contract family by feature, such as `Admissions/`, `Pagination/`, `Concurrency/`,
`Capabilities/`, and `Web/`.

## Exclude

- Domain entities or business workflows
- Application handlers, repositories, or service ports
- ASP.NET Core, EF Core, Blazor, or JavaScript interop code
- QR/image rendering libraries
- Handwritten API DTOs when generated OpenAPI clients are authoritative

The project remains package-free and may not reference Domain, Application, Infrastructure,
Persistence, API, BFF, or client projects.
