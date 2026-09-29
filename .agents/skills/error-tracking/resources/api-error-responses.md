ABOUTME: Standard error response envelope for API failures.
ABOUTME: Uses ProblemDetails with trace/correlation identifiers.

# API Error Response Contract

Define one consistent error contract for all API failures.

## Standard Format

Use RFC 7807 `ProblemDetails` as the base envelope:

- `type`: stable error category URI
- `title`: short summary
- `status`: HTTP status code
- `detail`: human-readable explanation
- `instance`: request path

Add extensions for operational tracing:

- `traceId`
- `correlationId` (if available)
- `errors` (validation dictionary for field-level issues)

## Status Code Mapping

- `400`: validation and malformed request
- `401`: unauthenticated
- `403`: authenticated but not permitted
- `404`: missing resource
- `409`: conflict (for example duplicate unique values)
- `422`: semantically invalid payload when distinct from shape validation
- `500`: unexpected server error

## Practical Rules

- Never return raw exception internals in production.
- Keep validation errors deterministic and field-addressable.
- Emit structured logs before writing response bodies.
- Include trace identifiers in both logs and response payload for incident triage.

## Example

```json
{
  "type": "https://httpstatuses.com/400",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/events",
  "traceId": "00-1f6...",
  "errors": {
    "title": ["Title is required"],
    "eventTypeId": ["Event type not found"]
  }
}
```
