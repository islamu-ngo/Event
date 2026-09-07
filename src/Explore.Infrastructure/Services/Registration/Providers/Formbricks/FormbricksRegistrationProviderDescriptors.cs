using Explore.Application.Contracts.Services.Registration;

namespace Explore.Infrastructure.Services.Registration.Providers.Formbricks;

public sealed class FormbricksCloudRegistrationProviderDescriptor(FormbricksRegistrationProviderAdapter adapter) : FormbricksRegistrationProviderDescriptor(adapter)
{
    public static RegistrationProviderTuple SupportedTuple { get; } =
        new("FORMBRICKS", "CLOUD", "v1", "ISLAMU_EVENT_FORMBRICKS_V1", "2026-08-10");

    public override RegistrationProviderTuple Tuple => SupportedTuple;
}

public sealed class FormbricksSelfHostedRegistrationProviderDescriptor(FormbricksRegistrationProviderAdapter adapter) : FormbricksRegistrationProviderDescriptor(adapter)
{
    public static RegistrationProviderTuple SupportedTuple { get; } =
        new("FORMBRICKS", "SELF_HOSTED", "v1", "ISLAMU_EVENT_FORMBRICKS_V1", "2026-08-10");

    public override RegistrationProviderTuple Tuple => SupportedTuple;
}

public abstract class FormbricksRegistrationProviderDescriptor(FormbricksRegistrationProviderAdapter adapter) :
    IRegistrationProviderDescriptor,
    IRegistrationProviderPresentation,
    IRegistrationProviderSchemaReader,
    IRegistrationProviderFormProvisioner,
    IRegistrationProviderFormCompatibilityChecker,
    IRegistrationProviderSubmissionWriter,
    IRegistrationProviderSubmissionReader,
    IRegistrationProviderCallbackVerifier,
    IRegistrationProviderSubscriptionManager,
    IRegistrationProviderReconciliationProvider,
    IRegistrationProviderSubmissionSink
{
    public abstract RegistrationProviderTuple Tuple { get; }

    public RegistrationProviderCapabilitySet ProvenCapabilities => FormbricksRegistrationProviderCapabilities.All;

    public Task<RegistrationProviderPresentationResult> GetPresentationAsync(RegistrationProviderPresentationRequest request, CancellationToken cancellationToken) =>
        adapter.GetPresentationAsync(request, cancellationToken);

    public Task<RegistrationProviderSchemaReadResult> ReadSchemaAsync(RegistrationProviderSchemaReadRequest request, CancellationToken cancellationToken) =>
        adapter.ReadSchemaAsync(request, cancellationToken);

    public Task<RegistrationProviderFormProvisionResult> ProvisionFormAsync(RegistrationProviderFormProvisionRequest request, CancellationToken cancellationToken) =>
        adapter.ProvisionFormAsync(request, cancellationToken);

    public RegistrationProviderFormCompatibilityResult CheckCompatibility(Explore.Domain.RegistrationFormVersion formVersion) =>
        adapter.CheckCompatibility(formVersion);

    public Task<RegistrationProviderSubmissionWriteResult> WriteSubmissionAsync(RegistrationProviderSubmissionWriteRequest request, CancellationToken cancellationToken) =>
        adapter.WriteSubmissionAsync(request, cancellationToken);

    public Task<RegistrationProviderSubmissionReadResult> ReadSubmissionAsync(RegistrationProviderSubmissionReadRequest request, CancellationToken cancellationToken) =>
        adapter.ReadSubmissionAsync(request, cancellationToken);

    public Task<RegistrationProviderCallbackVerificationResult> VerifyCallbackAsync(RegistrationProviderCallbackVerificationRequest request, CancellationToken cancellationToken) =>
        adapter.VerifyCallbackAsync(request, cancellationToken);

    public Task<RegistrationProviderSubscriptionResult> EnsureSubscriptionAsync(RegistrationProviderSubscriptionRequest request, CancellationToken cancellationToken) =>
        adapter.EnsureSubscriptionAsync(request, cancellationToken);

    public Task<RegistrationProviderReconciliationResult> ReconcileAsync(RegistrationProviderReconciliationRequest request, CancellationToken cancellationToken) =>
        adapter.ReconcileAsync(request, cancellationToken);

    public Task<RegistrationProviderSubmissionSinkResult> AcceptAsync(RegistrationProviderSubmissionSinkRequest request, CancellationToken cancellationToken) =>
        adapter.AcceptAsync(request, cancellationToken);
}

internal static class FormbricksRegistrationProviderCapabilities
{
    public static RegistrationProviderCapabilitySet All { get; } = new(
        Redirect: true,
        Embed: true,
        Manual: true,
        SchemaRead: true,
        FormProvision: true,
        SubmissionWrite: true,
        SubmissionRead: true,
        CallbackVerification: true,
        SubscriptionManagement: true,
        Reconciliation: true,
        SubmissionSink: true,
        AutoFinalize: true);
}
