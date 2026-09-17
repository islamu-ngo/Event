ABOUTME: HTTP-to-MediatR endpoint rules for this codebase.
ABOUTME: Covers status mapping, DTO boundaries, and auth placement.

# API Endpoint Design for CQRS

Use this guide when wiring HTTP endpoints to MediatR commands/queries.

## Core Rules

- Keep controllers transport-only: parse HTTP input, send request to MediatR, map result to HTTP status code.
- Keep handlers application-only: business logic, validation, orchestration, repository usage.
- Never return domain entities from API endpoints. Return DTOs or response envelopes.

## Route and Method Conventions

- Collection read: `GET /api/{entities}` -> query request -> paginated DTO result.
- Item read: `GET /api/{entities}/{id}` -> query request -> item DTO or `404`.
- Create: `POST /api/{entities}` -> command -> `BaseCommandResponse<Guid>` and `201 Created`.
- Update: `PUT /api/{entities}/{id}` -> command -> `BaseCommandResponse<Guid>` and `200 OK`.
- Delete: `DELETE /api/{entities}/{id}` -> command -> `bool` or `BaseCommandResponse<Guid>` and `204`/`404` (match feature pattern).

Use route constraints for clarity (`{id:guid}`) where applicable.

## HTTP Status Mapping

- Successful list/detail query: `200 OK`
- Successful create: `201 Created` with `Location` header
- Successful update: `200 OK`
- Successful delete: `204 NoContent`
- Validation failures: `400 BadRequest`
- Unauthenticated: `401 Unauthorized`
- Unauthorized: `403 Forbidden`
- Missing resource: `404 NotFound`

**Every failure body is RFC 7807 ProblemDetails.** Never return the command response itself on failure — a
caller would have to know which endpoint it hit before it could parse an error. Success returns the envelope;
failure returns a problem.

## Controller Pattern

A command whose failure codes map to a single status uses the generic mapper:

```csharp
[HttpPost]
[Authorize]
public async Task<ActionResult<BaseCommandResponse<Guid>>> Create(
    [FromBody] CreateEntityDto dto,
    CancellationToken cancellationToken)
{
    var response = await mediator.Send(new CreateEntityCommand { EntityDto = dto }, cancellationToken);

    if (!response.Success)
        return this.MapCommandResponse(response);   // ProblemDetails, status from FailureCode

    return CreatedAtRoute(RouteNames.GetEntityById, new { id = response.Id }, response);
}
```

A command with a richer failure vocabulary declares a `CommandFailurePolicy` instead of an `if`/`switch` chain,
so the mapping is a named, reusable, declaration-ordered value rather than per-endpoint branching:

```csharp
private static readonly ApiNotFoundProblemDescriptor PromotionNotFoundProblem = new(
    "Promotion not found",
    "Promotion was not found.");

private static readonly CommandFailurePolicy PromotionManagementFailures = CommandFailurePolicy
    .ValidatedBy(PromotionValidationProblem)
    .NotFound(PromotionNotFoundProblem, PromotionManagementNotFound);
```

Rules are evaluated in declaration order, and a policy composes: a stricter variant is built from a base policy
rather than copied (`GuestStartFailures = OrderLifecycleFailures.AuthenticationRequired(...)`).

See `docs/internal/API.md` § "Handler-Generated Failures" for which of the two to reach for. Do not hand-roll a private
failure-to-status mapper; `CommandResponseResultMapperTests` and focused HTTP
ProblemDetails scenarios prove typed failure-code behavior without inspecting
controller source.

## Handler Pattern Boundary

- Handler receives typed request and `CancellationToken`.
- Handler manually instantiates validator (if required by project pattern).
- Handler maps DTO -> Entity, saves through repository, maps result to response envelope.
- Handler does not set HTTP status codes.

## Pagination Pattern

- Query contracts should include at minimum `PageNumber` and `PageSize`.
- Return `PaginatedResult<TDto>` from query handlers.
- Controller adds any representation concerns (for example HATEOAS wrapping).

## Security Placement

- Endpoint gate in controller with `[AllowAnonymous]`/`[Authorize]`/`[Authorize(Roles = "...")]`.
- Resource ownership and business permission checks in handlers or authorization behaviors.
