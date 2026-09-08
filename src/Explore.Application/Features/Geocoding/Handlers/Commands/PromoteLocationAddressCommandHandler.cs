using System.Diagnostics.CodeAnalysis;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Geocoding.Requests.Commands;
using Explore.Application.Features.Geocoding.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Geocoding.Handlers.Commands;

public sealed class PromoteLocationAddressCommandHandler(
    ILocationRepository locations,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAuthorizationProvider authorizationProvider,
    TimeProvider timeProvider)
    : IRequestHandler<PromoteLocationAddressCommand, BaseCommandResponse<Guid>>
{
    private const string ValidationFailureCode = "address_promotion_validation_failed";

    public async Task<BaseCommandResponse<Guid>> Handle(
        PromoteLocationAddressCommand request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validator = new PromoteLocationAddressCommandValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                ValidationFailureCode,
                "Location address promotion failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        Guid tenantId = tenantContext.TenantId;
        if (tenantId == Guid.Empty
            || !currentUser.IsAuthenticated
            || currentUser.UserId is not { } actorId
            || actorId == Guid.Empty)
        {
            return Failure(
                FailureCodes.AuthenticationRequired,
                "Authenticated tenant context is required.");
        }

        Location? location = await locations.GetById(request.LocationId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsEligibleTarget(location, tenantId))
        {
            return Failure(
                FailureCodes.NotFound,
                "Location address promotion target was not found.");
        }

        AuthorizationDecision authorization;
        try
        {
            authorization = await authorizationProvider.AuthorizeAsync(
                new AuthorizationRequest(
                    ResourceKinds.Location,
                    location.Id.ToString(),
                    AuthorizationActions.Locations.ApproveTenantAddress,
                    Scope: new AuthorizationScope(TenantId: tenantId.ToString()),
                    Facts: new TenantScopedAuthorizationFacts(tenantId),
                    Subject: new AuthorizationSubject(actorId),
                    Tenant: new AuthorizationTenant(tenantId)),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AuthorizationException(
                ResourceKinds.Location,
                AuthorizationActions.Locations.ApproveTenantAddress);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!authorization.IsAllowed)
        {
            throw new AuthorizationException(
                ResourceKinds.Location,
                AuthorizationActions.Locations.ApproveTenantAddress);
        }

        if (location.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The location was modified by another request. Reload and retry.",
                nameof(Location));
        }

        if (location.AddressVisibility == LocationAddressVisibilityEnum.TenantApproved
            && location.HasCurrentDerivedKeys())
        {
            return Success(location.Id, "Location address is already approved for tenant reuse.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        bool changed = location.PromoteAddressToTenantApproved(
            actorId,
            timeProvider.GetUtcNow().UtcDateTime);
        cancellationToken.ThrowIfCancellationRequested();
        if (changed)
        {
            await locations.Update(location, cancellationToken);
        }
        return Success(location.Id, "Location address approved for tenant reuse.");
    }

    private static bool IsEligibleTarget([NotNullWhen(true)] Location? location, Guid tenantId) =>
        location is not null
        && location.TenantId == tenantId
        && location.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Active
        && location.LocationKindId != (int)LocationKindEnum.PrivateHome
        && location.Pii is not null;

    private static BaseCommandResponse<Guid> Success(Guid locationId, string message) =>
        BaseCommandResponse.Success(locationId, message);

    private static BaseCommandResponse<Guid> Failure(
        string code,
        string message,
        IEnumerable<string>? errors = null) => code switch
        {
            FailureCodes.AuthenticationRequired =>
                BaseCommandResponse.Authentication<Guid>(message),
            FailureCodes.NotFound =>
                BaseCommandResponse.NotFound<Guid>(message),
            _ => BaseCommandResponse.Failure<Guid>(
                code,
                message,
                errors ?? [message])
        };
}
