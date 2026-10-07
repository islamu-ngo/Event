using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.InstanceOnboarding.Services;
using FluentValidation;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class InspectAuthenticationCredentialCleanupQueryHandler(
    IUserContext userContext,
    IAdminContext adminContext,
    IInstanceBootstrapStateRepository bootstrapRepository,
    IAuthenticationCredentialCutoverCoordinator coordinator,
    TimeProvider timeProvider)
    : IQueryHandler<InspectAuthenticationCredentialCleanupQuery, AuthenticationCredentialCleanupInventoryDto>
{
    public async Task<AuthenticationCredentialCleanupInventoryDto> QueryAsync(
        InspectAuthenticationCredentialCleanupQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var (instanceId, _) = await AuthenticationCredentialCutoverHandlerSupport
                .RequireCurrentInstanceAsync(userContext, adminContext, bootstrapRepository, cancellationToken);
            if (request.OperationId == Guid.Empty)
                throw new ValidationException("Authentication credential cutover operation ID is required.");
            var inventory = await coordinator.InspectCleanupAsync(
                instanceId, request.OperationId, timeProvider.GetUtcNow(), cancellationToken);
            return new()
            {
                OperationId = inventory.OperationId, DeploymentRevision = inventory.DeploymentRevision,
                HeadStamp = inventory.HeadStamp, OperationStamp = inventory.OperationStamp,
                Settings = inventory.Settings.Select(row =>
                    new AuthenticationCredentialCleanupTargetDto(row.Id, row.SettingKey)).ToArray(),
                Bindings = inventory.Bindings.Select(row =>
                    new AuthenticationCredentialCleanupTargetDto(row.Id, row.SettingKey)).ToArray()
            };
        }
        catch (InvalidOperationException exception) when (
            AuthenticationCredentialCutoverHandlerSupport.IsGuardConflict(exception))
        {
            throw new Explore.Application.Exceptions.ConcurrencyConflictException(
                exception.Message, "Authentication credential cleanup inventory is not currently available.");
        }
    }
}

public sealed class GetCurrentAuthenticationCredentialCutoverQueryHandler(
    IUserContext userContext,
    IAdminContext adminContext,
    IInstanceBootstrapStateRepository bootstrapRepository,
    IAuthenticationCredentialCutoverRepository repository)
    : IQueryHandler<GetCurrentAuthenticationCredentialCutoverQuery, AuthenticationCredentialCutoverDto?>
{
    public async Task<AuthenticationCredentialCutoverDto?> QueryAsync(
        GetCurrentAuthenticationCredentialCutoverQuery request,
        CancellationToken cancellationToken)
    {
        var (instanceId, _) = await AuthenticationCredentialCutoverHandlerSupport
            .RequireCurrentInstanceAsync(userContext, adminContext, bootstrapRepository, cancellationToken);
        var head = await repository.GetHeadAsync(instanceId, cancellationToken);
        if (head is null)
            return null;
        var operation = await repository.GetAsync(instanceId, head.CurrentOperationId, cancellationToken);
        return operation is null ? null : AuthenticationCredentialCutoverReceiptMapper.Map(operation, head);
    }
}

public sealed class GetAuthenticationCredentialCutoverQueryHandler(
    IUserContext userContext,
    IAdminContext adminContext,
    IInstanceBootstrapStateRepository bootstrapRepository,
    IAuthenticationCredentialCutoverRepository repository)
    : IQueryHandler<GetAuthenticationCredentialCutoverQuery, AuthenticationCredentialCutoverDto?>
{
    public async Task<AuthenticationCredentialCutoverDto?> QueryAsync(
        GetAuthenticationCredentialCutoverQuery request,
        CancellationToken cancellationToken)
    {
        var (instanceId, _) = await AuthenticationCredentialCutoverHandlerSupport
            .RequireCurrentInstanceAsync(userContext, adminContext, bootstrapRepository, cancellationToken);
        if (request.OperationId == Guid.Empty)
            throw new ValidationException("Authentication credential cutover operation ID is required.");
        var operation = await repository.GetAsync(instanceId, request.OperationId, cancellationToken);
        if (operation is null)
            return null;
        var head = await repository.GetHeadAsync(instanceId, cancellationToken);
        return AuthenticationCredentialCutoverReceiptMapper.Map(operation, head);
    }
}
