using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Registration;
using Explore.Infrastructure.Services.Registration.Providers.MicrosoftForms;

namespace Explore.Infrastructure.Tests.Registration.MicrosoftForms;

public sealed class MicrosoftFormsRegistrationProviderDescriptorTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid WebhookBindingId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");
    private static readonly string CallbackKey = Guid.NewGuid().ToString("N");

    [Test]
    public async Task Registry_ResolvesOnlyExactPinnedTupleAndHonestCapabilities()
    {
        MicrosoftFormsRegistrationProviderDescriptor descriptor = Descriptor();
        RegistrationProviderRegistry registry = new([descriptor]);

        await Assert.That(registry.TryResolve(new("MICROSOFT_FORMS", "MICROSOFT_365", "POWER_AUTOMATE_V1", "ISLAMU_EVENT_MICROSOFT_FORMS_V1", "2026-08-11"))).IsSameReferenceAs(descriptor);
        await Assert.That(registry.TryResolve(new("MICROSOFT_FORMS", "MICROSOFT_365", "v2", "ISLAMU_EVENT_MICROSOFT_FORMS_V1", "2026-08-11"))).IsNull();
        await Assert.That(descriptor.ProvenCapabilities.CallbackVerification).IsTrue();
        await Assert.That(descriptor.ProvenCapabilities.SchemaRead).IsFalse();
        await Assert.That(descriptor.ProvenCapabilities.SubscriptionManagement).IsFalse();
        await Assert.That(descriptor.ProvenCapabilities.SubmissionRead).IsFalse();
    }

    [Test]
    public async Task Presentation_PrefillsRequiredCorrelationQuestionWithRawCapabilityToken()
    {
        MicrosoftFormsRegistrationProviderDescriptor descriptor = Descriptor();
        RegistrationProviderBinding binding = Binding();
        Guid attemptId = Guid.CreateVersion7();

        RegistrationProviderPresentationResult result = await descriptor.GetPresentationAsync(
            new(TenantId, binding, Connection(), descriptor.Tuple, attemptId, "attempt-token"), CancellationToken.None);

        await Assert.That(result.RedirectAvailable).IsTrue();
        await Assert.That(result.EmbedAvailable).IsTrue();
        await Assert.That(result.RedirectUri!.Query).Contains("id=form-123");
        await Assert.That(Uri.UnescapeDataString(result.RedirectUri.Query)).Contains($"question-attempt={attemptId:D}|attempt-token");
    }

    [Test]
    public async Task VerifyCallback_AcceptsValidEnvelopeAndRejectsBadKeyOrStaleTimestamp()
    {
        MicrosoftFormsRegistrationProviderDescriptor descriptor = Descriptor();
        RegistrationProviderBinding binding = Binding();
        byte[] validBody = Envelope(binding.Id, UtcNow);
        Dictionary<string, string> headers = new() { [MicrosoftFormsRegistrationProviderDescriptor.CallbackKeyHeader] = CallbackKey };

        RegistrationProviderCallbackVerificationResult valid = await descriptor.VerifyCallbackAsync(
            new(TenantId, binding, Connection(), descriptor.Tuple, validBody, headers), CancellationToken.None);
        RegistrationProviderCallbackVerificationResult badKey = await descriptor.VerifyCallbackAsync(
            new(TenantId, binding, Connection(), descriptor.Tuple, validBody,
                new Dictionary<string, string> { [MicrosoftFormsRegistrationProviderDescriptor.CallbackKeyHeader] = Guid.NewGuid().ToString("N") }), CancellationToken.None);
        RegistrationProviderCallbackVerificationResult stale = await descriptor.VerifyCallbackAsync(
            new(TenantId, binding, Connection(), descriptor.Tuple, Envelope(binding.Id, UtcNow.AddMinutes(-6)), headers), CancellationToken.None);
        RegistrationProviderCallbackVerificationResult malformed = await descriptor.VerifyCallbackAsync(
            new(TenantId, binding, Connection(), descriptor.Tuple, "{"u8.ToArray(), headers), CancellationToken.None);

        await Assert.That(valid.IsVerified).IsTrue();
        await Assert.That(valid.ProviderSubmissionId).IsEqualTo("42");
        await Assert.That(badKey.FailureCode).IsEqualTo("microsoft_forms_callback_key_invalid");
        await Assert.That(stale.FailureCode).IsEqualTo("microsoft_forms_callback_envelope_invalid");
        await Assert.That(malformed.FailureCode).IsEqualTo("microsoft_forms_callback_envelope_invalid");
    }

    private static MicrosoftFormsRegistrationProviderDescriptor Descriptor() =>
        new(new FakeSecretResolver(), new FixedTimeProvider(UtcNow));

    private static RegistrationProviderConnection Connection() => RegistrationProviderConnection.Create(
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000201"), TenantId, "Microsoft Forms",
        RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
        "MICROSOFT_FORMS", "MICROSOFT_365", "POWER_AUTOMATE_V1", "ISLAMU_EVENT_MICROSOFT_FORMS_V1", "2026-08-11",
        "https://forms.office.com", "https://forms.office.com/Pages/ResponsePage.aspx", "microsoft-365",
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000102"), WebhookBindingId, UtcNow);

    private static RegistrationProviderBinding Binding()
    {
        RegistrationProviderBinding binding = RegistrationProviderBinding.Create(
            TenantId, Connection().Id, Guid.CreateVersion7(), Guid.CreateVersion7(), RegistrationProviderPresentationModeEnum.Embed,
            RegistrationProviderCollectionModeEnum.ProviderHosted, RegistrationProviderCompletionModeEnum.Callback,
            RegistrationProviderTrustLevelEnum.CompletionOnly, WebhookBindingId, UtcNow);
        binding.SetDraftProvisionedSurvey("form-123", null);
        binding.SetDraftProvisionedSubscription(MicrosoftFormsRegistrationProviderDescriptor.ContractVersion, WebhookBindingId);
        binding.AddFieldMapping(RegistrationProviderFieldMapping.Create(binding,
            MicrosoftFormsRegistrationProviderDescriptor.CorrelationPlatformFieldKey, "question-attempt", true));
        return binding;
    }

    private static byte[] Envelope(Guid bindingId, DateTime timestamp) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        providerCode = MicrosoftFormsRegistrationProviderDescriptor.ProviderCode,
        bindingId,
        formId = "form-123",
        responseId = "42",
        attemptId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000301"),
        attemptToken = "attempt-token",
        timestamp,
        mappedValues = new { },
        contractVersion = MicrosoftFormsRegistrationProviderDescriptor.ContractVersion,
        idempotencyKey = "form-123:42"
    });

    private sealed class FakeSecretResolver : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(bindingId == WebhookBindingId
                ? SecretResolutionResult.Resolved(new ResolvedSecret("test", CallbackKey, SecretSourceType.EnvironmentVariable, SecretScope.Tenant, tenantId, UtcNow))
                : SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
