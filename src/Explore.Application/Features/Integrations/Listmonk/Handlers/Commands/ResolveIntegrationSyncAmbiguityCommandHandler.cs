using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Integrations.Validators;
using Explore.Application.Features.Integrations.Listmonk.Requests.Commands;
using Explore.Application.Features.Settings.Handlers;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.Integrations.Listmonk.Handlers.Commands;

public sealed class ResolveIntegrationSyncAmbiguityCommandHandler(
    IIntegrationSyncOutboxRepository repository,
    IAdminContext adminContext,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider)
    : IRequestHandler<ResolveIntegrationSyncAmbiguityCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ResolveIntegrationSyncAmbiguityCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ResolveIntegrationSyncAmbiguityDtoValidator();
        var validation = await validator.ValidateAsync(request.Resolution, cancellationToken);
        if (!validation.IsValid || request.OutboxId == Guid.Empty)
        {
            var errors = validation.Errors.Select(error => error.ErrorMessage).ToList();
            if (request.OutboxId == Guid.Empty)
            {
                errors.Add("OutboxId is required.");
            }
            return BaseCommandResponse.Validation<Guid>(
                errors,
                "Integration sync recovery validation failed.");
        }

        var authorization = await SettingCommandHelper.CheckAuthorizationAsync(
            SettingScope.Tenant,
            adminContext,
            tenantContext,
            currentUserService,
            cancellationToken);
        Guid? actorId = await SettingCommandHelper.ResolveCurrentUserIdAsync(
            adminContext,
            currentUserService,
            cancellationToken);
        if (!authorization.Authorized || actorId is null)
        {
            var message = authorization.Error ?? "Tenant administrator authorization is required.";
            return !authorization.Authorized
                ? BaseCommandResponse.Authorization<Guid>(message)
                : BaseCommandResponse.Authentication<Guid>(message);
        }

        IntegrationSyncOutbox? resolved = await repository.ResolveAmbiguousAsync(
            new IntegrationSyncRecoveryRequest(
                tenantContext.TenantId,
                request.OutboxId,
                request.Resolution.Decision,
                request.Resolution.EvidenceReference.Trim(),
                actorId.Value,
                timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);
        if (resolved is null)
        {
            return BaseCommandResponse.NotFound<Guid>(
                "The ambiguous integration sync row was not found or was already resolved.");
        }

        return BaseCommandResponse.Success(resolved.Id, "Integration sync ambiguity resolved.");
    }
}
