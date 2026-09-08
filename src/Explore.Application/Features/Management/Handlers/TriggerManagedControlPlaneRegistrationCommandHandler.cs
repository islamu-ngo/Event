using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Management;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Application.Features.Management.Handlers.Commands;

public sealed class TriggerManagedControlPlaneRegistrationCommandHandler(
    IOptions<ManagedControlPlaneOptions> options,
    IManagedControlPlaneRegistrationRepository registrationRepository,
    IInstanceBootstrapStateRepository bootstrapStateRepository,
    ISecretBindingRepository secretBindingRepository,
    ISecretResolver secretResolver,
    IDeploymentModeProvider deploymentModeProvider,
    IManagedControlPlaneRegistrationClient registrationClient,
    ILogger<TriggerManagedControlPlaneRegistrationCommandHandler> logger)
    : IRequestHandler<TriggerManagedControlPlaneRegistrationCommand, TriggerManagedRegistrationResultDto>
{
    public async Task<TriggerManagedRegistrationResultDto> Handle(
        TriggerManagedControlPlaneRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return Failure("Disabled", "managed_mode_disabled");
        }

        var registration = await registrationRepository.GetCurrentAsync(cancellationToken);
        if (registration?.Status == ManagedControlPlaneRegistrationStatus.Registered)
        {
            return Success(registration);
        }

        if (registration?.Status == ManagedControlPlaneRegistrationStatus.Revoked)
        {
            return Failure("Revoked", "registration_revoked", registration.Id);
        }

        var secrets = await ResolveSecretsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(secrets.ControlPlaneToEventSecret))
        {
            return Failure("Pending", "registration_secret_unavailable", registration.Id);
        }

        registration ??= await CreatePendingRegistrationAsync(settings, secrets, cancellationToken);

        var callbackRequest = new CompleteManagedInstanceRegistrationRequestDto(
            registration.Id,
            registration.ManagedInstanceId,
            registration.EventInstanceId,
            settings.RegistrationToken,
            registration.RequestHash,
            registration.ManagementApiVersion,
            registration.EventVersion,
            registration.DeploymentMode,
            new ManagedCredentialDto(
                registration.EventToControlPlaneKeyId,
                secrets.EventToControlPlaneSecret,
                [ManagedControlPlaneContract.EventToControlPlaneScope],
                registration.EventToControlPlaneCredentialExpiresAt),
            new ManagedCredentialDto(
                registration.ControlPlaneToEventKeyId,
                secrets.ControlPlaneToEventSecret,
                [
                    ManagedControlPlaneContract.ControlPlaneReadScope,
                    ManagedControlPlaneContract.ControlPlaneWriteScope
                ],
                registration.ControlPlaneToEventCredentialExpiresAt));

        try
        {
            var response = await registrationClient.CompleteRegistrationAsync(
                settings.ControlPlaneUrl!,
                callbackRequest,
                cancellationToken);
            if (response.ManagedInstanceId != registration.ManagedInstanceId
                || response.RegistrationAttemptId != registration.Id
                || !string.Equals(response.RegistrationState, "Registered", StringComparison.Ordinal))
            {
                await RecordFailureAsync(registration, "invalid_registration_ack", cancellationToken);
                return Failure("Pending", "invalid_registration_ack", registration.Id);
            }

            await MarkRegisteredAsync(registration, cancellationToken);
            return Success(registration);
        }
        catch (HttpRequestException)
        {
            logger.LogWarning(
                "Managed Control Plane registration attempt {RegistrationAttemptId} could not reach the configured endpoint.",
                registration.Id);
            await RecordFailureAsync(registration, "control_plane_unavailable", cancellationToken);
            return Failure("Pending", "control_plane_unavailable", registration.Id);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RecordFailureAsync(registration, "control_plane_timeout", cancellationToken);
            return Failure("Pending", "control_plane_timeout", registration.Id);
        }
    }

    private async Task<ManagedControlPlaneRegistration> CreatePendingRegistrationAsync(
        ManagedControlPlaneOptions settings,
        RegistrationSecrets secrets,
        CancellationToken cancellationToken)
    {
        var bootstrap = await bootstrapStateRepository.GetCurrent(cancellationToken);
        if (bootstrap is not { Status: InstanceBootstrapStatus.Completed })
        {
            throw new InvalidOperationException(
                "Instance onboarding must be complete before managed registration can start.");
        }

        var now = DateTime.UtcNow;
        var expiresAt = now.Add(settings.CredentialLifetime);
        var mode = await deploymentModeProvider.GetCurrentModeAsync(cancellationToken);
        var eventToControlPlaneKeyId = ApiKeyHashing.CreateKeyId();
        var controlPlaneToEventKeyId = ApiKeyHashing.CreateKeyId();
        var registrationId = Guid.CreateVersion7();
        var eventToControlPlaneSecretHash = ApiKeyHashing.ComputeHash(secrets.EventToControlPlaneSecret);
        var controlPlaneToEventSecretHash = ApiKeyHashing.ComputeHash(secrets.ControlPlaneToEventSecret!);
        var requestHash = ComputeRequestHash(
            registrationId,
            settings.ManagedInstanceId,
            bootstrap.Id,
            mode,
            eventToControlPlaneKeyId,
            eventToControlPlaneSecretHash,
            controlPlaneToEventKeyId,
            controlPlaneToEventSecretHash,
            expiresAt,
            expiresAt);
        SecretBinding binding = await secretBindingRepository.GetByKeyAndScopeAsync(
            ManagedControlPlaneContract.CredentialSecretSettingKey,
            SecretScope.Instance,
            null,
            cancellationToken)
            ?? throw new InvalidOperationException("Managed registration credential binding is missing.");

        var registration = new ManagedControlPlaneRegistration
        {
            Id = registrationId,
            ManagedInstanceId = settings.ManagedInstanceId,
            EventInstanceId = bootstrap.Id,
            ControlPlaneEndpoint = settings.ControlPlaneUrl!.GetLeftPart(UriPartial.Authority),
            ManagementApiVersion = ManagedControlPlaneContract.ManagementApiVersion,
            EventVersion = ManagementVersionResolver.EventVersion,
            DeploymentMode = mode,
            RequestHash = requestHash,
            EventToControlPlaneKeyId = eventToControlPlaneKeyId,
            EventToControlPlaneSecretHash = eventToControlPlaneSecretHash,
            ControlPlaneToEventKeyId = controlPlaneToEventKeyId,
            ControlPlaneToEventSecretHash = controlPlaneToEventSecretHash,
            EventToControlPlaneCredentialExpiresAt = expiresAt,
            ControlPlaneToEventCredentialExpiresAt = expiresAt,
            Status = ManagedControlPlaneRegistrationStatus.Pending,
            CreatedAt = now
        };

        registration.CredentialSecretBindingId = binding.Id;
        await registrationRepository.Create(registration);

        return registration;
    }

    private async Task<RegistrationSecrets> ResolveSecretsAsync(CancellationToken cancellationToken)
    {
        var resolved = await secretResolver.ResolveAsync(
            ManagedControlPlaneContract.CredentialSecretSettingKey,
            null,
            cancellationToken);
        return !resolved.IsResolved
            ? throw new InvalidOperationException("Managed registration credentials could not be resolved.")
            : JsonSerializer.Deserialize<RegistrationSecrets>(resolved.Value)
                ?? throw new InvalidOperationException("Managed registration credentials are malformed.");
    }

    private async Task MarkRegisteredAsync(
        ManagedControlPlaneRegistration registration,
        CancellationToken cancellationToken)
    {
        registration.MarkRegistered(DateTime.UtcNow);
        await registrationRepository.Update(registration);
    }

    private async Task RecordFailureAsync(
        ManagedControlPlaneRegistration registration,
        string failureCode,
        CancellationToken cancellationToken)
    {
        registration.RecordAttempt(DateTime.UtcNow, failureCode);
        await registrationRepository.Update(registration);
    }

    private static string ComputeRequestHash(
        Guid registrationId,
        Guid managedInstanceId,
        Guid eventInstanceId,
        DeploymentMode deploymentMode,
        string eventToControlPlaneKeyId,
        string eventToControlPlaneSecretHash,
        string controlPlaneToEventKeyId,
        string controlPlaneToEventSecretHash,
        DateTime eventToControlPlaneExpiresAt,
        DateTime controlPlaneToEventExpiresAt)
    {
        var canonical = string.Join('\n',
            registrationId.ToString("D"),
            managedInstanceId.ToString("D"),
            eventInstanceId.ToString("D"),
            ManagedControlPlaneContract.ManagementApiVersion,
            ManagementVersionResolver.EventVersion,
            deploymentMode.ToString(),
            eventToControlPlaneKeyId,
            eventToControlPlaneSecretHash,
            controlPlaneToEventKeyId,
            controlPlaneToEventSecretHash,
            eventToControlPlaneExpiresAt.ToString("O"),
            controlPlaneToEventExpiresAt.ToString("O"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static TriggerManagedRegistrationResultDto Success(ManagedControlPlaneRegistration registration) =>
        new(true, registration.Status.ToString(), null, registration.Id);

    private static TriggerManagedRegistrationResultDto Failure(
        string state,
        string failureCode,
        Guid? registrationAttemptId = null) =>
        new(false, state, failureCode, registrationAttemptId);

    private sealed record RegistrationSecrets(
        string EventToControlPlaneSecret,
        string? ControlPlaneToEventSecret);
}
