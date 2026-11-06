using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.TenantIsolation;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Onboarding;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.None)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresBootstrapStartupStorageTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GlobalSetupRefusesPartialCompletionPermissionsInsteadOfRepairingDrift()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var migrator = new ExploreDbContext(options.Options);
        migrator.BindTenantLease();
        var entity = migrator.Model.FindEntityType(typeof(PrivacyErasureSaga))!;
        var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(
            entity.GetTableName()!, entity.GetSchema());
        var names = migrator.GetService<ISqlGenerationHelper>();
        string table = names.DelimitIdentifier(store.Name, store.Schema);
        string column = names.DelimitIdentifier(
            entity.FindProperty(nameof(PrivacyErasureSaga.SubjectId))!.GetColumnName(store)!);
        string global = names.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
        await migrator.Database.ExecuteSqlRawAsync($"REVOKE SELECT ({column}) ON {table} FROM {global}", token);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresSetupLiveStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token));
        }
        finally
        {
            await migrator.Database.ExecuteSqlRawAsync(
                $"GRANT SELECT ({column}) ON {table} TO {global}", CancellationToken.None);
        }
    }

    [Test]
    public async Task GlobalSetupChecksErasureExistenceWithoutPrivateSagaReadsOrMutation()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var migrator = new ExploreDbContext(options.Options);
        migrator.BindTenantLease();
        Guid subjectId = Guid.CreateVersion7();
        var now = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        var intent = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
            PrivacyErasureSubjectKind.User, subjectId, PrivacyErasureReasonCode.AccountDeletion, 1, now, now);
        var saga = PrivacyErasureSaga.Start(intent, 1,
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), now.AddHours(1), now);
        try
        {
            migrator.PrivacyErasureSagas.Add(saga);
            await migrator.SaveChangesAsync(token);
            string runtimeBefore = await ReadRuntimeBootstrapPrivileges(migrator, token);
            await PostgresSetupLiveStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
            await Assert.That(await ReadRuntimeBootstrapPrivileges(migrator, token)).IsEqualTo(runtimeBefore);

            var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
            await using var original = await factory.CreateDbContextAsync(token);
            await original.Database.OpenConnectionAsync(token);
            var connector = (NpgsqlConnection)original.Database.GetDbConnection();
            int backend = connector.ProcessID;
            var contextId = original.ContextId;
            await original.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await original.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
                var physical = transaction.GetDbTransaction();
                var repository = new PrivacyErasureStateRepository(original);
                await Assert.That(await repository.ExistsBySubjectAsync(Guid.CreateVersion7(), token)).IsFalse();
                await Assert.That(await repository.ExistsBySubjectAsync(subjectId, token)).IsTrue();

                await DeniedAsync(async () => { await repository.GetBySubjectAsync(subjectId, token); });
                await DeniedAsync(async () =>
                {
                    await original.PrivacyErasureSagas.Where(row => row.SubjectId == subjectId)
                        .Select(row => row.ReceiptHash).SingleAsync(token);
                });
                await DeniedAsync(async () =>
                {
                    await original.PrivacyErasureSagas.Where(row => row.SubjectId == subjectId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PolicyVersion, 2), token);
                });
                await DeniedAsync(async () =>
                {
                    await original.PrivacyErasureSagas.Where(row => row.SubjectId == subjectId).ExecuteDeleteAsync(token);
                });
                await transaction.CreateSavepointAsync("denied_saga_insert", token);
                original.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(
                    PrivacyErasureIntent.Record(Guid.CreateVersion7(), 2, PrivacyErasureSubjectKind.User,
                        Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion, 1, now, now),
                    2, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), now.AddHours(1), now));
                var insertDenied = await Assert.ThrowsAsync<DbUpdateException>(() => original.SaveChangesAsync(token));
                await Assert.That(insertDenied!.InnerException is PostgresException
                    { SqlState: PostgresErrorCodes.InsufficientPrivilege }).IsTrue();
                await transaction.RollbackToSavepointAsync("denied_saga_insert", token);
                original.ChangeTracker.Clear();
                await Assert.That(await repository.ExistsBySubjectAsync(subjectId, token)).IsTrue();
                await Assert.That(original.ContextId).IsEqualTo(contextId);
                await Assert.That(connector.ProcessID).IsEqualTo(backend);
                await Assert.That(original.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
                await Assert.That(physical.Connection).IsSameReferenceAs(connector);
                await transaction.RollbackAsync(CancellationToken.None);

                var runtimeOptions = TestDbContextOptions.Create<ExploreDbContext>();
                PrimaryDatabaseProviderComposition.ConfigureApplication(runtimeOptions,
                    fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
                await using var runtime = new ExploreDbContext(runtimeOptions.Options);
                runtime.BindTenantLease();
                var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                    new PrivacyErasureStateRepository(runtime).ExistsBySubjectAsync(subjectId, token));
                await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);

                async Task DeniedAsync(Func<Task> operation)
                {
                    await transaction.CreateSavepointAsync("denied_saga_access", token);
                    try
                    {
                        var error = await Assert.ThrowsAsync<PostgresException>(operation);
                        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                    }
                    finally
                    {
                        await transaction.RollbackToSavepointAsync("denied_saga_access", CancellationToken.None);
                        original.ChangeTracker.Clear();
                    }
                }
            });
        }
        finally
        {
            await migrator.PrivacyErasureSagas.Where(row => row.IntentId == saga.IntentId)
                .ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task GlobalSetupSynchronizesIdentityEmailAndGrantsPlatformRoleInOriginalTransaction()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var migrator = new ExploreDbContext(options.Options);
        migrator.BindTenantLease();
        Guid userId = Guid.CreateVersion7();
        Guid loginId = Guid.CreateVersion7();
        string email = $"setup-{userId:N}@example.test";
        var now = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
        var user = new User
        {
            Id = userId,
            Pii = new UserPii { Email = email, FirstName = "Setup", LastName = "Administrator" },
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = now
        };
        try
        {
            migrator.Users.Add(user);
            migrator.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = loginId, UserId = userId, User = user,
                AuthenticationProviderId = (int)"keycloak".ParseAuthenticationProviderKind(),
                AuthenticationProvider = null!, ProviderKey = $"setup-{loginId:N}", CreatedAt = now
            });
            await migrator.SaveChangesAsync(token);
            string runtimeBefore = await ReadRuntimeBootstrapPrivileges(migrator, token);
            await PostgresSetupLiveStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
            await Assert.That(await ReadRuntimeBootstrapPrivileges(migrator, token)).IsEqualTo(runtimeBefore);

            var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
            await using var original = await factory.CreateDbContextAsync(token);
            await original.Database.OpenConnectionAsync(token);
            var connector = (NpgsqlConnection)original.Database.GetDbConnection();
            int backend = connector.ProcessID;
            var contextId = original.ContextId;
            await original.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var humanGate = new HumanSecuritySourceGate(original);
                await using var humanLease = await humanGate.AcquireAsync(token);
                await using var enrollmentGate = new EventDiscoveryEnrollmentGate(original);
                await using var enrollmentLease = await enrollmentGate.AcquireAsync(token);
                await using var transaction = await original.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
                var physical = transaction.GetDbTransaction();
                var emails = new UserIdentityEmailRepository(original);
                var roles = new PlatformUserRoleRepository(original);
                await Assert.That(await emails.GetByNormalizedEmailAsync(email, token)).IsNull();
                var claim = UserIdentityEmailClaim.Create(userId, email);
                await emails.CreateClaimAsync(claim, token);
                await Assert.That((await emails.GetByNormalizedEmailAsync(email, token))!.Id).IsEqualTo(claim.Id);
                var evidence = UserIdentityEmailEvidence.Create(userId, claim.Id, loginId, now);
                await emails.CreateEvidenceAsync(evidence, token);
                await Assert.That((await emails.GetEvidenceByBindingAsync(loginId, token))!.Id).IsEqualTo(evidence.Id);
                await Assert.That((await emails.GetByUserAsync(userId, token)).Single().Id).IsEqualTo(claim.Id);

                var role = await new RoleRepository(original).GetByMasterCodeAsync("platform.admin");
                await Assert.That(role).IsNotNull();
                await Assert.That(role!.Scope).IsEqualTo(RoleScopeEnum.Platform);
                int roleId = role.Id;
                await Assert.That(await roles.GetByUserAndRole(userId, roleId)).IsNull();
                Guid assignmentId = Guid.CreateVersion7();
                original.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = assignmentId, UserId = userId, User = null!, RoleId = roleId, Role = null!,
                    GrantedAt = now, GrantedBy = userId
                });
                await original.SaveChangesAsync(token);
                await Assert.That((await roles.GetByUserAndRole(userId, roleId))!.Id).IsEqualTo(assignmentId);
                await Assert.That(await roles.IsUserPlatformAdmin(userId)).IsTrue();
                await DeniedAsync(async () =>
                {
                    await original.PlatformUserRoles.Where(row => row.Id == assignmentId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.GrantedAt, now.AddMinutes(1)), token);
                });
                await DeniedAsync(async () =>
                {
                    await original.PlatformUserRoles.Where(row => row.Id == assignmentId).ExecuteDeleteAsync(token);
                });
                await DeniedAsync(async () =>
                {
                    await original.UserIdentityEmailClaims.Where(row => row.Id == claim.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NormalizedEmail,
                            $"changed-{userId:N}@example.test"), token);
                });
                await DeniedAsync(async () =>
                {
                    await original.UserIdentityEmailEvidence.Where(row => row.Id == evidence.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsActive, false), token);
                });
                await emails.RemoveEvidenceByBindingAsync(loginId, token);
                await Assert.That(await emails.GetEvidenceByBindingAsync(loginId, token)).IsNull();
                await Assert.That(await emails.GetByNormalizedEmailAsync(email, token)).IsNull();
                await Assert.That((await emails.GetByUserAsync(userId, token)).Count).IsEqualTo(0);

                await transaction.CreateSavepointAsync("private_pii_read", token);
                var piiDenied = await Assert.ThrowsAsync<PostgresException>(() =>
                    original.Users.IgnoreAutoIncludes().Where(row => row.Id == userId)
                        .Select(row => row.Pii.Email).SingleAsync(token));
                await Assert.That(piiDenied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                await transaction.RollbackToSavepointAsync("private_pii_read", token);
                await Assert.That(original.ContextId).IsEqualTo(contextId);
                await Assert.That(connector.ProcessID).IsEqualTo(backend);
                await Assert.That(original.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
                await Assert.That(physical.Connection).IsSameReferenceAs(connector);
                await transaction.RollbackAsync(CancellationToken.None);

                async Task DeniedAsync(Func<Task> operation)
                {
                    await transaction.CreateSavepointAsync("denied_identity_mutation", token);
                    try
                    {
                        var error = await Assert.ThrowsAsync<PostgresException>(operation);
                        await Assert.That(error!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                    }
                    finally
                    {
                        await transaction.RollbackToSavepointAsync("denied_identity_mutation", CancellationToken.None);
                    }
                }
            });
        }
        finally
        {
            await migrator.UserIdentityEmailEvidence.Where(row => row.ExternalLoginId == loginId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await migrator.UserIdentityEmailClaims.Where(row => row.UserId == userId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await migrator.PlatformUserRoles.Where(row => row.UserId == userId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await migrator.UserExternalLogins.Where(row => row.Id == loginId)
                .ExecuteDeleteAsync(CancellationToken.None);
            await migrator.Users.Where(row => row.Id == userId).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    private async Task<string> ReadRuntimeBootstrapPrivileges(ExploreDbContext migrator, CancellationToken token)
    {
        var names = migrator.GetService<ISqlGenerationHelper>();
        List<string> privileges = [];
        foreach (Type type in new[]
                 {
                     typeof(PrivacyErasureSaga), typeof(UserIdentityEmailClaim),
                     typeof(UserIdentityEmailEvidence), typeof(PlatformUserRole), typeof(Role), typeof(UserPii)
                 })
        {
            var entity = migrator.Model.FindEntityType(type)!;
            string table = names.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            privileges.Add(await migrator.Database.SqlQuery<string>($"""
                SELECT string_agg(a.attname || ':' || p.privilege || ':' ||
                    pg_catalog.has_column_privilege({fixture.NativeQueueEnrollment.RuntimeLogin},
                        a.attrelid, a.attnum, p.privilege)::text, ',' ORDER BY a.attname, p.privilege) AS "Value"
                FROM pg_catalog.pg_attribute a
                CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('REFERENCES')) p(privilege)
                WHERE a.attrelid = pg_catalog.to_regclass({table}) AND a.attnum > 0 AND NOT a.attisdropped
                """).SingleAsync(token));
            privileges.Add(await migrator.Database.SqlQuery<string>($"""
                SELECT pg_catalog.has_table_privilege({fixture.NativeQueueEnrollment.RuntimeLogin},
                    pg_catalog.to_regclass({table}), 'DELETE')::text AS "Value"
                """).SingleAsync(token));
        }
        return string.Join('|', privileges);
    }

    [Test]
    public async Task GlobalSetupSettingsRetainPrivateRowIsolationAndOriginalTransaction()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        string privateKey = $"native.private.{Guid.CreateVersion7():N}";
        const string originalBrand = "\"Original native brand\"";
        const string updatedBrand = "\"Updated native brand\"";
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using (var migrator = new ExploreDbContext(options.Options))
        {
            migrator.BindTenantLease();
            await migrator.Database.OpenConnectionAsync(token);
            await using var human = new HumanSecuritySourceGate(migrator);
            await using var heldHuman = await human.AcquireAsync(token);
            await using var enrollment = await new EventDiscoveryEnrollmentGate(migrator).AcquireAsync(token);
            var brand = await migrator.SystemSettings.SingleOrDefaultAsync(
                row => row.SettingKey == GovernanceSettingKeys.Branding.DisplayName, token);
            if (brand is null)
            {
                brand = new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = GovernanceSettingKeys.Branding.DisplayName,
                    ValueType = SettingValueType.String,
                    Value = originalBrand
                };
                migrator.SystemSettings.Add(brand);
            }
            brand.Value = originalBrand;
            await migrator.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.PublicExperience.Ctas)
                .ExecuteDeleteAsync(token);
            migrator.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = privateKey,
                ValueType = SettingValueType.String,
                Value = "\"Unrelated private value\""
            });
            await migrator.SaveChangesAsync(token);
            await PostgresSetupLiveStoragePermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
            await PostgresProvisioningPolicyPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
            await PostgresSetupLiveStoragePermissions.ApplyTrustedSettingsAsync(migrator, fixture.NativeQueueEnrollment, token);
            await PostgresSetupLiveStoragePermissions.ApplyTrustedSettingsAsync(migrator, fixture.NativeQueueEnrollment, token);
            await Assert.That(await migrator.Database.SqlQuery<bool>($"""
                SELECT pg_catalog.pg_has_role({fixture.NativeQueueEnrollment.RuntimeLogin},
                    {PostgresDiscoveryDisclosureContract.RuntimeRole}, 'MEMBER') AS "Value"
                """).SingleAsync(token)).IsFalse();
        }

        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var original = await factory.CreateDbContextAsync(token);
        await original.Database.OpenConnectionAsync(token);
        var connector = (NpgsqlConnection)original.Database.GetDbConnection();
        int backend = connector.ProcessID;
        var contextId = original.ContextId;
        var transactions = new EfCoreUnitOfWork(original);
        var settings = SystemSettingRepository.CreateReadOnly(
            original, new RelationalSettingMutationLock(original, transactions));
        await using var globalHuman = new HumanSecuritySourceGate(original);
        await using var heldGlobalHuman = await globalHuman.AcquireAsync(token);
        await using var heldGlobalEnrollment = await new EventDiscoveryEnrollmentGate(original).AcquireAsync(token);
        await using var driftOwner = new ExploreDbContext(options.Options);
        driftOwner.BindTenantLease();
        await driftOwner.Database.OpenConnectionAsync(token);
        var sqlNames = driftOwner.GetService<ISqlGenerationHelper>();
        string grantorName = $"setup_grantor_{Guid.CreateVersion7():N}";
        string grantor = sqlNames.DelimitIdentifier(grantorName);
        string executor = sqlNames.DelimitIdentifier(PostgresDiscoveryDisclosureContract.RuntimeRole);
        string global = sqlNames.DelimitIdentifier(fixture.NativeQueueEnrollment.GlobalLogin);
        await driftOwner.Database.ExecuteSqlRawAsync(
            $"CREATE ROLE {grantor} NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS", token);
        try
        {
            await driftOwner.Database.ExecuteSqlRawAsync($"""
                GRANT {executor} TO {grantor} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE;
                GRANT {executor} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN TRUE GRANTED BY {grantor}
                """, token);
            await Assert.That(await driftOwner.Database.SqlQuery<bool>($"""
                SELECT pg_catalog.pg_has_role({fixture.NativeQueueEnrollment.GlobalLogin},
                    {PostgresDiscoveryDisclosureContract.RuntimeRole}, 'SET') AS "Value"
                """).SingleAsync(token)).IsFalse();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresSetupLiveStoragePermissions.RequireTrustedSettingsCurrentAsync(original, fixture.NativeQueueEnrollment, token));
            await driftOwner.Database.ExecuteSqlRawAsync($"""
                REVOKE {executor} FROM {global} GRANTED BY {grantor};
                GRANT {grantor} TO {global} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                """, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresSetupLiveStoragePermissions.RequireTrustedSettingsCurrentAsync(original, fixture.NativeQueueEnrollment, token));
        }
        finally
        {
            await driftOwner.Database.ExecuteSqlRawAsync($"""
                REVOKE {executor} FROM {global} GRANTED BY {grantor};
                REVOKE {grantor} FROM {global};
                REVOKE {executor} FROM {grantor};
                DROP ROLE {grantor}
                """, CancellationToken.None);
        }
        await PostgresSetupLiveStoragePermissions.RequireTrustedSettingsCurrentAsync(original, fixture.NativeQueueEnrollment, token);
        await original.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await original.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, token);
            var physical = transaction.GetDbTransaction();
            await PostgresSetupLiveStoragePermissions.RequireTrustedSettingsCurrentAsync(original, fixture.NativeQueueEnrollment, token);
            await PostgresOrdinarySettingsContract.RequireReadyAsync(original, fixture.NativeQueueEnrollment, token);
            await PostgresOperatorIdentityEditorContract.RequireReadyAsync(original, fixture.NativeQueueEnrollment, token);
            var observed = await settings.GetByKey(GovernanceSettingKeys.Branding.DisplayName, token);
            await Assert.That(observed).IsNotNull();
            await Assert.That(observed!.Value).IsEqualTo(originalBrand);
            await Assert.That(await settings.GetByKey(privateKey, token)).IsNull();
            await Assert.That(await original.SystemSettings
                .Where(row => row.SettingKey == GovernanceSettingKeys.Branding.DisplayName)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, updatedBrand), token)).IsEqualTo(1);
            await Assert.That(await original.SystemSettings.Where(row => row.SettingKey == privateKey)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "\"Forbidden\""), token)).IsEqualTo(0);
            await Assert.That((await settings.GetByKey(GovernanceSettingKeys.Branding.DisplayName, token))!.Value)
                .IsEqualTo(updatedBrand);
            original.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.PublicExperience.Ctas,
                ValueType = SettingValueType.Json,
                Value = "[]"
            });
            await original.SaveChangesAsync(token);
            await Assert.That((await settings.GetByKey(GovernanceSettingKeys.PublicExperience.Ctas, token))!.Value)
                .IsEqualTo("[]");
            await Assert.That(original.ContextId).IsEqualTo(contextId);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            await Assert.That(original.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(transaction.GetDbTransaction()).IsSameReferenceAs(physical);
            await Assert.That(physical.Connection).IsSameReferenceAs(connector);
            await transaction.RollbackAsync(CancellationToken.None);
        });
        original.ChangeTracker.Clear();
        var rename = await Assert.ThrowsAsync<PostgresException>(() => original.SystemSettings
            .Where(row => row.SettingKey == GovernanceSettingKeys.Branding.DisplayName)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SettingKey, privateKey), token));
        await Assert.That(rename!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => original.SystemSettings
            .Where(row => row.SettingKey == GovernanceSettingKeys.Branding.DisplayName).ExecuteDeleteAsync(token));
        await Assert.That(delete!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        original.SystemSettings.Add(new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = $"native.private.insert.{Guid.CreateVersion7():N}",
            ValueType = SettingValueType.String,
            Value = "\"Forbidden private insertion\""
        });
        var insert = await Assert.ThrowsAsync<DbUpdateException>(() => original.SaveChangesAsync(token));
        await Assert.That(insert!.InnerException is PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege }).IsTrue();
        original.ChangeTracker.Clear();

        var settingEntity = driftOwner.Model.FindEntityType(typeof(SystemSetting))!;
        string table = driftOwner.GetService<Microsoft.EntityFrameworkCore.Storage.ISqlGenerationHelper>()
            .DelimitIdentifier(settingEntity.GetTableName()!, settingEntity.GetSchema());
        await driftOwner.Database.ExecuteSqlRawAsync(
            $"ALTER POLICY trusted_setup_settings_select ON {table} USING (true)", token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PostgresSetupLiveStoragePermissions.RequireTrustedSettingsCurrentAsync(original, fixture.NativeQueueEnrollment, token));
        await Assert.That(await settings.GetByKey(privateKey, token)).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PostgresSetupLiveStoragePermissions.ApplyTrustedSettingsAsync(driftOwner, fixture.NativeQueueEnrollment, token));
    }

    [Test]
    public async Task EnrolledGlobalOwnerConvergesInteractiveAndConfiguredBootstrapWithoutDeletion()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var migrationOptions = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(migrationOptions,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var factory = new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using var database = await factory.CreateDbContextAsync(token);
        await using var gate = new EventDiscoveryEnrollmentGate(database);
        var repository = new InstanceBootstrapStateRepository(database);
        var transactions = new EfCoreUnitOfWork(database);

        Task PrepareAsync(IConfiguration configuration) =>
            new ConfiguredAdministratorBootstrapStartupRunner(
                new ConfiguredAdministratorBootstrapProvider(configuration,
                    Options.Create(new InstanceOperatorIdentityOptions()), repository),
                repository, transactions, TimeProvider.System, gate).PrepareAsync(token);

        var interactive = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["INSTANCE_BOOTSTRAP_MODE"] = "Interactive",
            ["Deployment:Mode"] = "SingleTenant"
        }).Build();
        await PrepareAsync(interactive);
        await Assert.That(await repository.GetCurrent(token)).IsNull();

        IConfiguration configured = ConfiguredAdministratorBootstrapStartupConcurrencyTests.CreateConfiguration();
        await PrepareAsync(configured);
        var first = await repository.GetCurrent(token);
        await Assert.That(first!.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(first.Generation).IsEqualTo(1L);
        await PrepareAsync(configured);
        await Assert.That((await repository.GetCurrent(token))!.Id).IsEqualTo(first.Id);

        var replacement = new ConfigurationBuilder().AddConfiguration(configured)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "2"
            }).Build();
        await PrepareAsync(replacement);
        var current = await repository.GetCurrent(token);
        await Assert.That(current!.Generation).IsEqualTo(2L);
        await Assert.That(current.Id).IsNotEqualTo(first.Id);
        var prior = await database.InstanceBootstrapStates.AsNoTracking()
            .SingleAsync(state => state.Id == first.Id, token);
        await Assert.That(prior.Status).IsEqualTo(InstanceBootstrapStatus.Superseded);
        await Assert.That(await database.InstanceBootstrapStates.CountAsync(token)).IsEqualTo(2);

        await database.Database.OpenConnectionAsync(token);
        await PostgresOperatorIdentityEditorContract.RequireReadyAsync(
            database, fixture.NativeQueueEnrollment, token);
        await using (var migrator = new ExploreDbContext(migrationOptions.Options))
        {
            migrator.BindTenantLease();
            await migrator.Database.OpenConnectionAsync(token);
            await ManagedTenantWorkerCreationPermissionsNativeTests.EnsurePolicyProjectionAsync(migrator);
            await PostgresProvisioningPolicyPermissions.ApplyAsync(migrator, fixture.NativeQueueEnrollment, token);
            await PostgresTenantCreationPermissions.ApplyAsync(
                migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
            await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(
                migrator, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
        }
        await PostgresTenantCreationPermissions.RequireCurrentAsync(
            database, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
        await PostgresManagedTenantWorkerCreationPermissions.RequireCurrentAsync(
            database, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, token);
        await PostgresOperatorIdentityEditorContract.RequireReadyAsync(
            database, fixture.NativeQueueEnrollment, token);

        var denial = await Assert.ThrowsAsync<PostgresException>(() =>
            database.InstanceBootstrapStates.Where(state => state.Id == first.Id).ExecuteDeleteAsync(token));
        await Assert.That(denial!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }
}
