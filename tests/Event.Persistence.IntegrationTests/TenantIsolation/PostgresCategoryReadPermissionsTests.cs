using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresCategoryReadPermissionsTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task CanonicalRuntimeReadsOnlySelectedTenantPagedHierarchy()
    {
        var selected = await SeedAsync(includeChild: true);
        await SeedAsync(includeChild: false);

        await using var runtime = Open(PrimaryDatabaseRole.Runtime, selected.Tenant);
        string login = fixture.NativeQueueEnrollment.RuntimeLogin;
        bool originalRuntime = await runtime.Database.SqlQuery<bool>(
            $"SELECT session_user = {login} AND current_user = {login} AS \"Value\"").SingleAsync();
        await Assert.That(originalRuntime).IsTrue();

        var repository = new CategoryRepository(runtime);
        (List<Category> Items, int TotalCount) first;
        try
        {
            first = await repository.GetCategoriesWithDetailsPaged(1, 1);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            throw new InvalidOperationException("Canonical Runtime Category paging was denied with SQLSTATE 42501.");
        }

        await Assert.That(first.TotalCount).IsEqualTo(2);
        await Assert.That(first.Items.Count).IsEqualTo(1);
        await Assert.That(first.Items[0].Id).IsEqualTo(selected.Parent);
        await Assert.That(first.Items[0].TenantId).IsEqualTo(selected.Tenant);
        await Assert.That(first.Items[0].Parent).IsNull();

        var second = await repository.GetCategoriesWithDetailsPaged(2, 1);
        await Assert.That(second.TotalCount).IsEqualTo(2);
        await Assert.That(second.Items.Count).IsEqualTo(1);
        await Assert.That(second.Items[0].Id).IsEqualTo(selected.Child);
        await Assert.That(second.Items[0].TenantId).IsEqualTo(selected.Tenant);
        await Assert.That(second.Items[0].Parent).IsNotNull();
        await Assert.That(second.Items[0].Parent!.Id).IsEqualTo(selected.Parent);
        await Assert.That(second.Items[0].Parent!.TenantId).IsEqualTo(selected.Tenant);
    }

    [Test]
    public Task DetachedCallerTransactionCannotCommitCategoryGrants() => AssertCallerFailureAsync(BoundaryFailure.DetachAfterRelease);

    [Test]
    public Task BeforeReleaseFailurePreservesCallerWorkWithoutCategoryGrants() => AssertCallerFailureAsync(BoundaryFailure.ThrowBeforeRelease);

    [Test]
    public Task ChangedTenantAfterReleaseCannotCommitCategoryGrants() => AssertCallerFailureAsync(BoundaryFailure.ChangeTenantAfterRelease);

    [Test]
    public Task CancellationAndRollbackFailureSurviveTransactionAbort() => AssertCallerFailureAsync(BoundaryFailure.CancelAndFailRollback);

    [Test]
    public Task DenialAndRollbackFailureSurviveTransactionAbort() => AssertCallerFailureAsync(BoundaryFailure.DenyAndFailRollback);

    [Test]
    public async Task FreshReaderAdmissionIsExactIdempotentAndTenantBound()
    {
        var selected = await SeedAsync(includeChild: true);
        await SeedAsync(includeChild: false);
        await WithReaderAsync(selected.Tenant, async (login, database, enrollment) =>
        {
            await using var writer = Open(PrimaryDatabaseRole.Migrator, selected.Tenant);
            await writer.Database.OpenConnectionAsync();
            var names = new HumanPrincipalSourceSql(writer);
            // Native Runtime connection admission needs namespace access before Category authority can be tested.
            await ExecuteAsync(writer, $"GRANT USAGE ON SCHEMA {names.Quote(names.Schema)} TO {names.Quote(login)}");
            var adapter = new SelectedTenant(selected.Tenant);
            await using var runtime = Open(database, adapter);
            await runtime.Database.OpenConnectionAsync();
            await Assert.That(await runtime.Database.SqlQuery<bool>(
                $"SELECT session_user = {login} AND current_user = {login} AS \"Value\"").SingleAsync()).IsTrue();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresCategoryReadPermissions.RequireReadyAsync(runtime, enrollment));
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                new CategoryRepository(runtime).GetCategoriesWithDetailsPaged(1, 10));
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);

            await PostgresCategoryReadPermissions.ApplyAsync(writer, enrollment);
            string installed = await AuthorityImageAsync(writer, login);
            await PostgresCategoryReadPermissions.ApplyAsync(writer, enrollment);
            await Assert.That(await AuthorityImageAsync(writer, login) == installed).IsTrue()
                .Because("Idempotent Category installation must preserve the original authority image.");
            await using var admitted = Open(database, adapter);
            await admitted.Database.OpenConnectionAsync();
            await Assert.That(await admitted.Database.SqlQuery<bool>(
                $"SELECT session_user = {login} AND current_user = {login} AS \"Value\"").SingleAsync()).IsTrue();
            await PostgresCategoryReadPermissions.RequireReadyAsync(admitted, enrollment);
            var page = await new CategoryRepository(admitted).GetCategoriesWithDetailsPaged(1, 10);
            await Assert.That(page.TotalCount).IsEqualTo(2);
            await Assert.That(page.Items.All(category => category.TenantId == selected.Tenant)).IsTrue();
            await Assert.That(page.Items.Single(category => category.Id == selected.Child).Parent!.Id).IsEqualTo(selected.Parent);
            await Assert.That(await ScalarAsync(writer, $"""
                SELECT count(*)=6 FROM pg_catalog.pg_attribute a
                CROSS JOIN LATERAL pg_catalog.aclexplode(a.attacl) acl
                WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum>0
                    AND acl.grantee=pg_catalog.to_regrole(@login) AND acl.privilege_type='SELECT'
                    AND NOT acl.is_grantable
                """, login)).IsTrue();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresCategoryReadPermissions.ApplyAsync(runtime, enrollment));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresCategoryReadPermissions.RequireReadyAsync(runtime, null));
            adapter.TenantId = Guid.CreateVersion7();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresCategoryReadPermissions.RequireReadyAsync(admitted, enrollment));
            adapter.TenantId = selected.Tenant;
            await using var tenantless = Open(database, (SelectedTenant?)null);
            await tenantless.Database.OpenConnectionAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresCategoryReadPermissions.RequireReadyAsync(tenantless, enrollment));
        });
    }

    [Test]
    public async Task FreshReaderWithDownstreamMemberIsDeniedUntilExactEdgeIsRemoved()
    {
        var selected = await SeedAsync(includeChild: true);
        await WithReaderAsync(selected.Tenant, async (login, database, enrollment) =>
        {
            await InCallerTransactionAsync(selected.Tenant, async (writer, transaction) =>
            {
                var names = new HumanPrincipalSourceSql(writer);
                string downstream = "category_downstream_" + Guid.CreateVersion7().ToString("N");
                await ExecuteAsync(writer, $"CREATE ROLE {names.Quote(downstream)} NOLOGIN; GRANT {names.Quote(login)} TO {names.Quote(downstream)} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE");
                string readerBefore = await AuthorityImageAsync(writer, login);
                string downstreamBefore = await AuthorityImageAsync(writer, downstream);
                var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    PostgresCategoryReadPermissions.ApplyAsync(writer, enrollment));
                await Assert.That(denied!.Message).IsEqualTo("category_read_permissions_drift");
                await Assert.That(await AuthorityImageAsync(writer, login) == readerBefore).IsTrue()
                    .Because("Denied enrollment must not widen the fresh reader's authority.");
                await Assert.That(await AuthorityImageAsync(writer, downstream) == downstreamBefore).IsTrue()
                    .Because("Denied enrollment must not widen its downstream member's authority.");
                await ExecuteAsync(writer, $"REVOKE {names.Quote(login)} FROM {names.Quote(downstream)}; DROP ROLE {names.Quote(downstream)}");
                await PostgresCategoryReadPermissions.ApplyAsync(writer, enrollment);
                string installed = await AuthorityImageAsync(writer, login);
                await PostgresCategoryReadPermissions.ApplyAsync(writer, enrollment);
                await Assert.That(await AuthorityImageAsync(writer, login) == installed).IsTrue()
                    .Because("Stable enrollment after removing the exact membership edge must be idempotent.");
                await transaction.CommitAsync();
            });
            await using var admitted = Open(database, selected.Tenant);
            await admitted.Database.OpenConnectionAsync();
            await Assert.That(await admitted.Database.SqlQuery<bool>(
                $"SELECT session_user = {login} AND current_user = {login} AS \"Value\"").SingleAsync()).IsTrue();
            await PostgresCategoryReadPermissions.RequireReadyAsync(admitted, enrollment);
            await Assert.That((await new CategoryRepository(admitted).GetCategoriesWithDetailsPaged(1, 10)).TotalCount).IsEqualTo(2);
        });
    }

    [Test]
    [Arguments("insert")]
    [Arguments("update")]
    [Arguments("delete")]
    [Arguments("system-column")]
    [Arguments("global")]
    public async Task NativeReaderCannotWriteOrUseGlobalCategoryAuthority(string operation)
    {
        var selected = await SeedAsync(includeChild: false);
        await using var context = Open(operation == "global" ? PrimaryDatabaseRole.Global : PrimaryDatabaseRole.Runtime, selected.Tenant);
        await context.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(context);
        string table = names.Table(typeof(Category));
        string id = names.Column(typeof(Category), nameof(Category.Id));
        string sql = operation switch
        {
            "insert" => $"INSERT INTO {table} DEFAULT VALUES",
            "update" => $"UPDATE {table} SET {id}={id} WHERE false",
            "delete" => $"DELETE FROM {table} WHERE false",
            "system-column" => $"SELECT xmin FROM {table}",
            _ => $"SELECT {id} FROM {table}"
        };
        var denied = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(context, sql));
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    [Arguments("table")]
    [Arguments("write")]
    [Arguments("grant-option")]
    [Arguments("public")]
    [Arguments("inherited")]
    [Arguments("set-only")]
    [Arguments("admin-only")]
    [Arguments("system-column")]
    [Arguments("global")]
    [Arguments("downstream")]
    [Arguments("schema-create")]
    [Arguments("nologin")]
    [Arguments("rls-disabled")]
    [Arguments("rls-unforced")]
    [Arguments("policy-weakened")]
    [Arguments("extra-policy")]
    [Arguments("extra-column")]
    [Arguments("partial")]
    public async Task InstallerRejectsAuthorityAndPolicyDriftWithoutRepair(string change)
    {
        var selected = await SeedAsync(includeChild: false);
        await InCallerTransactionAsync(selected.Tenant, async (writer, transaction) =>
        {
            var names = new HumanPrincipalSourceSql(writer);
            string table = names.Table(typeof(Category));
            string column = names.Column(typeof(Category), nameof(Category.MasterCode));
            string runtime = names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
            string global = names.Quote(fixture.NativeQueueEnrollment.GlobalLogin);
            string other = names.Quote("category_drift_" + Guid.CreateVersion7().ToString("N"));
            await ExecuteAsync(writer, change switch
            {
                "table" => $"GRANT SELECT ON {table} TO {runtime}",
                "write" => $"GRANT UPDATE ({column}) ON {table} TO {runtime}",
                "grant-option" => $"GRANT SELECT ({column}) ON {table} TO {runtime} WITH GRANT OPTION",
                "public" => $"GRANT SELECT ({column}) ON {table} TO PUBLIC",
                "inherited" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({column}) ON {table} TO {other}; GRANT {other} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                "set-only" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({column}) ON {table} TO {other}; GRANT {other} TO {runtime} WITH INHERIT FALSE, SET TRUE, ADMIN FALSE",
                "admin-only" => $"CREATE ROLE {other} NOLOGIN; GRANT SELECT ({column}) ON {table} TO {other}; GRANT {other} TO {runtime} WITH INHERIT FALSE, SET FALSE, ADMIN TRUE",
                "system-column" => $"GRANT SELECT (xmin) ON {table} TO {runtime}",
                "global" => $"GRANT SELECT ({column}) ON {table} TO {global}",
                "downstream" => $"CREATE ROLE {other} NOLOGIN; GRANT {runtime} TO {other} WITH INHERIT TRUE, SET TRUE, ADMIN FALSE",
                "schema-create" => $"GRANT CREATE ON SCHEMA {names.Quote(names.Schema)} TO {runtime}",
                "nologin" => $"ALTER ROLE {runtime} NOLOGIN",
                "rls-disabled" => $"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY",
                "rls-unforced" => $"ALTER TABLE {table} NO FORCE ROW LEVEL SECURITY",
                "policy-weakened" => $"ALTER POLICY tenant_isolation ON {table} USING (true) WITH CHECK (true)",
                "extra-policy" => $"CREATE POLICY category_extra ON {table} TO {runtime} USING (true)",
                "extra-column" => $"ALTER TABLE {table} ADD COLUMN category_unexpected text; GRANT SELECT (category_unexpected) ON {table} TO {runtime}",
                "partial" => $"REVOKE SELECT ({column}) ON {table} FROM {runtime}",
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            });
            string before = await AuthorityImageAsync(writer, fixture.NativeQueueEnrollment.RuntimeLogin);
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostgresCategoryReadPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment));
            await Assert.That(denied!.Message).IsEqualTo("category_read_permissions_drift");
            await Assert.That(await AuthorityImageAsync(writer, fixture.NativeQueueEnrollment.RuntimeLogin) == before).IsTrue()
                .Because("A refused Category installation must preserve its original authority image.");

            if (change is "table" or "write" or "grant-option" or "public" or "inherited" or "system-column" or "downstream")
            {
                foreach (var owner in CategoryProjectionOwners(writer))
                {
                    await transaction.CreateSavepointAsync("category_projection_drift");
                    var rejected = await Assert.ThrowsAsync<PostgresException>(owner.Apply);
                    await Assert.That(rejected!.SqlState).IsEqualTo(change == "public"
                        ? PostgresErrorCodes.ObjectNotInPrerequisiteState : PostgresErrorCodes.InsufficientPrivilege);
                    await Assert.That(rejected.MessageText).IsEqualTo(change == "public"
                        ? "native_privilege_guard_not_ready" : owner.Prefix + "_runtime_source_privilege");
                    await transaction.RollbackToSavepointAsync("category_projection_drift");
                    await Assert.That(await AuthorityImageAsync(writer, fixture.NativeQueueEnrollment.RuntimeLogin) == before).IsTrue()
                        .Because("A refused event owner installation must preserve its original authority image.");
                }
            }
        });
    }

    [Test]
    public async Task CategoryInstallerCoexistsWithPublicAndPrivateOwnerReinstallation()
    {
        var selected = await SeedAsync(includeChild: true);
        await InCallerTransactionAsync(selected.Tenant, async (writer, _) =>
        {
            foreach (var owner in CategoryProjectionOwners(writer)) await owner.Apply();
            await PostgresCategoryReadPermissions.ApplyAsync(writer, fixture.NativeQueueEnrollment);
            foreach (var owner in CategoryProjectionOwners(writer)) await owner.Apply();
        });
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, selected.Tenant);
        await runtime.Database.OpenConnectionAsync();
        await PostgresCategoryReadPermissions.RequireReadyAsync(runtime, fixture.NativeQueueEnrollment);
        await Assert.That((await new CategoryRepository(runtime).GetCategoriesWithDetailsPaged(1, 10)).TotalCount).IsEqualTo(2);
    }

    [Test]
    public async Task UnexpectedModelPropertyCannotInstallCategoryAuthority()
    {
        var selected = await SeedAsync(includeChild: false);
        await WithReaderAsync(selected.Tenant, async (login, _, enrollment) =>
        {
            await using var writer = Open(PrimaryDatabaseRole.Migrator, selected.Tenant);
            await writer.Database.OpenConnectionAsync();
            string before = await AuthorityImageAsync(writer, login);
            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
            options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
            await using var drifted = new UnexpectedCategoryContext(options.Options) { TenantContext = new SelectedTenant(selected.Tenant) };
            drifted.BindTenantLease();
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresCategoryReadPermissions.ApplyAsync(drifted, enrollment));
            await Assert.That(denied!.Message).IsEqualTo("category_read_permissions_drift");
            await Assert.That(await AuthorityImageAsync(writer, login) == before).IsTrue()
                .Because("A refused Category model must preserve its original authority image.");
        });
    }

    private (string Prefix, Func<Task> Apply)[] CategoryProjectionOwners(ExploreDbContext context) =>
    [
        ("public_detail", () => PostgresPublicEventDetailContract.ApplyAsync(context, fixture.NativeQueueEnrollment)),
        ("private_management", () => PostgresPrivateEventManagementContract.ApplyAsync(context, fixture.NativeQueueEnrollment))
    ];

    private async Task InCallerTransactionAsync(Guid tenant, Func<ExploreDbContext, IDbContextTransaction, Task> action)
    {
        var resources = new Stack<IAsyncDisposable>();
        var writer = Open(PrimaryDatabaseRole.Migrator, tenant);
        resources.Push(writer);
        Exception? primary = null;
        try
        {
            var transaction = await writer.Database.BeginTransactionAsync();
            resources.Push(transaction);
            await action(writer, transaction);
        }
        catch (Exception failure) { primary = failure; }
        primary = await DisposeOwnedAsync(resources, primary);
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private async Task WithReaderAsync(Guid tenant,
        Func<string, PrimaryDatabaseConnectionOptions, PostgresNativeQueueEnrollment, Task> action)
    {
        var reader = await CreateReaderAsync(tenant);
        Exception? primary = null;
        try { await action(reader.Login, reader.Options, reader.Enrollment); }
        catch (Exception failure) { primary = failure; }
        try { await DropReaderAsync(reader.Options, tenant); }
        catch (Exception cleanup)
        {
            primary = primary is null ? cleanup : new AggregateException("category_test_role_cleanup_failed", primary, cleanup);
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static async Task<Exception?> DisposeOwnedAsync(Stack<IAsyncDisposable> resources, Exception? primary)
    {
        while (resources.TryPop(out var resource))
        {
            try { await resource.DisposeAsync(); }
            catch (Exception cleanup)
            {
                primary = primary is null ? cleanup : new AggregateException("category_test_resource_cleanup_failed", primary, cleanup);
            }
        }
        return primary;
    }

    private async Task AssertCallerFailureAsync(BoundaryFailure failureMode)
    {
        var selected = await SeedAsync(includeChild: true);
        var reader = await CreateReaderAsync(selected.Tenant);
        var adapter = new SelectedTenant(selected.Tenant);
        var resources = new Stack<IAsyncDisposable>();
        T Own<T>(T resource) where T : IAsyncDisposable
        {
            resources.Push(resource);
            return resource;
        }
        ExploreDbContext? writer = null;
        Exception? primary = null;
        try
        {
            using var cancellation = new CancellationTokenSource();
            var boundary = new SavepointBoundary(reader.Login, failureMode, cancellation, adapter);
            Guid priorWork = Guid.CreateVersion7();
            writer = Own(Open(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator), adapter, boundary));
            {
                var human = Own(new HumanSecuritySourceGate(writer));
                Own(await human.AcquireAsync());
                var enrollment = Own(new EventDiscoveryEnrollmentGate(writer));
                Own(await enrollment.AcquireAsync());
                var caller = Own(await writer.Database.BeginTransactionAsync());
                writer.Categories.Add(new Category
                {
                    Id = priorWork,
                    ConcurrencyStamp = Guid.CreateVersion7(),
                    TenantId = selected.Tenant,
                    Tenant = null!,
                    MasterCode = "prior-" + priorWork.ToString("N"),
                    FullName = "Prior caller work"
                });
                await writer.SaveChangesAsync();
                string before = await AuthorityImageAsync(writer, reader.Login);
                var physical = caller.GetDbTransaction();
                boundary.Arm(physical);
                if (failureMode == BoundaryFailure.DetachAfterRelease)
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        PostgresCategoryReadPermissions.ApplyAsync(writer, reader.Enrollment));
                else if (failureMode == BoundaryFailure.ChangeTenantAfterRelease)
                {
                    var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        PostgresCategoryReadPermissions.ApplyAsync(writer, reader.Enrollment));
                    await Assert.That(denied!.Message).IsEqualTo("tenant_lease_identity_changed");
                }
                else if (failureMode == BoundaryFailure.ThrowBeforeRelease)
                    await Assert.ThrowsAsync<SavepointReleaseFailure>(() =>
                        PostgresCategoryReadPermissions.ApplyAsync(writer, reader.Enrollment));
                else
                {
                    var failure = await Assert.ThrowsAsync<AggregateException>(() =>
                        PostgresCategoryReadPermissions.ApplyAsync(writer, reader.Enrollment, cancellation.Token));
                    var causes = failure!.Flatten().InnerExceptions;
                    await Assert.That(causes.Any(cause => cause is SavepointRollbackFailure)).IsTrue();
                    await Assert.That(causes.Any(cause => failureMode == BoundaryFailure.CancelAndFailRollback
                        ? cause is OperationCanceledException canceled && canceled.CancellationToken == cancellation.Token
                        : cause is UnauthorizedAccessException)).IsTrue();
                }
                adapter.TenantId = selected.Tenant;

                await Assert.That(boundary.GrantedAtBoundary).IsTrue();
                await Assert.That(boundary.ObservedBoundary).IsTrue();
                if (failureMode == BoundaryFailure.DetachAfterRelease) await Assert.That(boundary.DetachedLiveOriginal).IsTrue();
                bool commitRejected = false;
                try { await physical.CommitAsync(); }
                catch (InvalidOperationException) { commitRejected = true; }
                bool preservesCallerWork = failureMode == BoundaryFailure.ThrowBeforeRelease;
                await Assert.That(commitRejected).IsEqualTo(!preservesCallerWork);

                var independent = Own(Open(PrimaryDatabaseRole.Migrator, selected.Tenant));
                await independent.Database.OpenConnectionAsync();
                string after = await AuthorityImageAsync(independent, reader.Login);
                await Assert.That(after == before).IsTrue()
                    .Because("A denied installer must leave no committable Category authority in its retained caller transaction.");
                bool retainedPriorWork = await independent.Categories.AsNoTracking().AnyAsync(category => category.Id == priorWork);
                await Assert.That(retainedPriorWork).IsEqualTo(preservesCallerWork);
            }
        }
        catch (Exception failure) { primary = failure; }
        adapter.TenantId = selected.Tenant;
        primary = await DisposeOwnedAsync(resources, primary);
        Exception? cleanup = null;
        try { await DropReaderAsync(reader.Options, selected.Tenant); }
        catch (Exception failure) { cleanup = failure; }
        if (primary is not null && cleanup is not null)
            throw new AggregateException("category_test_role_cleanup_failed", primary, cleanup);
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        if (cleanup is not null) ExceptionDispatchInfo.Capture(cleanup).Throw();
    }

    private async Task<(Guid Tenant, Guid Parent, Guid Child)> SeedAsync(bool includeChild)
    {
        Guid tenantId = Guid.CreateVersion7();
        var tenant = new Tenant
        {
            Id = tenantId,
            FullName = "Category permission tenant",
            Slug = "category-read-" + tenantId.ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var parent = new Category
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = tenant,
            MasterCode = "parent",
            FullName = includeChild ? "A parent" : "0 foreign tenant"
        };
        var child = new Category
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = tenant,
            MasterCode = "child",
            FullName = "B child",
            ParentId = parent.Id,
            Parent = parent
        };

        await using var writer = Open(PrimaryDatabaseRole.Migrator, tenantId);
        writer.Tenants.Add(tenant);
        writer.Categories.Add(parent);
        if (includeChild) writer.Categories.Add(child);
        await writer.SaveChangesAsync();
        return (tenantId, parent.Id, child.Id);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid tenant, params IInterceptor[] interceptors) =>
        Open(fixture.GetNativeQueueOptions(role), tenant, interceptors);

    private static ExploreDbContext Open(PrimaryDatabaseConnectionOptions database, Guid tenant,
        params IInterceptor[] interceptors) => Open(database, new SelectedTenant(tenant), interceptors);

    private static ExploreDbContext Open(PrimaryDatabaseConnectionOptions database, SelectedTenant? tenant,
        params IInterceptor[] interceptors)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, database);
        options.AddInterceptors(database.Role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        options.AddInterceptors(interceptors);
        options.UseNpgsql(provider => provider.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        var context = new ExploreDbContext(options.Options) { TenantContext = tenant };
        context.BindTenantLease();
        return context;
    }

    private async Task<(string Login, PrimaryDatabaseConnectionOptions Options, PostgresNativeQueueEnrollment Enrollment)>
        CreateReaderAsync(Guid tenant)
    {
        string login = "category_reader_" + Guid.CreateVersion7().ToString("N");
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await using var writer = Open(PrimaryDatabaseRole.Migrator, tenant);
        await writer.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(writer);
        await ExecuteAsync(writer, $"""
            CREATE ROLE {names.Quote(login)} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
            """);
        return (login, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime) with { Username = login, Password = password },
            fixture.NativeQueueEnrollment with { RuntimeLogin = login });
    }

    private async Task DropReaderAsync(PrimaryDatabaseConnectionOptions database, Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        var configured = PrimaryDatabaseProviderComposition.ConfigureApplication(options, database);
        await using (var pooled = new NpgsqlConnection(configured.ConnectionString)) NpgsqlConnection.ClearPool(pooled);
        await using var writer = Open(PrimaryDatabaseRole.Migrator, tenant);
        await writer.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(writer);
        await ExecuteAsync(writer, $"DROP OWNED BY {names.Quote(database.Username!)}; DROP ROLE {names.Quote(database.Username!)};");
    }

    private static async Task<string> AuthorityImageAsync(ExploreDbContext context, string login)
    {
        var names = new HumanPrincipalSourceSql(context);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT pg_catalog.jsonb_build_object(
                'schema',(SELECT nspacl::text FROM pg_catalog.pg_namespace WHERE nspname=@schema),
                'table',(SELECT relacl::text FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass(@table)),
                'relation',(SELECT jsonb_build_array(relowner,relrowsecurity,relforcerowsecurity,relkind,relpersistence,relispartition)
                    FROM pg_catalog.pg_class WHERE oid=pg_catalog.to_regclass(@table)),
                'policies',(SELECT jsonb_agg(jsonb_build_array(polname,polcmd,polpermissive,polroles::text,
                    pg_catalog.pg_get_expr(polqual,polrelid),pg_catalog.pg_get_expr(polwithcheck,polrelid),
                    pg_catalog.obj_description(oid,'pg_policy')) ORDER BY polname)
                    FROM pg_catalog.pg_policy WHERE polrelid=pg_catalog.to_regclass(@table)),
                'role',(SELECT jsonb_build_array(rolcanlogin,rolsuper,rolbypassrls,rolcreaterole,rolcreatedb,rolreplication)
                    FROM pg_catalog.pg_roles WHERE oid=pg_catalog.to_regrole(@login)),
                'columns',(SELECT jsonb_agg(jsonb_build_array(a.attnum,a.attacl::text) ORDER BY a.attnum)
                    FROM pg_catalog.pg_attribute a WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum>0),
                'edges',(SELECT jsonb_agg(jsonb_build_array(m.roleid,m.member,m.inherit_option,m.set_option,m.admin_option)
                    ORDER BY m.roleid,m.member) FROM pg_catalog.pg_auth_members m
                    WHERE m.roleid=pg_catalog.to_regrole(@login) OR m.member=pg_catalog.to_regrole(@login)))::text
            """;
        command.Parameters.Add(new NpgsqlParameter("schema", names.Schema));
        command.Parameters.Add(new NpgsqlParameter("table", names.Table(typeof(Category))));
        command.Parameters.Add(new NpgsqlParameter("login", login));
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> ScalarAsync(ExploreDbContext context, string sql, string login)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        command.Parameters.Add(new NpgsqlParameter("table", new HumanPrincipalSourceSql(context).Table(typeof(Category))));
        command.Parameters.Add(new NpgsqlParameter("login", login));
        return await command.ExecuteScalarAsync() is true;
    }

    private static async Task<bool> CategoryReadsGrantedAsync(ExploreDbContext context, DbTransaction physical, string login)
    {
        var names = new HumanPrincipalSourceSql(context);
        await using var command = physical.Connection!.CreateCommand();
        command.Transaction = physical;
        command.CommandText = """
            SELECT count(*)=6 FROM pg_catalog.pg_attribute a
            WHERE a.attrelid=pg_catalog.to_regclass(@table) AND a.attnum>0 AND NOT a.attisdropped
                AND pg_catalog.has_column_privilege(pg_catalog.to_regrole(@login)::oid,a.attrelid,a.attnum,'SELECT')
            """;
        command.Parameters.Add(new NpgsqlParameter("table", names.Table(typeof(Category))));
        command.Parameters.Add(new NpgsqlParameter("login", login));
        return await command.ExecuteScalarAsync() is true;
    }

    private static async Task ExecuteAsync(ExploreDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class SavepointBoundary(string reader, BoundaryFailure failureMode, CancellationTokenSource cancellation,
        SelectedTenant adapter)
        : DbTransactionInterceptor
    {
        private DbTransaction? target;
        private bool failRollback;
        internal bool ObservedBoundary { get; private set; }
        internal bool GrantedAtBoundary { get; private set; }
        internal bool DetachedLiveOriginal { get; private set; }
        internal void Arm(DbTransaction transaction) => target = transaction;

        public override async ValueTask<InterceptionResult> ReleasingSavepointAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (failureMode is BoundaryFailure.DetachAfterRelease or BoundaryFailure.ChangeTenantAfterRelease
                || !ReferenceEquals(target, transaction)) return result;
            target = null;
            GrantedAtBoundary = await CategoryReadsGrantedAsync((ExploreDbContext)eventData.Context!, transaction, reader);
            ObservedBoundary = true;
            if (failureMode is BoundaryFailure.CancelAndFailRollback or BoundaryFailure.DenyAndFailRollback)
            {
                failRollback = true;
                if (failureMode == BoundaryFailure.DenyAndFailRollback)
                    throw new UnauthorizedAccessException("category_test_primary_denial");
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            throw new SavepointReleaseFailure();
        }

        public override ValueTask<InterceptionResult> RollingBackToSavepointAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (failRollback) throw new SavepointRollbackFailure();
            return ValueTask.FromResult(result);
        }

        public override async Task ReleasedSavepointAsync(DbTransaction transaction, TransactionEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (failureMode is not (BoundaryFailure.DetachAfterRelease or BoundaryFailure.ChangeTenantAfterRelease)
                || !ReferenceEquals(target, transaction)) return;
            target = null;
            var context = (ExploreDbContext)eventData.Context!;
            GrantedAtBoundary = await CategoryReadsGrantedAsync(context, transaction, reader);
            ObservedBoundary = true;
            if (failureMode == BoundaryFailure.ChangeTenantAfterRelease)
            {
                adapter.TenantId = Guid.CreateVersion7();
                return;
            }
            context.Database.UseTransaction(null);
            DetachedLiveOriginal = context.Database.CurrentTransaction is null && transaction.Connection?.State == ConnectionState.Open;
        }
    }

    private sealed class SavepointReleaseFailure : Exception { }
    private sealed class SavepointRollbackFailure : Exception { }

    private sealed class UnexpectedCategoryContext(DbContextOptions<ExploreDbContext> options) : ExploreDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Category>().Property<string>("UnexpectedCategoryProperty");
        }
    }

    private enum BoundaryFailure
    {
        DetachAfterRelease, ThrowBeforeRelease, ChangeTenantAfterRelease, CancelAndFailRollback, DenyAndFailRollback
    }

    private sealed class SelectedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; set; } = tenantId;
    }
}
