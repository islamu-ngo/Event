ABOUTME: Minimal native operation logging behavior rules.
ABOUTME: Centralizes request logging and error capture.

# Native Operation Logging Behavior (Lean)

## Rules
- Log start/end for each request with elapsed time.
- Log exceptions with request type and traceId.
- Keep request/response payloads out of logs unless sanitized.

## Related
- [loki-logging.md](loki-logging.md)
