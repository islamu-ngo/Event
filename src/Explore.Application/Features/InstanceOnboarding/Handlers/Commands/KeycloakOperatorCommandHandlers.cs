using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Keycloak;
using FluentValidation;
using ApplicationExceptions = Explore.Application.Exceptions;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public sealed class PlanKeycloakOperationCommandHandler(
    IKeycloakOperatorAuthority authority,
    KeycloakConnectionResolver resolver,
    IKeycloakAdminClient client,
    KeycloakOperationService operationService,
    TimeProvider timeProvider)
    : ICommandHandler<PlanKeycloakOperationCommand, KeycloakOperationDto>
{
    public async Task<KeycloakOperationDto> ExecuteAsync(
        PlanKeycloakOperationCommand request,
        CancellationToken cancellationToken)
    {
        KeycloakOperatorAuthority current =
            await authority.RequireAsync(cancellationToken);
        var validation = new KeycloakOperationPlanInputValidator().Validate(request.Input);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }
        KeycloakConnectionResolution binding =
            await resolver.ResolveRuntimeAsync(cancellationToken);
        if (binding.Status != KeycloakConnectionStatus.Resolved)
        {
            throw new InvalidOperationException(
                "The deployment-owned Keycloak binding is unavailable.");
        }

        KeycloakAdminInspectionResult inspection = await client.InspectAsync(
            new KeycloakAdminInspectionRequest(
                binding.Authority!,
                binding.Realm!,
                binding.BlazorClientId!,
                binding.ApiClientId,
                request.Input.AdministratorUsername,
                request.Input.AdministratorPassword),
            cancellationToken);
        if (inspection.Snapshot is null)
        {
            throw new InvalidOperationException(
                inspection.ReasonCode.Length == 0
                    ? "Keycloak inspection failed."
                    : inspection.ReasonCode);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        KeycloakOperation? operation = await operationService.CreateAsync(
            current.InstanceId,
            binding.Authority!,
            inspection.Snapshot,
            current.Actor,
            current.SetupGeneration,
            now,
            now.AddMinutes(15),
            request.Input.Intent!.Value,
            binding.PublicOrigin,
            binding.CredentialBindingRevision,
            cancellationToken);
        return operation is null
            ? new KeycloakOperationDto(
                Guid.Empty,
                "NoChanges",
                string.Empty,
                now,
                now,
                SettledAtUtc: now,
                IsCancellationRequested: false,
                Steps: [],
                Outcomes: [])
            : KeycloakOperatorHandlerSupport.Receipt(operation);
    }
}

public sealed class ApplyKeycloakOperationCommandHandler(
    IKeycloakOperationRepository repository,
    IKeycloakOperatorAuthority authority,
    KeycloakConnectionResolver resolver,
    KeycloakOperationService operationService,
    TimeProvider timeProvider)
    : ICommandHandler<ApplyKeycloakOperationCommand, KeycloakOperationDto>
{
    public async Task<KeycloakOperationDto> ExecuteAsync(
        ApplyKeycloakOperationCommand request,
        CancellationToken cancellationToken)
    {
        KeycloakOperatorHandlerSupport.Validate(request.Input);
        (KeycloakOperation operation, KeycloakOperatorAuthority current,
            KeycloakConnectionResolution binding) =
            await ResolveOwnedOperationAsync(
                request.OperationId,
                repository,
                authority,
                resolver,
                cancellationToken);
        KeycloakOperation result = await operationService.ApplyAsync(
            KeycloakOperatorHandlerSupport.ApplyContext(
                operation,
                request.Input,
                current,
                binding,
                timeProvider.GetUtcNow()),
            cancellationToken);
        return KeycloakOperatorHandlerSupport.Receipt(result);
    }

    internal static async Task<(
        KeycloakOperation Operation,
        KeycloakOperatorAuthority Authority,
        KeycloakConnectionResolution Binding)> ResolveOwnedOperationAsync(
        Guid operationId,
        IKeycloakOperationRepository repository,
        IKeycloakOperatorAuthority authority,
        KeycloakConnectionResolver resolver,
        CancellationToken cancellationToken)
    {
        KeycloakOperatorAuthority current =
            await authority.RequireAsync(cancellationToken);
        KeycloakOperation operation =
            await repository.GetAsync(operationId, cancellationToken)
            ?? throw new ApplicationExceptions.NotFoundException(
                nameof(KeycloakOperation),
                operationId);
        KeycloakOperatorHandlerSupport.RequireOwnership(operation, current);
        KeycloakConnectionResolution binding =
            await resolver.ResolveRuntimeAsync(cancellationToken);
        if (binding.Status != KeycloakConnectionStatus.Resolved)
        {
            throw new InvalidOperationException(
                "The deployment-owned Keycloak binding is unavailable.");
        }

        return (operation, current, binding);
    }
}

public sealed class ReconcileKeycloakOperationCommandHandler(
    IKeycloakOperationRepository repository,
    IKeycloakOperatorAuthority authority,
    KeycloakConnectionResolver resolver,
    KeycloakOperationService operationService,
    TimeProvider timeProvider)
    : ICommandHandler<ReconcileKeycloakOperationCommand, KeycloakOperationDto>
{
    public async Task<KeycloakOperationDto> ExecuteAsync(
        ReconcileKeycloakOperationCommand request,
        CancellationToken cancellationToken)
    {
        KeycloakOperatorHandlerSupport.Validate(request.Input);
        (KeycloakOperation operation, KeycloakOperatorAuthority current,
            KeycloakConnectionResolution binding) =
            await ApplyKeycloakOperationCommandHandler.ResolveOwnedOperationAsync(
                request.OperationId,
                repository,
                authority,
                resolver,
                cancellationToken);
        KeycloakOperation result = await operationService.ReconcileAsync(
            KeycloakOperatorHandlerSupport.ApplyContext(
                operation,
                request.Input,
                current,
                binding,
                timeProvider.GetUtcNow()),
            cancellationToken);
        return KeycloakOperatorHandlerSupport.Receipt(result);
    }
}

public sealed class CancelKeycloakOperationCommandHandler(
    IKeycloakOperationRepository repository,
    IKeycloakOperationCoordinator coordinator,
    IKeycloakOperatorAuthority authority,
    TimeProvider timeProvider)
    : ICommandHandler<CancelKeycloakOperationCommand, KeycloakOperationDto>
{
    public async Task<KeycloakOperationDto> ExecuteAsync(
        CancelKeycloakOperationCommand request,
        CancellationToken cancellationToken)
    {
        KeycloakOperatorAuthority current =
            await authority.RequireAsync(cancellationToken);
        KeycloakOperation operation =
            await repository.GetAsync(
                request.OperationId,
                cancellationToken)
            ?? throw new ApplicationExceptions.NotFoundException(
                nameof(KeycloakOperation),
                request.OperationId);
        KeycloakOperatorHandlerSupport.RequireOwnership(operation, current);
        await coordinator.RequestCancellationAsync(
            operation.Id,
            timeProvider.GetUtcNow(),
            cancellationToken);
        KeycloakOperation refreshed =
            await repository.GetAsync(
                operation.Id,
                cancellationToken)
            ?? throw new ApplicationExceptions.NotFoundException(
                nameof(KeycloakOperation),
                operation.Id);
        return KeycloakOperatorHandlerSupport.Receipt(refreshed);
    }
}
