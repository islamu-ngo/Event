using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class LocalCredentialBindingTests
{
    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    private sealed class Fixture : IAsyncDisposable
    {
        internal LocalIdentityLifecycleHttpFixture Lifecycle { get; private set; } = null!;
        internal BindingFaultInterceptor Faults { get; } = new();
        internal Guid InitiatorId => Lifecycle.Binding.LocalSubjectId;
        internal LocalCredentialOperationReceipt Receipt { get; private set; } = null!;

        internal static async Task<Fixture> CreateAsync(IdentityDatabaseTopology topology)
        {
            var fixture = new Fixture();
            try
            {
                fixture.Lifecycle = await LocalIdentityLifecycleHttpFixture.CreateAsync(
                    topology: topology, interceptor: fixture.Faults);
                await using (var database = fixture.Lifecycle.Native.CreateDatabase())
                {
                    database.PlatformUserRoles.Add(new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = fixture.InitiatorId,
                        User = null!,
                        RoleId = (int)RoleEnum.Admin,
                        Role = null!,
                        GrantedAt = fixture.Lifecycle.Clock.GetUtcNow().UtcDateTime
                    });
                    await database.SaveChangesAsync(CancellationToken);
                }

                var command = new CreateLocalIdentityCommand(Guid.CreateVersion7(),
                    $"binding-{Guid.CreateVersion7():N}@example.test", "Credential", "Owner");
                LocalCredentialCreateResult result;
                if (topology == IdentityDatabaseTopology.Colocated)
                {
                    await using var request = new Request(fixture.Lifecycle.Host.Services);
                    await request.AuthenticateLocalAsync(await fixture.Lifecycle.SignInAsync(), fixture.InitiatorId);
                    var original = await request.Services.GetRequiredService<ILocalAdministrationPreparation>()
                        .PrepareAsync(command, fixture.InitiatorId, CancellationToken);
                    result = await request.Services.GetRequiredService<ILocalHumanCredentialAdministration>()
                        .CreatePendingAsync(original, command, CancellationToken);
                }
                else
                {
                    // Independent Core provisioning establishes the pending precondition only.
                    // The retained External-positive handler cases still enter the genuine
                    // human port and remain blocked by its co-located-only contract.
                    await using var seed = fixture.Lifecycle.Host.Services.CreateAsyncScope();
                    result = await seed.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
                        .CreatePendingAsync(new(command.OperationId, fixture.InitiatorId,
                            command.Email, command.FirstName, command.LastName), CancellationToken);
                }
                await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
                fixture.Receipt = result.Receipt!;
                await Assert.That(fixture.Receipt.InitiatingApplicationUserId).IsEqualTo(fixture.InitiatorId);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal async Task<BaseCommandResponse<Guid>> ReconcileAsync()
        {
            await using var request = new Request(Lifecycle.Host.Services);
            await request.AuthenticateLocalAsync(await Lifecycle.SignInAsync(), InitiatorId);
            Faults.Observe(request.Services.GetRequiredService<ExploreDbContext>(), Receipt.PersonalActorId);
            return await request.Handler.ExecuteAsync(new(Receipt.OperationId), CancellationToken);
        }

        internal async Task<LocalIdentityUser> ReadIdentityUserAsync()
        {
            await using var database = Lifecycle.Native.CreateIdentityDatabase();
            return await database.Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(user => user.Id == Receipt.LocalSubjectId, CancellationToken);
        }

        internal async Task<LocalIdentityCredentialOperation> ReadOperationAsync()
        {
            await using var database = Lifecycle.Native.CreateIdentityDatabase();
            return await database.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleAsync(operation => operation.Id == Receipt.OperationId, CancellationToken);
        }

        internal async Task AssertStateAsync(LocalCredentialState expected)
        {
            await using var scope = Lifecycle.Host.Services.CreateAsyncScope();
            var status = await scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
                .ReadOperationAsync(Receipt.OperationId, CancellationToken);
            await Assert.That(status).IsNotNull();
            await Assert.That(status!.IsCurrent).IsTrue();
            await Assert.That(status.CredentialState).IsEqualTo(expected);
            await Assert.That(status.Receipt.OperationId).IsEqualTo(Receipt.OperationId);
            await Assert.That(status.Receipt.ApplicationUserId).IsEqualTo(Receipt.ApplicationUserId);
        }

        internal async Task AssertNoGraphAsync()
        {
            await using var database = Lifecycle.Native.CreateDatabase();
            await Assert.That(await database.Users.AnyAsync(user => user.Id == Receipt.ApplicationUserId, CancellationToken)).IsFalse();
            await Assert.That(await database.Actors.AnyAsync(actor => actor.Id == Receipt.PersonalActorId, CancellationToken)).IsFalse();
            await Assert.That(await database.UserExternalLogins.AnyAsync(login => login.Id == Receipt.ExternalLoginId, CancellationToken)).IsFalse();
        }

        internal async Task AssertExactGraphAsync()
        {
            await using var database = Lifecycle.Native.CreateDatabase();
            var user = await database.Users.AsNoTracking().Include(row => row.Pii)
                .SingleAsync(row => row.Id == Receipt.ApplicationUserId, CancellationToken);
            var actor = await database.Actors.AsNoTracking()
                .SingleAsync(row => row.Id == Receipt.PersonalActorId, CancellationToken);
            var login = await database.UserExternalLogins.AsNoTracking()
                .SingleAsync(row => row.Id == Receipt.ExternalLoginId, CancellationToken);
            var identity = await ReadIdentityUserAsync();
            await Assert.That(user.Email).IsEqualTo(identity.Email);
            await Assert.That(user.EmailVerified).IsEqualTo(true);
            await Assert.That(user.IsDeleted).IsFalse();
            await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.User);
            await Assert.That(actor.UserId).IsEqualTo(user.Id);
            await Assert.That(actor.IsDeleted || actor.IsSuspended).IsFalse();
            await Assert.That(login.UserId).IsEqualTo(user.Id);
            await Assert.That(login.AuthenticationProviderId).IsEqualTo((int)AuthenticationProviderKind.Local);
            await Assert.That(login.ProviderKey).IsEqualTo(Receipt.LocalSubjectId.ToString("D"));
            await Assert.That(await database.Actors.CountAsync(row => row.UserId == user.Id, CancellationToken)).IsEqualTo(1);
            await Assert.That(await database.UserExternalLogins.CountAsync(row => row.UserId == user.Id, CancellationToken)).IsEqualTo(1);
            await Assert.That(user.CreatedBy).IsEqualTo(InitiatorId);
            await Assert.That(actor.CreatedBy).IsEqualTo(InitiatorId);
            await Assert.That(login.CreatedBy).IsEqualTo(InitiatorId);
        }

        internal async Task RemovePlatformRoleAsync()
        {
            await using var database = Lifecycle.Native.CreateDatabase();
            database.PlatformUserRoles.RemoveRange(await database.PlatformUserRoles
                .Where(role => role.UserId == InitiatorId).ToListAsync(CancellationToken));
            await database.SaveChangesAsync(CancellationToken);
        }

        internal async Task<Guid> GrantTenantAdministratorAsync()
        {
            await using var database = Lifecycle.Native.CreateDatabase();
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "Binding authority tenant",
                Slug = $"binding-{Guid.CreateVersion7():N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = DateTime.UtcNow
            };
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                UserId = InitiatorId,
                User = await database.Users.SingleAsync(user => user.Id == InitiatorId, CancellationToken),
                StatusId = (int)TenantUserStatusEnum.Active,
                CreatedAt = DateTime.UtcNow
            };
            database.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                TenantUserId = membership.Id,
                TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await database.SaveChangesAsync(CancellationToken);
            return tenant.Id;
        }

        public async ValueTask DisposeAsync()
        {
            if (Lifecycle is not null) await Lifecycle.DisposeAsync();
        }
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal DefaultHttpContext Context { get; }
        internal IAdminContext Admin => Services.GetRequiredService<IAdminContext>();
        internal ReconcileLocalCredentialOperationCommandHandler Handler =>
            ActivatorUtilities.CreateInstance<ReconcileLocalCredentialOperationCommandHandler>(Services);

        internal Request(IServiceProvider services)
        {
            _scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = Services };
            _accessor = Services.GetRequiredService<IHttpContextAccessor>();
            _accessor.HttpContext = Context;
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }

        internal async Task AuthenticateAsync(string token)
        {
            Context.Request.Headers.Authorization = "Bearer " + token;
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
            var lifetime = await Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(Context.User, CancellationToken);
            await Assert.That(lifetime).IsNotNull();
            await Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken);
            await Assert.That(ReferenceEquals(
                Services.GetRequiredService<ILocalHumanCredentialAdministration>(),
                Services.GetRequiredService<ILocalAdministrationOwnerSource>())).IsTrue();
        }

        internal async Task AuthenticateLocalAsync(string token, Guid administrator)
        {
            await AuthenticateAsync(token);
            var lifetime = (await Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(Context.User, CancellationToken))!;
            await Assert.That(lifetime.AuthenticationScheme).IsEqualTo(ApiAuthenticationSchemeNames.LocalIdentity);
            await Assert.That(lifetime.LocalSession).IsNotNull();
            await Assert.That(await Admin.ResolveUserIdAsync(CancellationToken)).IsEqualTo(administrator);
        }

        public async ValueTask DisposeAsync()
        {
            _accessor.HttpContext = null;
            await _scope.DisposeAsync();
        }
    }

    private sealed class InjectedBindingFailure : Exception;

    private sealed class BindingFaultInterceptor : DbTransactionInterceptor, ISaveChangesInterceptor
    {
        private ExploreDbContext? _owner;
        private Guid _actorId;
        internal bool RejectGraph { get; set; }
        internal bool GraphFaultObserved { get; private set; }
        internal bool RejectActivationCommit { get; set; }
        internal bool ActivationCommitFaultObserved { get; private set; }
        internal int CommitsObserved { get; private set; }
        internal bool LoseNextAcknowledgement { get; set; }
        internal bool LostAcknowledgementObserved { get; private set; }

        internal void Observe(ExploreDbContext owner, Guid actorId)
        {
            _owner = owner;
            _actorId = actorId;
            CommitsObserved = 0;
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _owner) && RejectGraph
                && _owner!.ChangeTracker.Entries<Actor>()
                    .Any(entry => entry.State == EntityState.Added && entry.Entity.Id == _actorId))
            {
                GraphFaultObserved = true;
                throw new InjectedBindingFailure();
            }
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _owner) && RejectActivationCommit && ++CommitsObserved == 3)
            {
                // The selected owner commits ReadProvisioning, Bind, then Activate.
                RejectActivationCommit = false;
                ActivationCommitFaultObserved = true;
                throw new InjectedBindingFailure();
            }
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _owner) && LoseNextAcknowledgement)
            {
                LoseNextAcknowledgement = false;
                LostAcknowledgementObserved = true;
                throw new InjectedBindingFailure();
            }
            return Task.CompletedTask;
        }
    }
}
