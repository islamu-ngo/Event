namespace Explore.Application.Services;

using System.Diagnostics.Metrics;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Application.Telemetry;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;

public sealed class TenantDirectoryOperatorReadinessEvaluator(
    ITypedSettingsDocumentResolver settingsDocumentResolver,
    TenantDirectoryOperatorReadinessTelemetry telemetry)
    : ITenantDirectoryOperatorReadinessEvaluator
{
    public TenantDirectoryOperatorReadinessEvaluator(
        ITypedSettingsDocumentResolver settingsDocumentResolver)
        : this(settingsDocumentResolver, new TenantDirectoryOperatorReadinessTelemetry())
    {
    }

    public async Task<TenantDirectoryOperatorReadinessAssessment> EvaluateAsync(
        Guid tenantId,
        TenantDirectoryOperatorIdentityCapability capability,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant identity is required.", nameof(tenantId));
        }

        if (!Enum.IsDefined(capability))
        {
            throw new ArgumentOutOfRangeException(nameof(capability), capability, null);
        }

        TenantDirectoryOperatorReadinessAssessment Complete(
            TenantDirectoryOperatorReadinessAssessment assessment,
            TenantDirectoryOperatorReadinessResultCategory category)
        {
            telemetry.Record(capability, category, assessment.ReasonCodes);
            return assessment;
        }

        ResolvedSettingsDocument<TenantDirectoryOperatorIdentitySettings>? resolved;
        try
        {
            resolved = await settingsDocumentResolver
                .ResolveTenantDocumentAsync<TenantDirectoryOperatorIdentitySettings>(
                    new SettingsResolutionContext(
                        tenantId,
                        RequestedDocuments:
                        [
                            SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity
                        ]),
                    SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                    cancellationToken);
        }
        catch (Exception exception)
            when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return Complete(TenantDirectoryOperatorReadinessAssessment.IntegrityError,
                TenantDirectoryOperatorReadinessResultCategory.Malformed);
        }

        if (resolved is null)
        {
            return Complete(TenantDirectoryOperatorReadinessAssessment.Missing,
                TenantDirectoryOperatorReadinessResultCategory.Missing);
        }

        if (resolved.Source == SettingsDocumentSource.Tenant
            && resolved.SourceScopeId != tenantId)
        {
            return Complete(TenantDirectoryOperatorReadinessAssessment.IntegrityError,
                TenantDirectoryOperatorReadinessResultCategory.CrossTenant);
        }

        if (resolved.Source != SettingsDocumentSource.Tenant
            || resolved.DocumentId is not Guid documentId
            || documentId == Guid.Empty
            || !string.Equals(
                resolved.DocumentKey,
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                StringComparison.Ordinal)
            || resolved.SchemaVersion
                != TenantDirectoryOperatorIdentityDocumentDefaults.SchemaVersion)
        {
            return Complete(TenantDirectoryOperatorReadinessAssessment.IntegrityError,
                TenantDirectoryOperatorReadinessResultCategory.Malformed);
        }

        try
        {
            TenantDirectoryOperatorIdentityReadiness readiness =
                TenantDirectoryOperatorIdentity.Evaluate(resolved.Payload, capability);
            TenantDirectoryOperatorReadinessAssessment assessment = readiness.IsReady
                ? TenantDirectoryOperatorReadinessAssessment.Ready(
                    readiness.Identity!, resolved.ConcurrencyStamp, documentId)
                : TenantDirectoryOperatorReadinessAssessment.Incomplete(
                    readiness.ReasonCodes, resolved.ConcurrencyStamp);
            return Complete(assessment, readiness.IsReady
                ? TenantDirectoryOperatorReadinessResultCategory.Ready
                : TenantDirectoryOperatorReadinessResultCategory.Incomplete);
        }
        catch (Exception exception)
            when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return Complete(TenantDirectoryOperatorReadinessAssessment.IntegrityError,
                TenantDirectoryOperatorReadinessResultCategory.Malformed);
        }
    }
}

public sealed class TenantDirectoryOperatorReadinessTelemetry
{
    public const string MeterName = "Explore.Business";
    public const string InstrumentName = "explore.tenant_directory_operator.readiness";

    private readonly Counter<long> _evaluations;

    public TenantDirectoryOperatorReadinessTelemetry()
        : this(new Meter(MeterName))
    {
    }

    public TenantDirectoryOperatorReadinessTelemetry(IMeterFactory meterFactory)
        : this(meterFactory.Create(MeterName))
    {
    }

    private TenantDirectoryOperatorReadinessTelemetry(Meter meter)
    {
        _evaluations = meter.CreateCounter<long>(
            InstrumentName,
            unit: "{evaluation}",
            description: "Tenant directory-operator readiness evaluations by bounded result.");
    }

    internal void Record(
        TenantDirectoryOperatorIdentityCapability capability,
        TenantDirectoryOperatorReadinessResultCategory category,
        IEnumerable<string> reasonCodes)
    {
        string boundedReasons = string.Join(',', reasonCodes
            .Where(TenantDirectoryOperatorReadinessReasonCodePolicy.IsClosedCode)
            .Distinct(StringComparer.Ordinal));
        _evaluations.Add(
            1,
            new KeyValuePair<string, object?>("capability", CapabilityCode(capability)),
            new KeyValuePair<string, object?>("result_category", CategoryCode(category)),
            new KeyValuePair<string, object?>("reason_codes", boundedReasons.Length == 0 ? "none" : boundedReasons));
    }

    private static string CapabilityCode(TenantDirectoryOperatorIdentityCapability capability) => capability switch
    {
        TenantDirectoryOperatorIdentityCapability.Activation => "activation",
        TenantDirectoryOperatorIdentityCapability.PublicDisclosure => "public_disclosure",
        TenantDirectoryOperatorIdentityCapability.PaidCommerce => "paid_commerce",
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null)
    };

    private static string CategoryCode(TenantDirectoryOperatorReadinessResultCategory category) => category switch
    {
        TenantDirectoryOperatorReadinessResultCategory.Missing => "missing",
        TenantDirectoryOperatorReadinessResultCategory.Malformed => "malformed",
        TenantDirectoryOperatorReadinessResultCategory.CrossTenant => "cross_tenant",
        TenantDirectoryOperatorReadinessResultCategory.Incomplete => "incomplete",
        TenantDirectoryOperatorReadinessResultCategory.Ready => "ready",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };
}

internal enum TenantDirectoryOperatorReadinessResultCategory
{
    Missing,
    Malformed,
    CrossTenant,
    Incomplete,
    Ready
}
