using System.Data;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Services.Federation;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class PublicEventResponseClockProviderOperations
{
    internal static async Task<DateTimeOffset?> GetNextAsync(ExploreDbContext context,
        PublicEventDiscoveryReadBinding binding, ILocationPrivacyGovernanceService privacy,
        AtprotoEventGovernanceResolver federation, DateTimeOffset observedAtUtc, CancellationToken token)
    {
        binding.RequireOriginalOwner(context);
        if (!context.Database.IsNpgsql() || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("public_clock_original_postgresql_owner_required");
        if (context.Database.CurrentTransaction is null)
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                binding.RequireOriginalOwner(context);
                await using var owned = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var result = await GetNextAsync(context, binding, privacy, federation, observedAtUtc, token);
                await owned.CommitAsync(token);
                return result;
            });
        }

        binding.RequireSerializableOwner(context);
        var transaction = context.Database.CurrentTransaction.GetDbTransaction();
        Guid tenant = context.TenantFilterTenantId!.Value;
        DateTimeOffset? next = await ReadAsync("sessions");
        if ((await federation.ResolveAsync(tenant, null, token)).EventsEnabled)
            Include(await ReadAsync("remote"));
        RequireOwner();
        if (await ReadAsync("locations") is not null)
        {
            var governance = await privacy.ResolveAsync(tenant, token);
            RequireOwner();
            if (!governance.IsResolved)
                throw new EventDiscoveryUnavailableException();
            DateTime threshold = observedAtUtc.UtcDateTime.Subtract(governance.DefaultRevealOffset);
            var created = await ReadAsync("created", threshold, governance.AllowPublicExactAddress, governance.AllowPublicCoordinates);
            var reveal = await ReadAsync("reveal", threshold, governance.AllowPublicExactAddress, governance.AllowPublicCoordinates);
            // Keep arithmetic in .NET, as on the portable path: PostgreSQL intervals
            // cannot represent a 100ns governance offset. The two future components
            // deliberately bound the evaluator's max() conservatively, not exactly.
            if (created is { } creation)
                Include(new DateTimeOffset(creation.UtcDateTime.Add(governance.DefaultRevealOffset)));
            Include(reveal);
        }
        RequireOwner();
        return next;

        async Task<DateTimeOffset?> ReadAsync(string operation, DateTime? threshold = null,
            bool address = false, bool coordinates = false)
        {
            RequireOwner();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
            command.CommandText = PostgresPublicEventResponseClockContract.AdmissionSql(context, binding.Enrollment)
                + $"\nSELECT {PostgresPublicEventResponseClockContract.FunctionSql(context)}(@tenant,@operation,@now,@threshold,@address,@coordinates);";
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenant });
            command.Parameters.Add(new NpgsqlParameter("operation", NpgsqlDbType.Text) { Value = operation });
            command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = observedAtUtc.UtcDateTime });
            command.Parameters.Add(new NpgsqlParameter("threshold", NpgsqlDbType.TimestampTz) { Value = threshold ?? observedAtUtc.UtcDateTime });
            command.Parameters.Add(new NpgsqlParameter("address", NpgsqlDbType.Boolean) { Value = address });
            command.Parameters.Add(new NpgsqlParameter("coordinates", NpgsqlDbType.Boolean) { Value = coordinates });
            object? value = await command.ExecuteScalarAsync(token);
            RequireOwner();
            return value is DateTime instant ? new DateTimeOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc)) : null;
        }

        void RequireOwner()
        {
            binding.RequireSerializableOwner(context);
            if (!ReferenceEquals(transaction, context.Database.CurrentTransaction!.GetDbTransaction()))
                throw new UnauthorizedAccessException("public_clock_transaction_substitution");
        }

        void Include(DateTimeOffset? boundary)
        {
            if (boundary > observedAtUtc && (next is null || boundary < next))
                next = boundary;
        }
    }
}
