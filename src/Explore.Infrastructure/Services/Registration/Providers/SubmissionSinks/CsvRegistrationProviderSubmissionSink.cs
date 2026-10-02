using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Storage;

namespace Explore.Infrastructure.Services.Registration.Providers.SubmissionSinks;

public sealed class CsvRegistrationProviderSubmissionSink(
    ManagedStorageProducer producer,
    IStorageProducerOperationRepository storageObjects,
    IUnitOfWork unitOfWork,
    TimeProvider? timeProvider = null) : IRegistrationProviderDescriptor, IRegistrationProviderSubmissionSink
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
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

        string provider = request.Connection.ProviderWorkspaceId;
        if (provider is not (StorageProviders.Local or StorageProviders.S3Compatible))
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                "storage_provider_binding_unsupported");
        await using var stream = new MemoryStream(csv, writable: false);
        if (request.DisclosureUntilUtc is { } deadline && _timeProvider.GetUtcNow().UtcDateTime >= deadline)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                "registration_data_retention_expired");
        }

        StagedStorageWrite staged = await producer.WriteAsync(provider,
            new FileStorageWriteInput(
                request.TenantId,
                stream,
                "text/csv; charset=utf-8",
                $"registration-submission-{request.RegistrationSubmissionId:N}.csv",
                ".csv",
                csv.LongLength,
                MaxCsvBytes),
            cancellationToken);

        try
        {
            await unitOfWork.ExecuteSerializableAsync(async ct =>
            {
                var operation = await storageObjects.FenceProducerAsync(staged.OperationId, request.TenantId, ct)
                    ?? throw new InvalidOperationException("storage_producer_unavailable");
                if (request.DisclosureUntilUtc is { } expires && _timeProvider.GetUtcNow().UtcDateTime >= expires)
                    throw new RegistrationProviderSubmissionDeliveryException(
                        RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                        "registration_data_retention_expired");
                FileStorageWriteResult written = staged.Write;
                StorageObject storageObject = new()
                {
                    Id = operation.Id,
                    TenantId = request.TenantId,
                    Tenant = null!,
                    FileTypeId = (int)FileTypeEnum.Document,
                    FileType = null!,
                    Uri = $"/api/storageobject/{operation.Id}/content",
                    ObjectKey = operation.ObjectKey,
                    Provider = operation.Provider,
                    StorageProviderBindingId = operation.ProviderBindingId,
                    ProviderVersionId = operation.ProviderVersionId,
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
                    RegistrationContentRetentionUntilUtc = request.DisclosureUntilUtc,
                    CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                await storageObjects.CompleteProducerAsync(operation, storageObject, ct);
                return true;
            }, cancellationToken);
        }
        catch
        {
            await producer.RetireAsync(staged.OperationId, request.TenantId, CancellationToken.None);
            throw;
        }

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
