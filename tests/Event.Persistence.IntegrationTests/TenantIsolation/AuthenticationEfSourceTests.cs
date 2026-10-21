using System.Security.Cryptography;
using System.Diagnostics;
using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.Sqlite;
using Explore.Persistence.Seed;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

/// <summary>Uses only the actual enrolled authentication owner after retiring its old business routines.</summary>
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class AuthenticationEfSourceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task OrdinaryTenantKeyStorageAndAuthenticationCoexistWithoutRuntimePrivateReads()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        Rows rows = await SeedAsync(token);
        await RetireBusinessRoutinesAsync(token);
        var authentication = new ExternalApiKeyAuthenticationRepository(GlobalFactory(), fixture.NativeQueueEnrollment);
        await RequireAuthenticationReadyAsync();

        foreach (ExternalApiKeyOwnerType ownerType in new[] { ExternalApiKeyOwnerType.Tenant, ExternalApiKeyOwnerType.User })
        {
            Guid ownerId = ownerType == ExternalApiKeyOwnerType.User ? rows.UserId : rows.A;
            ExternalApiKey issued = Key(rows.A);
            issued.OwnerType = ownerType;
            issued.OwnerId = ownerId;
            issued.Name = $"Runtime issuance {issued.Id:N}";
            issued.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active;
            issued.CreatedBy = rows.UserId;
            string fingerprint = ExternalApiKeyIssuanceReceipt.ComputeOperationFingerprint(
                rows.UserId, rows.A, ownerType, ownerId, Guid.CreateVersion7().ToString("N"));
            string inputDigest = Convert.ToHexStringLower(SHA256.HashData(issued.Id.ToByteArray()));
            ExternalApiKeyIssuanceReceipt receipt = ExternalApiKeyIssuanceReceipt.Create(
                Guid.CreateVersion7(), fingerprint, inputDigest, rows.A, issued.Id, Now);

            await using (var context = Runtime(rows.A))
            {
                await using var gate = new HumanSecuritySourceGate(context);
                await using var retained = await gate.AcquireAsync(token);
                var receipts = new ExternalApiKeyIssuanceReceiptRepository(context);
                receipts.RequireCleanWriteScope(rows.A);
                await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async attemptToken =>
                {
                    var originalTransaction = context.Database.CurrentTransaction!.GetDbTransaction();
                    await Assert.That(originalTransaction.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
                    await Assert.That(await receipts.FindAsync(fingerprint, rows.A, attemptToken)).IsNull();
                    await Assert.That(await receipts.ExistsByOwnerAndNameAsync(ownerType, ownerId, issued.Name, attemptToken)).IsFalse();
                    await receipts.CreateAsync(receipt, issued, attemptToken);
                    await Assert.That(ReferenceEquals(originalTransaction,
                        context.Database.CurrentTransaction!.GetDbTransaction())).IsTrue();
                    gate.RequireCurrent();
                    return true;
                }, token);
            }

            await RequireAuthenticationReadyAsync();
            ExternalApiKey? authenticatedSource = await authentication.GetByKeyIdForAuthentication(issued.KeyId, token);
            await Assert.That(authenticatedSource?.Id).IsEqualTo(issued.Id);
            await Assert.That(authenticatedSource!.SecretHash == issued.SecretHash).IsTrue();

            // A fresh original transaction recovers metadata, never the already disclosed secret.
            await using (var context = Runtime(rows.A))
            {
                await using var gate = new HumanSecuritySourceGate(context);
                await using var retained = await gate.AcquireAsync(token);
                var receipts = new ExternalApiKeyIssuanceReceiptRepository(context);
                var keys = new ExternalApiKeyRepository(context);
                receipts.RequireCleanWriteScope(rows.A);
                await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async attemptToken =>
                {
                    var originalTransaction = context.Database.CurrentTransaction!.GetDbTransaction();
                    await Assert.That(originalTransaction.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
                    ExternalApiKeyIssuanceReceipt? replay = await receipts.FindAsync(fingerprint, rows.A, attemptToken);
                    await Assert.That(replay?.Id).IsEqualTo(receipt.Id);
                    await Assert.That(replay!.InputDigest).IsEqualTo(inputDigest);
                    ExternalApiKey? recovered = await receipts.GetIssuedKeyAsync(issued.Id, rows.A, ownerType, ownerId, attemptToken);
                    await Assert.That(recovered?.Id).IsEqualTo(issued.Id);
                    await Assert.That(recovered!.SecretHash).IsEqualTo(string.Empty);
                    await Assert.That(recovered.LastUsedIp).IsNull();
                    List<ExternalApiKey> listed = await keys.GetByOwner(ownerType, ownerId, attemptToken);
                    ExternalApiKey listedKey = listed.Single(key => key.Id == issued.Id);
                    await Assert.That(listedKey.SecretHash).IsEqualTo(string.Empty);
                    await Assert.That(listedKey.LastUsedIp).IsNull();
                    await Assert.That((await keys.GetByOwners(ownerType, [ownerId], attemptToken))
                        .Any(key => key.Id == issued.Id)).IsTrue();
                    ExternalApiKey? details = await keys.GetForTenantAsync(issued.Id, attemptToken);
                    await Assert.That(details?.Id).IsEqualTo(issued.Id);
                    await Assert.That(details!.SecretHash).IsEqualTo(string.Empty);
                    await Assert.That(details.LastUsedIp).IsNull();
                    await Assert.That(await context.ExternalApiKeys.IgnoreQueryFilters()
                        .AnyAsync(key => key.Id == rows.PlatformKey.Id || key.Id == rows.KeyB.Id, attemptToken)).IsFalse();

                    Guid[] parentIds = await context.Tenants.Where(row => row.Id == rows.A || row.Id == rows.B)
                        .Select(row => row.Id).ToArrayAsync(attemptToken);
                    await Assert.That(parentIds.Length).IsEqualTo(2);
                    await Assert.That(parentIds.Contains(rows.A) && parentIds.Contains(rows.B)).IsTrue();
                    foreach (bool tenantDescription in new[] { true, false })
                    {
                        await context.Database.CurrentTransaction!.CreateSavepointAsync("runtime_parent_private_denial", attemptToken);
                        try
                        {
                            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                            {
                                if (tenantDescription)
                                    await context.Tenants.Where(row => row.Id == rows.A).Select(row => row.FullName)
                                        .SingleOrDefaultAsync(attemptToken);
                                else
                                    await context.Users.Where(row => row.Id == rows.UserId).Select(row => row.Pii.Email)
                                        .SingleOrDefaultAsync(attemptToken);
                            });
                            await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                        }
                        finally
                        {
                            await context.Database.CurrentTransaction!.RollbackToSavepointAsync("runtime_parent_private_denial", attemptToken);
                            await context.Database.CurrentTransaction!.ReleaseSavepointAsync("runtime_parent_private_denial", attemptToken);
                        }
                    }

                    foreach (string reference in new[] { "credit", "owner", "status" })
                    {
                        IQueryable<string> text = reference switch
                        {
                            "credit" => context.Set<ExternalApiKeyCreditPeriod>()
                                .Where(row => row.Id == issued.ExternalApiKeyCreditPeriodId).Select(row => row.MasterCode),
                            "owner" => context.Set<ExternalApiKeyOwnerTypeLookup>()
                                .Where(row => row.Id == issued.ExternalApiKeyOwnerTypeId).Select(row => row.MasterCode),
                            _ => context.Set<ExternalApiKeyStatus>()
                                .Where(row => row.Id == issued.ExternalApiKeyStatusId).Select(row => row.MasterCode)
                        };
                        await context.Database.CurrentTransaction!.CreateSavepointAsync("runtime_lookup_text_denial", attemptToken);
                        try
                        {
                            var denied = await Assert.ThrowsAsync<PostgresException>(() => text.SingleOrDefaultAsync(attemptToken));
                            await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                        }
                        finally
                        {
                            await context.Database.CurrentTransaction!.RollbackToSavepointAsync("runtime_lookup_text_denial", attemptToken);
                            await context.Database.CurrentTransaction!.ReleaseSavepointAsync("runtime_lookup_text_denial", attemptToken);
                        }
                    }

                    foreach (bool readHash in new[] { true, false })
                    {
                        await context.Database.CurrentTransaction!.CreateSavepointAsync("runtime_credential_read_denial", attemptToken);
                        try
                        {
                            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                            {
                                if (readHash)
                                    await context.ExternalApiKeys.Where(key => key.Id == issued.Id)
                                        .Select(key => key.SecretHash).SingleOrDefaultAsync(attemptToken);
                                else
                                    await context.ExternalApiKeys.Where(key => key.Id == issued.Id)
                                        .Select(key => key.LastUsedIp).SingleOrDefaultAsync(attemptToken);
                            });
                            await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                        }
                        finally
                        {
                            await context.Database.CurrentTransaction!.RollbackToSavepointAsync("runtime_credential_read_denial", attemptToken);
                            await context.Database.CurrentTransaction!.ReleaseSavepointAsync("runtime_credential_read_denial", attemptToken);
                        }
                    }

                    ExternalApiKey managed = (await keys.GetForManagementAsync(issued.Id, attemptToken))!;
                    managed.Name += " updated";
                    managed.Scopes = "events:read events:write";
                    managed.ExpiresAt = Now.AddYears(2);
                    managed.UpdatedAt = Now;
                    managed.UpdatedBy = rows.UserId;
                    await keys.Update(managed, attemptToken);
                    ExternalApiKey updated = (await keys.GetForTenantAsync(issued.Id, attemptToken))!;
                    await Assert.That(updated.Name).IsEqualTo(managed.Name);
                    await Assert.That(updated.Scopes).IsEqualTo(managed.Scopes);
                    await Assert.That(updated.ExpiresAt).IsEqualTo(managed.ExpiresAt);
                    updated.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Revoked;
                    await keys.Update(updated, attemptToken);
                    await Assert.That((await keys.GetForTenantAsync(issued.Id, attemptToken))!.ExternalApiKeyStatusId)
                        .IsEqualTo((int)ExternalApiKeyStatusEnum.Revoked);
                    await Assert.That(ReferenceEquals(originalTransaction,
                        context.Database.CurrentTransaction!.GetDbTransaction())).IsTrue();
                    gate.RequireCurrent();
                    return true;
                }, token);
            }

            await RequireAuthenticationReadyAsync();
            ExternalApiKey failed = Key(rows.A);
            failed.Name = $"Rolled back runtime issuance {failed.Id:N}";
            ExternalApiKeyIssuanceReceipt failedReceipt = ExternalApiKeyIssuanceReceipt.Create(
                Guid.CreateVersion7(), Convert.ToHexStringLower(SHA256.HashData(failed.Id.ToByteArray())),
                inputDigest, rows.A, failed.Id, Now);
            var expectedFailure = new InvalidOperationException("runtime_key_creation_late_failure");
            bool savedBeforeFailure = false;
            await using (var context = Runtime(rows.A))
            {
                await using var gate = new HumanSecuritySourceGate(context);
                await using var retained = await gate.AcquireAsync(token);
                var receipts = new ExternalApiKeyIssuanceReceiptRepository(context);
                receipts.RequireCleanWriteScope(rows.A);
                var actualFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new EfCoreUnitOfWork(context).ExecuteSerializableAsync<bool>(async attemptToken =>
                    {
                        await receipts.CreateAsync(failedReceipt, failed, attemptToken);
                        savedBeforeFailure = true;
                        throw expectedFailure;
                    }, token));
                await Assert.That(ReferenceEquals(actualFailure, expectedFailure)).IsTrue();
            }
            await Assert.That(savedBeforeFailure).IsTrue();
            await using var observer = Migrator();
            ExternalApiKey actual = await observer.ExternalApiKeys
                .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
                .SingleAsync(key => key.Id == issued.Id, token);
            await Assert.That(actual.SecretHash == issued.SecretHash).IsTrue();
            await Assert.That(actual.CreatedAt).IsEqualTo(issued.CreatedAt);
            await Assert.That(actual.CreatedBy).IsEqualTo(issued.CreatedBy);
            await Assert.That(actual.TenantId).IsEqualTo(rows.A);
            await Assert.That(actual.OwnerId).IsEqualTo(ownerId);
            await Assert.That(actual.OwnerType).IsEqualTo(ownerType);
            await Assert.That(actual.Name).IsEqualTo(issued.Name + " updated");
            await Assert.That(actual.Scopes).IsEqualTo("events:read events:write");
            await Assert.That(actual.ExpiresAt).IsEqualTo(Now.AddYears(2));
            await Assert.That(actual.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Revoked);
            await Assert.That(await observer.ExternalApiKeyIssuanceReceipts.IgnoreQueryFilters()
                .CountAsync(row => row.OperationFingerprint == fingerprint, token)).IsEqualTo(1);
            await Assert.That(await observer.ExternalApiKeys.IgnoreQueryFilters()
                .AnyAsync(key => key.Id == failed.Id, token)).IsFalse();
            await Assert.That(await observer.ExternalApiKeyIssuanceReceipts.IgnoreQueryFilters()
                .AnyAsync(row => row.Id == failedReceipt.Id, token)).IsFalse();
            await RequireAuthenticationReadyAsync();
        }

        async Task RequireAuthenticationReadyAsync()
        {
            await using var selected = await GlobalFactory().CreateDbContextAsync(token);
            await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(selected, fixture.NativeQueueEnrollment, token);
        }
    }

    [Test]
    public async Task ApiKeyAuthenticationAndAtomicUsageRemainExactWithoutDatabaseBusinessRoutines()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        Rows rows = await SeedAsync(token);
        await RetireBusinessRoutinesAsync(token);
        var factory = GlobalFactory();
        var repository = new ExternalApiKeyAuthenticationRepository(factory, fixture.NativeQueueEnrollment);
        await using var request = Runtime(rows.B);
        await request.Database.OpenConnectionAsync(token);

        foreach (ExternalApiKey expected in new[] { rows.KeyA, rows.KeyB, rows.PlatformKey })
        {
            ExternalApiKey? found = await repository.GetByKeyIdForAuthentication(expected.KeyId, token);
            await Assert.That(found?.Id).IsEqualTo(expected.Id);
            await Assert.That(found!.TenantId).IsEqualTo(expected.TenantId);
            await Assert.That(found.SecretHash == expected.SecretHash).IsTrue();
            await Assert.That(found.Scopes).IsEqualTo(expected.Scopes);
            await Assert.That(found.OwnerId).IsEqualTo(expected.OwnerId);
        }
        await Assert.That(await repository.GetByKeyIdForAuthentication(ApiKeyHashing.CreateKeyId(), token)).IsNull();
        await Assert.That(await request.ExternalApiKeys.IgnoreQueryFilters()
            .AnyAsync(row => row.Id == rows.KeyA.Id || row.Id == rows.PlatformKey.Id, token)).IsFalse();
        var privateRead = await Assert.ThrowsAsync<PostgresException>(async () =>
            await request.ExternalApiKeys.IgnoreQueryFilters().Where(row => row.Id == rows.KeyA.Id)
                .Select(row => row.SecretHash).SingleOrDefaultAsync(token));
        await Assert.That(privateRead.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        var privateWrite = await Assert.ThrowsAsync<PostgresException>(async () =>
            await request.ExternalApiKeys.IgnoreQueryFilters().Where(row => row.Id == rows.KeyA.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastUsedAt, Now), token));
        await Assert.That(privateWrite.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(rows.B);

        TimeSpan interval = TimeSpan.FromMinutes(5);
        await Assert.That(await repository.TouchUsageMetadata(rows.KeyA.Id, Now, "192.0.2.1", interval, token)).IsTrue();
        await Assert.That(await repository.TouchUsageMetadata(rows.KeyA.Id, Now.AddMinutes(5), "192.0.2.2", interval, token)).IsFalse();
        // Two actual repository calls use independent Global contexts. Only one may win
        // the strict compare-and-set when both carry the same next usage instant.
        bool[] contenders = await Task.WhenAll(
            repository.TouchUsageMetadata(rows.KeyA.Id, Now.AddMinutes(5).AddSeconds(1), "192.0.2.3", interval, token),
            repository.TouchUsageMetadata(rows.KeyA.Id, Now.AddMinutes(5).AddSeconds(1), "192.0.2.4", interval, token));
        await Assert.That(contenders.Count(won => won)).IsEqualTo(1);
        await using var observer = Migrator();
        ExternalApiKey actual = await observer.ExternalApiKeys
            .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
            .AsNoTracking().SingleAsync(row => row.Id == rows.KeyA.Id, token);
        await Assert.That(actual.LastUsedAt).IsEqualTo(Now.AddMinutes(5).AddSeconds(1));
        await Assert.That(actual.LastUsedIp is "192.0.2.3" or "192.0.2.4").IsTrue();
        await Assert.That(Equals(ImmutableKeyImage(actual), ImmutableKeyImage(rows.KeyA))).IsTrue();
        foreach (ExternalApiKey untouched in new[] { rows.KeyB, rows.PlatformKey })
        {
            ExternalApiKey persisted = await observer.ExternalApiKeys
                .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
                .AsNoTracking().SingleAsync(row => row.Id == untouched.Id, token);
            await Assert.That(persisted.LastUsedAt).IsNull();
            await Assert.That(persisted.LastUsedIp).IsNull();
            await Assert.That(Equals(ImmutableKeyImage(persisted), ImmutableKeyImage(untouched))).IsTrue();
        }
    }

    [Test]
    [Arguments("owner")]
    [Arguments("runtime")]
    [Arguments("purpose")]
    [Arguments("public")]
    public async Task TenantParentStorageCoexistsOnlyWithUnrelatedDormantOwnerPolicies(string audience)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        Rows rows = await SeedAsync(token);
        await RetireBusinessRoutinesAsync(token);
        var authentication = new ExternalApiKeyAuthenticationRepository(GlobalFactory(), fixture.NativeQueueEnrollment);
        await Assert.That((await authentication.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token))?.Id).IsEqualTo(rows.KeyA.Id);
        await using var migrator = Migrator();
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(Tenant));
        string runtime = fixture.NativeQueueEnrollment.RuntimeLogin;
        await Assert.That(await migrator.Database.SqlQuery<bool>($"""
            SELECT NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid)
                AND NOT pg_catalog.pg_has_role({runtime},{PostgresTenantLookupContract.OwnerRole},'MEMBER') AS "Value"
            FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({table})
            """).SingleAsync(token)).IsTrue();
        string role = audience switch
        {
            "owner" => names.Quote(PostgresTenantLookupContract.OwnerRole),
            "runtime" => names.Quote(runtime),
            "purpose" => names.Quote(PostgresEventCreationPublisherReadContract.RuntimeRole),
            _ => "PUBLIC"
        };
        if (audience == "purpose")
            await Assert.That(await migrator.Database.SqlQuery<bool>($"""
                SELECT pg_catalog.pg_has_role({runtime},{PostgresEventCreationPublisherReadContract.RuntimeRole},'MEMBER') AS "Value"
                """).SingleAsync(token)).IsTrue();
        string policyName = "parent_policy_" + Guid.CreateVersion7().ToString("N");
        string quotedPolicy = names.Quote(policyName);
        await migrator.Database.ExecuteSqlRawAsync($"CREATE POLICY {quotedPolicy} ON {table} FOR SELECT TO {role} USING (true)", token);
        try
        {
            if (audience == "owner")
            {
                await PostgresTenantApiKeyStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
                await Assert.That((await authentication.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token))?.Id).IsEqualTo(rows.KeyA.Id);
            }
            else
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresTenantApiKeyStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                    await authentication.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token));
            }
            await Assert.That(await migrator.Database.SqlQuery<bool>($"""
                SELECT NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                    AND EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid AND p.polname={policyName}) AS "Value"
                FROM pg_catalog.pg_class c WHERE c.oid=pg_catalog.to_regclass({table})
                """).SingleAsync(token)).IsTrue();
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync($"DROP POLICY {quotedPolicy} ON {table}", CancellationToken.None);
        }
        await Assert.That((await authentication.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token))?.Id).IsEqualTo(rows.KeyA.Id);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReferenceLookupDriftCannotBeGrantedToRuntimeOrRepairedByInstallation(bool extraColumn)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        Rows rows = await SeedAsync(token);
        await RetireBusinessRoutinesAsync(token);
        var authentication = new ExternalApiKeyAuthenticationRepository(GlobalFactory(), fixture.NativeQueueEnrollment);
        await Assert.That((await authentication.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token))?.Id).IsEqualTo(rows.KeyA.Id);
        await using var migrator = Migrator();
        await using var runtime = Runtime(rows.A);
        await Assert.That(await runtime.Set<ExternalApiKeyStatus>()
            .AnyAsync(row => row.Id == rows.KeyA.ExternalApiKeyStatusId, token)).IsTrue();
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(ExternalApiKeyStatus));
        string column = extraColumn ? "unexpected_reference_" + Guid.CreateVersion7().ToString("N")
            : migrator.Model.FindEntityType(typeof(ExternalApiKeyStatus))!
                .FindProperty(nameof(ExternalApiKeyStatus.MasterCode))!.GetColumnName()!;
        string quotedColumn = names.Quote(column);
        string login = fixture.NativeQueueEnrollment.RuntimeLogin;
        if (extraColumn)
            await migrator.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {quotedColumn} text", token);
        bool ownsGrant = false;
        try
        {
            await Assert.That(await migrator.Database.SqlQuery<bool>($"SELECT pg_catalog.has_column_privilege({login},{table},{column},'SELECT') AS \"Value\"")
                .SingleAsync(token)).IsFalse();
            await migrator.Database.ExecuteSqlRawAsync($"GRANT SELECT ({quotedColumn}) ON {table} TO {names.Quote(login)}", token);
            ownsGrant = true;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await authentication.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantApiKeyStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
            await Assert.That(await migrator.Database.SqlQuery<bool>($"SELECT pg_catalog.has_column_privilege({login},{table},{column},'SELECT') AS \"Value\"")
                .SingleAsync(token)).IsTrue();
        }
        finally
        {
            if (ownsGrant)
                await migrator.Database.ExecuteSqlRawAsync($"REVOKE SELECT ({quotedColumn}) ON {table} FROM {names.Quote(login)}", CancellationToken.None);
            if (extraColumn)
                await migrator.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} DROP COLUMN {quotedColumn}", CancellationToken.None);
        }
        await Assert.That((await authentication.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token))?.Id).IsEqualTo(rows.KeyA.Id);
    }

    [Test]
    public async Task UnknownKeyStorageColumnCannotBeGrantedToRuntimeOrRepairedByInstallation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        Rows rows = await SeedAsync(token);
        await RetireBusinessRoutinesAsync(token);
        var authentication = new ExternalApiKeyAuthenticationRepository(GlobalFactory(), fixture.NativeQueueEnrollment);
        await Assert.That((await authentication.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token))?.Id).IsEqualTo(rows.KeyA.Id);
        await using var migrator = Migrator();
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(ExternalApiKey));
        string column = "unexpected_key_" + Guid.CreateVersion7().ToString("N");
        string quotedColumn = names.Quote(column);
        string runtime = fixture.NativeQueueEnrollment.RuntimeLogin;
        await migrator.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {quotedColumn} text", token);
        try
        {
            await migrator.Database.ExecuteSqlRawAsync($"GRANT SELECT ({quotedColumn}) ON {table} TO {names.Quote(runtime)}", token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await authentication.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantApiKeyStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
            await Assert.That(await migrator.Database.SqlQuery<bool>($"SELECT pg_catalog.has_column_privilege({runtime},{table},{column},'SELECT') AS \"Value\"")
                .SingleAsync(token)).IsTrue();
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync($"REVOKE SELECT ({quotedColumn}) ON {table} FROM {names.Quote(runtime)}", CancellationToken.None);
            await migrator.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} DROP COLUMN {quotedColumn}", CancellationToken.None);
        }
        await Assert.That((await authentication.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token))?.Id).IsEqualTo(rows.KeyA.Id);
    }

    [Test]
    public async Task ScannerAuthenticationRetainsPairedCandidatesAndRejectsCrossTenantAmbiguityWithoutBusinessRoutines()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        Rows rows = await SeedAsync(token);
        await RetireBusinessRoutinesAsync(token);
        var repository = new AdmissionScannerAuthenticationRepository(GlobalFactory(), fixture.NativeQueueEnrollment);
        await using var request = Runtime(rows.B);
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await request.AdmissionScannerCapabilities.IgnoreQueryFilters().AnyAsync(row => row.Id == rows.ScannerA.Id, token));
        await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        AdmissionScannerCapability? found = await repository.FindByDigestCandidatesAsync(
            [Candidate(rows.ScannerA)], token);
        await Assert.That(found?.Id).IsEqualTo(rows.ScannerA.Id);
        await Assert.That(found!.TenantId).IsEqualTo(rows.A);
        await Assert.That(found.AdmissionTargetId).IsEqualTo(rows.ScannerA.AdmissionTargetId);
        await Assert.That(found.LookupDigest == rows.ScannerA.LookupDigest).IsTrue();
        await Assert.That(found.Actions).IsEqualTo(rows.ScannerA.Actions);
        await Assert.That(await repository.FindByDigestCandidatesAsync(
            [new(rows.ScannerA.LookupKeyVersion, rows.ScannerB.LookupDigest),
             new(rows.ScannerB.LookupKeyVersion, rows.ScannerA.LookupDigest)], token)).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await repository.FindByDigestCandidatesAsync([Candidate(rows.ScannerA), Candidate(rows.ScannerB)], token));
        await Assert.That(await repository.FindByDigestCandidatesAsync([], token)).IsNull();
        await Assert.That(await repository.FindByDigestCandidatesAsync([new(0, rows.ScannerA.LookupDigest)], token)).IsNull();
        await Assert.That(await repository.FindByDigestCandidatesAsync([Candidate(rows.ScannerA), Candidate(rows.ScannerA)], token)).IsNull();
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(rows.B);
    }

    [Test]
    public async Task AuthenticationProfilesInstallOnlyFixedColumnsAndRefusePrivilegeDriftWithoutRepair()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        Rows rows = await SeedAsync(token);
        await RetireBusinessRoutinesAsync(token);
        await using var migrator = Migrator();
        await migrator.Database.OpenConnectionAsync(token);
        await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
        await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
        foreach (string signature in Signatures(migrator))
            await Assert.That(await migrator.Database.SqlQuery<bool>($"SELECT pg_catalog.to_regprocedure({signature}) IS NULL AS \"Value\"")
                .SingleAsync(token)).IsTrue();

        var names = new HumanPrincipalSourceSql(migrator);
        string keyTable = names.Table(typeof(ExternalApiKey));
        string scopes = names.Column(typeof(ExternalApiKey), nameof(ExternalApiKey.Scopes));
        string keyId = names.Column(typeof(ExternalApiKey), nameof(ExternalApiKey.KeyId));
        string target = names.Quote(PostgresExternalApiKeyAuthenticationContract.GlobalRole);
        string global = fixture.NativeQueueEnrollment.GlobalLogin;
        await Assert.That(await migrator.Database.SqlQuery<bool>($"SELECT pg_catalog.has_table_privilege({global}, {keyTable}, 'SELECT') AS \"Value\"")
            .SingleAsync(token)).IsFalse();
        await Assert.That(await migrator.Database.SqlQuery<bool>($"SELECT pg_catalog.has_column_privilege({global}, {keyTable}, {names.ColumnName(typeof(ExternalApiKey), nameof(ExternalApiKey.KeyId))}, 'SELECT') AS \"Value\"")
            .SingleAsync(token)).IsTrue();
        var repository = new ExternalApiKeyAuthenticationRepository(GlobalFactory(), fixture.NativeQueueEnrollment);
        await Assert.That((await repository.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token))?.Id).IsEqualTo(rows.KeyA.Id);

        await AssertDriftAsync($"REVOKE SELECT ({keyId}) ON {keyTable} FROM {target}",
            $"GRANT SELECT ({keyId}) ON {keyTable} TO {target}");
        await AssertDriftAsync($"GRANT UPDATE ({scopes}) ON {keyTable} TO {target}",
            $"REVOKE UPDATE ({scopes}) ON {keyTable} FROM {target}");

        string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        await migrator.Database.ExecuteSqlRawAsync($"GRANT {target} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE", token);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await repository.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresExternalApiKeyAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
            string runtimeLogin = fixture.NativeQueueEnrollment.RuntimeLogin;
            string authenticationRole = PostgresExternalApiKeyAuthenticationContract.GlobalRole;
            await Assert.That(await migrator.Database.SqlQuery<bool>($"SELECT pg_catalog.pg_has_role({runtimeLogin}, {authenticationRole}, 'MEMBER') AS \"Value\"")
                .SingleAsync(token)).IsTrue();
        }
        finally { await migrator.Database.ExecuteSqlRawAsync($"REVOKE {target} FROM {runtime}", CancellationToken.None); }
        await Assert.That((await repository.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token))?.Id).IsEqualTo(rows.KeyA.Id);

        string scanner = names.Table(typeof(AdmissionScannerCapability));
        string digest = names.Column(typeof(AdmissionScannerCapability), nameof(AdmissionScannerCapability.LookupDigest));
        string scannerRole = names.Quote(PostgresAdmissionScannerAuthenticationContract.GlobalRole);
        var scanners = new AdmissionScannerAuthenticationRepository(GlobalFactory(), fixture.NativeQueueEnrollment);
        await Assert.That((await scanners.FindByDigestCandidatesAsync([Candidate(rows.ScannerA)], token))?.Id).IsEqualTo(rows.ScannerA.Id);
        await migrator.Database.ExecuteSqlRawAsync($"REVOKE SELECT ({digest}) ON {scanner} FROM {scannerRole}", token);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await scanners.FindByDigestCandidatesAsync([Candidate(rows.ScannerA)], token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
            await Assert.That(await migrator.Database.SqlQuery<bool>($"SELECT pg_catalog.has_column_privilege({global}, {scanner}, {names.ColumnName(typeof(AdmissionScannerCapability), nameof(AdmissionScannerCapability.LookupDigest))}, 'SELECT') AS \"Value\"")
                .SingleAsync(token)).IsFalse();
        }
        finally { await migrator.Database.ExecuteSqlRawAsync($"GRANT SELECT ({digest}) ON {scanner} TO {scannerRole}", CancellationToken.None); }
        await Assert.That((await scanners.FindByDigestCandidatesAsync([Candidate(rows.ScannerA)], token))?.Id).IsEqualTo(rows.ScannerA.Id);

        await using var selected = await GlobalFactory().CreateDbContextAsync(token);
        await selected.Database.OpenConnectionAsync(token);
        var privateWrite = await Assert.ThrowsAsync<PostgresException>(() => selected.Database.ExecuteSqlRawAsync(
            $"UPDATE {keyTable} SET {scopes}={scopes} WHERE false", token));
        await Assert.That(privateWrite.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        string label = names.Column(typeof(AdmissionScannerCapability), nameof(AdmissionScannerCapability.DeviceLabel));
        var scannerWrite = await Assert.ThrowsAsync<PostgresException>(() => selected.Database.ExecuteSqlRawAsync(
            $"UPDATE {scanner} SET {label}={label} WHERE false", token));
        await Assert.That(scannerWrite.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        string pii = names.Table(typeof(UserPii));
        var privateRead = await Assert.ThrowsAsync<PostgresException>(() => selected.Database.ExecuteSqlRawAsync(
            $"SELECT * FROM {pii} LIMIT 0", token));
        await Assert.That(privateRead.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);

        async Task AssertDriftAsync(string attack, string inverse)
        {
            await migrator.Database.ExecuteSqlRawAsync(attack, token);
            try
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                    await repository.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresExternalApiKeyAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
                // Installation must refuse, not silently repair the test-owned drift.
                bool widened = attack.StartsWith("GRANT", StringComparison.Ordinal);
                string column = names.ColumnName(typeof(ExternalApiKey), widened ? nameof(ExternalApiKey.Scopes) : nameof(ExternalApiKey.KeyId));
                string privilege = widened ? "UPDATE" : "SELECT";
                await Assert.That(await migrator.Database.SqlQuery<bool>($"SELECT pg_catalog.has_column_privilege({global}, {keyTable}, {column}, {privilege}) AS \"Value\"")
                    .SingleAsync(token)).IsEqualTo(widened);
            }
            finally { await migrator.Database.ExecuteSqlRawAsync(inverse, CancellationToken.None); }
            await Assert.That((await repository.GetByKeyIdForAuthentication(rows.KeyA.KeyId, token))?.Id).IsEqualTo(rows.KeyA.Id);
        }
    }

    [Test]
    public async Task RecomputedPolicyCommentCannotAuthorizeChangedReadOrUsageExpressions()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        await using var migrator = Migrator();
        await migrator.Database.OpenConnectionAsync(token);
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(typeof(ExternalApiKey));
        await using var selected = await GlobalFactory().CreateDbContextAsync(token);
        await selected.Database.OpenConnectionAsync(token);
        await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(selected, fixture.NativeQueueEnrollment, token);
        foreach (string policy in new[] { "api_key_authentication_read", "api_key_authentication_usage" })
        {
            string before = await ImageAsync(policy);
            string qualifier = await migrator.Database.SqlQuery<string>($"SELECT pg_catalog.pg_get_expr(polqual,polrelid) AS \"Value\" FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({table}) AND polname={policy}").SingleAsync(token);
            string check = await migrator.Database.SqlQuery<string>($"SELECT COALESCE(pg_catalog.pg_get_expr(polwithcheck,polrelid),'') AS \"Value\" FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({table}) AND polname={policy}").SingleAsync(token);
            string stamp = await migrator.Database.SqlQuery<string>($"SELECT pg_catalog.obj_description(oid,'pg_policy') AS \"Value\" FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({table}) AND polname={policy}").SingleAsync(token);
            string quotedPolicy = names.Quote(policy);
            if (check.Length == 0)
                await migrator.Database.ExecuteSqlRawAsync($"ALTER POLICY {quotedPolicy} ON {table} USING(true)", token);
            else
                await migrator.Database.ExecuteSqlRawAsync($"ALTER POLICY {quotedPolicy} ON {table} USING(true) WITH CHECK(true)", token);
            try
            {
                // A valid-looking stamp must not convert a different expression into source authority.
                await migrator.Database.ExecuteSqlRawAsync($"""
                    DO $auth_policy_test$
                    DECLARE changed_stamp text;
                    BEGIN
                        SELECT regexp_replace(pg_catalog.obj_description(oid,'pg_policy'),'[^:]*:[^:]*$','')
                            || pg_catalog.md5(polqual::text) || ':' || COALESCE(pg_catalog.md5(polwithcheck::text),'')
                        INTO changed_stamp FROM pg_catalog.pg_policy
                        WHERE polrelid=pg_catalog.to_regclass({SqlText(table)}) AND polname={SqlText(policy)};
                        EXECUTE pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',{SqlText(policy)},{SqlText(table)},changed_stamp);
                    END $auth_policy_test$;
                    """, token);
                string changed = await ImageAsync(policy);
                await Assert.That(changed == before).IsFalse();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(selected, fixture.NativeQueueEnrollment, token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresExternalApiKeyAuthenticationContract.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
                await using var newSelection = await GlobalFactory().CreateDbContextAsync(token);
                var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => newSelection.Database.OpenConnectionAsync(token));
                await Assert.That(refused.Message).IsEqualTo("postgres_tenant_runtime_authority_not_ready");
                await Assert.That(await ImageAsync(policy) == changed).IsTrue();
            }
            finally
            {
                if (check.Length == 0)
                    await migrator.Database.ExecuteSqlRawAsync($"ALTER POLICY {quotedPolicy} ON {table} USING({qualifier})", CancellationToken.None);
                else
                    await migrator.Database.ExecuteSqlRawAsync($"ALTER POLICY {quotedPolicy} ON {table} USING({qualifier}) WITH CHECK({check})", CancellationToken.None);
                await migrator.Database.ExecuteSqlRawAsync($"COMMENT ON POLICY {quotedPolicy} ON {table} IS {SqlText(stamp)}", CancellationToken.None);
            }
            await Assert.That(await ImageAsync(policy) == before).IsTrue();
            await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(selected, fixture.NativeQueueEnrollment, token);
        }

        Task<string> ImageAsync(string policy) => migrator.Database.SqlQuery<string>($"SELECT polqual::text || ':' || COALESCE(polwithcheck::text,'') || ':' || pg_catalog.obj_description(oid,'pg_policy') AS \"Value\" FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass({table}) AND polname={policy}").SingleAsync(token);
    }

    private static string SqlText(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal) + "'";

    [Test]
    public async Task LostOriginalConnectorAfterActualUsageUpdateCannotCommitMetadataOnAReplacement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        Rows rows = await SeedAsync(token);
        await using var selected = await GlobalFactory().CreateDbContextAsync(token);
        await selected.Database.OpenConnectionAsync(token);
        var original = RelationalConnectionIdentity.Capture(selected);
        var originalContext = selected.ContextId;
        int? backend = original.BackendProcessId;
        int updates = 0;
        bool exactOwner = false;
        using (var diagnostics = new UsageUpdateDiagnostics(selected, command =>
        {
            updates++;
            exactOwner = selected.ContextId == originalContext && original.Matches(selected)
                && original.BackendProcessId == backend
                && ReferenceEquals(command.Connection, selected.Database.GetDbConnection())
                && ReferenceEquals(command.Transaction, selected.Database.CurrentTransaction?.GetDbTransaction());
            command.Connection!.Close();
        }))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExternalApiKeyAuthenticationQuery.TouchUsageAsync(
                selected, rows.KeyA.Id, Now, "192.0.2.9", Now.AddMinutes(-5), token, fixture.NativeQueueEnrollment));
        }
        await Assert.That(updates).IsEqualTo(1);
        await Assert.That(exactOwner).IsTrue();
        await Assert.That(original.State).IsEqualTo(ConnectionState.Closed);
        await using var independent = Migrator();
        ExternalApiKey persisted = await independent.ExternalApiKeys
            .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
            .AsNoTracking().SingleAsync(row => row.Id == rows.KeyA.Id, token);
        await Assert.That(persisted.LastUsedAt).IsNull();
        await Assert.That(persisted.LastUsedIp).IsNull();
        await Assert.That(Equals(ImmutableKeyImage(persisted), ImmutableKeyImage(rows.KeyA))).IsTrue();
    }

    [Test]
    public async Task UsageUpdateBorrowsTheOriginalTransactionAndRollsBackWithItsCaller()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        Rows rows = await SeedAsync(token);
        await using var selected = await GlobalFactory().CreateDbContextAsync(token);
        await selected.Database.OpenConnectionAsync(token);
        var original = RelationalConnectionIdentity.Capture(selected);
        var originalContext = selected.ContextId;
        int? backend = original.BackendProcessId;

        await selected.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await selected.Database.BeginTransactionAsync(token);
            var physical = transaction.GetDbTransaction();
            bool touched = await ExternalApiKeyAuthenticationQuery.TouchUsageAsync(
                selected, rows.KeyA.Id, Now, "192.0.2.9", Now.AddMinutes(-5), token, fixture.NativeQueueEnrollment);

            await Assert.That(touched).IsTrue();
            await Assert.That(selected.ContextId).IsEqualTo(originalContext);
            await Assert.That(original.Matches(selected)).IsTrue();
            await Assert.That(original.BackendProcessId).IsEqualTo(backend);
            await Assert.That(ReferenceEquals(selected.Database.CurrentTransaction, transaction)).IsTrue();
            await Assert.That(ReferenceEquals(selected.Database.CurrentTransaction!.GetDbTransaction(), physical)).IsTrue();
            ExternalApiKey pending = await selected.ExternalApiKeys
                .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
                .AsNoTracking().SingleAsync(row => row.Id == rows.KeyA.Id, token);
            await Assert.That(pending.LastUsedAt).IsEqualTo(Now);
            await Assert.That(pending.LastUsedIp).IsEqualTo("192.0.2.9");
            await transaction.RollbackAsync(token);
        });

        await using var independent = Migrator();
        ExternalApiKey persisted = await independent.ExternalApiKeys
            .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
            .AsNoTracking().SingleAsync(row => row.Id == rows.KeyA.Id, token);
        await Assert.That(persisted.LastUsedAt).IsNull();
        await Assert.That(persisted.LastUsedIp).IsNull();
        await Assert.That(Equals(ImmutableKeyImage(persisted), ImmutableKeyImage(rows.KeyA))).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PostCommitUsageTimeoutCannotReplayOrLoseItsOriginalFailureDuringCleanup(bool failCleanup)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        Rows rows = await SeedAsync(token);
        await using var selected = await GlobalFactory().CreateDbContextAsync(token);
        await selected.Database.OpenConnectionAsync(token);
        var original = RelationalConnectionIdentity.Capture(selected);
        var originalContext = selected.ContextId;
        int? backend = original.BackendProcessId;
        var postCommitFailure = new UsageAcknowledgementTimeout();
        var cleanupFailure = new UsageTransactionCleanupFailure();
        System.Data.Common.DbTransaction? originalTransaction = null;
        int updates = 0;
        int commits = 0;
        int disposals = 0;
        bool exactUpdateOwner = true;
        bool exactCommitOwner = true;
        bool postCommitFaultReached = false;
        bool cleanupFaultReached = false;
        Exception? failure = null;
        bool? returned = null;

        using (var diagnostics = new UsageSettlementDiagnostics(selected, data =>
        {
            updates++;
            exactUpdateOwner &= selected.ContextId == originalContext && original.Matches(selected)
                && original.State == ConnectionState.Open && original.BackendProcessId == backend
                && ReferenceEquals(data.Command.Connection, selected.Database.GetDbConnection())
                && data.Command.Transaction is not null
                && ReferenceEquals(data.Command.Transaction, selected.Database.CurrentTransaction?.GetDbTransaction());
            originalTransaction ??= data.Command.Transaction;
        }, data =>
        {
            commits++;
            exactCommitOwner &= selected.ContextId == originalContext && original.Matches(selected)
                && original.State == ConnectionState.Open && original.BackendProcessId == backend
                && originalTransaction is not null && ReferenceEquals(data.Transaction, originalTransaction);
            if (!postCommitFaultReached)
            {
                postCommitFaultReached = true;
                throw postCommitFailure;
            }
        }, data =>
        {
            if (!ReferenceEquals(data.Transaction, originalTransaction)) return;
            disposals++;
            if (failCleanup && postCommitFaultReached && !cleanupFaultReached)
            {
                cleanupFaultReached = true;
                throw cleanupFailure;
            }
        }))
        {
            try
            {
                returned = await ExternalApiKeyAuthenticationQuery.TouchUsageAsync(
                    selected, rows.KeyA.Id, Now, "192.0.2.9", Now.AddMinutes(-5), token, fixture.NativeQueueEnrollment);
            }
            catch (Exception error) { failure = error; }
        }

        await using var independent = Migrator();
        ExternalApiKey persisted = await independent.ExternalApiKeys
            .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
            .AsNoTracking().SingleAsync(row => row.Id == rows.KeyA.Id, token);
        await Assert.That(persisted.LastUsedAt).IsEqualTo(Now);
        await Assert.That(persisted.LastUsedIp).IsEqualTo("192.0.2.9");
        await Assert.That(Equals(ImmutableKeyImage(persisted), ImmutableKeyImage(rows.KeyA))).IsTrue();

        string diagnostic = $"updates={updates}; commits={commits}; disposals={disposals}; postCommitFault={postCommitFaultReached}; cleanupFault={cleanupFaultReached}; returned={returned}";
        await Assert.That(postCommitFaultReached).IsTrue().Because(diagnostic);
        await Assert.That(cleanupFaultReached).IsEqualTo(failCleanup).Because(diagnostic);
        await Assert.That(updates).IsEqualTo(1).Because(diagnostic);
        await Assert.That(commits).IsEqualTo(1).Because(diagnostic);
        await Assert.That(disposals).IsEqualTo(1).Because(diagnostic);
        await Assert.That(exactUpdateOwner && exactCommitOwner).IsTrue().Because(diagnostic);
        await Assert.That(selected.ContextId).IsEqualTo(originalContext);
        await Assert.That(original.Matches(selected)).IsTrue();
        await Assert.That(original.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(original.BackendProcessId).IsEqualTo(backend);
        await Assert.That(failure).IsNotNull().Because(diagnostic);
        await Assert.That(returned).IsNull().Because(diagnostic);
        Exception[] causes = UsageExceptionCauses(failure!).ToArray();
        await Assert.That(causes.Any(cause => ReferenceEquals(cause, postCommitFailure))).IsTrue().Because(diagnostic);
        if (failCleanup)
            await Assert.That(causes.Any(cause => ReferenceEquals(cause, cleanupFailure))).IsTrue().Because(diagnostic);
    }

    private static IEnumerable<Exception> UsageExceptionCauses(Exception failure)
    {
        yield return failure;
        if (failure is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions)
                foreach (Exception cause in UsageExceptionCauses(inner)) yield return cause;
        }
        else if (failure.InnerException is { } inner)
        {
            foreach (Exception cause in UsageExceptionCauses(inner)) yield return cause;
        }
    }

    private sealed class UsageAcknowledgementTimeout() : TimeoutException("The usage commit acknowledgement observer failed.");
    private sealed class UsageTransactionCleanupFailure() : InvalidOperationException("The original usage transaction cleanup observer failed.");

    private sealed class UsageSettlementDiagnostics : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly ExploreDbContext _owner;
        private readonly Action<CommandExecutedEventData> _afterUpdate;
        private readonly Action<TransactionEventData> _afterCommit;
        private readonly Action<TransactionEventData> _afterDispose;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _listeners;

        internal UsageSettlementDiagnostics(ExploreDbContext owner, Action<CommandExecutedEventData> afterUpdate,
            Action<TransactionEventData> afterCommit, Action<TransactionEventData> afterDispose)
        {
            _owner = owner;
            _afterUpdate = afterUpdate;
            _afterCommit = afterCommit;
            _afterDispose = afterDispose;
            _listeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == "Microsoft.EntityFrameworkCore")
                _subscriptions.Add(value.Subscribe(this, name => name == RelationalEventId.CommandExecuted.Name
                    || name == RelationalEventId.TransactionCommitted.Name || name == RelationalEventId.TransactionDisposed.Name));
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is CommandExecutedEventData command && ReferenceEquals(command.Context, _owner)
                && command.Command.CommandText.TrimStart().StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase))
                _afterUpdate(command);
            else if (value.Value is TransactionEventData transaction && ReferenceEquals(transaction.Context, _owner))
            {
                if (value.Key == RelationalEventId.TransactionCommitted.Name) _afterCommit(transaction);
                else if (value.Key == RelationalEventId.TransactionDisposed.Name) _afterDispose(transaction);
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            _listeners.Dispose();
            foreach (IDisposable subscription in _subscriptions) subscription.Dispose();
        }
    }

    private sealed class UsageUpdateDiagnostics : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly ExploreDbContext _owner;
        private readonly Action<System.Data.Common.DbCommand> _afterUpdate;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _listeners;

        internal UsageUpdateDiagnostics(ExploreDbContext owner, Action<System.Data.Common.DbCommand> afterUpdate)
        {
            _owner = owner;
            _afterUpdate = afterUpdate;
            _listeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == "Microsoft.EntityFrameworkCore")
                _subscriptions.Add(value.Subscribe(this, name => name == RelationalEventId.CommandExecuted.Name));
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is CommandExecutedEventData data && ReferenceEquals(data.Context, _owner)
                && data.Command.CommandText.TrimStart().StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase))
                _afterUpdate(data.Command);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            _listeners.Dispose();
            foreach (IDisposable subscription in _subscriptions) subscription.Dispose();
        }
    }

    private GlobalExploreDbContextFactory GlobalFactory() => new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 4);

    private ExploreDbContext Migrator()
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(builder.Options);
        context.BindTenantLease();
        return context;
    }

    private ExploreDbContext Runtime(Guid tenant)
    {
        ExploreDbContext context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenant));
        context.BindTenantLease();
        return context;
    }

    private async Task RetireBusinessRoutinesAsync(CancellationToken token)
    {
        await using var context = Migrator();
        foreach (string signature in Signatures(context))
            await context.Database.ExecuteSqlRawAsync($"DROP FUNCTION IF EXISTS {signature}", token);
    }

    private static string[] Signatures(ExploreDbContext context) =>
    [
        $"{PostgresExternalApiKeyAuthenticationContract.FunctionSql(context)}(text)",
        $"{PostgresExternalApiKeyAuthenticationContract.UsageFunctionSql(context)}(uuid, timestamp with time zone, text, timestamp with time zone)",
        $"{PostgresAdmissionScannerAuthenticationContract.FunctionSql(context)}(integer[], text[])"
    ];

    private async Task<Rows> SeedAsync(CancellationToken token)
    {
        await using var context = Migrator();
        await using var human = new HumanSecuritySourceGate(context);
        await using var humanLease = await human.AcquireAsync(token);
        await using var enrollment = new EventDiscoveryEnrollmentGate(context);
        await using var enrollmentLease = await enrollment.AcquireAsync(token);
        Guid a = Guid.CreateVersion7();
        Guid b = Guid.CreateVersion7();
        context.Tenants.AddRange(Tenant(a), Tenant(b));
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = Now,
            ConcurrencyStamp = Guid.CreateVersion7(),
            Pii = new UserPii { Email = $"auth-{Guid.CreateVersion7():N}@example.test", FirstName = "Authentication", LastName = "Owner" }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            CreatedAt = Now,
            Pii = new ActorPii { DisplayName = "Authentication owner" }
        };
        context.Actors.Add(actor);
        ExternalApiKey keyA = Key(a);
        ExternalApiKey keyB = Key(b);
        ExternalApiKey platform = Key(null);
        context.ExternalApiKeys.AddRange(keyA, keyB, platform);
        AdmissionScannerCapability scannerA = Scanner(a, 7);
        AdmissionScannerCapability scannerB = Scanner(b, 8);
        await context.SaveChangesAsync(token);
        return new(a, b, user.Id, keyA, keyB, platform, scannerA, scannerB);

        AdmissionScannerCapability Scanner(Guid tenant, int version)
        {
            var source = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                Tenant = null!,
                ActorId = actor.Id,
                Actor = actor,
                Title = "Authentication source",
                EventStatus = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Digital,
                EventFormat = null!,
                CreatedAt = Now,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            context.Events.Add(source);
            AdmissionTarget target = AdmissionTarget.Create(Guid.CreateVersion7(), tenant, source.Id, AdmissionTargetTypeEnum.Event, null, null);
            context.AdmissionTargets.Add(target);
            var scanner = AdmissionScannerCapability.Issue(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(), source.Id, target.Id,
                version, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "Authentication scanner",
                AdmissionScannerCapabilityAction.CheckIn, Now.AddHours(1), actor.Id, Now);
            context.AdmissionScannerCapabilities.Add(scanner);
            return scanner;
        }
    }

    private static readonly DateTime Now = new(2028, 4, 5, 12, 0, 0, DateTimeKind.Utc);
    private static AdmissionScannerCapabilityDigestCandidate Candidate(AdmissionScannerCapability scanner) => new(scanner.LookupKeyVersion, scanner.LookupDigest);

    private static object ImmutableKeyImage(ExternalApiKey key) => new
    {
        key.Id,
        key.TenantId,
        key.Name,
        key.Description,
        key.KeyId,
        key.SecretHash,
        key.Scopes,
        key.ExternalApiKeyOwnerTypeId,
        key.OwnerId,
        key.ExternalApiKeyStatusId,
        key.ExternalApiKeyCreditPeriodId,
        key.CreditLimit,
        key.MaxRolloverCredits,
        key.ExpiresAt,
        key.CreatedAt,
        key.CreatedBy,
        key.UpdatedAt,
        key.UpdatedBy
    };

    private static Tenant Tenant(Guid id) => new()
    {
        Id = id,
        FullName = $"Authentication {id:N}",
        Slug = $"auth-{id:N}",
        CreatedAt = Now,
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static ExternalApiKey Key(Guid? tenant) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant,
        Name = "Authentication key",
        KeyId = ApiKeyHashing.CreateKeyId(),
        SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()),
        Scopes = "events:read",
        OwnerType = tenant.HasValue ? ExternalApiKeyOwnerType.Tenant : ExternalApiKeyOwnerType.InstanceAdmin,
        OwnerId = tenant ?? Guid.CreateVersion7(),
        ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.PendingRotation,
        ExternalApiKeyStatus = null!,
        ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
        ExternalApiKeyCreditPeriod = null!,
        CreatedAt = Now
    };

    private sealed record Rows(Guid A, Guid B, Guid UserId, ExternalApiKey KeyA, ExternalApiKey KeyB, ExternalApiKey PlatformKey,
        AdmissionScannerCapability ScannerA, AdmissionScannerCapability ScannerB);
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}

