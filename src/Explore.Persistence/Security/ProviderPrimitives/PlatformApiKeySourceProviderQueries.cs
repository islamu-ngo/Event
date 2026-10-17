using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>Closed source-function transport; admission and original-owner custody remain with the issuer.</summary>
internal sealed class PlatformApiKeySourceProviderQueries(ExploreDbContext context)
{
    internal bool RequiresEnrollment => context.Database.IsNpgsql();

    internal void RequireSupported()
    {
        if (!context.Database.IsSqlite() && !context.Database.IsNpgsql())
            throw new NotSupportedException("platform_key_dedicated_source_native_contract_not_supported");
    }

    internal async Task<string> ReadPostgresAsync(RelationalConnectionIdentity connection,
        DbTransaction? transaction, PlatformApiKeySourcePurpose purpose, HumanCommitSubject subject,
        HumanAuthenticationLifetime lifetime, HumanSupportForwardingObservation forwarding, CancellationToken token)
    {
        await using var command = connection.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"SELECT {PostgresPlatformApiKeySourceContract.FunctionSql(context, transaction is not null)}"
            + "(@actor,@provider,@account,@purpose,@stamp,@session,@resolved)";
        void Add(string name, NpgsqlDbType type, object? value) =>
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
        Add("actor", NpgsqlDbType.Uuid, subject.PrincipalId);
        Add("provider", NpgsqlDbType.Integer, (int)subject.AccountKey!.ProviderKind);
        Add("account", NpgsqlDbType.Text, subject.AccountKey.Value);
        Add("purpose", NpgsqlDbType.Integer, (int)purpose);
        Add("stamp", NpgsqlDbType.Text, lifetime.LocalSession?.SecurityStamp);
        Add("session", NpgsqlDbType.Uuid, forwarding.SessionId);
        Add("resolved", NpgsqlDbType.Uuid, forwarding.ResolvedTenantId);
        return await command.ExecuteScalarAsync(token) as string
            ?? throw new InvalidOperationException("platform_key_source_payload_missing");
    }
}
