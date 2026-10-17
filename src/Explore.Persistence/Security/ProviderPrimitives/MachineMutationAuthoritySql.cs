using System.Data;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

internal static class MachineMutationAuthoritySql
{
    internal static async Task<bool> AuthorizeInstanceFooterPatchAsync(
        ExploreDbContext context, PersistedApiKeyAuthenticationProof proof, Guid tenantId,
        Guid groupId, Guid? linkId,
        IHttpContextAccessor httpContext, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId || tenantId == Guid.Empty
            || proof.OwnerType != ExternalApiKeyOwnerType.InstanceAdmin || proof.TenantId is not null
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            return false;
        if (context.Database.IsNpgsql())
        {
            await PostgresMachineMutationAuthorityContract.RequireReadyAsync(context, cancellationToken);
            var sql = new InstanceApiKeyCapabilitySql(context);
            return await context.Database.SqlQueryRaw<bool>(
                $"SELECT {sql.Function(PostgresMachineMutationAuthorityContract.FunctionName)}(@key, @key_id, @owner, @tenant, @group, @link, @scopes, @expires, @status) AS \"Value\"",
                new NpgsqlParameter("key", proof.RowId), new NpgsqlParameter("key_id", proof.KeyId),
                new NpgsqlParameter("owner", proof.OwnerId), new NpgsqlParameter("tenant", tenantId),
                new NpgsqlParameter("group", groupId),
                new NpgsqlParameter("link", NpgsqlDbType.Uuid) { Value = (object?)linkId ?? DBNull.Value },
                new NpgsqlParameter("scopes", proof.Scopes.ToArray()),
                new NpgsqlParameter("expires", NpgsqlDbType.TimestampTz) { Value = (object?)proof.ExpiresAt ?? DBNull.Value },
                new NpgsqlParameter("status", proof.StatusId)).SingleAsync(cancellationToken);
        }
        if (!await new ExternalApiKeyIssuanceAuthority(context, httpContext)
                .IsInstanceAdministratorForCommitAsync(proof.OwnerId, cancellationToken))
            return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, cancellationToken);
        int? statusId = await context.Tenants.AsNoTracking().Where(row => row.Id == tenantId)
            .Select(row => (int?)row.TenantStatusId).SingleOrDefaultAsync(cancellationToken);
        if (statusId is not { } tenantStatus)
            return false;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenantStatus], cancellationToken);
        if (!await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, cancellationToken))
            return false;

        // Other providers have no PostgreSQL definer function. This one exact-row
        // command remains on the caller's connection and never selects secret/usage columns.
        var entity = context.Model.FindEntityType(typeof(ExternalApiKey))!;
        var mapping = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var helper = context.GetService<ISqlGenerationHelper>();
        string table = helper.DelimitIdentifier(mapping.Name, mapping.Schema);
        string C(string name) => helper.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(mapping)!);
        string predicate = $"{C("Id")} = @key AND {C("KeyId")} = @key_id AND {C("OwnerId")} = @owner"
            + $" AND {C("TenantId")} IS NULL AND {C("ExternalApiKeyOwnerTypeId")} = {(int)ExternalApiKeyOwnerType.InstanceAdmin}";
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        foreach (var value in new (string Name, object Value)[] { ("key", proof.RowId), ("key_id", proof.KeyId), ("owner", proof.OwnerId) })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = value.Name;
            parameter.Value = value.Value;
            command.Parameters.Add(parameter);
        }
        command.CommandText = $"UPDATE {table} SET {C("Id")} = {C("Id")} WHERE {predicate}";
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            return false;
        if (!await FenceFooterTargetsAsync(context, tenantId, groupId, linkId, cancellationToken))
            return false;
        command.CommandText = $"SELECT {C("ExternalApiKeyStatusId")}, {C("ExpiresAt")}, {C("Scopes")} FROM {table} WHERE {predicate}";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return false;
        int status = reader.GetInt32(0);
        DateTime? expires = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
        string[] scopes = reader.GetString(2).Split([' ', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        DateTime now = clock.GetUtcNow().UtcDateTime;
        return proof.StatusId is (int)ExternalApiKeyStatusEnum.Active or (int)ExternalApiKeyStatusEnum.PendingRotation
            && status is (int)ExternalApiKeyStatusEnum.Active or (int)ExternalApiKeyStatusEnum.PendingRotation
            && (proof.ExpiresAt is null || proof.ExpiresAt > now) && (expires is null || expires > now)
            && MachineScopeMapping.ScopesPermit(proof.Scopes, ResourceKinds.Tenant, AuthorizationActions.Update)
            && MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Tenant, AuthorizationActions.Update);
    }

    internal static async Task<bool> FenceFooterTargetsAsync(
        ExploreDbContext context, Guid tenantId, Guid groupId, Guid? linkId, CancellationToken cancellationToken)
    {
        var group = await new FooterLinkGroupRepository(context).GetForMutationAsync(groupId, cancellationToken);
        if (group is null || group.TenantId != tenantId)
            return false;
        if (linkId is not { } id)
            return true;
        var link = await new FooterLinkRepository(context).GetForMutationAsync(id, groupId, cancellationToken);
        return link is not null && link.FooterLinkGroupId == groupId;
    }
}
