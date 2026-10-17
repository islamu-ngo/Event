using System.Data;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Services;

internal sealed class InstanceApiKeyCapabilityStore(
    ExploreDbContext context, PrimaryDatabaseRole role, Guid actorId,
    Guid? targetId, string? operationKey, IHttpContextAccessor httpContext)
    : IExternalApiKeyIssuanceReceiptRepository, IAsyncDisposable
{
    private IAsyncDisposable? _ownerLease;
    private IDbContextTransaction? _ownerTransaction;
    private readonly InstanceApiKeyCapabilityQueries native = new(context);
    private readonly ExternalApiKeyIssuanceAuthority _portableAuthority = new(context, httpContext);
    private readonly int? _provider = httpContext.HttpContext?.User.GetProviderIdentity()?.AccountKey is { } account
        ? (int)account.ProviderKind : null;
    private readonly string? _account = httpContext.HttpContext?.User.GetProviderIdentity()?.AccountKey?.Value;

    private void RequireTransaction()
    {
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("instance_api_key_transaction_required");
    }

    private void RequireRole(PrimaryDatabaseRole expected)
    {
        RequireTransaction();
        if (role != expected)
            throw new InvalidOperationException("instance_api_key_operation_not_admitted");
    }

    private object[] Parameters(params NpgsqlParameter[] extra) =>
    [
        new NpgsqlParameter("actor", actorId),
        new NpgsqlParameter("provider", NpgsqlDbType.Integer) { Value = (object?)_provider ?? DBNull.Value },
        new NpgsqlParameter("account", NpgsqlDbType.Text) { Value = (object?)_account ?? DBNull.Value },
        .. extra
    ];

    private NpgsqlParameter KeyParameter() =>
        new("key", NpgsqlDbType.Uuid) { Value = (object?)targetId ?? DBNull.Value };

    private NpgsqlParameter OperationParameter() =>
        new("operation", NpgsqlDbType.Text) { Value = operationKey ?? throw new InvalidOperationException("instance_key_operation_required") };

    private async Task<bool> ScalarAsync(string function, string arguments, object[] parameters, CancellationToken token)
    {
        try
        {
            return await native.ScalarAsync(function, arguments, parameters, token);
        }
        catch (PostgresException exception) when (exception.SqlState == "42501")
        {
            throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.Update);
        }
    }

    private async Task DemandPortableActorAsync(CancellationToken token)
    {
        if (!await _portableAuthority.IsInstanceAdministratorForCommitAsync(actorId, token))
            throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.Update);
    }

    private IQueryable<ExternalApiKey> PlatformKeys() => context.ExternalApiKeys
        .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyPlatformManagement)
        .Where(key => key.TenantId == null
            && key.ExternalApiKeyOwnerTypeId == (int)ExternalApiKeyOwnerType.InstanceAdmin);

    private static IQueryable<ExternalApiKey> Metadata(IQueryable<ExternalApiKey> keys) => keys.AsNoTracking()
        .Select(key => new ExternalApiKey
        {
            Id = key.Id,
            TenantId = key.TenantId,
            Name = key.Name,
            Description = key.Description,
            KeyId = key.KeyId,
            SecretHash = string.Empty,
            Scopes = key.Scopes,
            ExternalApiKeyOwnerTypeId = key.ExternalApiKeyOwnerTypeId,
            OwnerId = key.OwnerId,
            ExternalApiKeyStatusId = key.ExternalApiKeyStatusId,
            ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = key.ExternalApiKeyCreditPeriodId,
            ExternalApiKeyCreditPeriod = null!,
            CreditLimit = key.CreditLimit,
            MaxRolloverCredits = key.MaxRolloverCredits,
            ExpiresAt = key.ExpiresAt,
            LastUsedAt = key.LastUsedAt,
            CreatedAt = key.CreatedAt,
            CreatedBy = key.CreatedBy,
            UpdatedAt = key.UpdatedAt,
            UpdatedBy = key.UpdatedBy
        });

    public async Task<IReadOnlyList<ExternalApiKey>> ReadAsync(CancellationToken token)
    {
        RequireTransaction();
        if (role is not PrimaryDatabaseRole.InstanceKeyRead and not PrimaryDatabaseRole.InstanceKeyWrite)
            throw new InvalidOperationException("instance_api_key_operation_not_admitted");
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
        {
            string function = role == PrimaryDatabaseRole.InstanceKeyRead
                ? PostgresInstanceApiKeyManagementContract.ReadFunction
                : PostgresInstanceApiKeyManagementContract.WriteReadFunction;
            try
            {
                return await native.ReadAsync(function, Parameters(KeyParameter()), token);
            }
            catch (PostgresException exception) when (exception.SqlState == "42501")
            {
                throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.View);
            }
        }
        if (role == PrimaryDatabaseRole.InstanceKeyWrite)
            await HoldPortableOwnerAsync(token);
        await DemandPortableActorAsync(token);
        var selected = targetId is { } target ? PlatformKeys().Where(key => key.Id == target)
            : PlatformKeys().Where(key => key.OwnerId == actorId);
        foreach (Guid id in await selected.OrderBy(key => key.Id).Select(key => key.Id).ToListAsync(token))
            await RelationalEntityRowFence.AcquireGlobalAsync<ExternalApiKey>(context, id, token);
        return await Metadata(selected).ToListAsync(token);
    }

    private async Task HoldPortableOwnerAsync(CancellationToken token)
    {
        if (ReferenceEquals(_ownerTransaction, context.Database.CurrentTransaction))
            return;
        if (_ownerLease is not null)
            await _ownerLease.DisposeAsync();
        _ownerLease = null;
        _ownerTransaction = context.Database.CurrentTransaction;
        Guid ownerId = role == PrimaryDatabaseRole.InstanceKeyIssuance ? actorId
            : await PlatformKeys().Where(key => key.Id == targetId).Select(key => key.OwnerId).SingleOrDefaultAsync(token);
        if (ownerId != Guid.Empty)
            _ownerLease = await RelationalNamedLock.AcquireTransactionAsync(context, $"instance-api-key-owner:{ownerId:D}", token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownerLease is not null)
            await _ownerLease.DisposeAsync();
        _ownerLease = null;
        _ownerTransaction = null;
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken token)
    {
        RequireRole(PrimaryDatabaseRole.InstanceKeyWrite);
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
            return await ScalarAsync(PostgresInstanceApiKeyManagementContract.NameFunction,
                "@actor, @provider, @account, @key, @name",
                Parameters(KeyParameter(), new NpgsqlParameter("name", name)), token);
        var key = (await ReadAsync(token)).SingleOrDefault();
        return key is not null && await PlatformKeys().AsNoTracking().AnyAsync(
            row => row.OwnerId == key.OwnerId && row.Name == name && row.Id != key.Id, token);
    }

    public async Task UpdatePolicyAsync(
        string name, string scopes, DateTime? expiresAt, DateTime changedAt, CancellationToken token)
    {
        RequireRole(PrimaryDatabaseRole.InstanceKeyWrite);
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
        {
            if (!await ScalarAsync(PostgresInstanceApiKeyManagementContract.UpdateFunction,
                "@actor, @provider, @account, @key, @name, @scopes, @expires, @changed",
                Parameters(KeyParameter(), new NpgsqlParameter("name", name), new NpgsqlParameter("scopes", scopes),
                    new NpgsqlParameter("expires", NpgsqlDbType.TimestampTz) { Value = (object?)expiresAt ?? DBNull.Value },
                    new NpgsqlParameter("changed", changedAt)), token))
                throw new InvalidOperationException("instance_api_key_target_unavailable");
            return;
        }
        var key = (await ReadAsync(token)).SingleOrDefault()
            ?? throw new InvalidOperationException("instance_api_key_target_unavailable");
        if (await NameExistsAsync(name, token))
            throw new InvalidOperationException("instance_key_name_conflict");
        await PlatformKeys().Where(row => row.Id == key.Id && row.OwnerId == key.OwnerId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Name, name)
                .SetProperty(row => row.Scopes, scopes).SetProperty(row => row.ExpiresAt, expiresAt)
                .SetProperty(row => row.UpdatedAt, changedAt).SetProperty(row => row.UpdatedBy, actorId), token);
    }

    public async Task<bool> RevokeAsync(DateTime changedAt, CancellationToken token)
    {
        RequireRole(PrimaryDatabaseRole.InstanceKeyWrite);
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
            return await ScalarAsync(PostgresInstanceApiKeyManagementContract.RevokeFunction,
                "@actor, @provider, @account, @key, @changed",
                Parameters(KeyParameter(), new NpgsqlParameter("changed", changedAt)), token);
        var key = (await ReadAsync(token)).SingleOrDefault();
        if (key is null)
            return false;
        await PlatformKeys().Where(row => row.Id == key.Id && row.OwnerId == key.OwnerId
                && row.ExternalApiKeyStatusId != (int)ExternalApiKeyStatusEnum.Revoked)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.ExternalApiKeyStatusId, (int)ExternalApiKeyStatusEnum.Revoked)
                .SetProperty(row => row.UpdatedAt, changedAt).SetProperty(row => row.UpdatedBy, actorId), token);
        return true;
    }

    public async Task<bool> IsAuthorizedForCommitAsync(CancellationToken token)
    {
        RequireRole(PrimaryDatabaseRole.InstanceKeyIssuance);
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
            return await ScalarAsync(PostgresInstanceApiKeyIssuanceContract.AuthorityFunction,
                "@actor, @provider, @account, @operation", Parameters(OperationParameter()), token);
        await HoldPortableOwnerAsync(token);
        return await _portableAuthority.IsInstanceAdministratorForCommitAsync(actorId, token);
    }

    public void RequireCleanWriteScope(Guid? tenantId)
    {
        if (role != PrimaryDatabaseRole.InstanceKeyIssuance || tenantId is not null
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("instance_api_key_clean_issuance_scope_required");
    }

    public async Task<bool> ExistsByOwnerAndNameAsync(
        ExternalApiKeyOwnerType ownerType, Guid ownerId, string name, CancellationToken token)
    {
        RequireRole(PrimaryDatabaseRole.InstanceKeyIssuance);
        if (ownerType != ExternalApiKeyOwnerType.InstanceAdmin || ownerId != actorId)
            throw new InvalidOperationException("instance_api_key_self_ownership_required");
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
            return await ScalarAsync(PostgresInstanceApiKeyIssuanceContract.NameFunction,
                "@actor, @provider, @account, @operation, @name",
                Parameters(OperationParameter(), new NpgsqlParameter("name", name)), token);
        if (!await IsAuthorizedForCommitAsync(token))
            throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.Update);
        return await PlatformKeys().AsNoTracking().AnyAsync(key => key.OwnerId == actorId && key.Name == name, token);
    }

    private string Fingerprint => ExternalApiKeyIssuanceReceipt.ComputeOperationFingerprint(
        actorId, null, ExternalApiKeyOwnerType.InstanceAdmin, actorId, operationKey!);

    public async Task<ExternalApiKeyIssuanceReceipt?> FindAsync(
        string operationFingerprint, Guid? tenantId, CancellationToken token)
    {
        RequireRole(PrimaryDatabaseRole.InstanceKeyIssuance);
        if (tenantId is not null || operationFingerprint != Fingerprint)
            throw new InvalidOperationException("instance_api_key_receipt_scope_mismatch");
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
        {
            try
            {
                return await native.ReceiptAsync(Parameters(OperationParameter()), token);
            }
            catch (PostgresException exception) when (exception.SqlState == "42501")
            {
                throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.Update);
            }
        }
        if (!await IsAuthorizedForCommitAsync(token))
            throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.Update);
        return await context.ExternalApiKeyIssuanceReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyPlatformManagement).AsNoTracking()
            .SingleOrDefaultAsync(receipt => receipt.TenantId == null && receipt.OperationFingerprint == Fingerprint, token);
    }

    public async Task<ExternalApiKey?> GetIssuedKeyAsync(
        Guid keyId, Guid? tenantId, ExternalApiKeyOwnerType ownerType, Guid ownerId, CancellationToken token)
    {
        RequireRole(PrimaryDatabaseRole.InstanceKeyIssuance);
        if (tenantId is not null || ownerType != ExternalApiKeyOwnerType.InstanceAdmin || ownerId != actorId)
            throw new InvalidOperationException("instance_api_key_self_ownership_required");
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
        {
            try
            {
                return await native.RecoverAsync(Parameters(OperationParameter(), new NpgsqlParameter("key", keyId)), token);
            }
            catch (PostgresException exception) when (exception.SqlState == "42501")
            {
                throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.Update);
            }
        }
        var receipt = await FindAsync(Fingerprint, null, token);
        if (receipt?.ExternalApiKeyId != keyId)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ExternalApiKey>(context, keyId, token);
        var key = await Metadata(PlatformKeys().Where(row => row.Id == keyId && row.OwnerId == actorId))
            .SingleOrDefaultAsync(token);
        if (key is null)
            return null;
        await RelationalEntityRowFence.AcquireLookupAsync<ExternalApiKeyStatus>(
            context, [key.ExternalApiKeyStatusId], token);
        bool usable = await context.ExternalApiKeyStatuses.AsNoTracking().AnyAsync(
            status => status.Id == key.ExternalApiKeyStatusId && status.IsUsable, token);
        return usable && (key.ExpiresAt is null || key.ExpiresAt > DateTime.UtcNow) ? key : null;
    }

    public async Task CreateAsync(ExternalApiKeyIssuanceReceipt receipt, ExternalApiKey key, CancellationToken token)
    {
        RequireRole(PrimaryDatabaseRole.InstanceKeyIssuance);
        if (key.TenantId is not null || key.OwnerType != ExternalApiKeyOwnerType.InstanceAdmin || key.OwnerId != actorId
            || receipt.TenantId is not null || receipt.OperationFingerprint != Fingerprint
            || receipt.ExternalApiKeyId != key.Id || receipt.CreatedAtUtc != key.CreatedAt)
            throw new InvalidOperationException("instance_api_key_issuance_scope_mismatch");
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
        {
            string payload = JsonSerializer.Serialize(new
            {
                key.Id,
                key.Name,
                key.Description,
                key.KeyId,
                key.SecretHash,
                key.Scopes,
                CreditPeriodId = key.ExternalApiKeyCreditPeriodId,
                key.CreditLimit,
                key.MaxRolloverCredits,
                key.ExpiresAt
            });
            _ = await ScalarAsync(PostgresInstanceApiKeyIssuanceContract.CreateFunction,
                "@actor, @provider, @account, @operation, @payload, @receipt, @digest, @created",
                Parameters(OperationParameter(), new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload },
                    new NpgsqlParameter("receipt", receipt.Id), new NpgsqlParameter("digest", receipt.InputDigest),
                    new NpgsqlParameter("created", receipt.CreatedAtUtc)), token);
            return;
        }
        if (!await IsAuthorizedForCommitAsync(token))
            throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.Update);
        context.ExternalApiKeys.Add(key);
        context.ExternalApiKeyIssuanceReceipts.Add(receipt);
        await context.SaveChangesAsync(token);
    }

    public async Task<IReadOnlyList<TenantApiKeyUsageSummary>> ReportAsync(
        DateOnly from, DateOnly to, CancellationToken token)
    {
        RequireRole(PrimaryDatabaseRole.PlatformKeyReport);
        if (from > to)
            throw new ArgumentException("The reporting range is invalid.");
        if (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
        {
            try
            {
                return await native.ReportAsync(Parameters(new NpgsqlParameter("from", from), new NpgsqlParameter("to", to)), token);
            }
            catch (PostgresException exception) when (exception.SqlState == "42501")
            {
                throw new AuthorizationException(ResourceKinds.InstanceSetting, AuthorizationActions.View);
            }
        }
        await DemandPortableActorAsync(token);
        return await new ExternalApiKeyQuotaRepository(context).GetUsagePlatformWide(from, to, token);
    }
}
