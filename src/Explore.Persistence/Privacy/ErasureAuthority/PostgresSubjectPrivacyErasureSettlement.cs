using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Privacy.ErasureAuthority;

/// <summary>
/// Verifies the committed retained fact, then uses fixed native operations on the
/// existing application transaction. No private entity is materialized or tracked.
/// </summary>
public sealed class PostgresSubjectPrivacyErasureSettlement(ExploreDbContext context)
    : ISubjectPrivacyErasureSettlement
{
    private PrivacyErasureIntent? _preparedIntent;
    private Guid? _transactionId;

    public async Task<IReadOnlyList<PrivacyErasureProviderCandidate>> PrepareAsync(
        PrivacyErasureIntent intent, IPrivacyErasureAuthority authority, CancellationToken cancellationToken)
    {
        RequireTransaction();
        IReadOnlyList<PrivacyErasureIntent> retained = await authority.ReadAfterAsync(
            intent.AuthoritySequence - 1, 1, cancellationToken);
        if (retained.Count != 1 || retained[0].IntentId != intent.IntentId
            || retained[0].SubjectId != intent.SubjectId || retained[0].SubjectKind != intent.SubjectKind
            || retained[0].AuthoritySequence != intent.AuthoritySequence
            || retained[0].PolicyVersion != intent.PolicyVersion || retained[0].ReasonCode != intent.ReasonCode
            || retained[0].RecordedAtUtc != intent.RecordedAtUtc)
            throw new InvalidOperationException("subject_erasure_retained_authority_mismatch");

        using JsonDocument result = await PostgresSubjectPrivacyErasureOperations.ExecuteAsync(context, "prepare", intent, null, cancellationToken);
        context.DisclosureMutations.Enlist(result.RootElement.GetProperty("tenants")
            .EnumerateArray().Select(value => value.GetGuid()));
        _preparedIntent = intent;
        _transactionId = context.Database.CurrentTransaction!.TransactionId;
        return result.RootElement.GetProperty("candidates").EnumerateArray().Select(value =>
            new PrivacyErasureProviderCandidate(
                (PrivacyErasureProviderKind)value.GetProperty("provider").GetInt32(),
                (PrivacyErasureProviderAction)value.GetProperty("action").GetInt32(),
                value.GetProperty("tenant").ValueKind == JsonValueKind.Null ? null : value.GetProperty("tenant").GetGuid(),
                value.GetProperty("target").GetGuid(),
                (PrivacyErasureProviderLocatorKind)value.GetProperty("kind").GetInt32(),
                value.GetProperty("locator").GetString()!)).ToArray();
    }

    public async Task<SubjectPrivacyErasureEffects> DisposeAsync(
        PrivacyErasureIntent intent, DateTime appliedAtUtc, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (!ReferenceEquals(intent, _preparedIntent)
            || _transactionId != context.Database.CurrentTransaction!.TransactionId)
            throw new InvalidOperationException("subject_erasure_preparation_required");
        using JsonDocument result = await PostgresSubjectPrivacyErasureOperations.ExecuteAsync(context, "dispose", intent, appliedAtUtc, cancellationToken);
        _preparedIntent = null;
        _transactionId = null;
        Guid[] tenants = result.RootElement.GetProperty("tenants").EnumerateArray()
            .Select(value => value.GetGuid()).ToArray();
        context.DisclosureMutations.Enlist(tenants);
        return new(
            result.RootElement.GetProperty("homes").EnumerateArray().Select(value =>
                new ErasedSubjectHome(value.GetProperty("id").GetGuid(), value.GetProperty("tenant").GetGuid(),
                    value.GetProperty("stamp").GetGuid())).ToArray(),
            result.RootElement.GetProperty("eventLocations").EnumerateArray().Select(value =>
                new ErasedSubjectEventLocation(value.GetProperty("id").GetGuid(), value.GetProperty("tenant").GetGuid(),
                    value.GetProperty("event").GetGuid(), value.GetProperty("location").GetGuid(),
                    value.GetProperty("version").GetInt32())
                {
                    RetainedCorrectionMessageId = value.GetProperty("message").GetGuid()
                }).ToArray(),
            tenants);
    }

    private void RequireTransaction()
    {
        if (!context.Database.IsNpgsql() || context.TenantFilterTenantId is not null
            || context.Database.CurrentTransaction is not { } transaction
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("subject_erasure_serializable_global_transaction_required");
    }
}
