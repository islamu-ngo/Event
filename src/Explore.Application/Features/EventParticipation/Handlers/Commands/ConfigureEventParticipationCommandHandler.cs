using Explore.Application.Caching;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event.Validators;
using Explore.Application.Features.EventParticipation.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventParticipation.Handlers.Commands;

public sealed class ConfigureEventParticipationCommandHandler(
    IEventParticipationConfigurationRepository configurations,
    ITenantContext tenantContext,
    HybridCache cache)
    : IRequestHandler<ConfigureEventParticipationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ConfigureEventParticipationCommand request,
        CancellationToken cancellationToken)
    {
        if (request.EventId == Guid.Empty || request.ExpectedConcurrencyStamp == Guid.Empty)
        {
            return Failure(
                request.EventId,
                "event_participation_configuration_validation_failed",
                "Event id and expected concurrency stamp are required.");
        }

        if (request.ParticipationConfiguration is null)
        {
            return Failure(
                request.EventId,
                "event_participation_configuration_validation_failed",
                "Participation configuration is required.");
        }

        var validator = new ConfigureEventParticipationDtoValidator();
        var validation = await validator.ValidateAsync(request.ParticipationConfiguration, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.EventId,
                "event_participation_configuration_validation_failed",
                "Event participation configuration failed validation.",
                validation.Errors.Select(error => $"{error.PropertyName}: {error.ErrorMessage}"));
        }

        EventParticipationConfiguration? configuration =
            await configurations.GetByEventAndTenantAsync(
                request.EventId,
                tenantContext.TenantId,
                cancellationToken);
        if (configuration is null)
        {
            return Failure(
                request.EventId,
                "event_participation_configuration_not_found",
                "Event participation configuration was not found.");
        }

        if (configuration.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
        {
            return Failure(
                request.EventId,
                "event_participation_configuration_concurrency_conflict",
                "The event participation configuration changed since it was loaded. Refresh the event and try again.");
        }

        try
        {
            configuration.Reconfigure(
                request.ParticipationConfiguration.ParticipationHandlingModeId,
                request.ParticipationConfiguration.AdvanceRegistrationObligationId,
                request.ParticipationConfiguration.IdentityAccessModeId,
                request.ParticipationConfiguration.GuestRecoveryPolicy);
        }
        catch (EventParticipationConfigurationValidationException exception)
        {
            return Failure(
                request.EventId,
                "event_participation_configuration_invalid",
                "Event participation configuration is invalid.",
                exception.Errors.Select(error => $"{error.Code}: {error.Message}"));
        }
        catch (InvalidOperationException)
        {
            return Failure(
                request.EventId,
                "event_participation_configuration_attachment_conflict",
                "Existing registration requirement attachments are incompatible with the requested participation mode.");
        }

        await configurations.UpdateAsync(configuration, cancellationToken);
        await cache.RemoveAsync($"event:detail:{request.EventId}", cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantContext.TenantId), cancellationToken);
        return BaseCommandResponse.Success(
            request.EventId,
            "Event participation configuration updated.");
    }

    private static BaseCommandResponse<Guid> Failure(
        Guid eventId,
        string failureCode,
        string message,
        IEnumerable<string>? errors = null) => BaseCommandResponse.Failure<Guid>(
            failureCode,
            message,
            errors ?? [message],
            eventId);
}
