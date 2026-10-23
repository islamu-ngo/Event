using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

internal static class HumanEmailSourceQueries
{
    internal static void RequireProvider(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment)
    {
        if (context.Database.IsNpgsql())
        {
            if (enrollment is null) throw new InvalidOperationException("human_email_enrollment_required");
            enrollment.Validate();
        }
        else if (!context.Database.IsSqlite())
            throw new NotSupportedException("human_email_dedicated_source_native_contract_not_supported");
    }

    internal static async Task<HumanEmailSourceFacts> ReadAsync(ExploreDbContext context,
        RelationalConnectionIdentity connection, DbTransaction? transaction,
        PostgresNativeQueueEnrollment? enrollment, HumanCommitSubject subject, HumanAuthenticationLifetime lifetime,
        HumanSupportForwardingObservation forwarding, HumanEmailSourcePurpose purpose, Guid? tenant,
        bool route, Action requireSelection, CancellationToken ct)
    {
        if (context.Database.IsSqlite())
        {
            var facts = await HumanEmailSourceSqlite.ReadAsync(context, transaction, subject, lifetime,
                forwarding, tenant, route, ct);
            requireSelection();
            return facts;
        }
        await using var command = connection.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = PostgresHumanEmailSourceContract.AdmissionSql(context, enrollment!);
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = $"SELECT {(transaction is null ? PostgresHumanEmailSourceContract.ObserveFunctionSql(context)
            : PostgresHumanEmailSourceContract.AcquireFunctionSql(context))}"
            + "(@actor,@provider,@account,@purpose,@tenant,@stamp,@session,@resolved,@route)";
        void Add(string name, NpgsqlDbType type, object? value) =>
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
        Add("actor", NpgsqlDbType.Uuid, subject.PrincipalId);
        Add("provider", NpgsqlDbType.Integer, (int)subject.AccountKey!.ProviderKind);
        Add("account", NpgsqlDbType.Text, subject.AccountKey.Value);
        Add("purpose", NpgsqlDbType.Integer, (int)purpose);
        Add("tenant", NpgsqlDbType.Uuid, tenant);
        Add("stamp", NpgsqlDbType.Text, lifetime.LocalSession?.SecurityStamp);
        Add("session", NpgsqlDbType.Uuid, forwarding.SessionId);
        Add("resolved", NpgsqlDbType.Uuid, forwarding.ResolvedTenantId);
        Add("route", NpgsqlDbType.Boolean, route);
        string json = (string)(await command.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("human_email_native_facts_missing"));
        command.Parameters.Clear();
        command.CommandText = PostgresHumanEmailSourceContract.AdmissionSql(context, enrollment!);
        await command.ExecuteNonQueryAsync(ct);
        requireSelection();
        return HumanEmailSourceProjection.Read(json, subject, tenant, forwarding,
            lifetime.LocalSession is not null, route);
    }
}
