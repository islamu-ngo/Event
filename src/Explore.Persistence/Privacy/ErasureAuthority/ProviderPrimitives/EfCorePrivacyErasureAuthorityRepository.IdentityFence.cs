using System.Data;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Privacy.ErasureAuthority.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Privacy.ErasureAuthority.Repositories;

public sealed partial class EfCorePrivacyErasureAuthorityRepository
{
    private const string FactColumns =
        "authority_sequence, intent_id, subject_kind, subject_id, reason_code, policy_version, requested_at_utc, recorded_at_utc, retention_expires_at_utc, is_legal_hold_pseudonymized";
    private readonly AsyncLocal<bool> _identityGate = new();
    private readonly AsyncLocal<PrivacyErasureRecoveryGate?> _recoveryGate = new();

    public Task<T> ExecuteEnrollmentSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, Func<CancellationToken, Task> beforePrimaryCommit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(beforePrimaryCommit);
        if (_identityGate.Value)
            throw new InvalidOperationException("Nested enrollment must reuse its outer identity operation.");
        // The primary database retains its own transaction and pre-commit validation.
        return ExecuteSerializedAsync(operation, cancellationToken);
    }

    public async Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (_identityGate.Value)
            return await operation(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await using (NpgsqlCommand command = CreateCommand(
            "SELECT privacy_erasure_authority.lock_identity_fence(NULL, NULL)"))
            await command.ExecuteNonQueryAsync(cancellationToken);
        using var recoveryGate = new PrivacyErasureRecoveryGate(dbContext,
            await ReadRecoveryStateAsync(cancellationToken));
        _identityGate.Value = true;
        _recoveryGate.Value = recoveryGate;
        try
        {
            T result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            _identityGate.Value = false;
            _recoveryGate.Value = null;
        }
    }

    public async Task<PrivacyErasureRecoveryState> AcquireRecoveryStateAsync(CancellationToken cancellationToken)
    {
        PrivacyErasureRecoveryGate gate = _recoveryGate.Value
            ?? throw new InvalidOperationException("privacy_erasure_recovery_gate_required");
        gate.RequireActive();
        RequireIdentityGate();
        return gate.Acquire(await ReadRecoveryStateAsync(cancellationToken));
    }

    private async Task<PrivacyErasureRecoveryState> ReadRecoveryStateAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(
            "SELECT store_id, revision, recovery_generation, high_water_sequence, retained_floor_sequence, identity_key_id, identity_key_verification_tag FROM privacy_erasure_authority.read_locked_recovery_state()");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("privacy_erasure_recovery_state_unavailable");
        return new PrivacyErasureRecoveryState(reader.GetGuid(0), reader.GetInt64(1), reader.GetInt64(2),
            new PrivacyErasureAuthorityState(reader.GetInt64(3), reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
    }

    public async Task ValidateKeyAsync(string keyId, string verificationTag, CancellationToken cancellationToken)
    {
        RequireIdentityGate();
        await using NpgsqlCommand command = CreateCommand(
            "SELECT privacy_erasure_authority.lock_identity_fence(@key_id, @verification_tag)");
        command.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, keyId);
        command.Parameters.AddWithValue("verification_tag", NpgsqlDbType.Text, verificationTag);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PrivacyErasureIntent?> FindAsync(
        PrivacyIdentityFingerprint fingerprint, CancellationToken cancellationToken)
    {
        RequireIdentityGate();
        await using NpgsqlCommand command = CreateCommand(
            $"SELECT {FactColumns} FROM privacy_erasure_authority.find_identity_fence(@kind, @key_id, @fingerprint)");
        command.Parameters.AddWithValue("kind", NpgsqlDbType.Integer, (int)fingerprint.IdentityKind);
        command.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, fingerprint.KeyId);
        command.Parameters.AddWithValue("fingerprint", NpgsqlDbType.Text, fingerprint.Fingerprint);
        PrivacyErasureIntent? fact;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            fact = await reader.ReadAsync(cancellationToken) ? ReadFact(reader) : null;
        return fact is null ? null : await LoadFencesAsync(fact, cancellationToken);
    }

    private async Task<PrivacyErasureIntent> LoadFencesAsync(
        PrivacyErasureIntent fact, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = CreateCommand(
            "SELECT identity_kind, key_id, fingerprint FROM privacy_erasure_authority.read_identity_fences(@sequence)");
        command.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, fact.AuthoritySequence);
        var fingerprints = new List<PrivacyIdentityFingerprint>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            fingerprints.Add(new((AuthenticationProviderKind)reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        return PrivacyErasureIntent.Record(fact.IntentId, fact.AuthoritySequence, fact.SubjectKind,
            fact.SubjectId, fact.ReasonCode, fact.PolicyVersion, fact.RequestedAtUtc, fact.RecordedAtUtc,
            fact.RetentionExpiresAtUtc, fingerprints, fact.IsLegalHoldPseudonymized);
    }

    private void RequireIdentityGate()
    {
        if (!_identityGate.Value)
            throw new InvalidOperationException("Identity lookup requires the authority gate.");
    }

    public async Task<bool> IsSubjectFencedAsync(Guid userId, CancellationToken cancellationToken)
    {
        RequireIdentityGate();
        await using NpgsqlCommand command = CreateCommand(
            "SELECT privacy_erasure_authority.is_identity_subject_fenced(@subject_id)");
        command.Parameters.AddWithValue("subject_id", NpgsqlDbType.Uuid, userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The retained subject lookup returned no result."));
    }
}
