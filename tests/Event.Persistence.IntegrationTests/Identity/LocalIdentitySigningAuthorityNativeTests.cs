using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Services.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
[ClassDataSource<LocalIdentitySigningAuthorityNativeTests.SigningFixture>(Shared = SharedType.PerAssembly)]
public sealed class LocalIdentitySigningAuthorityNativeTests(LocalIdentitySigningAuthorityNativeTests.SigningFixture fixture)
{
    [Test]
    public async Task ActualLocalWriterIssuesAnApiValidatedSessionWithoutPrivateSecretBindingAccess()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.ResetAsync();
        using var diagnostics = new SelectedWriterFailures();
        string? repositoryFailure = null;
        bool signatureValidated = false;
        AuthenticatedSettingsFixture authentication;
        try
        {
            authentication = await AuthenticatedSettingsFixture.CreateAsync(OpenInstanceMigrator, fixture,
                configureServices: services =>
                {
                    services.PostConfigure<JwtBearerOptions>(ApiAuthenticationSchemeNames.LocalIdentity, options =>
                    {
                        var validateSession = options.Events.OnTokenValidated;
                        options.Events.OnTokenValidated = async context =>
                        {
                            signatureValidated = true;
                            await validateSession(context);
                        };
                    });
                    services.RemoveAll<IUserRepository>();
                    services.AddScoped<IUserRepository>(provider =>
                    {
                        var context = provider.GetRequiredService<ExploreDbContext>();
                        if (ReferenceEquals(context, provider.GetRequiredService<LocalIdentityExecutionLease>().Application))
                            diagnostics.Select(context);
                        return new ObservedUserRepository(context, failure => repositoryFailure = failure,
                            diagnostics.EnterUpdate);
                    });
                });
        }
        catch (Exception failure)
        {
            throw new InvalidOperationException("Actual Local login failed; " + diagnostics.Summary()
                + "; repository_failure=" + (repositoryFailure ?? "none"), failure);
        }
        await using var ownedAuthentication = authentication;

