using System.Text.Json;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Privacy.ErasureAuthority;

internal static class PostgresSubjectPrivacyErasureOperations
{
    internal static async Task<JsonDocument> ExecuteAsync(
        ExploreDbContext context, string operation, PrivacyErasureIntent intent, DateTime? appliedAtUtc, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {PostgresSubjectPrivacyErasureContract.FunctionSql(context, operation)}"
            + "(@intent, @subject, @sequence, @policy, @reason, @recorded"
            + (appliedAtUtc.HasValue ? ", @applied)" : ")");
        command.Parameters.Add(new NpgsqlParameter("intent", NpgsqlDbType.Uuid) { Value = intent.IntentId });
        command.Parameters.Add(new NpgsqlParameter("subject", NpgsqlDbType.Uuid) { Value = intent.SubjectId });
        command.Parameters.Add(new NpgsqlParameter("sequence", NpgsqlDbType.Bigint) { Value = intent.AuthoritySequence });
        command.Parameters.Add(new NpgsqlParameter("policy", NpgsqlDbType.Integer) { Value = intent.PolicyVersion });
        command.Parameters.Add(new NpgsqlParameter("reason", NpgsqlDbType.Integer) { Value = (int)intent.ReasonCode });
        command.Parameters.Add(new NpgsqlParameter("recorded", NpgsqlDbType.TimestampTz) { Value = intent.RecordedAtUtc });
        if (appliedAtUtc.HasValue)
            command.Parameters.Add(new NpgsqlParameter("applied", NpgsqlDbType.TimestampTz) { Value = appliedAtUtc.Value });
        string json = (string)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("subject_erasure_result_missing"));
        return JsonDocument.Parse(json);
    }
}
