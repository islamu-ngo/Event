using System.Net;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Services.Registration.Providers.Formbricks;
using Explore.Infrastructure.Services.Registration.Providers.SubmissionSinks;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed class AnonymousRetentionProviderHandoffTests
{
    private static readonly DateTime Deadline = new(2027, 1, 8, 14, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Test]
    [Arguments("sheets")]
    [Arguments("webhook")]
    public async Task RegisteredFactoriesHonorClockAfterExternalSecretResolution(string provider)
    {
        DateTime deadline = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        var clock = new Clock(deadline.AddTicks(-1));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SecretResolutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveTenantBindingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.SetResult(); return release.Task; });
        using var transport = new Transport();
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.ConfigureInfrastructureServices(new ConfigurationBuilder().Build());
        registrations.AddSingleton<TimeProvider>(clock);
        registrations.AddSingleton(secrets);
        string clientName = provider == "sheets"
            ? GoogleSheetsRegistrationProviderSubmissionSink.HttpClientName
            : WebhookRegistrationProviderSubmissionSink.HttpClientName;
        registrations.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => transport);
        using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = services.CreateScope();
        IRegistrationProviderSubmissionSink sink = provider == "sheets"
            ? scope.ServiceProvider.GetRequiredService<GoogleSheetsRegistrationProviderSubmissionSink>()
            : scope.ServiceProvider.GetRequiredService<WebhookRegistrationProviderSubmissionSink>();
        var request = Request(provider, deadline);

        Task send = sink.AcceptAsync(request, CancellationToken.None);
        await entered.Task.WaitAsync(Timeout);
        clock.Now = deadline;
        release.SetResult(Secret(request.TenantId));
        var failure = await FailureAsync(send);

        await Assert.That(failure.FailureCode).IsEqualTo("registration_data_retention_expired");
        await Assert.That(failure.FailureKind).IsEqualTo(RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff);
        await Assert.That(transport.Calls).IsEqualTo(0);
    }

    [Test]
    [Arguments("sheets")]
    [Arguments("webhook")]
    [Arguments("formbricks")]
    [Arguments("formbricks-write")]
    public async Task ExpiryDuringExternalSecretResolutionNeverHandsOff(string provider)
    {
        var clock = new Clock(Deadline.AddTicks(-1));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<SecretResolutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveTenantBindingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.SetResult(); return release.Task; });
        using var transport = new Transport();
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        var request = Request(provider, Deadline);
        var sink = Sink(provider, services, transport, secrets);

        Task send = SendAsync(provider, sink, request);
        await entered.Task.WaitAsync(Timeout);
        clock.Now = Deadline;
        release.SetResult(Secret(request.TenantId));
        var failure = await FailureAsync(send);

        await Assert.That(failure.FailureCode).IsEqualTo("registration_data_retention_expired");
        await Assert.That(failure.FailureKind).IsEqualTo(RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff);
        await Assert.That(transport.Calls).IsEqualTo(0);
    }

    [Test]
    [Arguments("sheets", false)]
    [Arguments("webhook", false)]
    [Arguments("formbricks", false)]
    [Arguments("sheets", true)]
    [Arguments("webhook", true)]
    [Arguments("formbricks", true)]
    public async Task LawfulHandoffIsNotRetractedWhenTransportCompletesAfterExpiry(string provider, bool nonanonymous)
    {
        var clock = new Clock(Deadline.AddTicks(-1));
        using var transport = new Transport(block: true);
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveTenantBindingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Secret(call.ArgAt<Guid>(0)));
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        var request = Request(provider, nonanonymous ? null : Deadline);
        var sink = Sink(provider, services, transport, secrets);

        Task send = SendAsync(provider, sink, request);
        await transport.Entered.Task.WaitAsync(Timeout);
        clock.Now = Deadline.AddDays(1);
        transport.Release.SetResult(HttpStatusCode.OK);
        await send.WaitAsync(Timeout);

        await Assert.That(transport.Calls).IsEqualTo(1);
        await Assert.That(transport.Body).Contains("included-name");
    }

    [Test]
    [Arguments("sheets")]
    [Arguments("webhook")]
    [Arguments("formbricks")]
    public async Task FailureAfterLawfulHandoffStaysAmbiguousAcrossExpiry(string provider)
    {
        var clock = new Clock(Deadline.AddTicks(-1));
        using var transport = new Transport(block: true);
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveTenantBindingAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Secret(call.ArgAt<Guid>(0)));
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        Task send = SendAsync(provider, Sink(provider, services, transport, secrets), Request(provider, Deadline));
        await transport.Entered.Task.WaitAsync(Timeout);
        clock.Now = Deadline;
        transport.Release.SetResult(HttpStatusCode.InternalServerError);
        var failure = await FailureAsync(send);

        await Assert.That(failure.FailureKind).IsEqualTo(RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff);
        await Assert.That(failure.FailureCode).IsEqualTo("provider_write_outcome_unknown");
        await Assert.That(transport.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task CsvAtExpiryDoesNotWriteStorageOrMetadata()
    {
        var clock = new Clock(Deadline);
        var storage = Substitute.For<IFileStorageProvider>();
        storage.WriteAsync(Arg.Any<FileStorageWriteInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Written(call.ArgAt<FileStorageWriteInput>(0)!));
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        resolver.GetRequired(StorageProviders.Local).Returns(storage);
        var metadata = Substitute.For<IStorageObjectRepository>();
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        var sink = ActivatorUtilities.CreateInstance<CsvRegistrationProviderSubmissionSink>(services, resolver, metadata);

        var failure = await FailureAsync(sink.AcceptAsync(Request("csv", Deadline), CancellationToken.None));

        await Assert.That(failure.FailureCode).IsEqualTo("registration_data_retention_expired");
        await Assert.That(failure.FailureKind).IsEqualTo(RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff);
        await storage.DidNotReceive().WriteAsync(Arg.Any<FileStorageWriteInput>(), Arg.Any<CancellationToken>());
        await metadata.DidNotReceive().Create(Arg.Any<StorageObject>());
    }

    [Test]
    public async Task CsvStorageCompletionAfterExpiryPersistsExactBoundedMetadata()
    {
        var clock = new Clock(Deadline.AddTicks(-1));
        var entered = new TaskCompletionSource<FileStorageWriteInput>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<FileStorageWriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = Substitute.For<IFileStorageProvider>();
        storage.WriteAsync(Arg.Any<FileStorageWriteInput>(), Arg.Any<CancellationToken>())
            .Returns(call => { entered.SetResult(call.ArgAt<FileStorageWriteInput>(0)!); return release.Task; });
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        resolver.GetRequired(StorageProviders.Local).Returns(storage);
        var metadata = Substitute.For<IStorageObjectRepository>();
        StorageObject? artifact = null;
        metadata.Create(Arg.Do<StorageObject>(value => artifact = value)).Returns(call => call.ArgAt<StorageObject>(0)!);
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).BuildServiceProvider();
        var sink = ActivatorUtilities.CreateInstance<CsvRegistrationProviderSubmissionSink>(services, resolver, metadata);
        var request = Request("csv", Deadline);

        Task send = sink.AcceptAsync(request, CancellationToken.None);
        FileStorageWriteInput input = await entered.Task.WaitAsync(Timeout);
        using var reader = new StreamReader(input.Content, leaveOpen: true);
        string csv = await reader.ReadToEndAsync();
        clock.Now = Deadline.AddMinutes(1);
        release.SetResult(Written(input));
        await send.WaitAsync(Timeout);

        await Assert.That(csv).Contains("included-name");
        await Assert.That(artifact).IsNotNull();
        await Assert.That(artifact!.OwningResourceId).IsEqualTo(request.RegistrationSubmissionId);
        await Assert.That(artifact.RegistrationContentRetentionUntilUtc).IsEqualTo(Deadline);
    }

    private static Task SendAsync(string provider, IRegistrationProviderSubmissionSink sink, RegistrationProviderSubmissionSinkRequest request) =>
        provider == "formbricks-write"
            ? ((IRegistrationProviderSubmissionWriter)sink).WriteSubmissionAsync(new(request.TenantId, request.Binding,
                request.Connection, request.Tuple, request.AttemptId, request.Answers)
            { DisclosureUntilUtc = request.DisclosureUntilUtc }, CancellationToken.None)
            : sink.AcceptAsync(request, CancellationToken.None);

    private static IRegistrationProviderSubmissionSink Sink(string provider, IServiceProvider services, Transport transport, ISecretResolver secrets)
    {
        var client = new HttpClient(transport);
        if (provider == "sheets")
            return ActivatorUtilities.CreateInstance<GoogleSheetsRegistrationProviderSubmissionSink>(services, client, secrets);
        if (provider.StartsWith("formbricks", StringComparison.Ordinal))
            return ActivatorUtilities.CreateInstance<FormbricksRegistrationProviderAdapter>(services, client, secrets);
        var options = Substitute.For<IOptionsMonitor<WebhookOptions>>();
        options.CurrentValue.Returns(new WebhookOptions());
        return ActivatorUtilities.CreateInstance<WebhookRegistrationProviderSubmissionSink>(services, client, secrets,
            new WebhookEndpointSafetyPolicy(options), options);
    }

    internal static RegistrationProviderSubmissionSinkRequest Request(string provider, DateTime? deadline)
    {
        var tuple = provider switch
        {
            "sheets" => GoogleSheetsRegistrationProviderSubmissionSink.SupportedTuple,
            "webhook" => WebhookRegistrationProviderSubmissionSink.SupportedTuple,
            "csv" => CsvRegistrationProviderSubmissionSink.SupportedTuple,
            _ => new RegistrationProviderTuple("FORMBRICKS", "CLOUD", "v1", "ISLAMU_EVENT_FORMBRICKS_V1", "2026-08-10")
        };
        Guid tenantId = Guid.CreateVersion7();
        var connection = RegistrationProviderConnection.Create(tenantId, "retention",
            RegistrationProviderKindEnum.ExternalApi, RegistrationProviderDeploymentKindEnum.HostedSaas,
            tuple.ProviderCode, tuple.ProviderDeploymentCode, tuple.ApiVersion, tuple.AdapterPolicyVersion, tuple.ConformanceEvidenceRevision,
            provider == "sheets" ? "https://sheets.googleapis.com/v4" : "https://8.8.8.8/api/v1",
            "https://8.8.8.8/registration", provider == "csv" ? StorageProviders.Local : "workspace",
            Guid.CreateVersion7(), Guid.CreateVersion7(), Deadline.AddDays(-1));
        var binding = RegistrationProviderBinding.Create(tenantId, connection.Id, Guid.CreateVersion7(), Guid.CreateVersion7(),
            RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.MirrorOnly,
            RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.SelectedFields, null, Deadline.AddDays(-1));
        binding.SetDraftProvisionedSurvey("survey", null);
        return new(tenantId, binding, connection, tuple, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new Dictionary<string, string> { ["name"] = "included-name" }, null)
        { DisclosureUntilUtc = deadline };
    }

    private static SecretResolutionResult Secret(Guid tenantId) => SecretResolutionResult.Resolved(
        new ResolvedSecret("provider", "external-secret", SecretSourceType.EnvironmentVariable, SecretScope.Tenant, tenantId, new DateTimeOffset(Deadline)));

    private static FileStorageWriteResult Written(FileStorageWriteInput input) =>
        new(StorageProviders.Local, input.ObjectKey!, input.ExpectedSizeBytes!.Value, input.ContentType, "sha256:test");

    private static async Task<RegistrationProviderSubmissionDeliveryException> FailureAsync(Task action)
    {
        RegistrationProviderSubmissionDeliveryException? failure = null;
        try { await action.WaitAsync(Timeout); }
        catch (RegistrationProviderSubmissionDeliveryException exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
        return failure!;
    }

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class Transport(bool block = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpStatusCode> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Entered.SetResult();
            var status = block ? await Release.Task.WaitAsync(Timeout, cancellationToken) : HttpStatusCode.OK;
            return new(status) { Content = new StringContent("{\"data\":{\"id\":\"accepted\"}}") };
        }
    }
}
