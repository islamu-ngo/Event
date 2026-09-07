namespace Event.Application.UnitTests.Services;

using System.Diagnostics.Metrics;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using NSubstitute;

[NotInParallel("TenantDirectoryOperatorReadinessMeter")]
public sealed class TenantDirectoryOperatorReadinessEvaluatorTests
{
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly ITypedSettingsDocumentResolver _resolver =
        Substitute.For<ITypedSettingsDocumentResolver>();

    [Test]
    public async Task EvaluateAsync_MissingDocumentReturnsPayloadFreeMissingFailure()
    {
        using var signal = new ReadinessMeasurementSignal();
        _resolver.ResolveTenantDocumentAsync<TenantDirectoryOperatorIdentitySettings>(
                Arg.Any<SettingsResolutionContext>(),
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                Arg.Any<CancellationToken>())
            .Returns((ResolvedSettingsDocument<TenantDirectoryOperatorIdentitySettings>?)null);
        var evaluator = new TenantDirectoryOperatorReadinessEvaluator(_resolver);

        TenantDirectoryOperatorReadinessAssessment result = await evaluator.EvaluateAsync(
            _tenantId,
            TenantDirectoryOperatorIdentityCapability.Activation,
            CancellationToken.None);

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo("tenant_directory_operator_identity_missing");
        await Assert.That(result.Identity).IsNull();
        await AssertMeasurement(signal, "activation", "missing",
            "tenant_directory_operator_identity_missing");
    }

    [Test]
    public async Task EvaluateAsync_MalformedDocumentNeverEmitsHostileExceptionValues()
    {
        const string hostilePayload = "HOSTILE-name@example.test/registration/secret?return=https://evil.test";
        using var signal = new ReadinessMeasurementSignal();
        _resolver.ResolveTenantDocumentAsync<TenantDirectoryOperatorIdentitySettings>(
                Arg.Any<SettingsResolutionContext>(),
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                Arg.Any<CancellationToken>())
            .Returns<Task<ResolvedSettingsDocument<TenantDirectoryOperatorIdentitySettings>?>>(
                _ => throw new JsonException(hostilePayload));

        TenantDirectoryOperatorReadinessAssessment result = await new TenantDirectoryOperatorReadinessEvaluator(_resolver)
            .EvaluateAsync(_tenantId, TenantDirectoryOperatorIdentityCapability.PublicDisclosure, CancellationToken.None);

        await Assert.That(result).IsEqualTo(TenantDirectoryOperatorReadinessAssessment.IntegrityError);
        ReadinessMeasurement measurement = await AssertMeasurement(signal, "public_disclosure", "malformed",
            "tenant_directory_operator_identity_integrity_error");
        await Assert.That(string.Join('|', measurement.Fields.Select(field => $"{field.Key}={field.Value}")))
            .DoesNotContain(hostilePayload);
    }

    [Test]
    public async Task EvaluateAsync_IncompleteDocumentReturnsFieldCodesAndRevision()
    {
        using var signal = new ReadinessMeasurementSignal();
        Guid revision = Guid.CreateVersion7();
        Configure(new TenantDirectoryOperatorIdentitySettings
        {
            PublicName = "Community Events"
        }, revision);
        var evaluator = new TenantDirectoryOperatorReadinessEvaluator(_resolver);

        TenantDirectoryOperatorReadinessAssessment result = await evaluator.EvaluateAsync(
            _tenantId,
            TenantDirectoryOperatorIdentityCapability.Activation,
            CancellationToken.None);

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo("tenant_directory_operator_identity_incomplete");
        await Assert.That(result.DocumentRevision).IsEqualTo(revision);
        await Assert.That(result.ReasonCodes)
            .Contains(TenantDirectoryOperatorIdentityReasonCodes.MissingLegalName);
        await Assert.That(result.ReasonCodes)
            .All(reason => !reason.Contains("Community Events", StringComparison.Ordinal));
        await AssertMeasurement(signal, "activation", "incomplete", string.Join(',', result.ReasonCodes));
    }

