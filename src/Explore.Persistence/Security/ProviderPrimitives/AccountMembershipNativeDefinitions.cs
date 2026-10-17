using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

internal static class AccountMembershipNativeDefinitions
{
    private static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };

    internal static async Task<(ImmutableArray<HumanPrincipalRoleFact>, ImmutableArray<HumanPrincipalPermissionFact>)>
        ReadAsync(ExploreDbContext context, RelationalConnectionIdentity owner, HumanCommitSubject subject,
            HumanAuthenticationLifetime lifetime, PostgresNativeQueueEnrollment enrollment, Guid? tenant,
            CancellationToken token)
    {
        var connection = owner.Connection;
        // These additional fixed outputs must be emitted by the SAME attested P
        // routines. Missing vocabulary is a refusal, never an ordinary table-read
        // fallback, independent connection, or substituted Global login.
        bool opened = connection.State != ConnectionState.Open;
        if (opened) await context.Database.OpenConnectionAsync(token);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
            command.CommandText = PostgresHumanPrincipalSourceContract.AdmissionSql(context, enrollment!);
            await command.ExecuteNonQueryAsync(token);
            command.CommandText = $"SELECT {(command.Transaction is null
                ? PostgresHumanPrincipalSourceContract.ObserveFunctionSql(context)
                : PostgresHumanPrincipalSourceContract.AcquireFunctionSql(context))}"
                + "(@actor,@provider,@account,@purpose,@tenant,@stamp)";
            command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = subject!.PrincipalId });
            command.Parameters.Add(new NpgsqlParameter("provider", NpgsqlDbType.Integer) { Value = (int)subject.AccountKey!.ProviderKind });
            command.Parameters.Add(new NpgsqlParameter("account", NpgsqlDbType.Text) { Value = subject.AccountKey.Value });
            command.Parameters.Add(new NpgsqlParameter("purpose", NpgsqlDbType.Integer) { Value = (int)PolicySourceSetPurpose.RuntimeTenant });
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenant!.Value });
            command.Parameters.Add(new NpgsqlParameter("stamp", NpgsqlDbType.Text)
            { Value = (object?)lifetime!.LocalSession?.SecurityStamp ?? DBNull.Value });
            using var document = JsonDocument.Parse((string)(await command.ExecuteScalarAsync(token)
                ?? throw new InvalidOperationException("Account native policy source returned no facts.")));
            command.Parameters.Clear();
            command.CommandText = PostgresHumanPrincipalSourceContract.AdmissionSql(context, enrollment!);
            await command.ExecuteNonQueryAsync(token);
            if (!document.RootElement.TryGetProperty("account_roles", out var roles)
                || !document.RootElement.TryGetProperty("account_permissions", out var permissions))
                throw new NotSupportedException("account_membership_native_definition_vocabulary_not_installed");
            var roleFacts = roles.Deserialize<ImmutableArray<HumanPrincipalRoleFact>>(Json);
            var permissionFacts = permissions.Deserialize<ImmutableArray<HumanPrincipalPermissionFact>>(Json);
            if (roleFacts.IsDefault || permissionFacts.IsDefault)
                throw new InvalidOperationException("Account native definition arrays are missing.");
            return (roleFacts, permissionFacts);
        }
        finally
        {
            if (opened) await context.Database.CloseConnectionAsync();
        }
    }
}