        try
        {
            await authentication.RunAsync(null, async session =>
            {
                var http = session.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                await Assert.That(http.User.Identity!.IsAuthenticated).IsTrue();
                var authority = http.User.TryGetLocalSessionAuthority();
                await Assert.That(authority).IsNotNull();
                await Assert.That(authority!.LocalSubjectId).IsEqualTo(authentication.ActorId);

                await using var identity = await session.Services.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
                    .CreateAsync(token);
                var writer = identity.Services.GetRequiredService<ExploreDbContext>();
                await using (var command = writer.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "SELECT session_user";
                    await Assert.That(await command.ExecuteScalarAsync(token)).IsEqualTo(
                        fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter).Username);
                }
                var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                {
                    _ = await writer.SecretBindings.AsNoTracking().FirstOrDefaultAsync(
                        binding => binding.SettingKey == SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, token);
                });
                await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
                return true;
            }, cancellationToken: token);
        }
        catch (Exception failure)
        {
            throw new InvalidOperationException(
                $"Actual Local session validation failed; signature_validated={signatureValidated}.", failure);
        }
        await Assert.That(signatureValidated).IsTrue();
    }

    [Test]
    public async Task ExactLocalAccountResolutionUsesWriterBindingWithoutPrivateBirthReads()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.ResetAsync();
        await fixture.PrepareLocalIdentityWriterAsync();
        Guid subject = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        var account = new ProviderAccountKey(AuthenticationProviderKind.Local, subject.ToString("D"));
        await using (var seed = OpenInstanceMigrator())
        {
            await using var human = new HumanSecuritySourceGate(seed);
            await using var humanLease = await human.AcquireAsync(token);
            await using var discovery = new EventDiscoveryEnrollmentGate(seed);
            await using var discoveryLease = await discovery.AcquireAsync(token);
            var user = new User
            {
                Id = subject,
                EmailVerified = true,
                Pii = new UserPii
                {
                    Email = $"lookup-{subject:N}@example.test",
                    FirstName = "Identity",
                    LastName = "Lookup"
                }
            };
            seed.Users.Add(user);
            seed.Actors.Add(new Actor
            {
                Id = actorId,
                UserId = subject,
                User = user,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Identity lookup" }
            });
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = subject,
                User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                AuthenticationProvider = null!,
                ProviderKey = account.Value
            });
            await seed.SaveChangesAsync(token);
        }

        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter);
        var enrollment = new PostgresLocalIdentityWriterEnrollment(fixture.NativeQueueEnrollment.MigratorLogin,
            fixture.NativeQueueEnrollment.RuntimeLogin, options.Username!, fixture.NativeQueueEnrollment.GlobalLogin);
        var factory = new LocalIdentityWriterDbContextFactory(options, poolSize: 1, applicationEnrollment: enrollment);
        await using var selected = await factory.CreateAsync(null, token);
        var application = selected.Application;
        await using (var session = application.Database.GetDbConnection().CreateCommand())
        {
            session.CommandText = "SELECT session_user";
            await Assert.That(await session.ExecuteScalarAsync(token)).IsEqualTo(options.Username);
        }

        // Each denial is outside an account transaction so it cannot poison the lookup.
        // Null private values still require SELECT authority; no birth declaration is fabricated.
        foreach (string property in new[] { "_dateOfBirth", "_birthDateCollectionReceiptId", "_birthDateRevision" })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                _ = await application.UserPii.Where(pii => pii.UserId == subject)
                    .Select(pii => EF.Property<object>(pii, property)).SingleAsync(token);
            });
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }

        var resolver = new IdentityAccountResolver(new UserRepository(application),
            new UserExternalLoginRepository(application), new UserIdentityEmailRepository(application),
            new PrivacyErasureStateRepository(application), Options.Create(new IdentityCorrelationOptions()));
        IdentityAccountResolution resolution = await resolver.ResolveAsync(account, null, token);

        await Assert.That(resolution.Decision).IsEqualTo(IdentityCorrelationDecision.ExactBinding);
        await Assert.That(resolution.User).IsNotNull();
        await Assert.That(resolution.User!.Id).IsEqualTo(subject);
        await Assert.That(resolution.User.Pii).IsNull();
        await Assert.That(resolution.User.Actor).IsNotNull();
        await Assert.That(resolution.User.Actor!.Id).IsEqualTo(actorId);
        await Assert.That(resolution.User.Actor.UserId).IsEqualTo(subject);
        await Assert.That(resolution.User.Actor.Pii).IsNull();
        await Assert.That(application.ChangeTracker.Entries<UserPii>().Any()).IsFalse();
    }

    [Test]
    public async Task LocalBindingReadPreservesAccountAndActorAdmissionFactsWithoutPrivateStorageAccess()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.ResetAsync();
        await fixture.PrepareLocalIdentityWriterAsync();
        string[] cases = ["active", "deleted-user", "deleted-actor", "suspended-actor", "wrong-type", "foreign-owner"];
        var subjects = cases.ToDictionary(value => value, _ => Guid.CreateVersion7());
        Guid foreignOwner = Guid.CreateVersion7();
        await using (var seed = OpenInstanceMigrator())
        {
            await using var human = new HumanSecuritySourceGate(seed);
            await using var humanLease = await human.AcquireAsync(token);
            await using var discovery = new EventDiscoveryEnrollmentGate(seed);
            await using var discoveryLease = await discovery.AcquireAsync(token);
            foreach (Guid id in subjects.Values.Append(foreignOwner))
                seed.Users.Add(new User
                {
                    Id = id,
                    IsDeleted = id == subjects["deleted-user"],
                    Pii = new UserPii { Email = $"binding-{id:N}@example.test", FirstName = "Binding", LastName = "Reader" }
                });
            foreach (string scenario in cases)
                seed.Actors.Add(new Actor
                {
                    Id = Guid.CreateVersion7(),
                    UserId = scenario == "foreign-owner" ? foreignOwner : subjects[scenario],
                    ActorTypeId = (int)(scenario == "wrong-type" ? ActorTypeEnum.Group : ActorTypeEnum.User),
                    ActorType = null!,
                    IsDeleted = scenario == "deleted-actor",
                    IsSuspended = scenario == "suspended-actor",
                    Pii = new ActorPii { DisplayName = "Binding reader" }
                });
            await seed.SaveChangesAsync(token);
        }

        var writerOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter);
        var enrollment = new PostgresLocalIdentityWriterEnrollment(fixture.NativeQueueEnrollment.MigratorLogin,
            fixture.NativeQueueEnrollment.RuntimeLogin, writerOptions.Username!, fixture.NativeQueueEnrollment.GlobalLogin);
        var factory = new LocalIdentityWriterDbContextFactory(writerOptions, poolSize: 1, applicationEnrollment: enrollment);
        await using var selected = await factory.CreateAsync(null, token);
        IUserRepository repository = new UserRepository(selected.Application);
        foreach (string scenario in cases)
        {
            User? user = await repository.GetLocalIdentityBindingAsync(subjects[scenario], token);
            if (scenario == "deleted-user")
            {
                await Assert.That(user).IsNull();
                continue;
            }
            await Assert.That(user).IsNotNull();
            await Assert.That(user!.Id).IsEqualTo(subjects[scenario]);
            await Assert.That(user.Pii).IsNull();
            if (scenario is "deleted-actor" or "foreign-owner")
            {
                await Assert.That(user.Actor).IsNull();
                continue;
            }
            await Assert.That(user.Actor).IsNotNull();
            await Assert.That(user.Actor!.UserId).IsEqualTo(user.Id);
            await Assert.That(user.Actor.Pii).IsNull();
            await Assert.That(user.Actor.IsSuspended).IsEqualTo(scenario == "suspended-actor");
            await Assert.That(user.Actor.ActorTypeId)
                .IsEqualTo((int)(scenario == "wrong-type" ? ActorTypeEnum.Group : ActorTypeEnum.User));
        }
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            _ = await selected.Application.StorageObjects.Select(value => value.Id).FirstOrDefaultAsync(token);
        });
        await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task BootstrapObservationUsesOriginalWriterAndCannotReadPrivateClaimFingerprintsOrWriteState()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.ResetAsync();
        await fixture.PrepareLocalIdentityWriterAsync();
        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter);
        var enrollment = new PostgresLocalIdentityWriterEnrollment(fixture.NativeQueueEnrollment.MigratorLogin,
            fixture.NativeQueueEnrollment.RuntimeLogin, options.Username!, fixture.NativeQueueEnrollment.GlobalLogin);
        var factory = new LocalIdentityWriterDbContextFactory(options, poolSize: 1, applicationEnrollment: enrollment);
        await using var selected = await factory.CreateAsync(null, token);
        var context = selected.Application;
        var reader = new IdentitySynchronizationBootstrapReader(context);
        await Assert.That(await reader.GetCurrentAsync(token)).IsNull();

        Guid completedBy = Guid.CreateVersion7();
        DateTime created = DateTime.UtcNow;
        string fingerprint = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await using (var seed = OpenInstanceMigrator())
        {
            await using var human = new HumanSecuritySourceGate(seed);
            await using var humanLease = await human.AcquireAsync(token);
            await using var discovery = new EventDiscoveryEnrollmentGate(seed);
            await using var discoveryLease = await discovery.AcquireAsync(token);
            seed.Users.Add(new User { Id = completedBy, Pii = null! });
            var latest = InstanceBootstrapState.CreateConfiguredAdministratorPending(Guid.CreateVersion7(),
                AuthenticationProviderKind.Local, DeploymentMode.SingleTenant, 2, fingerprint, fingerprint, created);
            latest.CompleteConfiguredAdministrator(AuthenticationProviderKind.Local, 2, fingerprint, completedBy, created.AddSeconds(1));
            seed.InstanceBootstrapStates.AddRange(latest,
                InstanceBootstrapState.CreateConfiguredAdministratorPending(Guid.CreateVersion7(),
                    AuthenticationProviderKind.Keycloak, DeploymentMode.MultiTenant, 1, fingerprint, fingerprint, created.AddMinutes(1)));
            await seed.SaveChangesAsync(token);
        }

        var connection = context.Database.GetDbConnection();
        var contextId = context.ContextId;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            var observed = await reader.GetCurrentAsync(token);
            await Assert.That(observed).IsNotNull();
            await Assert.That(observed!.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
            await Assert.That(observed.Mode).IsEqualTo(InstanceBootstrapMode.ConfiguredAdministrator);
            await Assert.That(observed.ProviderKind).IsEqualTo(AuthenticationProviderKind.Local);
            await Assert.That(observed.CompletedByUserId).IsEqualTo(completedBy);
            await Assert.That(context.ContextId).IsEqualTo(contextId);
            await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connection);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync(token);
        });
        foreach (string property in new[] { nameof(InstanceBootstrapState.ConfigurationFingerprint),
            nameof(InstanceBootstrapState.SelectorFingerprint), nameof(InstanceBootstrapState.CompletedIdentityFingerprint) })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                _ = await context.InstanceBootstrapStates.Select(state => EF.Property<string?>(state, property)).FirstOrDefaultAsync(token);
            });
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        var fullRead = await Assert.ThrowsAsync<PostgresException>(() => new InstanceBootstrapStateRepository(context).GetCurrent(token));
        await Assert.That(fullRead!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        var delete = await Assert.ThrowsAsync<PostgresException>(() => context.InstanceBootstrapStates.ExecuteDeleteAsync(token));
        await Assert.That(delete!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task BootstrapPrivateColumnGrantDriftIsRejectedWithoutRepair()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.PrepareLocalIdentityWriterAsync();
        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter);
        var enrollment = new PostgresLocalIdentityWriterEnrollment(fixture.NativeQueueEnrollment.MigratorLogin,
            fixture.NativeQueueEnrollment.RuntimeLogin, options.Username!, fixture.NativeQueueEnrollment.GlobalLogin);
        await using var context = OpenInstanceMigrator();
        var entity = context.Model.FindEntityType(typeof(InstanceBootstrapState))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var names = context.GetService<ISqlGenerationHelper>();
        string target = names.DelimitIdentifier(table.Name, table.Schema);
        string column = entity.FindProperty(nameof(InstanceBootstrapState.SelectorFingerprint))!.GetColumnName(table)!;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            await context.Database.ExecuteSqlRawAsync(
                $"GRANT SELECT ({names.DelimitIdentifier(column)}) ON {target} TO {names.DelimitIdentifier(options.Username!)}", token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresLocalIdentityWriterPermissions.ApplyAsync(
                context, enrollment, IdentityDatabaseTopology.Colocated, LocalIdentityWriterInventory.Store.Application, token));
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT pg_catalog.has_column_privilege(@role,@table,@column,'SELECT')";
            command.Parameters.Add(new NpgsqlParameter("role", options.Username!));
            command.Parameters.Add(new NpgsqlParameter("table", target));
            command.Parameters.Add(new NpgsqlParameter("column", column));
            await Assert.That(await command.ExecuteScalarAsync(token)).IsEqualTo(true);
            await transaction.RollbackAsync(token);
        });
        await PostgresLocalIdentityWriterPermissions.ApplyAsync(context, enrollment,
            IdentityDatabaseTopology.Colocated, LocalIdentityWriterInventory.Store.Application, token);
    }

    [Test]
    public async Task ErasureExistenceObservationRetainsRefusalWithoutPrivateSagaPayloadAccess()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.ResetAsync();
        await fixture.PrepareLocalIdentityWriterAsync();
        Guid subject = Guid.CreateVersion7();
        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter);
        var enrollment = new PostgresLocalIdentityWriterEnrollment(fixture.NativeQueueEnrollment.MigratorLogin,
            fixture.NativeQueueEnrollment.RuntimeLogin, options.Username!, fixture.NativeQueueEnrollment.GlobalLogin);
        var factory = new LocalIdentityWriterDbContextFactory(options, poolSize: 1, applicationEnrollment: enrollment);
        await using var selected = await factory.CreateAsync(null, token);
        var context = selected.Application;
        IPrivacyErasureStateRepository repository = new PrivacyErasureStateRepository(context);
        await Assert.That(await repository.ExistsBySubjectAsync(subject, token)).IsFalse();
        DateTime now = DateTime.UtcNow;
        var intent = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
            subject, PrivacyErasureReasonCode.AccountDeletion, 1, now, now);
        await using (var seed = OpenInstanceMigrator())
        {
            await using var human = new HumanSecuritySourceGate(seed);
            await using var humanLease = await human.AcquireAsync(token);
            await using var discovery = new EventDiscoveryEnrollmentGate(seed);
            await using var discoveryLease = await discovery.AcquireAsync(token);
            seed.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(intent, 1,
                RandomNumberGenerator.GetBytes(32), now.AddMinutes(5), now));
            await seed.SaveChangesAsync(token);
        }
        await Assert.That(await repository.ExistsBySubjectAsync(subject, token)).IsTrue();
        await Assert.That(await repository.ExistsBySubjectAsync(Guid.CreateVersion7(), token)).IsFalse();
        Func<Task>[] privateReads =
        [
            async () => { _ = await repository.GetBySubjectAsync(subject, token); },
            async () => { _ = await context.PrivacyErasureSagas.Select(saga => saga.ReceiptHash).FirstOrDefaultAsync(token); },
            async () => { _ = await context.PrivacyErasureSagas.Select(saga => saga.FenceToken).FirstOrDefaultAsync(token); },
            async () => { _ = await context.PrivacyErasureSagas.Select(saga => saga.Status).FirstOrDefaultAsync(token); }
        ];
        foreach (var read in privateReads)
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(read);
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        var delete = await Assert.ThrowsAsync<PostgresException>(() => context.PrivacyErasureSagas.ExecuteDeleteAsync(token));
        await Assert.That(delete!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RestrictedWriterSynchronizesOnlyTheEmailMirrorAndRollsBackAfterAccountSaveFailure(bool failAfterAccountSave)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await fixture.ResetAsync();
        await fixture.PrepareLocalIdentityWriterAsync();
        Guid subject = Guid.CreateVersion7();
        string originalEmail = $"profile-{subject:N}@example.test";
        string synchronizedEmail = $"synchronized-{subject:N}@example.test";
        Guid originalStamp;
        await using (var seed = OpenInstanceMigrator())
        {
            await using var human = new HumanSecuritySourceGate(seed);
            await using var humanLease = await human.AcquireAsync(token);
            await using var discovery = new EventDiscoveryEnrollmentGate(seed);
            await using var discoveryLease = await discovery.AcquireAsync(token);
            var user = new User
            {
                Id = subject,
                EmailVerified = false,
                Pii = new UserPii { Email = originalEmail, FirstName = "Profile", LastName = "Owner" }
            };
            seed.Users.Add(user);
            await seed.SaveChangesAsync(token);
            originalStamp = user.ConcurrencyStamp;
        }

        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.LocalIdentityWriter);
        var enrollment = new PostgresLocalIdentityWriterEnrollment(fixture.NativeQueueEnrollment.MigratorLogin,
            fixture.NativeQueueEnrollment.RuntimeLogin, options.Username!, fixture.NativeQueueEnrollment.GlobalLogin);
        var failure = new InvalidOperationException("The email mirror write failed after the account save.");
        var fault = new IdentityEmailWriteFault(failure);
        var factory = new LocalIdentityWriterDbContextFactory(options, poolSize: 1,
            applicationEnrollment: enrollment, interceptors: [fault]);
        await using var selected = await factory.CreateAsync(null, token);
        var application = selected.Application;
        await using var writerHuman = new HumanSecuritySourceGate(application);
        await using var writerHumanLease = await writerHuman.AcquireAsync(token);
        await using var writerDiscovery = new EventDiscoveryEnrollmentGate(application);
        await using var writerDiscoveryLease = await writerDiscovery.AcquireAsync(token);
        IUserRepository repository = new UserRepository(application);
        User? account = null;
        bool accountSaved = false;
        void AfterAccountSave(object? sender, SavedChangesEventArgs args)
        {
            if (!ReferenceEquals(sender, application) || args.EntitiesSavedCount <= 0
                || account is null || account.ConcurrencyStamp == originalStamp)
                return;
            accountSaved = true;
            fault.Armed = failAfterAccountSave;
        }
        Task<bool> SynchronizeAsync() => new EfCoreUnitOfWork(application).ExecuteSerializableAsync(async ct =>
        {
            var transaction = application.Database.CurrentTransaction;
            await Assert.That(transaction).IsNotNull();
            account = await repository.GetIdentityAccountForSynchronizationAsync(subject, ct);
            await Assert.That(account).IsNotNull();
            await Assert.That(account!.Pii).IsNull();
            await Assert.That(application.Entry(account).State).IsEqualTo(EntityState.Unchanged);
            await repository.SaveIdentityProfileAsync(account, synchronizedEmail, true, ct);
            await Assert.That(application.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            return true;
        }, token);

        application.SavedChanges += AfterAccountSave;
        try
        {
            if (failAfterAccountSave)
            {
                var observed = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    _ = await SynchronizeAsync();
                });
                await Assert.That(observed).IsSameReferenceAs(failure);
            }
            else
            {
                await Assert.That(await SynchronizeAsync()).IsTrue();
            }
        }
        finally
        {
            application.SavedChanges -= AfterAccountSave;
            fault.Armed = false;
        }
        await Assert.That(accountSaved).IsTrue();
        await Assert.That(fault.Reached).IsEqualTo(failAfterAccountSave);
        await Assert.That(application.Database.CurrentTransaction).IsNull();
        application.ChangeTracker.Clear();
        var persisted = await application.Users.IgnoreAutoIncludes().AsNoTracking()
            .Where(user => user.Id == subject)
            .Select(user => new { user.EmailVerified, user.ConcurrencyStamp, user.IsDeleted, user.LastActiveTenantId })
            .SingleAsync(token);
        var profile = await application.UserPii.AsNoTracking().Where(pii => pii.UserId == subject)
            .Select(pii => new { pii.Email, pii.FirstName, pii.LastName }).SingleAsync(token);
        await Assert.That(persisted.EmailVerified).IsEqualTo(!failAfterAccountSave);
        await Assert.That(persisted.ConcurrencyStamp == originalStamp).IsEqualTo(failAfterAccountSave);
        await Assert.That(profile.Email == (failAfterAccountSave ? originalEmail : synchronizedEmail)).IsTrue();
        await Assert.That(profile.FirstName == "Profile" && profile.LastName == "Owner").IsTrue();
        await Assert.That(persisted.IsDeleted).IsFalse();
        await Assert.That(persisted.LastActiveTenantId).IsNull();
        await Assert.That(application.ChangeTracker.Entries<UserPii>().Any()).IsFalse();
        foreach (string property in new[] { "_dateOfBirth", "_birthDateCollectionReceiptId", "_birthDateRevision" })
        {
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                _ = await application.UserPii.Where(pii => pii.UserId == subject)
                    .Select(pii => EF.Property<object>(pii, property)).SingleAsync(token);
            });
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    private sealed class IdentityEmailWriteFault(Exception failure) : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Reached { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.CommandSource == CommandSource.ExecuteUpdate)
            {
                Reached = true;
                throw failure;
            }
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private ExploreDbContext OpenInstanceMigrator()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    public sealed class SigningFixture : PostgreSqlContainerFixture;

    private sealed class ObservedUserRepository(ExploreDbContext context, Action<string> observe, Action enterUpdate)
        : UserRepository(context), IUserRepository
    {
        async Task<User?> IUserRepository.GetLocalIdentityBindingAsync(Guid id, CancellationToken cancellationToken)
        {
            try
            {
                return await base.GetLocalIdentityBindingAsync(id, cancellationToken);
            }
            catch (Exception failure)
            {
                var chain = new List<Exception>();
                for (Exception? current = failure; current is not null && chain.Count < 8; current = current.InnerException)
                    chain.Add(current);
                var methods = chain.SelectMany(exception => new StackTrace(exception, fNeedFileInfo: false).GetFrames() ?? [])
                    .Select(frame => frame.GetMethod()).Where(method => method is not null)
                    .Select(method => $"{method!.DeclaringType?.FullName}.{method.Name}")
                    .Distinct(StringComparer.Ordinal).Take(32);
                observe($"operation=UserRepository.GetLocalIdentityBindingAsync; types={string.Join(',', chain.Select(exception => exception.GetType().FullName))}; "
                    + $"sql_states={string.Join(',', chain.OfType<PostgresException>().Select(exception => exception.SqlState))}; "
                    + $"methods={string.Join(',', methods)}");
                throw;
            }
        }

        public override async Task Update(User entity)
        {
            enterUpdate();
            try
            {
                await base.Update(entity);
            }
            catch (Exception failure)
            {
                var chain = new List<Exception>();
                for (Exception? current = failure; current is not null && chain.Count < 8; current = current.InnerException)
                    chain.Add(current);
                string[] properties;
                try
                {
                    properties = context.Entry(entity).Properties.Where(property => property.IsModified)
                        .Select(property => property.Metadata.Name).Order(StringComparer.Ordinal).ToArray();
                }
                catch
                {
                    properties = ["unavailable"];
                }
                var methods = chain.SelectMany(exception => new StackTrace(exception, fNeedFileInfo: false).GetFrames() ?? [])
                    .Select(frame => frame.GetMethod()).Where(method => method is not null)
                    .Select(method => $"{method!.DeclaringType?.FullName}.{method.Name}")
                    .Distinct(StringComparer.Ordinal).Take(64);
                observe($"operation=UserRepository.Update; types={string.Join(',', chain.Select(exception => exception.GetType().FullName))}; "
                    + $"sql_states={string.Join(',', chain.OfType<PostgresException>().Select(exception => exception.SqlState))}; "
                    + $"modified_properties={string.Join(',', properties)}; methods={string.Join(',', methods)}");
                throw;
            }
        }
    }

    private sealed class SelectedWriterFailures : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly object sync = new();
        private readonly HashSet<ExploreDbContext> contexts = new(ReferenceEqualityComparer.Instance);
        private readonly List<IDisposable> subscriptions = [];
        private readonly List<string> failures = [];
        private readonly IDisposable listeners;
        private bool updateEntered;
        private bool disposed;

        internal SelectedWriterFailures() => listeners = DiagnosticListener.AllListeners.Subscribe(this);

        internal void Select(ExploreDbContext context)
        {
            lock (sync) contexts.Add(context);
        }

        internal void EnterUpdate()
        {
            lock (sync) updateEntered = true;
        }

        internal string Summary()
        {
            lock (sync)
                return $"selected_repository_created={contexts.Count != 0}; update_entered={updateEntered}; "
                    + $"ef_failures=[{string.Join(" | ", failures)}]";
        }

        public void OnNext(DiagnosticListener listener)
        {
            try
            {
                if (listener.Name != DbLoggerCategory.Name) return;
                lock (sync)
                {
                    if (!disposed)
                        subscriptions.Add(listener.Subscribe(this, name =>
                            name == RelationalEventId.CommandError.Name || name == CoreEventId.SaveChangesFailed.Name));
                }
            }
            catch
            {
                // A diagnostic subscription must never alter the actual operation.
            }
        }

        public void OnNext(KeyValuePair<string, object?> diagnostic)
        {
            try
            {
                (DbContext? Context, Exception? Failure) observed = diagnostic.Value switch
                {
                    CommandErrorEventData command => (command.Context, command.Exception),
                    DbContextErrorEventData save => (save.Context, save.Exception),
                    _ => (null, null)
                };
                lock (sync)
                {
                    if (disposed || failures.Count >= 4 || observed.Context is not ExploreDbContext context
                        || !contexts.Contains(context) || observed.Failure is null)
                        return;
                    var chain = new List<Exception>();
                    for (Exception? current = observed.Failure; current is not null && chain.Count < 8; current = current.InnerException)
                        chain.Add(current);
                    var methods = chain.SelectMany(exception => new StackTrace(exception, fNeedFileInfo: false).GetFrames() ?? [])
                        .Select(frame => frame.GetMethod()).Where(method => method is not null)
                        .Select(method => $"{method!.DeclaringType?.FullName}.{method.Name}")
                        .Distinct(StringComparer.Ordinal).Take(32);
                    failures.Add($"types={string.Join(',', chain.Select(exception => exception.GetType().FullName))}; "
                        + $"sql_states={string.Join(',', chain.OfType<PostgresException>().Select(exception => exception.SqlState))}; "
                        + $"mapped_entity_types={ClassifyPermissionFailure(context, chain)}; "
                        + $"methods={string.Join(',', methods)}");
                }
            }
            catch
            {
                // Never replace the original EF failure with an observer failure.
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }

        private static string ClassifyPermissionFailure(ExploreDbContext context, IEnumerable<Exception> failures)
        {
            try
            {
                var matches = new HashSet<string>(StringComparer.Ordinal);
                foreach (var failure in failures.OfType<PostgresException>()
                    .Where(exception => exception.SqlState == PostgresErrorCodes.InsufficientPrivilege))
                {
                    foreach (var entity in context.Model.GetEntityTypes())
                    {
                        if (entity.GetTableName() is { } table
                            && string.Equals(failure.MessageText, $"permission denied for table {table}", StringComparison.Ordinal))
                            matches.Add(entity.ClrType.FullName ?? entity.ClrType.Name);
                    }
                }
                return matches.Count == 0 ? "unknown" : string.Join(',', matches.Order(StringComparer.Ordinal).Take(8));
            }
            catch
            {
                return "unknown";
            }
        }

        public void Dispose()
        {
            IDisposable[] captured;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                captured = subscriptions.ToArray();
                subscriptions.Clear();
                contexts.Clear();
            }
            listeners.Dispose();
            foreach (var subscription in captured) subscription.Dispose();
        }
    }
}
