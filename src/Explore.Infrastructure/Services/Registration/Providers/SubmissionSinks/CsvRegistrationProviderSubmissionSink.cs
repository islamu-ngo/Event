using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services.Registration.Providers.SubmissionSinks;

public sealed class CsvRegistrationProviderSubmissionSink(
    IFileStorageProviderResolver storageProviderResolver,
    IStorageObjectRepository storageObjects) : IRegistrationProviderDescriptor, IRegistrationProviderSubmissionSink
{
    private const long MaxCsvBytes = 64 * 1024;

    public static RegistrationProviderTuple SupportedTuple { get; } = new(
        "EXCEL_COMPATIBLE",
        "CSV_STORAGE",
        "v1",
        "ISLAMU_EVENT_APPROVED_FIELDS_CSV_V1",
        "2026-08-12");

    public RegistrationProviderTuple Tuple => SupportedTuple;

    public RegistrationProviderCapabilitySet ProvenCapabilities { get; } = new(
        Redirect: false,
        Embed: false,
        Manual: false,
        SchemaRead: false,
        FormProvision: false,
        SubmissionWrite: false,
        SubmissionRead: false,
        CallbackVerification: false,
        SubscriptionManagement: false,
        Reconciliation: false,
        SubmissionSink: true,
        AutoFinalize: false);

    public async Task<RegistrationProviderSubmissionSinkResult> AcceptAsync(
        RegistrationProviderSubmissionSinkRequest request,
        CancellationToken cancellationToken)
    {
        byte[] csv = Encoding.UTF8.GetBytes(BuildCsv(request.Answers));
        if (csv.LongLength > MaxCsvBytes)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                "provider_submission_payload_too_large");
        }

        string provider = StorageProviders.All.Contains(request.Connection.ProviderWorkspaceId, StringComparer.Ordinal)
            ? request.Connection.ProviderWorkspaceId
            : StorageProviders.Local;
        string objectKey = $"registration-submission-sinks/{request.TenantId:N}/{request.RegistrationSubmissionId:N}.csv";
        await using var stream = new MemoryStream(csv, writable: false);
        FileStorageWriteResult written = await storageProviderResolver.GetRequired(provider).WriteAsync(
            new FileStorageWriteInput(
                request.TenantId,
                stream,
                "text/csv; charset=utf-8",
                $"registration-submission-{request.RegistrationSubmissionId:N}.csv",
                ".csv",
                csv.LongLength,
                MaxCsvBytes,
                objectKey),
            cancellationToken);

        await storageObjects.Create(new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = request.TenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!,
            Uri = written.ObjectKey,
            ObjectKey = written.ObjectKey,
            Provider = written.Provider,
            FullName = $"Registration submission {request.RegistrationSubmissionId:N}.csv",
            SafeDisplayName = $"registration-submission-{request.RegistrationSubmissionId:N}.csv",
            Extension = ".csv",
            ContentType = written.ContentType,
            Sha256Checksum = written.Sha256Checksum,
            Size = written.SizeBytes,
            Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            Purpose = StorageObjectPurposes.Document,
            LifecycleState = StorageObjectLifecycleStates.Active,
            OwningResourceKind = "registration_submission_sink",
            OwningResourceId = request.RegistrationSubmissionId,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        });

        return new RegistrationProviderSubmissionSinkResult(true, request.RegistrationSubmissionId, false);
    }

    private static string BuildCsv(IReadOnlyDictionary<string, string> answers)
    {
        string[] keys = [.. answers.Keys.Order(StringComparer.Ordinal)];
        return string.Join(',', keys.Select(Escape)) + "\n" +
            string.Join(',', keys.Select(key => Escape(answers[key]))) + "\n";
    }

    private static string Escape(string value) =>
        '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
}