public sealed class AuthenticationOrdinalSourceTests
{
    [Test]
    public async Task CaseInsensitiveStorageCannotAuthenticateACaseVariantKeyId()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"authentication-ordinal-{Guid.CreateVersion7():N}.db");
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString());
            await connection.OpenAsync();
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = databasePath
            });
            options.UseSqlite(connection);
            await using var context = new CaseInsensitiveAuthenticationContext(options.Options);
            context.BindTenantLease();
            await context.Database.EnsureCreatedAsync();
            await using var human = new HumanSecuritySourceGate(context);
            await using var humanLease = await human.AcquireAsync(default);
            await using var enrollment = new EventDiscoveryEnrollmentGate(context);
            await using var enrollmentLease = await enrollment.AcquireAsync(default);
            await LookupTableSeeder.SeedAsync(context, default);
            var key = new ExternalApiKey
            {
                Id = Guid.CreateVersion7(),
                KeyId = "a" + ApiKeyHashing.CreateKeyId(),
                Name = "Ordinal authentication",
                SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()),
                Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                OwnerId = Guid.CreateVersion7(),
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                ExternalApiKeyCreditPeriod = null!
            };
            context.ExternalApiKeys.Add(key);
            await context.SaveChangesAsync();
            string variant = key.KeyId.ToUpperInvariant();
            await Assert.That(variant == key.KeyId).IsFalse();
            // This first query proves that the real relational collation, not a mocked comparer,
            // selects the persisted row for the wrong case.
            await Assert.That(await context.ExternalApiKeys
                .IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
                .Where(row => row.KeyId == variant).Select(row => row.Id).SingleAsync()).IsEqualTo(key.Id);
            await Assert.That(await ExternalApiKeyAuthenticationQuery.ReadAsync(context, variant, default)).IsNull();
            await Assert.That((await ExternalApiKeyAuthenticationQuery.ReadAsync(context, key.KeyId, default))?.Id).IsEqualTo(key.Id);
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    private sealed class CaseInsensitiveAuthenticationContext(DbContextOptions<ExploreDbContext> options) : ExploreDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ExternalApiKey>().Property(row => row.KeyId).UseCollation("NOCASE");
        }
    }
}

