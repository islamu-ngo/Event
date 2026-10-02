using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Privacy.ErasureAuthority.Repositories;

public sealed partial class EfCorePrivacyErasureAuthorityRepository(
    PrivacyErasureAuthorityDbContext dbContext,
    IOptions<PrivacyErasureOptions> options)
    : IPrivacyErasureAuthority, IPrivacyErasureAuthorityMaintenance, IPrivacyIdentityFenceAuthority
{
    public const int MaximumReadBatchSize = 500;

    public async Task<PrivacyErasureAuthorityState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using NpgsqlCommand command = CreateCommand(
                $"SELECT high_water_sequence, retained_floor_sequence, identity_key_id, identity_key_verification_tag FROM {PrivacyErasureAuthorityDatabaseContract.GetStateFunctionSql}() CROSS JOIN privacy_erasure_authority.read_identity_key_state()");
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new PrivacyErasureAuthorityState(reader.GetInt64(0), reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3));
            }

            throw new InvalidOperationException("The erasure-authority state query returned no state.");
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    public Task<PrivacyErasureIntent> AppendAsync(
        PrivacyErasureRequest intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return ExecuteSerializedAsync(async token =>
        {
            await using NpgsqlCommand command = CreateCommand(
                $"SELECT {FactColumns} FROM privacy_erasure_authority.append_identity_fenced_erasure(@intent_id, @subject_kind, @subject_id, @reason_code, @policy_version, @authority_retention, @key_id, @verification_tag, @fences)");
            command.Parameters.AddWithValue("intent_id", NpgsqlDbType.Uuid, intent.IntentId);
            command.Parameters.AddWithValue("subject_kind", NpgsqlDbType.Smallint, (short)intent.SubjectKind);
            command.Parameters.AddWithValue("subject_id", NpgsqlDbType.Uuid, intent.SubjectId);
            command.Parameters.AddWithValue("reason_code", NpgsqlDbType.Smallint, (short)intent.ReasonCode);
            command.Parameters.AddWithValue("policy_version", NpgsqlDbType.Integer, intent.PolicyVersion);
            command.Parameters.AddWithValue("authority_retention", NpgsqlDbType.Interval, options.Value.AuthorityRetention);
            command.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, (object?)intent.IdentityKeyId ?? DBNull.Value);
            command.Parameters.AddWithValue("verification_tag", NpgsqlDbType.Text, (object?)intent.IdentityKeyVerificationTag ?? DBNull.Value);
            command.Parameters.AddWithValue("fences", NpgsqlDbType.Jsonb,
                System.Text.Json.JsonSerializer.Serialize(intent.IdentityFences.Select(fence => new
                {
                    kind = (int)fence.IdentityKind, key_id = fence.KeyId, fingerprint = fence.Fingerprint
                })));
            PrivacyErasureIntent? fact = null;
            try
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
                if (await reader.ReadAsync(token))
                {
                    fact = ReadFact(reader);
                }
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InvalidParameterValue)
            {
                throw new InvalidOperationException("The erasure authority rejected the append payload for this IntentId.");
            }

            return fact is null
                ? throw new InvalidOperationException("The erasure-authority append did not return a retained fact.")
                : await LoadFencesAsync(fact, token);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PrivacyErasureIntent>> ReadAfterAsync(
        long authoritySequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(authoritySequence);
        if (limit is < 1 or > MaximumReadBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using NpgsqlCommand command = CreateCommand(
                $"SELECT {FactColumns} FROM privacy_erasure_authority.read_identity_fenced_intents_after(@authority_sequence, @limit)");
            command.Parameters.AddWithValue("authority_sequence", NpgsqlDbType.Bigint, authoritySequence);
            command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
            var facts = new List<PrivacyErasureIntent>(limit);
            try
            {
                await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    facts.Add(ReadFact(reader));
                }
            }
            catch (PostgresException exception) when (
                exception.SqlState == PrivacyErasureAuthorityDatabaseContract.StaleCheckpointSqlState)
            {
                throw new Explore.Application.Exceptions.StaleRestoreBelowRetainedFloorException();
            }

            var retained = new List<PrivacyErasureIntent>(facts.Count);
            foreach (PrivacyErasureIntent fact in facts)
                retained.Add(await LoadFencesAsync(fact, cancellationToken));
            return retained;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    public async Task<PrivacyErasureRetentionEvaluation> EvaluateRetentionAsync(
        PrivacyErasureRetentionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using NpgsqlCommand command = CreateCommand(
                $"SELECT eligible_count, held_count, current_floor_sequence, projected_floor_sequence FROM {PrivacyErasureAuthorityDatabaseContract.EvaluateRetentionFunctionSql}(@as_of_utc, @batch_size, @held_authority_sequences)");
            AddMaintenanceParameters(command, request);
            NpgsqlDataReader reader;
            try
            {
                reader = await command.ExecuteReaderAsync(cancellationToken);
            }
            catch (PostgresException exception) when (
                exception.SqlState == PrivacyErasureAuthorityDatabaseContract.SequenceGapSqlState)
            {
                throw new Explore.Application.Exceptions.PrivacyErasureSequenceGapException();
            }
            await using (reader)
                if (await reader.ReadAsync(cancellationToken))
                {
                    return new PrivacyErasureRetentionEvaluation(
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.GetInt64(2),
                        reader.GetInt64(3));
                }

            throw new InvalidOperationException("The erasure-authority retention evaluation returned no result.");
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    public async Task<PrivacyErasureCompactionResult> CompactExpiredIntentsAsync(
        PrivacyErasureRetentionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using NpgsqlCommand command = CreateCommand(
                $"SELECT deleted_count, pseudonymized_count, high_water_sequence, retained_floor_sequence, identity_key_id, identity_key_verification_tag FROM {PrivacyErasureAuthorityDatabaseContract.CompactRetentionFunctionSql}(@as_of_utc, @batch_size, @held_authority_sequences) CROSS JOIN privacy_erasure_authority.read_identity_key_state()");
            AddMaintenanceParameters(command, request);
            NpgsqlDataReader reader;
            try
            {
                reader = await command.ExecuteReaderAsync(cancellationToken);
            }
            catch (PostgresException exception) when (
                exception.SqlState == PrivacyErasureAuthorityDatabaseContract.SequenceGapSqlState)
            {
                throw new Explore.Application.Exceptions.PrivacyErasureSequenceGapException();
            }
            await using (reader)
                if (await reader.ReadAsync(cancellationToken))
                {
                    return new PrivacyErasureCompactionResult(
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        new PrivacyErasureAuthorityState(reader.GetInt64(2), reader.GetInt64(3),
                            reader.IsDBNull(4) ? null : reader.GetString(4),
                            reader.IsDBNull(5) ? null : reader.GetString(5)));
                }

            throw new InvalidOperationException("The erasure-authority compaction returned no result.");
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            as NpgsqlTransaction;
        return new NpgsqlCommand(sql, connection, transaction);
    }

    private static void AddMaintenanceParameters(
        NpgsqlCommand command,
        PrivacyErasureRetentionRequest request)
    {
        command.Parameters.AddWithValue("as_of_utc", NpgsqlDbType.TimestampTz, request.AsOfUtc);
        command.Parameters.AddWithValue("batch_size", NpgsqlDbType.Integer, request.BatchSize);
        command.Parameters.AddWithValue(
            "held_authority_sequences",
            NpgsqlDbType.Array | NpgsqlDbType.Bigint,
            request.HeldAuthoritySequences.Order().ToArray());
    }

    private static PrivacyErasureIntent ReadFact(NpgsqlDataReader reader) =>
        PrivacyErasureIntent.Record(
            reader.GetGuid(1),
            reader.GetInt64(0),
            (PrivacyErasureSubjectKind)reader.GetInt16(2),
            reader.GetGuid(3),
            (PrivacyErasureReasonCode)reader.GetInt16(4),
            reader.GetInt32(5),
            DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc),
            DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc),
            DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc),
            isLegalHoldPseudonymized: reader.GetBoolean(9));
}
