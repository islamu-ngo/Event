ABOUTME: Minimal tracing rules for OpenTelemetry usage.
ABOUTME: Defines spans, tagging, and error semantics.

# OpenTelemetry Tracing Patterns

Use traces to connect HTTP, application, and data-layer behavior.

## Trace Pipeline

- Incoming HTTP request span
- Native operation request span (command/query)
- Database spans for repository calls
- External dependency spans (storage, auth provider, webhooks)

## ActivitySource Guidelines

- Define one `ActivitySource` per service/application assembly.
- Use stable operation names (`Events.GetList`, `Events.Create`, etc.).
- Attach semantic tags (route, status_code, tenant, request_type).

## Error Semantics

- Mark spans as error for exceptions.
- Add exception type/message as tags/events (redacted where needed).
- Correlate with ProblemDetails `traceId` returned to clients.

## Performance Insights

- Track p50/p95/p99 at route and handler granularity.
- Flag slow spans using thresholds aligned with SLOs.
