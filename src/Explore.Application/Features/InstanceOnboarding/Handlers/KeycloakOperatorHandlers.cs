using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain.Keycloak;
using FluentValidation;
using ApplicationExceptions = Explore.Application.Exceptions;

namespace Explore.Application.Features.InstanceOnboarding.Handlers;

internal static class KeycloakOperatorHandlerSupport
{
    public static void Validate(KeycloakOperationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var validator = new KeycloakOperationInputValidator();
        var result = validator.Validate(input);
        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }

    public static void RequireOwnership(
        KeycloakOperation operation,
        KeycloakOperatorAuthority authority)
    {
        if (operation.Target.InstanceId != authority.InstanceId
            || operation.SetupGeneration != authority.SetupGeneration
            || !string.Equals(
                operation.Actor,
                authority.Actor,
                StringComparison.Ordinal))
        {
            throw new ApplicationExceptions.AuthorizationException(
                "The current authority does not own this receipt.");
        }
    }

    public static KeycloakOperationDto Receipt(
        KeycloakOperation operation) =>
        new(
            operation.Id,
            operation.State.ToString(),
            operation.Digest,
            operation.CreatedAtUtc,
            operation.ExpiresAtUtc,
            operation.SettledAtUtc,
            operation.IsCancellationRequested,
            operation.ChangeSet.Steps
                .Select(step => step.StepId)
                .ToArray(),
            operation.StepOutcomes.Items
                .Select(outcome => new KeycloakStepOutcomeDto(
                    outcome.StepId,
                    outcome.Kind.ToString(),
                    outcome.ProviderResourceId,
                    outcome.ObservedFingerprint))
                .ToArray());

    public static KeycloakTarget Target(
        KeycloakOperatorAuthority authority,
        KeycloakConnectionResolution binding) =>
        new(
            authority.InstanceId,
            binding.Authority!.AbsoluteUri,
            binding.Realm!,
            binding.BlazorClientId!);

    public static KeycloakOperationApplyContext ApplyContext(
        KeycloakOperation operation,
        KeycloakOperationInput input,
        KeycloakOperatorAuthority authority,
        KeycloakConnectionResolution binding,
        DateTimeOffset nowUtc) =>
        new(
            operation.Id,
            authority.Actor,
            authority.SetupGeneration,
            Target(authority, binding),
            operation.Digest,
            binding.ApiClientId,
            input.AdministratorUsername!,
            input.AdministratorPassword!,
            nowUtc);
}

public sealed class GetKeycloakConnectionQueryHandler(
    IKeycloakOperatorAuthority authority,
    KeycloakConnectionResolver resolver)
    : IQueryHandler<GetKeycloakConnectionQuery, KeycloakConnectionDto>
{
    public async Task<KeycloakConnectionDto> QueryAsync(
        GetKeycloakConnectionQuery request,
        CancellationToken cancellationToken)
    {
        _ = await authority.RequireAsync(cancellationToken);
        KeycloakConnectionResolution result =
            await resolver.ResolveRuntimeAsync(cancellationToken);
        return new KeycloakConnectionDto(
            result.Status.ToString().ToLowerInvariant(),
            result.Authority?.GetLeftPart(UriPartial.Authority),
            result.Realm,
            result.BlazorClientId,
            result.Status == KeycloakConnectionStatus.Resolved);
    }
}

public sealed class InspectKeycloakOperationQueryHandler(
    IKeycloakOperatorAuthority authority,
    KeycloakConnectionResolver resolver,
    IKeycloakAdminClient client,
    KeycloakOperationService operationService)
    : IQueryHandler<InspectKeycloakOperationQuery, KeycloakInspectionDto>
{
    public async Task<KeycloakInspectionDto> QueryAsync(
        InspectKeycloakOperationQuery request,
        CancellationToken cancellationToken)
    {
        _ = await authority.RequireAsync(cancellationToken);
        KeycloakOperatorHandlerSupport.Validate(request.Input);
        KeycloakConnectionResolution binding =
            await resolver.ResolveRuntimeAsync(cancellationToken);
        if (binding.Status != KeycloakConnectionStatus.Resolved)
        {
            return new KeycloakInspectionDto(
                binding.Status.ToString().ToLowerInvariant(),
                "keycloak_binding_unavailable",
                null,
                null,
                null,
                []);
        }

        KeycloakAdminInspectionResult result = await client.InspectAsync(
            new KeycloakAdminInspectionRequest(
                binding.Authority!,
                binding.Realm!,
                binding.BlazorClientId!,
                binding.ApiClientId,
                request.Input.AdministratorUsername,
                request.Input.AdministratorPassword),
            cancellationToken);
        IReadOnlyList<string> findings = result.Snapshot is null
            ? []
            : operationService.Plan(result.Snapshot)?.Steps
                .Select(step => step.StepId)
                .ToArray()
              ?? [];
        return new KeycloakInspectionDto(
            result.Status.ToString().ToLowerInvariant(),
            result.ReasonCode,
            binding.Authority!.GetLeftPart(UriPartial.Authority),
            binding.Realm,
            binding.BlazorClientId,
            findings);
    }
}

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
        KeycloakOperatorHandlerSupport.Validate(request.Input);
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

public sealed class GetKeycloakOperationQueryHandler(
    IKeycloakOperationRepository repository,
    IKeycloakOperatorAuthority authority)
    : IQueryHandler<GetKeycloakOperationQuery, KeycloakOperationDto?>
{
    public async Task<KeycloakOperationDto?> QueryAsync(
        GetKeycloakOperationQuery request,
        CancellationToken cancellationToken)
    {
        KeycloakOperatorAuthority current =
            await authority.RequireAsync(cancellationToken);
        KeycloakOperation? operation =
            await repository.GetAsync(
                request.OperationId,
                cancellationToken);
        if (operation is null)
        {
            return null;
        }

        KeycloakOperatorHandlerSupport.RequireOwnership(operation, current);
        return KeycloakOperatorHandlerSupport.Receipt(operation);
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
