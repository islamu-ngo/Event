
using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Models;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Application.Features.InstanceOnboarding.Services;

public sealed class LocalAdministratorBootstrapOperation(
    IInstanceBootstrapStateRepository bootstrapRepository,
    IConfiguredAdministratorBootstrapProvider configuredProvider,
    ILocalCredentialAdministration credentials,
    ISecretResolver secretResolver,
    InstanceOnboardingCompletionOperation completion,
    ISetupSecretProvider setupSecretProvider,
    IDeploymentModeProvider deploymentModeProvider,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IAuthenticationProviderDispatcher providerDispatcher)
{
    public async Task<BaseCommandResponse<Guid>> CompleteInteractiveAsync(
        Guid operationId, string username, string temporaryPassword, string? email, string? firstName, string? lastName,
        CompleteInstanceOnboardingRequest settings, ClaimsPrincipal setupPrincipal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setupPrincipal);
        ArgumentNullException.ThrowIfNull(settings);
        if (!setupPrincipal.Identities.Any(identity => identity.IsAuthenticated
                && identity.AuthenticationType == ApiAuthenticationSchemeNames.SetupSecret)
            || !await setupSecretProvider.IsSetupModeActiveAsync(cancellationToken))
            return Failure("local_bootstrap_authority_invalid");
        if (await providerDispatcher.GetActivePrimaryProviderAsync(cancellationToken) != AuthenticationProviderKind.Local)
            return Failure("local_bootstrap_provider_inactive");
        if (operationId.Version != 7 || operationId.Variant is < 8 or > 11)
            return Failure("local_bootstrap_operation_invalid");
        if (string.IsNullOrWhiteSpace(temporaryPassword)
            || temporaryPassword.Length is < LocalIdentityOptions.MinimumPasswordLength or > LocalIdentityOptions.MaximumPasswordLength)
            return Failure("local_bootstrap_credential_invalid");

        settings.DeploymentMode = await deploymentModeProvider.GetConfiguredOnboardingModeAsync(cancellationToken);
        var validation = await new CompleteInstanceOnboardingRequestValidator().ValidateAsync(settings, cancellationToken);
        if (!validation.IsValid || (settings.DeploymentMode == DeploymentMode.SingleTenant && settings.DirectoryOperatorIdentity is null))
            return Failure("local_bootstrap_settings_invalid");
        LocalCredentialCreateRequest intent;
        try
        {
            intent = new LocalCredentialCreateRequest(operationId, operationId, email,
                firstName ?? "Administrator", lastName ?? string.Empty, username);
        }
        catch (ArgumentException) { return Failure("local_bootstrap_profile_invalid"); }
        if (!await credentials.ValidateBootstrapCreationAsync(intent, temporaryPassword, cancellationToken))
            return Failure("local_bootstrap_credential_invalid");

        bool admitted = await unitOfWork.ExecuteBootstrapConvergenceAsync(async token =>
        {
            InstanceBootstrapState? current = await bootstrapRepository.GetCurrentForUpdate(token);
            if (current is null)
            {
                await bootstrapRepository.Create(InstanceBootstrapState.CreateInteractivePending(
                    operationId, settings.DeploymentMode, timeProvider.GetUtcNow().UtcDateTime));
                return true;
            }
            return current.Id == operationId && current.Mode == InstanceBootstrapMode.Interactive
                && current.Status == InstanceBootstrapStatus.Pending && current.DeploymentMode == settings.DeploymentMode;
        }, cancellationToken);
        if (!admitted) return Failure("local_bootstrap_authority_invalid");
        LocalCredentialCreateResult created = await credentials.CreateBootstrapPendingAsync(
            intent, localSubjectId: null, temporaryPassword, cancellationToken);
        if (created.Outcome is not (LocalCredentialCreateOutcome.Created or LocalCredentialCreateOutcome.Replayed))
            return Failure("local_bootstrap_credential_conflict");
        LocalCredentialProvisioningSnapshot? snapshot = await credentials.ReadProvisioningAsync(operationId, cancellationToken);
        if (snapshot is null || !Matches(snapshot, intent)) return Failure("local_bootstrap_credential_conflict");
        return await CompleteAndActivateAsync(null, settings, snapshot, cancellationToken);
    }

    public async Task<BaseCommandResponse<Guid>> CompleteConfiguredAsync(
        ProviderAccountKey accountKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        if (await providerDispatcher.GetActivePrimaryProviderAsync(cancellationToken) != AuthenticationProviderKind.Local)
            return Failure("local_bootstrap_provider_inactive");
        InstanceBootstrapState? current = await bootstrapRepository.GetCurrent(cancellationToken);
        if (current?.Status == InstanceBootstrapStatus.Completed)
        {
            await ReconcileCompletedAsync(cancellationToken);
            return BaseCommandResponse.Success(current.Id);
        }
        if (current is null || current.Status != InstanceBootstrapStatus.Pending
            || current.Mode != InstanceBootstrapMode.ConfiguredAdministrator
            || current.ProviderKind != AuthenticationProviderKind.Local || accountKey.ProviderKind != AuthenticationProviderKind.Local)
            return Failure("local_bootstrap_authority_invalid");
        ConfiguredAdministratorBootstrapBinding? binding = await configuredProvider.GetVerifiedBindingAsync(accountKey, cancellationToken);
        if (binding is null || binding.Generation != current.Generation || binding.AccountKey != accountKey
            || !Guid.TryParseExact(accountKey.Value, "D", out Guid subject))
            return Failure("local_bootstrap_authority_invalid");
        var intent = new LocalCredentialCreateRequest(current.Id, subject, binding.AdministratorProfile.Email,
            binding.AdministratorProfile.FirstName ?? "Administrator", binding.AdministratorProfile.LastName ?? string.Empty,
            username: accountKey.Value);
        LocalCredentialProvisioningSnapshot? snapshot = await credentials.ReadProvisioningAsync(current.Id, cancellationToken);
        if (snapshot is null)
        {
            SecretResolutionResult secret = await secretResolver.ResolveAsync(
                SecretDefinitionRegistry.Keys.Authentication.LocalBootstrapPassword, tenantId: null, cancellationToken);
            if (!secret.IsResolved || string.IsNullOrWhiteSpace(secret.Value)) return Failure("local_bootstrap_secret_unavailable");
            LocalCredentialCreateResult created = await credentials.CreateBootstrapPendingAsync(intent, subject, secret.Value, cancellationToken);
            if (created.Outcome is not (LocalCredentialCreateOutcome.Created or LocalCredentialCreateOutcome.Replayed))
                return Failure("local_bootstrap_credential_conflict");
            snapshot = await credentials.ReadProvisioningAsync(current.Id, cancellationToken);
        }
        if (snapshot is null || snapshot.Receipt.LocalSubjectId != subject || !Matches(snapshot, intent))
            return Failure("local_bootstrap_credential_conflict");
        return await CompleteAndActivateAsync(binding, binding.Settings, snapshot, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> CompleteAndActivateAsync(ConfiguredAdministratorBootstrapBinding? binding,
        CompleteInstanceOnboardingRequest settings, LocalCredentialProvisioningSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (await providerDispatcher.GetActivePrimaryProviderAsync(cancellationToken) != AuthenticationProviderKind.Local)
            return Failure("local_bootstrap_provider_inactive");
        BaseCommandResponse<Guid> result = await completion.CompleteLocalBootstrapAsync(binding, settings, snapshot, cancellationToken);
        if (result.IsSuccess) await ReconcileCompletedAsync(cancellationToken);
        return result;
    }

    public async Task ReconcileCompletedAsync(CancellationToken cancellationToken = default)
    {
        if (await providerDispatcher.GetActivePrimaryProviderAsync(cancellationToken) != AuthenticationProviderKind.Local)
            return;
        InstanceBootstrapState? current = await bootstrapRepository.GetCurrent(cancellationToken);
        if (current?.Status != InstanceBootstrapStatus.Completed
            || current.ProviderKind is not (null or AuthenticationProviderKind.Local)) return;
        LocalCredentialProvisioningSnapshot? snapshot = await credentials.ReadProvisioningAsync(current.Id, cancellationToken);
        if (snapshot is null && current.Mode == InstanceBootstrapMode.Interactive) return;
        Guid? initiator = current.Mode == InstanceBootstrapMode.Interactive ? current.Id : current.CompletedByUserId;
        if (snapshot is null || snapshot.Receipt.LocalSubjectId != current.CompletedByUserId
            || snapshot.Receipt.InitiatingApplicationUserId != initiator || snapshot.Receipt.Kind != LocalCredentialOperationKind.Create)
            throw new InvalidOperationException("local_bootstrap_receipt_invalid");
        if (snapshot.Receipt.Stage != LocalCredentialOperationStage.ProvisioningPending) return;
        LocalCredentialActivationOutcome activation = await credentials.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(current.Id, snapshot.OperationConcurrencyStamp), cancellationToken);
        if (activation is not (LocalCredentialActivationOutcome.Activated or LocalCredentialActivationOutcome.AlreadyActivated))
            throw new InvalidOperationException("local_bootstrap_activation_incomplete");
    }

    private static bool Matches(LocalCredentialProvisioningSnapshot snapshot, LocalCredentialCreateRequest intent) =>
        snapshot.Receipt.OperationId == intent.OperationId && snapshot.Receipt.InitiatingApplicationUserId == intent.InitiatingApplicationUserId
        && snapshot.Receipt.Kind == LocalCredentialOperationKind.Create && snapshot.EmailVerified
        && snapshot.Email == intent.Email && string.Equals(snapshot.Username, intent.Username, StringComparison.OrdinalIgnoreCase)
        && snapshot.FirstName == intent.FirstName && snapshot.LastName == intent.LastName;

    private static BaseCommandResponse<Guid> Failure(string code) =>
        BaseCommandResponse.Failure<Guid>(code, "Local administrator bootstrap did not complete.");
}
