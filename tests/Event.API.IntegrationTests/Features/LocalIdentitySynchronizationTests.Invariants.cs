using System.Data.Common;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Event.API.IntegrationTests.Features;

public sealed partial class LocalIdentitySynchronizationTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task VerifiedAddressFormattingCorrelatesOnlyTrustedIssuer(bool trustedIssuer)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        const string issuer = "https://identity.example.test/realms/synchronization";
        factory.Services.GetRequiredService<IOptions<IdentityCorrelationOptions>>().Value.TrustedIssuers = [issuer];
        string address = $"normalized-{Guid.CreateVersion7():N}@example.test";
        Graph owner = await SeedGraphAsync(factory, Guid.CreateVersion7(), address,
            ExternalKey(AuthenticationProviderKind.Keycloak));
        await SeedIdentityClaimAsync(factory, owner, address);
        Counts before = await ReadCountsAsync(factory);
        ProviderAccountKey incoming = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            trustedIssuer ? issuer : "https://identity.example.test/realms/untrusted",
            Guid.CreateVersion7().ToString("D"));

        BaseCommandResponse<Guid> result = await SynchronizeAsync(factory,
            Command(incoming, Guid.Empty, $"  {address.ToUpperInvariant()}  "));

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id == owner.UserId).IsEqualTo(trustedIssuer);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before with
        {
            Users = before.Users + (trustedIssuer ? 0 : 1),
            Actors = before.Actors + (trustedIssuer ? 0 : 1),
            Logins = before.Logins + 1
        });
        await using ExploreDbContext stored = factory.CreateDatabase();
        var registry = new UserIdentityEmailRepository(stored);
        UserIdentityEmailClaim claim = (await registry.GetByNormalizedEmailAsync(address, CancellationToken))!;
        await Assert.That(claim.UserId).IsEqualTo(owner.UserId);
        UserExternalLogin binding = (await new UserExternalLoginRepository(stored).GetByProviderAndKey(incoming))!;
        UserIdentityEmailEvidence? proof = await registry.GetEvidenceByBindingAsync(binding.Id, CancellationToken);
        await Assert.That(proof is not null).IsEqualTo(trustedIssuer);
        if (trustedIssuer)
        {
            await Assert.That(proof!.ClaimId).IsEqualTo(claim.Id);
            await Assert.That(proof.IsActive).IsTrue();
        }
    }

    [Test]
    public async Task ProviderSignupInitializesNamesOnceAndPreservesLaterProfileEdits()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        ProviderAccountKey key = ExternalKey(AuthenticationProviderKind.Keycloak);
        string address = $"signup-{Guid.CreateVersion7():N}@example.test";
        Counts before = await ReadCountsAsync(factory);

        BaseCommandResponse<Guid> created = await SynchronizeAsync(factory, Command(key, Guid.Empty, address));

        await Assert.That(created.IsSuccess).IsTrue();
        Profile initial = await ReadProfileAsync(factory, created.Id);
        await Assert.That(initial.FirstName).IsEqualTo("Incoming");
        await Assert.That(initial.LastName).IsEqualTo("Profile");
        await Assert.That(initial.ActorName).IsEqualTo("Incoming Profile");
        await Assert.That(initial.ActorId).IsNotNull();
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before with
        {
            Users = before.Users + 1,
            Actors = before.Actors + 1,
            Logins = before.Logins + 1
        });
        await using (ExploreDbContext edit = factory.CreateDatabase())
        {
            User user = await edit.Users.Include(value => value.Pii)
                .Include(value => value.Actor).ThenInclude(actor => actor!.Pii)
                .SingleAsync(value => value.Id == created.Id, CancellationToken);
            user.FirstName = "Chosen";
            user.LastName = "Family";
            user.Actor!.DisplayName = "Chosen Public Name";
            await edit.SaveChangesAsync(CancellationToken);
        }
        Profile edited = await ReadProfileAsync(factory, created.Id);
        SyncUserCommand renamedProvider = Command(key, Guid.Empty, address) with
        {
            UserDto = new UserDto
            {
                Email = address,
                EmailVerified = true,
                FirstName = "Provider Changed",
                LastName = "Provider Family"
            }
        };

        BaseCommandResponse<Guid> signedIn = await SynchronizeAsync(factory, renamedProvider);

        await Assert.That(signedIn.IsSuccess).IsTrue();
        await Assert.That(signedIn.Id).IsEqualTo(created.Id);
        Profile stored = await ReadProfileAsync(factory, created.Id);
        await Assert.That(stored.FirstName).IsEqualTo(edited.FirstName);
        await Assert.That(stored.LastName).IsEqualTo(edited.LastName);
        await Assert.That(stored.ActorName).IsEqualTo(edited.ActorName);
        await Assert.That(stored.ActorId).IsEqualTo(initial.ActorId);
        await Assert.That(stored.ActorStamp).IsEqualTo(edited.ActorStamp);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before with
        {
            Users = before.Users + 1,
            Actors = before.Actors + 1,
            Logins = before.Logins + 1
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FreeVerifiedAddressSupersedesOnlyItsBindingProofAndBecomesRecipientAuthority(bool independentProof)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        factory.Services.GetRequiredService<IOptions<IdentityCorrelationOptions>>().Value.TrustedIssuers =
            ["https://identity.example.test/realms/synchronization"];
        ProviderAccountKey key = ExternalKey(AuthenticationProviderKind.Keycloak);
        string oldAddress = $"old-{Guid.CreateVersion7():N}@example.test";
        string newAddress = $"new-{Guid.CreateVersion7():N}@example.test";
        Graph bound = await SeedGraphAsync(factory, Guid.CreateVersion7(), oldAddress, key);
        Guid? independentBindingId = null;
        if (independentProof)
        {
            ProviderAccountKey independentKey = ExternalKey(AuthenticationProviderKind.Keycloak);
            await AddLoginAsync(factory, bound.UserId, independentKey);
            await using ExploreDbContext read = factory.CreateDatabase();
            independentBindingId = (await new UserExternalLoginRepository(read)
                .GetByProviderAndKey(independentKey))!.Id;
        }
        await SeedIdentityClaimAsync(factory, bound, oldAddress, independentBindingId);
        Counts before = await ReadCountsAsync(factory);

        BaseCommandResponse<Guid> result = await SynchronizeAsync(factory, Command(key, Guid.Empty, newAddress));

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(bound.UserId);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
        await using (ExploreDbContext stored = factory.CreateDatabase())
        {
            var registry = new UserIdentityEmailRepository(stored);
            UserIdentityEmailClaim newClaim = (await registry.GetByNormalizedEmailAsync(newAddress, CancellationToken))!;
            await Assert.That(newClaim.UserId).IsEqualTo(bound.UserId);
            UserIdentityEmailEvidence proof = (await registry.GetEvidenceByBindingAsync(bound.LoginId, CancellationToken))!;
            await Assert.That(proof.ClaimId).IsEqualTo(newClaim.Id);
            await Assert.That(proof.UserId).IsEqualTo(bound.UserId);
            await Assert.That(proof.IsActive).IsTrue();
            UserIdentityEmailClaim? oldClaim = await registry.GetByNormalizedEmailAsync(oldAddress, CancellationToken);
            await Assert.That(oldClaim is not null).IsEqualTo(independentProof);
            if (independentBindingId is Guid bindingId)
            {
                UserIdentityEmailEvidence remaining = (await registry.GetEvidenceByBindingAsync(bindingId, CancellationToken))!;
                await Assert.That(remaining.ClaimId).IsEqualTo(oldClaim!.Id);
                await Assert.That(remaining.IsActive).IsTrue();
            }
            await Assert.That((await new UserExternalLoginRepository(stored).GetByProviderAndKey(key))!.Id)
                .IsEqualTo(bound.LoginId);
            User recipient = (await new UserRepository(stored).GetUserWithDetails(bound.UserId, CancellationToken))!;
            await Assert.That(recipient.Actor!.Id).IsEqualTo(bound.ActorId);
            await Assert.That(recipient.Email).IsEqualTo(newAddress);
            await Assert.That(RecipientEmailAddressResolver.Resolve(recipient, bound.UserId).Email).IsEqualTo(newAddress);
        }
        await using (ExploreDbContext editContact = factory.CreateDatabase())
        {
            User user = await editContact.Users.Include(value => value.Pii)
                .SingleAsync(value => value.Id == bound.UserId, CancellationToken);
            user.Email = "unverified-contact@example.test";
            await editContact.SaveChangesAsync(CancellationToken);
        }
        await using ExploreDbContext verification = factory.CreateDatabase();
        User contactEdited = (await new UserRepository(verification).GetUserWithDetails(bound.UserId, CancellationToken))!;
        RecipientEmailAddressResolution authority = RecipientEmailAddressResolver.Resolve(contactEdited, bound.UserId);
        await Assert.That(authority.HasVerifiedEmail).IsTrue();
        await Assert.That(authority.Email == contactEdited.Email).IsFalse();
        await Assert.That(contactEdited.IdentityEmailClaims.Any(claim => claim.NormalizedEmail == authority.Email
            && claim.Evidence.Any(proof => proof.IsActive))).IsTrue();
        if (!independentProof)
            await Assert.That(authority.Email).IsEqualTo(newAddress);
    }

    [Test]
    public async Task ConcurrentExternalAdmissionsCommitOneCanonicalOwnerWithoutOrphanGraphs()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        CancellationToken token = timeout.Token;
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))).Build();
        await postgres.StartAsync(token);
        string address = $"concurrent-{Guid.CreateVersion7():N}@example.test";
        var barrier = new ExternalAdmissionBarrier(address);
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            postgreSqlConnectionString: postgres.GetConnectionString());
        await using var instrumented = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Configure<IdentityCorrelationOptions>(options => options.TrustedIssuers =
                ["https://identity.example.test/realms/synchronization"]);
            services.ConfigureDbContext<ExploreDbContext>(options =>
                options.AddInterceptors(barrier.UserSaved), ServiceLifetime.Singleton);
            services.ConfigureDbContext<CoLocatedPrivacyErasureAuthorityDbContext>(options =>
                options.AddInterceptors(barrier.AuthorityStarted), ServiceLifetime.Singleton);
        }));
        await using AsyncServiceScope firstScope = instrumented.Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope = instrumented.Services.CreateAsyncScope();
        foreach (IServiceProvider services in new[] { firstScope.ServiceProvider, secondScope.ServiceProvider })
            services.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var first = firstScope.ServiceProvider.GetRequiredService<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>();
        var second = secondScope.ServiceProvider.GetRequiredService<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(firstScope.ServiceProvider.GetRequiredService<ExploreDbContext>().ContextId
            == secondScope.ServiceProvider.GetRequiredService<ExploreDbContext>().ContextId).IsFalse();
        await Assert.That(firstScope.ServiceProvider.GetRequiredService<CoLocatedPrivacyErasureAuthorityDbContext>().ContextId
            == secondScope.ServiceProvider.GetRequiredService<CoLocatedPrivacyErasureAuthorityDbContext>().ContextId).IsFalse();
        Counts before = await ReadCountsAsync(factory);
        ProviderAccountKey firstKey = ExternalKey(AuthenticationProviderKind.Keycloak);
        ProviderAccountKey secondKey = ExternalKey(AuthenticationProviderKind.Keycloak);
        barrier.Enabled = true;
        Task<BaseCommandResponse<Guid>> firstAdmission = first.ExecuteAsync(Command(firstKey, Guid.Empty, address), token);
        Task<BaseCommandResponse<Guid>>? secondAdmission = null;
        try
        {
            Task firstState = await Task.WhenAny(barrier.UncommittedUser.Task, firstAdmission).WaitAsync(token);
            if (firstState == firstAdmission)
            {
                BaseCommandResponse<Guid> early = await firstAdmission;
                throw new InvalidOperationException(
                    $"Admission completed before the subscribed save signal: success={early.IsSuccess}, code={early.FailureCode}.");
            }
            secondAdmission = second.ExecuteAsync(Command(secondKey, Guid.Empty, address.ToUpperInvariant()), token);
            await barrier.SecondAuthorityTransaction.Task.WaitAsync(token);
            await Assert.That(firstAdmission.IsCompleted).IsFalse();
            await Assert.That(secondAdmission.IsCompleted).IsFalse();
            await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        BaseCommandResponse<Guid> winner = await firstAdmission.WaitAsync(token);
        BaseCommandResponse<Guid> follower = await secondAdmission!.WaitAsync(token);
        await Assert.That(winner.IsSuccess).IsTrue();
        await Assert.That(follower.IsSuccess).IsTrue();
        await Assert.That(follower.Id).IsEqualTo(winner.Id);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before with
        {
            Users = before.Users + 1,
            Actors = before.Actors + 1,
            Logins = before.Logins + 2
        });
        await using ExploreDbContext stored = factory.CreateDatabase();
        var registry = new UserIdentityEmailRepository(stored);
        UserIdentityEmailClaim claim = (await registry.GetByNormalizedEmailAsync(address, token))!;
        await Assert.That(claim.UserId).IsEqualTo(winner.Id);
        await Assert.That(await stored.UserIdentityEmailClaims.CountAsync(value => value.NormalizedEmail == address, token))
            .IsEqualTo(1);
        await Assert.That(await stored.UserIdentityEmailEvidence.CountAsync(value => value.ClaimId == claim.Id, token))
            .IsEqualTo(2);
        foreach (ProviderAccountKey key in new[] { firstKey, secondKey })
        {
            UserExternalLogin binding = (await new UserExternalLoginRepository(stored).GetByProviderAndKey(key))!;
            await Assert.That(binding.UserId).IsEqualTo(winner.Id);
            UserIdentityEmailEvidence proof = (await registry.GetEvidenceByBindingAsync(binding.Id, token))!;
            await Assert.That(proof.ClaimId).IsEqualTo(claim.Id);
            await Assert.That(proof.UserId).IsEqualTo(winner.Id);
            await Assert.That(proof.IsActive).IsTrue();
        }
        await Assert.That(await stored.Actors.CountAsync(value => value.UserId == winner.Id, token)).IsEqualTo(1);
    }

    [Test]
    public async Task OrganizerAndIndependentUserSharingContactKeepHistoryAndCredentialAuthority()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        factory.Services.GetRequiredService<IOptions<IdentityCorrelationOptions>>().Value.TrustedIssuers =
            ["https://identity.example.test/realms/synchronization"];
        string contact = $"public-{Guid.CreateVersion7():N}@example.test";
        string organizerAuthority = $"organizer-{Guid.CreateVersion7():N}@example.test";
        ProviderAccountKey organizerKey = ExternalKey(AuthenticationProviderKind.Keycloak);
        ProviderAccountKey memberKey = ExternalKey(AuthenticationProviderKind.Keycloak);
        Graph organizer = await SeedGraphAsync(factory, Guid.CreateVersion7(), contact, organizerKey);
        Graph member = await SeedGraphAsync(factory, Guid.CreateVersion7(), contact, memberKey);
        await SeedIdentityClaimAsync(factory, organizer, organizerAuthority);
        await SeedIdentityClaimAsync(factory, member, contact);
        Guid eventId = Guid.CreateVersion7();
        DateTime createdAt;
        await using (ExploreDbContext seed = factory.CreateDatabase())
        {
            var history = new EventBuilder().WithId(eventId).WithActorId(organizer.ActorId)
                .WithTenantId(PlatformDefaults.DefaultTenantId).WithTitle("Organizer history").Build();
            seed.Events.Add(history);
            await seed.SaveChangesAsync(CancellationToken);
            createdAt = history.CreatedAt;
        }
        Counts before = await ReadCountsAsync(factory);

        BaseCommandResponse<Guid> organizerResult = await SynchronizeAsync(factory, Command(organizerKey, member.UserId, contact));
        BaseCommandResponse<Guid> memberResult = await SynchronizeAsync(factory, Command(memberKey, organizer.UserId, contact));

        await Assert.That(organizerResult.IsSuccess).IsTrue();
        await Assert.That(memberResult.IsSuccess).IsTrue();
        await Assert.That(organizerResult.Id).IsEqualTo(organizer.UserId);
        await Assert.That(memberResult.Id).IsEqualTo(member.UserId);
        await Assert.That(organizerResult.Id == memberResult.Id).IsFalse();
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
        await using AsyncServiceScope read = factory.Services.CreateAsyncScope();
        read.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        ExploreDbContext stored = read.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Explore.Domain.Event retained = await stored.Events.SingleAsync(value => value.Id == eventId, CancellationToken);
        await Assert.That(retained.ActorId).IsEqualTo(organizer.ActorId);
        await Assert.That(retained.CreatedAt).IsEqualTo(createdAt);
        await Assert.That((await new UserExternalLoginRepository(stored).GetByProviderAndKey(organizerKey))!.Id)
            .IsEqualTo(organizer.LoginId);
        await Assert.That((await new UserExternalLoginRepository(stored).GetByProviderAndKey(memberKey))!.Id)
            .IsEqualTo(member.LoginId);
        User organizerUser = (await new UserRepository(stored).GetUserWithDetails(organizer.UserId, CancellationToken))!;
        User memberUser = (await new UserRepository(stored).GetUserWithDetails(member.UserId, CancellationToken))!;
        await Assert.That(organizerUser.Actor!.Id).IsEqualTo(organizer.ActorId);
        await Assert.That(memberUser.Actor!.Id).IsEqualTo(member.ActorId);
        await Assert.That(organizerUser.Email).IsEqualTo(contact);
        await Assert.That(memberUser.Email).IsEqualTo(contact);
        await Assert.That(RecipientEmailAddressResolver.Resolve(organizerUser, organizer.UserId).HasVerifiedEmail).IsFalse();
        await Assert.That(RecipientEmailAddressResolver.Resolve(memberUser, member.UserId).Email).IsEqualTo(contact);
        await Assert.That((await new UserIdentityEmailRepository(stored)
            .GetByNormalizedEmailAsync(contact, CancellationToken))!.UserId).IsEqualTo(member.UserId);
    }

    private sealed class ExternalAdmissionBarrier(string address)
    {
        private int _userEntered;
        private int _authorityStarted;
        public bool Enabled { get; set; }
        public TaskCompletionSource UncommittedUser { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondAuthorityTransaction { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SaveChangesInterceptor UserSaved => new UserSaveBarrier(this, address);
        public DbTransactionInterceptor AuthorityStarted => new AuthorityTransactionBarrier(this);

        private sealed class UserSaveBarrier(ExternalAdmissionBarrier owner, string address) : SaveChangesInterceptor
        {
            public override async ValueTask<int> SavedChangesAsync(
                SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
            {
                if (owner.Enabled && eventData.Context is ExploreDbContext context
                    && context.Users.Local.Any(user => user.Email == address)
                    && Interlocked.CompareExchange(ref owner._userEntered, 1, 0) == 0)
                {
                    owner.UncommittedUser.TrySetResult();
                    await owner.Release.Task.WaitAsync(cancellationToken);
                }
                return result;
            }
        }

        private sealed class AuthorityTransactionBarrier(ExternalAdmissionBarrier owner) : DbTransactionInterceptor
        {
            public override ValueTask<DbTransaction> TransactionStartedAsync(
                DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
                CancellationToken cancellationToken = default)
            {
                if (owner.Enabled && Interlocked.Increment(ref owner._authorityStarted) == 2)
                    owner.SecondAuthorityTransaction.TrySetResult();
                return ValueTask.FromResult(result);
            }
        }
    }
}
