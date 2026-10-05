ABOUTME: Structured logging rules for Loki in this project.
ABOUTME: Focuses on fields, labels, and sensitive data.

# Loki Structured Logging

Use structured logs so queries remain reliable and cheap.

## Required Log Structure

- Timestamp
- Severity
- Message template (not interpolated free text)
- Exception (if present)
- Context fields: `traceId`, `correlationId`, `requestPath`, `requestMethod`, `userId`, `tenantId`

## Labels vs Fields

- Labels should be low-cardinality (`app`, `env`, `service`).
- High-cardinality data belongs in log fields, not labels.

## API Logging Rules

- Log auth failures without raw token contents.
- Log validation failures at warning level with summarized field names.
- Log unexpected exceptions at error level with exception object.
- Keep personally sensitive data out of logs unless explicitly required and protected.

## Native Operation Pipeline Integration

- Log start/end for command and query handling.
- Include elapsed time and request type.
- Emit warning when elapsed time crosses threshold.
