using System.Text.Json;
using System.Text.RegularExpressions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationProviderManagedPublishPreflightService(
    IRegistrationFormAuthoringRepository formRepository,
    IRegistrationProviderRepository providerRepository,
    IRegistrationProviderRegistry providerRegistry,
    IRegistrationProviderCallbackUriBuilder callbackUriBuilder,
    ISecretBindingRepository secretBindingRepository,
    IRegistrationProviderSubscriptionStateRepository subscriptionStateRepository,
    TimeProvider timeProvider) : IRegistrationProviderManagedPublishPreflight
{
    private static readonly Regex GoogleEntryFieldKeyPattern = new("^entry\\.\\d+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<RegistrationProviderManagedPublishPreflightResult> RunAsync(
        Guid tenantId,
        Guid eventId,
        RegistrationProviderBinding binding,
        CancellationToken cancellationToken)
    {
        if (binding.Connection is not { } connection)
        {
            return Failure("registration_provider_connection_missing");
        }

        RegistrationProviderTuple tuple = new(
            connection.ProviderCode,
            connection.ProviderDeploymentCode,
            connection.ApiVersion,
            connection.AdapterPolicyVersion,
            connection.ConformanceEvidenceRevision);
        IRegistrationProviderDescriptor? descriptor = providerRegistry.TryResolve(tuple);
        RegistrationFormVersion? version = await formRepository.GetVersionAsync(
            eventId,
            binding.RegistrationFormId,
            binding.RegistrationFormVersionId,
            cancellationToken);
        if (version is null)
        {
            return Failure("registration_provider_form_version_not_found");
        }

        if (descriptor is IRegistrationProviderDelegatedAutomation delegatedAutomation && !UsesManagedAutomationPath(descriptor))
        {
            return await ValidateDelegatedAutomationAsync(
                tenantId, binding, version, delegatedAutomation, cancellationToken);
        }

        if (descriptor is not IRegistrationProviderFormCompatibilityChecker compatibilityChecker ||
            descriptor is not IRegistrationProviderSchemaReader schemaReader)
        {
            return Failure("registration_provider_managed_publish_unsupported");
        }

        RegistrationProviderFormCompatibilityResult compatibility = compatibilityChecker.CheckCompatibility(version);
        if (!compatibility.IsCompatible)
        {
            return RegistrationProviderManagedPublishPreflightResult.Failure(
                compatibility.Issues[0].Code,
                [.. compatibility.Issues.Select(issue => issue.Code)]);
        }

        if (descriptor is IRegistrationProviderDelegatedAutomation managedDelegatedAutomation &&
            !HasExactlyOneGoogleCorrelationMapping(binding, managedDelegatedAutomation.RequiredCorrelationPlatformFieldKey))
        {
            return Failure("registration_provider_correlation_mapping_invalid");
        }

        if (string.IsNullOrWhiteSpace(binding.ProviderSurveyId))
        {
            if (descriptor is not IRegistrationProviderFormProvisioner provisioner)
            {
                return Failure("registration_provider_managed_publish_unsupported");
            }

            RegistrationProviderFormProvisionResult provisioned;
            try
            {
                provisioned = await provisioner.ProvisionFormAsync(
                    new(tenantId, binding, connection, tuple, version),
                    cancellationToken);
                binding.SetDraftProvisionedSurvey(provisioned.ProviderFormId, provisioned.ProviderRevisionId);
                await providerRepository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Failure("registration_provider_remote_acceptance_ambiguous");
            }
        }

        if (descriptor.ProvenCapabilities.SubscriptionManagement && string.IsNullOrWhiteSpace(binding.ProviderWebhookId))
        {
            IReadOnlyList<SecretBinding> tenantSecrets = await secretBindingRepository.GetByScopeAsync(
                SecretScope.Tenant, tenantId, cancellationToken);
            if (tenantSecrets.Any(secret =>
                    string.Equals(secret.SettingKey, SecretDefinitionRegistry.Keys.RegistrationProviders.WebhookSecret, StringComparison.Ordinal) &&
                    string.Equals(secret.Qualifier, binding.Id.ToString("N"), StringComparison.Ordinal)))
            {
                return Failure("registration_provider_remote_acceptance_ambiguous");
            }

            if (descriptor is not IRegistrationProviderSubscriptionManager subscriptionManager)
            {
                return Failure("registration_provider_managed_publish_unsupported");
            }

            RegistrationProviderSubscriptionResult subscription;
            try
            {
                subscription = await subscriptionManager.EnsureSubscriptionAsync(
                    new(tenantId, binding, connection, tuple, callbackUriBuilder.Build(connection.ProviderCode, binding.Id)),
                    cancellationToken);
                if (!subscription.IsActive || string.IsNullOrWhiteSpace(subscription.ProviderSubscriptionId))
                {
                    return Failure("registration_provider_remote_acceptance_ambiguous");
                }

                if (subscription.ExternalSecretProvisioningRequired)
                {
                    return Failure("registration_provider_external_secret_provisioning_required");
                }

                binding.SetDraftProvisionedSubscription(subscription.ProviderSubscriptionId);

                if (subscription.ExpiresAtUtc is { } expiresAt &&
                    await subscriptionStateRepository.GetAsync(tenantId, binding.Id, "RESPONSES", cancellationToken) is null)
                {
                    DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                    RegistrationProviderSubscriptionState state = RegistrationProviderSubscriptionState.Create(
                        tenantId,
                        binding.Id,
                        "RESPONSES",
                        subscription.ProviderSubscriptionId,
                        DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc),
                        now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                        now);
                    state.ReceiveNotification(now);
                    await subscriptionStateRepository.AddAsync(state, cancellationToken);
                }
                await providerRepository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Failure("registration_provider_remote_acceptance_ambiguous");
            }
        }
        else if (descriptor.ProvenCapabilities.SubscriptionManagement && binding.WebhookSecretBindingId is not null &&
                 !await BindingWebhookSecretIsValidAsync(tenantId, binding, cancellationToken))
        {
            return Failure("registration_provider_webhook_missing");
        }

        RegistrationProviderSchemaReadResult remoteSchema;
        try
        {
            remoteSchema = await schemaReader.ReadSchemaAsync(new(tenantId, binding, connection, tuple), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException or FormatException)
        {
            return Failure("registration_provider_preflight_unavailable");
        }

        if (!remoteSchema.IsActive)
        {
            return Failure("registration_provider_survey_inactive");
        }

        return string.Equals(remoteSchema.Fingerprint, compatibility.Fingerprint, StringComparison.Ordinal)
            ? RegistrationProviderManagedPublishPreflightResult.Success()
            : Failure("registration_provider_fingerprint_mismatch");
    }

    private async Task<RegistrationProviderManagedPublishPreflightResult> ValidateDelegatedAutomationAsync(
        Guid tenantId,
        RegistrationProviderBinding binding,
        RegistrationFormVersion version,
        IRegistrationProviderDelegatedAutomation delegatedAutomation,
        CancellationToken cancellationToken)
    {
        if (binding.TrustLevelId != (int)RegistrationProviderTrustLevelEnum.CompletionOnly ||
            binding.CompletionModeId != (int)RegistrationProviderCompletionModeEnum.Callback ||
            string.IsNullOrWhiteSpace(binding.ProviderSurveyId) ||
            !string.Equals(binding.ProviderWebhookId, delegatedAutomation.ConnectorContractVersion, StringComparison.Ordinal))
        {
            return Failure("registration_provider_delegated_automation_configuration_invalid");
        }

        if (!await BindingWebhookSecretIsValidAsync(tenantId, binding, cancellationToken))
        {
            return Failure("registration_provider_webhook_missing");
        }

        HashSet<string> mapped = [.. binding.FieldMappings.Where(mapping => !mapping.IsDeleted).Select(mapping => mapping.PlatformFieldKey)];
        bool requiredMappingsComplete = version.Sections.Where(section => !section.IsDeleted)
            .SelectMany(section => section.Fields)
            .Where(field => !field.IsDeleted && field.IsRequired)
            .All(field => mapped.Contains($"{field.Namespace}.{field.Key}"));
        if (!requiredMappingsComplete || !mapped.Contains(delegatedAutomation.RequiredCorrelationPlatformFieldKey))
        {
            return Failure("registration_provider_required_mapping_missing");
        }

        return await providerRepository.GetLastCallbackAtAsync(tenantId, binding.Id, cancellationToken) is not null
            ? RegistrationProviderManagedPublishPreflightResult.Success()
            : Failure("registration_provider_test_callback_required");
    }

    private static RegistrationProviderManagedPublishPreflightResult Failure(string code) =>
        RegistrationProviderManagedPublishPreflightResult.Failure(code);

    private static bool UsesManagedAutomationPath(IRegistrationProviderDescriptor descriptor) =>
        descriptor.ProvenCapabilities.FormProvision || descriptor.ProvenCapabilities.SubscriptionManagement;

    private static bool HasExactlyOneGoogleCorrelationMapping(RegistrationProviderBinding binding, string platformFieldKey)
    {
        if (!string.Equals(platformFieldKey, "system.registration_attempt_token", StringComparison.Ordinal))
        {
            return true;
        }

        List<RegistrationProviderFieldMapping> mappings = [.. binding.FieldMappings.Where(mapping =>
            !mapping.IsDeleted &&
            string.Equals(mapping.PlatformFieldKey, platformFieldKey, StringComparison.Ordinal))];
        return mappings.Count == 1 && GoogleEntryFieldKeyPattern.IsMatch(mappings[0].ProviderFieldKey);
    }

    private async Task<bool> BindingWebhookSecretIsValidAsync(Guid tenantId, RegistrationProviderBinding binding, CancellationToken cancellationToken)
    {
        if (binding.WebhookSecretBindingId is not { } secretBindingId)
        {
            return false;
        }

        SecretBinding? secret = await secretBindingRepository.GetByTenantAndIdAsync(tenantId, secretBindingId, cancellationToken);
        return secret is not null &&
            string.Equals(secret.SettingKey, SecretDefinitionRegistry.Keys.RegistrationProviders.WebhookSecret, StringComparison.Ordinal) &&
            string.Equals(secret.Qualifier, binding.Id.ToString("N"), StringComparison.Ordinal);
    }
}