    [Test]
    public async Task EvaluateAsync_ForeignSourceIsIntegrityFailure()
    {
        using var signal = new ReadinessMeasurementSignal();
        Configure(Complete(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var evaluator = new TenantDirectoryOperatorReadinessEvaluator(_resolver);

        TenantDirectoryOperatorReadinessAssessment result = await evaluator.EvaluateAsync(
            _tenantId,
            TenantDirectoryOperatorIdentityCapability.PublicDisclosure,
            CancellationToken.None);

        await Assert.That(result.IsReady).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo("tenant_directory_operator_identity_integrity_error");
        await Assert.That(result.Identity).IsNull();
        await AssertMeasurement(signal, "public_disclosure", "cross_tenant",
            "tenant_directory_operator_identity_integrity_error");
    }

    [Test]
    public async Task EvaluateAsync_ValidTenantDocumentReturnsNormalizedIdentity()
    {
        using var signal = new ReadinessMeasurementSignal();
        Guid revision = Guid.CreateVersion7();
        Configure(Complete() with
        {
            JurisdictionCountryCode = " be ",
            PublicContactEmail = " CONTACT@EXAMPLE.TEST "
        }, revision);
        var evaluator = new TenantDirectoryOperatorReadinessEvaluator(_resolver);

        TenantDirectoryOperatorReadinessAssessment result = await evaluator.EvaluateAsync(
            _tenantId,
            TenantDirectoryOperatorIdentityCapability.PaidCommerce,
            CancellationToken.None);

        await Assert.That(result.IsReady).IsTrue();
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(result.DocumentRevision).IsEqualTo(revision);
        await Assert.That(result.Identity).IsNotNull();
        await Assert.That(result.Identity!.JurisdictionCountryCode).IsEqualTo("BE");
        await Assert.That(result.Identity.PublicContactEmail).IsEqualTo("contact@example.test");
        await AssertMeasurement(signal, "paid_commerce", "ready", "none");
    }

    private async Task<ReadinessMeasurement> AssertMeasurement(
        ReadinessMeasurementSignal signal,
        string capability,
        string category,
        string reasonCodes)
    {
        ReadinessMeasurement measurement = await signal.Measurement.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(signal.Count).IsEqualTo(1);
        await Assert.That(measurement.Fields.Keys).IsEquivalentTo(
            ["capability", "result_category", "reason_codes"]);
        await Assert.That(measurement.Fields.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Fields["capability"]).IsEqualTo(capability);
        await Assert.That(measurement.Fields["result_category"]).IsEqualTo(category);
        await Assert.That(measurement.Fields["reason_codes"]).IsEqualTo(reasonCodes);
        return measurement;
    }

    private void Configure(
        TenantDirectoryOperatorIdentitySettings payload,
        Guid revision,
        Guid? sourceTenantId = null)
    {
        _resolver.ResolveTenantDocumentAsync<TenantDirectoryOperatorIdentitySettings>(
                Arg.Any<SettingsResolutionContext>(),
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                Arg.Any<CancellationToken>())
            .Returns(new ResolvedSettingsDocument<TenantDirectoryOperatorIdentitySettings>
            {
                DocumentId = Guid.CreateVersion7(),
                DocumentKey = SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                SchemaVersion = TenantDirectoryOperatorIdentityDocumentDefaults.SchemaVersion,
                DefaultsVersion = TenantDirectoryOperatorIdentityDocumentDefaults.DefaultsVersion,
                Payload = payload,
                Source = SettingsDocumentSource.Tenant,
                SourceScopeId = sourceTenantId ?? _tenantId,
                ConcurrencyStamp = revision
            });
    }

    private static TenantDirectoryOperatorIdentitySettings Complete() => new()
    {
        PublicName = "Community Events",
        LegalName = "Community Events ASBL",
        OperatorKindCode = TenantDirectoryOperatorKinds.RegisteredOrganization,
        JurisdictionCountryCode = "BE",
        PublicContactEmail = "contact@example.test",
        LegalNoticeUrl = "https://example.test/legal",
        TermsUrl = "https://example.test/terms",
        PrivacyUrl = "https://example.test/privacy"
    };

    private sealed class ReadinessMeasurementSignal : IDisposable
    {
        private readonly TaskCompletionSource<ReadinessMeasurement> _measurement =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MeterListener _listener = new();
        private int _count;

        public ReadinessMeasurementSignal()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == TenantDirectoryOperatorReadinessTelemetry.MeterName
                    && instrument.Name == TenantDirectoryOperatorReadinessTelemetry.InstrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                Interlocked.Increment(ref _count);
                _measurement.TrySetResult(new ReadinessMeasurement(
                    tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
            });
            _listener.Start();
        }

        public Task<ReadinessMeasurement> Measurement => _measurement.Task;
        public int Count => Volatile.Read(ref _count);
        public void Dispose() => _listener.Dispose();
    }

    private sealed record ReadinessMeasurement(IReadOnlyDictionary<string, object?> Fields);
}