[ClassDataSource<AuthenticationUnusualLoginSourceTests.UnusualGlobalFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class AuthenticationUnusualLoginSourceTests(AuthenticationUnusualLoginSourceTests.UnusualGlobalFixture fixture)
{
    [Test]
    public async Task FreshDeclaredGlobalWithBackslashApostropheAndDollarRetainsExactAuthenticationPolicies()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        var enrollment = fixture.NativeQueueEnrollment;
        await Assert.That(enrollment.GlobalLogin.Contains('\\') && enrollment.GlobalLogin.Contains('\'')
            && enrollment.GlobalLogin.Contains('$')).IsTrue();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var installer = new ExploreDbContext(options.Options);
        installer.BindTenantLease();
        await installer.Database.OpenConnectionAsync(token);
        await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(installer, enrollment, token);
        await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(installer, enrollment, token);
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var original = await factory.CreateDbContextAsync(token);
        await original.Database.OpenConnectionAsync(token);
        var contextId = original.ContextId;
        var connection = RelationalConnectionIdentity.Capture(original);
        int? backend = connection.BackendProcessId;
        await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(original, enrollment, token);
        await PostgresAdmissionScannerAuthenticationContract.RequireReadyAsync(original, enrollment, token);
        await Assert.That(await ExternalApiKeyAuthenticationQuery.ReadAsync(original, ApiKeyHashing.CreateKeyId(), token, enrollment)).IsNull();
        await Assert.That(await AdmissionScannerAuthenticationQuery.ReadAsync(original,
            [new(7, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))], token, enrollment)).IsNull();
        await Assert.That(original.ContextId == contextId && connection.Matches(original)
            && connection.BackendProcessId == backend && connection.State == ConnectionState.Open).IsTrue();
    }

    public sealed class UnusualGlobalFixture() : PostgreSqlContainerFixture(
        globalLoginSuffix: @"\O'Reilly$")
    {
    }
}
