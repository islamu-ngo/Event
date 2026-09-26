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
    public static void Validate(KeycloakInspectionCredentialsDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var validator = new KeycloakInspectionCredentialsValidator();
        var result = validator.Validate(input);
        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }

    public static void Validate(KeycloakOperationCredentialsDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var validator = new KeycloakOperationCredentialsValidator();
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
        KeycloakOperationCredentialsDto input,
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
            binding.PublicOrigin,
            binding.CredentialBindingRevision,
            binding.ClientSecret,
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
            result.Status == KeycloakConnectionStatus.Resolved,
            CredentialOwnership: "deployment-managed",
            CredentialStatus: result.Status.ToString().ToLowerInvariant(),
            RequiresCoordinatedRestart: true,
            OperatorGuidance:
                "rotate_in_deployment_authority_restart_and_reinspect");
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
            : result.Snapshot.RealmExists
                ? operationService.Plan(result.Snapshot)?.Steps
                    .Select(step => step.StepId)
                    .ToArray()
                  ?? []
                : [KeycloakOperationService.CreateRealmStepId];
        return new KeycloakInspectionDto(
            result.Status.ToString().ToLowerInvariant(),
            result.ReasonCode,
            binding.Authority!.GetLeftPart(UriPartial.Authority),
            binding.Realm,
            binding.BlazorClientId,
            findings);
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
