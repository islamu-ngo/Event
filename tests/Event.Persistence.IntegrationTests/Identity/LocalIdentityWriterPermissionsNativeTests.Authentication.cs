using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Authentication;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed partial class LocalIdentityWriterPermissionsNativeTests
{
    [Test]
    public Task ConfiguredRetryingAuthenticationCommitsOneFailedAccessAndPreservesTheOriginalSelectedCustody() =>
        ExerciseConfiguredAuthenticationAsync(failAfterBookkeeping: false);

    [Test]
    public Task TransientFailureAfterActualAuthenticationBookkeepingRollsBackWithoutReplayingThePasswordAttempt() =>
        ExerciseConfiguredAuthenticationAsync(failAfterBookkeeping: true);

    [Test]
    public Task CleanupDiagnosticFailureRetainsTheOriginalAuthenticationFailureWithoutReplayingTheAttempt() =>
        ExerciseConfiguredAuthenticationAsync(failAfterBookkeeping: true, failDuringCleanup: true);

    private static async Task ExerciseConfiguredAuthenticationAsync(bool failAfterBookkeeping, bool failDuringCleanup = false)
    {
        await using var fixture = new NativeFixture(IdentityDatabaseTopology.Colocated);
        // Admission failures here remain fixture prerequisites, not evidence about authentication.
        await fixture.InitializeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken token = timeout.Token;
        var migratorOptions = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator);
        var writerOptions = migratorOptions with
        {
            Role = PrimaryDatabaseRole.LocalIdentityWriter,
            Username = $"local_auth_writer_{Guid.CreateVersion7():N}",
            Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        };
        var enrollment = new PostgresLocalIdentityWriterEnrollment(migratorOptions.Username!,
            fixture.NativeQueueEnrollment.RuntimeLogin, writerOptions.Username!, fixture.NativeQueueEnrollment.GlobalLogin);
        Guid initiator = Guid.CreateVersion7();
        await using (var migrator = (ExploreDbContext)CreateContext(migratorOptions, LocalIdentityWriterInventory.Store.Application))
        {
            await migrator.Database.OpenConnectionAsync(token);
            await EnsureDeclaredTablesAsync(migrator, IdentityDatabaseTopology.Colocated, LocalIdentityWriterInventory.Store.Application);
            await using (var role = Command(migrator,
                $"CREATE ROLE {Identifier(writerOptions.Username!)} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD {Literal(writerOptions.Password!)}"))
                await role.ExecuteNonQueryAsync(token);
            await PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator, enrollment,
                IdentityDatabaseTopology.Colocated, LocalIdentityWriterInventory.Store.Application, token);
            await using var human = new HumanSecuritySourceGate(migrator);
            await using var gate = await human.AcquireAsync(token);
            migrator.Users.Add(new User
            {
                Id = initiator,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                Pii = new UserPii { Email = $"initiator-{initiator:N}@example.test", FirstName = "Native", LastName = "Initiator" }
            });
            await migrator.SaveChangesAsync(token);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new LocalIdentityWriterDbContextFactory(writerOptions, poolSize: 1,
            applicationEnrollment: enrollment));
        var runtime = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime);
        services.AddPooledDbContextFactory<ExploreDbContext>(options =>
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, runtime);
            options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        });
        services.AddScoped<ExploreDbContextExecutionLease>();
        services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>()
            .AddUserValidator<OptionalEmailLocalIdentityUserValidator>().AddEntityFrameworkStores<ExploreDbContext>();
        services.AddLocalIdentityExecutionContextSelection(IdentityDatabaseTopology.Colocated);
        await using var provider = services.BuildIsolatedServiceProvider(validateScopes: true);
        await using var request = provider.CreateAsyncScope();
        var selected = await request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>().CreateAsync(token);
        bool selectedRetired = false;
        Exception? bodyFailure = null;
        try
        {
            var execution = selected.Services.GetRequiredService<LocalIdentityExecutionLease>();
            var application = selected.Services.GetRequiredService<ExploreDbContext>();
            var manager = selected.Services.GetRequiredService<UserManager<LocalIdentityUser>>();
            execution.RequireCurrent(application, application);
            await Assert.That(application.Database.CreateExecutionStrategy().RetriesOnFailure).IsTrue();
            await RequireSessionAsync(application, enrollment.WriterLogin);
            var connector = (NpgsqlConnection)application.Database.GetDbConnection();
            int backend = connector.ProcessID;
            var contextId = application.ContextId;
            var states = new LocalIdentityCredentialStateStore(application, application, manager, TimeProvider.System, execution);
            string address = $"native-auth-{Guid.CreateVersion7():N}@example.test";
            var creation = await states.CreatePendingAsync(new LocalCredentialCreateRequest(Guid.CreateVersion7(), initiator,
                address, "Native", "Authentication"), token);
            await Assert.That(creation.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            var receipt = creation.Receipt!;
            var user = new User
            {
                Id = receipt.LocalSubjectId,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                Pii = new UserPii { Email = address, FirstName = "Native", LastName = "Authentication" }
            };
            application.Actors.Add(new Actor
            {
                Id = receipt.PersonalActorId,
                UserId = user.Id,
                User = user,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                CreatedAt = DateTime.UtcNow,
                Pii = new ActorPii { DisplayName = "Native Authentication" }
            });
            application.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = receipt.ExternalLoginId,
                UserId = user.Id,
                User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                AuthenticationProvider = null!,
                ProviderKey = user.Id.ToString("D"),
                CreatedAt = DateTime.UtcNow
            });
            await application.SaveChangesAsync(token);
            application.ChangeTracker.Clear();
            var provisioning = await states.ReadProvisioningAsync(receipt.OperationId, token);
            var activated = await states.ActivateChangeRequiredAsync(new LocalCredentialActivationRequest(receipt.OperationId,
                provisioning!.OperationConcurrencyStamp), token);
            await Assert.That(activated).IsEqualTo(LocalCredentialActivationOutcome.Activated);
            var before = await application.Set<LocalIdentityEligibility>().AsNoTracking()
                .SingleAsync(row => row.LocalSubjectId == receipt.LocalSubjectId, token);
            int beforeAttempts = await application.LocalIdentityUsers.AsNoTracking().Where(row => row.Id == receipt.LocalSubjectId)
                .Select(row => row.AccessFailedCount).SingleAsync(token);
            await Assert.That(before.State).IsEqualTo(LocalIdentityEligibilityState.ChangeRequired);

            var signer = new LocalJwtTokenGenerator(Options.Create(new LocalIdentityOptions
            { JwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) }), TimeProvider.System);
            var authentication = new LocalIdentityAuthService(manager, signer, states);
            if (failAfterBookkeeping)
            {
                var failure = new TimeoutException("The native authentication bookkeeping fault was injected after its actual save.");
                var cleanupFailure = new InvalidOperationException("The original authentication transaction disposal diagnostic failed.");
                using var cleanupDiagnostics = failDuringCleanup ? new AuthenticationCleanupFault(application, cleanupFailure) : null;
                int physicalSaves = 0;
                bool originalTransactionWasLive = false;
                void FailAfterActualBookkeeping(object? sender, SavedChangesEventArgs args)
                {
                    if (!ReferenceEquals(sender, application) || args.EntitiesSavedCount <= 0
                        || !application.LocalIdentityUsers.Local.Any(value => value.Id == receipt.LocalSubjectId
                            && value.AccessFailedCount == beforeAttempts + 1))
                        return;
                    physicalSaves++;
                    originalTransactionWasLive = application.Database.CurrentTransaction is not null;
                    cleanupDiagnostics?.Arm(application.Database.CurrentTransaction
                        ?? throw new InvalidOperationException("The cleanup diagnostic requires actual transactional bookkeeping."));
                    throw failure;
                }

                Exception? observed = null;
                application.SavedChanges += FailAfterActualBookkeeping;
                try
                {
                    await authentication.AuthenticateAsync(
                        new LocalAuthRequestDto(address, Guid.CreateVersion7().ToString("N")), token);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }
                finally
                {
                    application.SavedChanges -= FailAfterActualBookkeeping;
                }
                if (failDuringCleanup)
                {
                    Exception? invariantFailure = null;
                    Exception? retirementRefusal = null;
                    try
                    {
                        if (observed is not AggregateException combined)
                            throw new InvalidOperationException(
                                $"The dual-failure premise was not reached: observedType={observed?.GetType().FullName ?? "none"}; primaryRetained={ReferenceEquals(observed, failure)}; cleanupDiagnosticReached={cleanupDiagnostics!.Reached}; actualSaves={physicalSaves}.",
                                observed);
                        var causes = combined.Flatten().InnerExceptions;
                        await Assert.That(causes.Any(cause => ReferenceEquals(cause, failure))).IsTrue();
                        await Assert.That(causes.Any(cause => ReferenceEquals(cause, cleanupFailure))).IsTrue();
                        await Assert.That(cleanupDiagnostics!.Reached).IsTrue();
                        await Assert.That(physicalSaves).IsEqualTo(1);
                        await Assert.That(originalTransactionWasLive).IsTrue();
                    }
                    catch (Exception failureDuringAssertion)
                    {
                        invariantFailure = failureDuringAssertion;
                    }
                    finally
                    {
                        cleanupDiagnostics!.Dispose();
                        // The diagnostic interrupted EF after physical disposal but before its
                        // CurrentTransaction bookkeeping cleared. Strict custody must refuse that
                        // stale wrapper while the owner still releases all retained resources.
                        selectedRetired = true;
                        try { await selected.DisposeAsync(); }
                        catch (Exception refusal) { retirementRefusal = refusal; }
                    }
                    if (invariantFailure is not null)
                    {
                        if (retirementRefusal is not null)
                            throw new AggregateException(invariantFailure, retirementRefusal);
                        ExceptionDispatchInfo.Capture(invariantFailure).Throw();
                    }
                    await Assert.That(retirementRefusal is ObjectDisposedException).IsTrue();
                    await Assert.That(connector.State).IsEqualTo(ConnectionState.Closed);
                }
                else
                {
                    await Assert.That(observed).IsSameReferenceAs(failure);
                    await Assert.That(physicalSaves).IsEqualTo(1);
                    await Assert.That(originalTransactionWasLive).IsTrue();
                }
                await using var observer = (ExploreDbContext)CreateContext(migratorOptions,
                    LocalIdentityWriterInventory.Store.Application);
                int persistedAttempts = await observer.LocalIdentityUsers.AsNoTracking()
                    .Where(value => value.Id == receipt.LocalSubjectId).Select(value => value.AccessFailedCount).SingleAsync(token);
                var persisted = await observer.Set<LocalIdentityEligibility>().AsNoTracking()
                    .SingleAsync(value => value.LocalSubjectId == receipt.LocalSubjectId, token);
                await Assert.That(persistedAttempts).IsEqualTo(beforeAttempts);
                await Assert.That(persisted.Generation).IsEqualTo(before.Generation);
                await Assert.That(persisted.CurrentOperationId).IsEqualTo(before.CurrentOperationId);
                await Assert.That(persisted.PersonalActorId).IsEqualTo(before.PersonalActorId);
                await Assert.That(persisted.ExternalLoginId).IsEqualTo(before.ExternalLoginId);
                await Assert.That(persisted.State).IsEqualTo(before.State);
                await Assert.That(persisted.LockoutUntilUtc).IsEqualTo(before.LockoutUntilUtc);
                if (!failDuringCleanup)
                {
                    await Assert.That(application.Database.CurrentTransaction).IsNull();
                    await Assert.That(application.ContextId).IsEqualTo(contextId);
                    await Assert.That(application.Database.GetDbConnection()).IsSameReferenceAs(connector);
                    await Assert.That(connector.ProcessID).IsEqualTo(backend);
                    execution.RequireCurrent(application, application);
                    HumanSecuritySourceGate.RequireCurrent(application);
                    LocalIdentityLifecycleGateCustody.RequireHeld(application);
                }
                return;
            }
            var result = await authentication.AuthenticateAsync(new LocalAuthRequestDto(address, Guid.CreateVersion7().ToString("N")), token);
            await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.InvalidCredentials);

            int afterAttempts = await application.LocalIdentityUsers.AsNoTracking().Where(row => row.Id == receipt.LocalSubjectId)
                .Select(row => row.AccessFailedCount).SingleAsync(token);
            var after = await application.Set<LocalIdentityEligibility>().AsNoTracking()
                .SingleAsync(row => row.LocalSubjectId == receipt.LocalSubjectId, token);
            await Assert.That(afterAttempts).IsEqualTo(beforeAttempts + 1);
            await Assert.That(after.Generation).IsEqualTo(before.Generation + 1);
            await Assert.That(after.CurrentOperationId).IsEqualTo(receipt.OperationId);
            await Assert.That(after.PersonalActorId).IsEqualTo(receipt.PersonalActorId);
            await Assert.That(after.ExternalLoginId).IsEqualTo(receipt.ExternalLoginId);
            await Assert.That(after.State).IsEqualTo(LocalIdentityEligibilityState.ChangeRequired);
            await Assert.That(after.LockoutUntilUtc).IsNull();
            await Assert.That(application.Database.CurrentTransaction).IsNull();
            await Assert.That(application.ContextId).IsEqualTo(contextId);
            await Assert.That(application.Database.GetDbConnection()).IsSameReferenceAs(connector);
            await Assert.That(connector.ProcessID).IsEqualTo(backend);
            execution.RequireCurrent(application, application);
            HumanSecuritySourceGate.RequireCurrent(application);
            LocalIdentityLifecycleGateCustody.RequireHeld(application);
        }
        catch (Exception failure)
        {
            bodyFailure = failure;
            throw;
        }
        finally
        {
            if (!selectedRetired)
            {
                selectedRetired = true;
                try { await selected.DisposeAsync(); }
                catch (Exception cleanupFailure)
                {
                    if (bodyFailure is not null)
                        throw new AggregateException(bodyFailure, cleanupFailure);
                    throw;
                }
            }
        }
    }

    private sealed class AuthenticationCleanupFault : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly ExploreDbContext context;
        private readonly DbContextId contextId;
        private readonly Exception failure;
        private readonly object lifetime = new();
        private readonly List<IDisposable> subscriptions = [];
        private readonly IDisposable allListeners;
        private DbTransaction? transaction;
        private Guid transactionId;
        private bool disposed;

        internal AuthenticationCleanupFault(ExploreDbContext context, Exception failure)
        {
            this.context = context;
            contextId = context.ContextId;
            this.failure = failure;
            allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        internal bool Reached { get; private set; }

        internal void Arm(IDbContextTransaction original)
        {
            lock (lifetime)
            {
                if (disposed || transaction is not null)
                    throw new InvalidOperationException("The original cleanup diagnostic belongs to one authentication attempt.");
                transaction = original.GetDbTransaction();
                transactionId = original.TransactionId;
            }
        }

        public void OnNext(DiagnosticListener listener)
        {
            lock (lifetime)
                if (!disposed && listener.Name == "Microsoft.EntityFrameworkCore")
                    subscriptions.Add(listener.Subscribe(this, name => name == RelationalEventId.TransactionDisposed.Name));
        }

        public void OnNext(KeyValuePair<string, object?> diagnostic)
        {
            lock (lifetime)
            {
                if (disposed || Reached || transaction is null
                    || diagnostic.Key != RelationalEventId.TransactionDisposed.Name
                    || diagnostic.Value is not TransactionEventData data
                    || !ReferenceEquals(data.Context, context) || data.Context?.ContextId != contextId
                    || !ReferenceEquals(data.Transaction, transaction) || data.TransactionId != transactionId)
                    return;
                Reached = true;
            }
            throw failure;
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void Dispose()
        {
            lock (lifetime)
            {
                if (disposed) return;
                disposed = true;
                allListeners.Dispose();
                foreach (var subscription in subscriptions) subscription.Dispose();
            }
        }
    }
}
