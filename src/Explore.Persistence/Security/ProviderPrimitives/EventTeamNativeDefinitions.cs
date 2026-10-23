using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

internal static class EventTeamNativeDefinitions
{
    private static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };

    internal static async Task<EventTeamPolicyFacts> ReadAsync(
        ExploreDbContext context, RelationalConnectionIdentity owner, PostgresNativeQueueEnrollment enrollment,
        Guid principal, Guid tenant, EventTeamSourceTarget target, CancellationToken token)
    {
        var connection = owner.Connection;
        bool opened = connection.State != ConnectionState.Open;
        if (opened) await context.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
            command.CommandText = PostgresHumanEventAssignmentSourceContract.AdmissionSql(context, enrollment!);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = $"SELECT {(command.Transaction is null
                ? PostgresHumanEventAssignmentSourceContract.ObserveFunctionSql(context)
                : PostgresHumanEventAssignmentSourceContract.AcquireFunctionSql(context))}(@actor,@purpose,@tenant,@events)";
            command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = principal });
            command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer)
            { Value = (int)PolicySourceSetPurpose.RuntimeTenant });
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenant });
            command.Parameters.Add(new NpgsqlParameter("events", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            { Value = new[] { target.EventId } });
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(token)
                ?? throw new InvalidOperationException("Event team native source returned no facts.")));
            command.Parameters.Clear();
            command.CommandText = PostgresHumanEventAssignmentSourceContract.AdmissionSql(context, enrollment!);
            await command.ExecuteNonQueryAsync(token);
            ImmutableArray<T> Rows<T>(string name)
            {
                if (!document.RootElement.TryGetProperty(name, out var value))
                    throw new NotSupportedException("event_team_native_definition_vocabulary_not_installed");
                if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(row =>
                    row.ValueKind != JsonValueKind.Object || typeof(T).GetProperties().Any(property =>
                        !row.TryGetProperty(property.Name, out _))))
                    throw new InvalidOperationException("Event team native definition fields are missing.");
                var rows = value.Deserialize<ImmutableArray<T>>(Json);
                return rows.IsDefault
                    ? throw new InvalidOperationException("Event team native arrays are missing.") : rows;
            }
            // Parent-owned installation must extend the SAME routine's projection,
            // column privileges, locks and attestation. No direct-table fallback.
            return new(Rows<EventTeamRoleFact>("event_team_roles"),
                Rows<EventTeamPermissionFact>("event_team_permissions"),
                Rows<EventTeamRolePermissionFact>("event_team_role_permissions"),
                Rows<EventTeamAssignmentFact>("event_team_assignments"), null);
        }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
    }
}
