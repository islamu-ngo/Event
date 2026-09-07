// ABOUTME: Exercises Local and external account separation through native MediatR synchronization over SQLite.
// ABOUTME: Rejects implicit Local account adoption while preserving exact bindings and independent external creation.

using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class LocalIdentitySynchronizationTests
{
    public enum UnlinkedLocalAttempt
    {
        NewAccount,
        MatchingExternalEmail,
        MatchingExternalUserId
    }

    public enum ConfiguredAdoptionAttempt
    {
        ExternalUserId,
        UnlinkedLocal
    }

    public enum InvalidLocalBinding
    {
        MalformedSubject,
        NoncanonicalSubject,
        SubjectBelongsToAnotherUser,
        MissingPersonalActor,
        SuspendedPersonalActor,
        DeletedPersonalActor,
        MismatchedRequestedUserId
    }

    [Test]
    [Arguments(UnlinkedLocalAttempt.NewAccount)]
    [Arguments(UnlinkedLocalAttempt.MatchingExternalEmail)]
    [Arguments(UnlinkedLocalAttempt.MatchingExternalUserId)]
    public async Task UnlinkedLocalCannotCreateOrAdoptAnApplicationAccount(UnlinkedLocalAttempt attempt)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Guid localSubject = Guid.CreateVersion7();
        string email = $"local-{localSubject:N}@example.test";
        Graph? external = attempt == UnlinkedLocalAttempt.NewAccount
            ? null
            : await SeedGraphAsync(factory: factory, userId: Guid.CreateVersion7(), email: email,
                accountKey: ExternalKey(AuthenticationProviderKind.Keycloak));
        Guid requestedUserId = attempt == UnlinkedLocalAttempt.MatchingExternalUserId
            ? external!.UserId
            : localSubject;
        Counts before = await ReadCountsAsync(factory);
        Profile? original = external is null ? null : await ReadProfileAsync(factory, external.UserId);

        BaseCommandResponse<Guid> response = await SynchronizeAsync(factory,
            Command(accountKey: LocalKey(localSubject), userId: requestedUserId, email: email));

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
        if (external is not null)
            await Assert.That(await ReadProfileAsync(factory, external.UserId)).IsEqualTo(original);
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Keycloak)]
    [Arguments(AuthenticationProviderKind.Google)]
    public async Task ExternalUserIdCannotAdoptLocalOwnedAccount(AuthenticationProviderKind provider)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph local = await SeedLocalGraphAsync(factory);
        Counts before = await ReadCountsAsync(factory);
        Profile original = await ReadProfileAsync(factory, local.UserId);

        BaseCommandResponse<Guid> response = await SynchronizeAsync(factory,
            Command(accountKey: ExternalKey(provider), userId: local.UserId,
                email: $"external-{Guid.CreateVersion7():N}@example.test"));

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
        await Assert.That(await ReadProfileAsync(factory, local.UserId)).IsEqualTo(original);
    }

    [Test]
    [Arguments(ConfiguredAdoptionAttempt.ExternalUserId)]
    [Arguments(ConfiguredAdoptionAttempt.UnlinkedLocal)]
    public async Task InvalidLocalAdoptionIsRejectedBeforeConfiguredBootstrapTransaction(ConfiguredAdoptionAttempt attempt)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph local = await SeedLocalGraphAsync(factory);
        var boundary = new TransactionBoundaryInterceptor(beforeStart: _ => Task.CompletedTask);
        await using var instrumented = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(boundary), ServiceLifetime.Singleton)));
        await using AsyncServiceScope scope = instrumented.Services.CreateAsyncScope();
        await using (ExploreDbContext seed = factory.CreateDatabase())
        {
            await seed.InstanceBootstrapStates.ExecuteDeleteAsync(CancellationToken);
            seed.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateConfiguredAdministratorPending(
                id: Guid.CreateVersion7(), providerKind: AuthenticationProviderKind.Keycloak,
                deploymentMode: DeploymentMode.SingleTenant, generation: 1,
                configurationFingerprint: new string('a', 64), selectorFingerprint: new string('b', 64),
                createdAt: DateTime.UtcNow));
            await seed.SaveChangesAsync(CancellationToken);
        }
        Counts before = await ReadCountsAsync(factory);
        Profile original = await ReadProfileAsync(factory, local.UserId);
        boundary.Enabled = true;
        Guid unlinkedSubject = Guid.CreateVersion7();
        ProviderAccountKey incoming = attempt == ConfiguredAdoptionAttempt.ExternalUserId
            ? ExternalKey(AuthenticationProviderKind.Keycloak)
            : LocalKey(unlinkedSubject);

        BaseCommandResponse<Guid> response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(accountKey: incoming,
                userId: attempt == ConfiguredAdoptionAttempt.ExternalUserId ? local.UserId : unlinkedSubject,
                email: original.Email), CancellationToken);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(boundary.TransactionsStarted).IsEqualTo(0);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
        await Assert.That(await ReadProfileAsync(factory, local.UserId)).IsEqualTo(original);
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That((await stored.InstanceBootstrapStates.SingleAsync(CancellationToken)).Status)
            .IsEqualTo(InstanceBootstrapStatus.Pending);
    }

    [Test]
    public async Task NewlyCommittedLocalOwnershipCannotBeAdoptedFromStaleEmailPreRead()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph existing = await SeedGraphAsync(factory: factory, userId: Guid.CreateVersion7(),
            email: $"race-{Guid.CreateVersion7():N}@example.test", accountKey: ExternalKey(AuthenticationProviderKind.Keycloak));
        Profile original = await ReadProfileAsync(factory, existing.UserId);
        Counts before = await ReadCountsAsync(factory);
        var boundary = new TransactionBoundaryInterceptor(beforeStart: _ => AddLoginAsync(
            factory: factory, userId: existing.UserId, accountKey: LocalKey(existing.UserId)));
        await using var instrumented = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(boundary), ServiceLifetime.Singleton)));
        await using AsyncServiceScope scope = instrumented.Services.CreateAsyncScope();
        boundary.Enabled = true;

        BaseCommandResponse<Guid> response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(accountKey: ExternalKey(AuthenticationProviderKind.Google), userId: Guid.Empty,
                email: original.Email), CancellationToken);

        await Assert.That(boundary.TransactionsStarted).IsEqualTo(1);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await ReadProfileAsync(factory, existing.UserId)).IsEqualTo(original);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before with { Logins = before.Logins + 1 });
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That(await stored.UserExternalLogins.AnyAsync(login => login.UserId == existing.UserId
            && login.AuthenticationProviderId == (int)AuthenticationProviderKind.Local, CancellationToken)).IsTrue();
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Keycloak)]
    [Arguments(AuthenticationProviderKind.Google)]
    public async Task VerifiedExternalEmailCreatesSeparateAccountInsteadOfAdoptingLocal(AuthenticationProviderKind provider)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph local = await SeedLocalGraphAsync(factory);
        Profile original = await ReadProfileAsync(factory, local.UserId);
        ProviderAccountKey incoming = ExternalKey(provider);

        BaseCommandResponse<Guid> response = await SynchronizeAsync(factory,
            Command(accountKey: incoming, userId: Guid.Empty, email: original.Email));

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id == local.UserId).IsFalse();
        await Assert.That(await ReadProfileAsync(factory, local.UserId)).IsEqualTo(original);
        await using ExploreDbContext database = factory.CreateDatabase();
        UserExternalLogin linked = await database.UserExternalLogins.SingleAsync(
            login => login.AuthenticationProviderId == (int)provider && login.ProviderKey == incoming.Value, CancellationToken);
        await Assert.That(linked.UserId).IsEqualTo(response.Id);
        await Assert.That(await database.Actors.CountAsync(actor => actor.UserId == linked.UserId, CancellationToken)).IsEqualTo(1);
        await Assert.That(await database.UserExternalLogins.AnyAsync(login => login.Id == local.LoginId
            && login.UserId == local.UserId, CancellationToken)).IsTrue();
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Keycloak)]
    [Arguments(AuthenticationProviderKind.Google)]
    public async Task ExplicitExternalBindingRemainsUsableOnLocalOwnedAccount(AuthenticationProviderKind provider)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph local = await SeedLocalGraphAsync(factory);
        ProviderAccountKey incoming = ExternalKey(provider);
        await AddLoginAsync(factory: factory, userId: local.UserId, accountKey: incoming);
        Counts before = await ReadCountsAsync(factory);

        BaseCommandResponse<Guid> response = await SynchronizeAsync(factory,
            Command(accountKey: incoming, userId: Guid.CreateVersion7(),
                email: $"explicit-{local.UserId:N}@example.test"));

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo(local.UserId);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
        await using ExploreDbContext database = factory.CreateDatabase();
        await Assert.That(await database.Actors.AnyAsync(actor => actor.Id == local.ActorId
            && actor.UserId == local.UserId, CancellationToken)).IsTrue();
        await Assert.That(await database.UserExternalLogins.AnyAsync(login => login.Id == local.LoginId
            && login.UserId == local.UserId, CancellationToken)).IsTrue();
    }

    [Test]
    [Arguments(AuthenticationProviderKind.Keycloak)]
    [Arguments(AuthenticationProviderKind.Google)]
    public async Task VerifiedExternalEmailStillMatchesNonLocalAccount(AuthenticationProviderKind provider)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        string email = $"external-{Guid.CreateVersion7():N}@example.test";
        Graph existing = await SeedGraphAsync(factory: factory, userId: Guid.CreateVersion7(), email: email,
            accountKey: ExternalKey(AuthenticationProviderKind.Keycloak));
        ProviderAccountKey incoming = ExternalKey(provider);

        BaseCommandResponse<Guid> response = await SynchronizeAsync(factory,
            Command(accountKey: incoming, userId: Guid.Empty, email: email));

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo(existing.UserId);
        await using ExploreDbContext database = factory.CreateDatabase();
        await Assert.That(await database.UserExternalLogins.AnyAsync(login => login.UserId == existing.UserId
            && login.AuthenticationProviderId == (int)provider && login.ProviderKey == incoming.Value, CancellationToken)).IsTrue();
        await Assert.That(await database.Actors.CountAsync(actor => actor.UserId == existing.UserId, CancellationToken)).IsEqualTo(1);
    }

    [Test]
    public async Task ExistingLocalBindingUsesCanonicalSubjectWithoutAllocatingAnotherGraph()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph local = await SeedLocalGraphAsync(factory);
        Counts before = await ReadCountsAsync(factory);
        Profile profile = await ReadProfileAsync(factory, local.UserId);

        BaseCommandResponse<Guid> response = await SynchronizeAsync(factory,
            Command(accountKey: LocalKey(local.UserId), userId: Guid.Empty, email: profile.Email));

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo(local.UserId);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
    }

    [Test]
    [Arguments(InvalidLocalBinding.MalformedSubject)]
    [Arguments(InvalidLocalBinding.NoncanonicalSubject)]
    [Arguments(InvalidLocalBinding.SubjectBelongsToAnotherUser)]
    [Arguments(InvalidLocalBinding.MissingPersonalActor)]
    [Arguments(InvalidLocalBinding.SuspendedPersonalActor)]
    [Arguments(InvalidLocalBinding.DeletedPersonalActor)]
    [Arguments(InvalidLocalBinding.MismatchedRequestedUserId)]
    public async Task InvalidLocalBindingCannotMutateOrRepairApplicationGraph(InvalidLocalBinding defect)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph local = await SeedLocalGraphAsync(factory);
        string subject = defect switch
        {
            InvalidLocalBinding.MalformedSubject => "not-a-local-subject",
            InvalidLocalBinding.NoncanonicalSubject => local.UserId.ToString("N"),
            InvalidLocalBinding.SubjectBelongsToAnotherUser => Guid.CreateVersion7().ToString("D"),
            _ => local.UserId.ToString("D")
        };
        await using (ExploreDbContext seed = factory.CreateDatabase())
        {
            await seed.UserExternalLogins.Where(login => login.Id == local.LoginId)
                .ExecuteUpdateAsync(update => update.SetProperty(login => login.ProviderKey, subject), CancellationToken);
            if (defect == InvalidLocalBinding.MissingPersonalActor)
                await seed.Actors.Where(actor => actor.Id == local.ActorId).ExecuteDeleteAsync(CancellationToken);
            else if (defect == InvalidLocalBinding.SuspendedPersonalActor)
                await seed.Actors.Where(actor => actor.Id == local.ActorId)
                    .ExecuteUpdateAsync(update => update.SetProperty(actor => actor.IsSuspended, true), CancellationToken);
            else if (defect == InvalidLocalBinding.DeletedPersonalActor)
                await seed.Actors.Where(actor => actor.Id == local.ActorId)
                    .ExecuteUpdateAsync(update => update.SetProperty(actor => actor.IsDeleted, true), CancellationToken);
        }
        Counts before = await ReadCountsAsync(factory);
        Profile original = await ReadProfileAsync(factory, local.UserId);

        BaseCommandResponse<Guid> response = await SynchronizeAsync(factory,
            Command(accountKey: new ProviderAccountKey(providerKind: AuthenticationProviderKind.Local, value: subject),
                userId: defect == InvalidLocalBinding.MismatchedRequestedUserId ? Guid.CreateVersion7() : Guid.Empty,
                email: $"changed-{local.UserId:N}@example.test"));

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
        await Assert.That(await ReadProfileAsync(factory, local.UserId)).IsEqualTo(original);
        await using ExploreDbContext stored = factory.CreateDatabase();
        UserExternalLogin preserved = await stored.UserExternalLogins.SingleAsync(login => login.Id == local.LoginId, CancellationToken);
        await Assert.That(preserved.UserId).IsEqualTo(local.UserId);
        await Assert.That(preserved.ProviderKey).IsEqualTo(subject);
    }

    [Test]
    public async Task LocalActorSuspendedAfterPreReadCannotSynchronizeProfile()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph local = await SeedLocalGraphAsync(factory);
        Counts before = await ReadCountsAsync(factory);
        Profile original = await ReadProfileAsync(factory, local.UserId);
        var boundary = new TransactionBoundaryInterceptor(beforeStart: async cancellationToken =>
        {
            await using ExploreDbContext mutation = factory.CreateDatabase();
            await mutation.Actors.Where(actor => actor.Id == local.ActorId)
                .ExecuteUpdateAsync(update => update.SetProperty(actor => actor.IsSuspended, true), cancellationToken);
        });
        await using var instrumented = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(boundary), ServiceLifetime.Singleton)));
        await using AsyncServiceScope scope = instrumented.Services.CreateAsyncScope();
        boundary.Enabled = true;

        BaseCommandResponse<Guid> response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(accountKey: LocalKey(local.UserId), userId: local.UserId,
                email: $"changed-{local.UserId:N}@example.test"), CancellationToken);

        await Assert.That(boundary.TransactionsStarted).IsEqualTo(1);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
        await Assert.That(await ReadProfileAsync(factory, local.UserId)).IsEqualTo(original);
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That((await stored.Actors.SingleAsync(actor => actor.Id == local.ActorId, CancellationToken)).IsSuspended).IsTrue();
    }

    [Test]
    public async Task RemovedExplicitExternalBindingCannotBeRecreatedOnLocalOwnedAccount()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph local = await SeedLocalGraphAsync(factory);
        ProviderAccountKey incoming = ExternalKey(AuthenticationProviderKind.Keycloak);
        await AddLoginAsync(factory: factory, userId: local.UserId, accountKey: incoming);
        Counts before = await ReadCountsAsync(factory);
        Profile original = await ReadProfileAsync(factory, local.UserId);
        var boundary = new TransactionBoundaryInterceptor(beforeStart: async cancellationToken =>
        {
            await using ExploreDbContext mutation = factory.CreateDatabase();
            await mutation.UserExternalLogins.Where(login => login.UserId == local.UserId
                && login.AuthenticationProviderId == (int)incoming.ProviderKind && login.ProviderKey == incoming.Value)
                .ExecuteDeleteAsync(cancellationToken);
        });
        await using var instrumented = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(boundary), ServiceLifetime.Singleton)));
        await using AsyncServiceScope scope = instrumented.Services.CreateAsyncScope();
        boundary.Enabled = true;

        BaseCommandResponse<Guid> response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(accountKey: incoming, userId: local.UserId,
                email: $"changed-{local.UserId:N}@example.test"), CancellationToken);

        await Assert.That(boundary.TransactionsStarted).IsEqualTo(1);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before with { Logins = before.Logins - 1 });
        await Assert.That(await ReadProfileAsync(factory, local.UserId)).IsEqualTo(original);
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That(await stored.UserExternalLogins.AnyAsync(login => login.AuthenticationProviderId == (int)incoming.ProviderKind
            && login.ProviderKey == incoming.Value, CancellationToken)).IsFalse();
        await Assert.That(await stored.UserExternalLogins.AnyAsync(login => login.Id == local.LoginId
            && login.UserId == local.UserId, CancellationToken)).IsTrue();
    }

    [Test]
    public async Task DuplicateEmailAcrossLocalAndExternalAccountsRemainsAmbiguous()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph local = await SeedLocalGraphAsync(factory);
        Profile originalLocal = await ReadProfileAsync(factory, local.UserId);
        Graph external = await SeedGraphAsync(factory: factory, userId: Guid.CreateVersion7(),
            email: originalLocal.Email, accountKey: ExternalKey(AuthenticationProviderKind.Keycloak));
        Profile originalExternal = await ReadProfileAsync(factory, external.UserId);
        Counts before = await ReadCountsAsync(factory);
        var boundary = new TransactionBoundaryInterceptor(beforeStart: _ => Task.CompletedTask);
        await using var instrumented = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(boundary), ServiceLifetime.Singleton)));
        await using AsyncServiceScope scope = instrumented.Services.CreateAsyncScope();
        boundary.Enabled = true;

        BaseCommandResponse<Guid> response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(accountKey: ExternalKey(AuthenticationProviderKind.Google), userId: Guid.Empty,
                email: originalLocal.Email), CancellationToken);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(boundary.TransactionsStarted).IsEqualTo(0);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before);
        await Assert.That(await ReadProfileAsync(factory, local.UserId)).IsEqualTo(originalLocal);
        await Assert.That(await ReadProfileAsync(factory, external.UserId)).IsEqualTo(originalExternal);
    }

    [Test]
    public async Task UnlinkedConfiguredAdministratorCannotSelectExistingAccountThroughDtoId()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph target = await SeedGraphAsync(factory: factory, userId: Guid.CreateVersion7(),
            email: $"existing-{Guid.CreateVersion7():N}@example.test", accountKey: ExternalKey(AuthenticationProviderKind.Google));
        Profile original = await ReadProfileAsync(factory, target.UserId);
        string subject = Guid.CreateVersion7().ToString("D");
        string configuredEmail = $"configured-{Guid.CreateVersion7():N}@example.test";
        ProviderAccountKey incoming = ConfiguredKey(subject);
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureNativeAdministrator(services: services, subject: subject, email: configuredEmail)));
        await using AsyncServiceScope scope = configured.Services.CreateAsyncScope();
        await PrepareNativeAdministratorAsync(factory: factory, services: scope.ServiceProvider, accountKey: incoming);
        Counts before = await ReadCountsAsync(factory);

        BaseCommandResponse<Guid> response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(accountKey: incoming, userId: target.UserId, email: original.Email), CancellationToken);

        await Assert.That(response.IsSuccess).IsTrue();
        await using ExploreDbContext stored = factory.CreateDatabase();
        InstanceBootstrapState bootstrap = await stored.InstanceBootstrapStates.SingleAsync(CancellationToken);
        await Assert.That(bootstrap.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That(bootstrap.CompletedByUserId.HasValue && bootstrap.CompletedByUserId != target.UserId).IsTrue();
        Guid administratorId = bootstrap.CompletedByUserId!.Value;
        await Assert.That(await stored.PlatformUserRoles.AnyAsync(role => role.UserId == target.UserId, CancellationToken)).IsFalse();
        await Assert.That(await stored.PlatformUserRoles.AnyAsync(role => role.UserId == administratorId
            && role.Role.MasterCode == "platform.admin", CancellationToken)).IsTrue();
        await Assert.That(await stored.UserExternalLogins.AnyAsync(login => login.UserId == administratorId
            && login.AuthenticationProviderId == (int)incoming.ProviderKind && login.ProviderKey == incoming.Value, CancellationToken)).IsTrue();
        await Assert.That(await stored.Actors.CountAsync(actor => actor.UserId == administratorId, CancellationToken)).IsEqualTo(1);
        await Assert.That((await stored.Users.Include(user => user.Pii).SingleAsync(user => user.Id == administratorId, CancellationToken)).Email)
            .IsEqualTo(configuredEmail);
        await Assert.That(await ReadProfileAsync(factory, target.UserId)).IsEqualTo(original);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before with
        {
            Users = before.Users + 1, Actors = before.Actors + 1, Logins = before.Logins + 1, PlatformRoles = before.PlatformRoles + 1
        });
    }

    [Test]
    public async Task ConfiguredExternalBindingRemovedAtClaimBoundaryCannotGrantLocalTarget()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        Graph local = await SeedLocalGraphAsync(factory);
        Profile original = await ReadProfileAsync(factory, local.UserId);
        string subject = Guid.CreateVersion7().ToString("D");
        ProviderAccountKey incoming = ConfiguredKey(subject);
        await AddLoginAsync(factory: factory, userId: local.UserId, accountKey: incoming);
        var boundary = new TransactionBoundaryInterceptor(beforeStart: async cancellationToken =>
        {
            await using ExploreDbContext mutation = factory.CreateDatabase();
            await mutation.UserExternalLogins.Where(login => login.UserId == local.UserId
                && login.AuthenticationProviderId == (int)incoming.ProviderKind && login.ProviderKey == incoming.Value)
                .ExecuteDeleteAsync(cancellationToken);
        });
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            ConfigureNativeAdministrator(services: services, subject: subject, email: original.Email);
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(boundary), ServiceLifetime.Singleton);
        }));
        await using AsyncServiceScope scope = configured.Services.CreateAsyncScope();
        await PrepareNativeAdministratorAsync(factory: factory, services: scope.ServiceProvider, accountKey: incoming);
        Counts before = await ReadCountsAsync(factory);
        boundary.Enabled = true;

        BaseCommandResponse<Guid> response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(accountKey: incoming, userId: local.UserId, email: original.Email), CancellationToken);

        await Assert.That(boundary.TransactionsStarted).IsEqualTo(1);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await ReadProfileAsync(factory, local.UserId)).IsEqualTo(original);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before with { Logins = before.Logins - 1 });
        await using ExploreDbContext stored = factory.CreateDatabase();
        InstanceBootstrapState bootstrap = await stored.InstanceBootstrapStates.SingleAsync(CancellationToken);
        await Assert.That(bootstrap.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(bootstrap.CompletedByUserId).IsNull();
        await Assert.That(await stored.PlatformUserRoles.AnyAsync(role => role.UserId == local.UserId, CancellationToken)).IsFalse();
        await Assert.That(await stored.UserExternalLogins.AnyAsync(login => login.AuthenticationProviderId == (int)incoming.ProviderKind
            && login.ProviderKey == incoming.Value, CancellationToken)).IsFalse();
        await Assert.That(await stored.UserExternalLogins.AnyAsync(login => login.Id == local.LoginId
            && login.UserId == local.UserId, CancellationToken)).IsTrue();
    }

    [Test]
    public async Task NativeConfiguredAuthorityCanClaimItsExactExistingExternalBinding()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        string subject = Guid.CreateVersion7().ToString("D");
        string email = $"configured-{Guid.CreateVersion7():N}@example.test";
        ProviderAccountKey incoming = ConfiguredKey(subject);
        Graph existing = await SeedGraphAsync(factory: factory, userId: Guid.CreateVersion7(), email: email, accountKey: incoming);
        Profile original = await ReadProfileAsync(factory, existing.UserId);
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureNativeAdministrator(services: services, subject: subject, email: email)));
        await using AsyncServiceScope scope = configured.Services.CreateAsyncScope();
        await PrepareNativeAdministratorAsync(factory: factory, services: scope.ServiceProvider, accountKey: incoming);
        Counts before = await ReadCountsAsync(factory);

        BaseCommandResponse<Guid> response = await scope.ServiceProvider.GetRequiredService<ISender>().Send(
            Command(accountKey: incoming, userId: Guid.Empty, email: email), CancellationToken);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(await ReadProfileAsync(factory, existing.UserId)).IsEqualTo(original);
        await Assert.That(await ReadCountsAsync(factory)).IsEqualTo(before with { PlatformRoles = before.PlatformRoles + 1 });
        await using ExploreDbContext stored = factory.CreateDatabase();
        InstanceBootstrapState bootstrap = await stored.InstanceBootstrapStates.SingleAsync(CancellationToken);
        await Assert.That(bootstrap.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That(bootstrap.CompletedByUserId).IsEqualTo(existing.UserId);
        await Assert.That(await stored.PlatformUserRoles.AnyAsync(role => role.UserId == existing.UserId
            && role.Role.MasterCode == "platform.admin", CancellationToken)).IsTrue();
        await Assert.That(await stored.UserExternalLogins.AnyAsync(login => login.Id == existing.LoginId
            && login.UserId == existing.UserId && login.ProviderKey == incoming.Value, CancellationToken)).IsTrue();
    }

    private static ProviderAccountKey ConfiguredKey(string subject)
    {
        ProviderAccountKey oidc = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            issuer: "https://identity.example.test/realms/configured", subject: subject);
        return new ProviderAccountKey(providerKind: AuthenticationProviderKind.Keycloak, value: oidc.Value);
    }

    private static void ConfigureNativeAdministrator(IServiceCollection services, string subject, string email)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["INSTANCE_BOOTSTRAP_MODE"] = "ConfiguredAdministrator",
            ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "keycloak",
            ["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = subject,
            ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1",
            ["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = email,
            ["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"] = "Configured",
            ["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"] = "Administrator",
            ["Keycloak:Authority"] = "https://identity.example.test/realms/configured",
            ["Deployment:Mode"] = "MultiTenant"
        }).Build();
        services.RemoveAll<ConfiguredAdministratorBootstrapProvider>();
        services.AddScoped(provider => new ConfiguredAdministratorBootstrapProvider(
            configuration: configuration,
            instanceOperatorIdentity: provider.GetRequiredService<IInstanceOperatorIdentity>(),
            bootstrapRepository: provider.GetRequiredService<IInstanceBootstrapStateRepository>()));
    }

    private static async Task PrepareNativeAdministratorAsync(
        LocalAdmissionWebApplicationFactory factory, IServiceProvider services, ProviderAccountKey accountKey)
    {
        await using (ExploreDbContext mutation = factory.CreateDatabase())
        {
            InstanceBootstrapState initial = await mutation.InstanceBootstrapStates.SingleAsync(CancellationToken);
            await mutation.InstanceBootstrapStates.Where(state => state.Id == initial.Id).ExecuteDeleteAsync(CancellationToken);
        }
        await services.GetRequiredService<ConfiguredAdministratorBootstrapStartupRunner>().PrepareAsync(CancellationToken);
        await Assert.That(await services.GetRequiredService<IConfiguredAdministratorBootstrapProvider>()
            .GetVerifiedBindingAsync(accountKey, CancellationToken)).IsNotNull();
        await using ExploreDbContext stored = factory.CreateDatabase();
        await Assert.That((await stored.InstanceBootstrapStates.SingleAsync(CancellationToken)).Status)
            .IsEqualTo(InstanceBootstrapStatus.Pending);
    }

    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    private static ProviderAccountKey LocalKey(Guid subject) => new(
        providerKind: AuthenticationProviderKind.Local, value: subject.ToString("D"));

    private static ProviderAccountKey ExternalKey(AuthenticationProviderKind provider)
    {
        ProviderAccountKey oidc = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            issuer: "https://identity.example.test/realms/synchronization", subject: Guid.CreateVersion7().ToString("D"));
        return new ProviderAccountKey(providerKind: provider, value: oidc.Value);
    }

    private static SyncUserCommand Command(ProviderAccountKey accountKey, Guid userId, string email) => new()
    {
        AccountKey = accountKey,
        UserDto = new UserDto
        {
            Id = userId,
            AuthProvider = accountKey.ProviderKind.ToAuthenticationProviderCode(),
            AuthProviderId = accountKey.Value,
            Email = email,
            FirstName = "Incoming",
            LastName = "Profile",
            EmailVerified = true
        }
    };

    private static async Task<BaseCommandResponse<Guid>> SynchronizeAsync(
        LocalAdmissionWebApplicationFactory factory, SyncUserCommand command)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, CancellationToken);
    }

    private static Task<Graph> SeedLocalGraphAsync(LocalAdmissionWebApplicationFactory factory)
    {
        Guid userId = Guid.CreateVersion7();
        return SeedGraphAsync(factory: factory, userId: userId,
            email: $"managed-{userId:N}@example.test", accountKey: LocalKey(userId));
    }

    private static async Task<Graph> SeedGraphAsync(
        LocalAdmissionWebApplicationFactory factory, Guid userId, string email, ProviderAccountKey accountKey)
    {
        await using ExploreDbContext database = factory.CreateDatabase();
        var user = new User
        {
            Id = userId,
            Pii = new UserPii { Email = email, FirstName = "Original", LastName = "Profile" },
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(), UserId = userId, User = user,
            ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            Pii = new ActorPii { DisplayName = "Original Profile" }, CreatedAt = DateTime.UtcNow
        };
        var login = new UserExternalLogin
        {
            Id = Guid.CreateVersion7(), UserId = userId, User = user,
            AuthenticationProviderId = (int)accountKey.ProviderKind, AuthenticationProvider = null!,
            ProviderKey = accountKey.Value, CreatedAt = DateTime.UtcNow
        };
        database.AddRange(user, actor, login);
        await database.SaveChangesAsync(CancellationToken);
        return new Graph(UserId: user.Id, ActorId: actor.Id, LoginId: login.Id);
    }

    private static async Task AddLoginAsync(
        LocalAdmissionWebApplicationFactory factory, Guid userId, ProviderAccountKey accountKey)
    {
        await using ExploreDbContext database = factory.CreateDatabase();
        database.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = Guid.CreateVersion7(), UserId = userId, User = null!,
            AuthenticationProviderId = (int)accountKey.ProviderKind, AuthenticationProvider = null!,
            ProviderKey = accountKey.Value, CreatedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync(CancellationToken);
    }

    private static async Task<Counts> ReadCountsAsync(LocalAdmissionWebApplicationFactory factory)
    {
        await using ExploreDbContext database = factory.CreateDatabase();
        return new Counts(
            Users: await database.Users.CountAsync(CancellationToken),
            Actors: await database.Actors.CountAsync(CancellationToken),
            Logins: await database.UserExternalLogins.CountAsync(CancellationToken),
            PlatformRoles: await database.PlatformUserRoles.CountAsync(CancellationToken));
    }

    private static async Task<Profile> ReadProfileAsync(LocalAdmissionWebApplicationFactory factory, Guid userId)
    {
        await using ExploreDbContext database = factory.CreateDatabase();
        User user = await database.Users.Include(candidate => candidate.Pii)
            .Include(candidate => candidate.Actor).ThenInclude(actor => actor!.Pii)
            .SingleAsync(candidate => candidate.Id == userId, CancellationToken);
        return new Profile(Email: user.Email, FirstName: user.FirstName, LastName: user.LastName,
            EmailVerified: user.EmailVerified, UserStamp: user.ConcurrencyStamp,
            ActorId: user.Actor?.Id, ActorName: user.Actor?.DisplayName, ActorStamp: user.Actor?.ConcurrencyStamp);
    }

    private sealed record Graph(Guid UserId, Guid ActorId, Guid LoginId);
    private sealed record Counts(int Users, int Actors, int Logins, int PlatformRoles);
    private sealed record Profile(string Email, string FirstName, string LastName, bool? EmailVerified,
        Guid UserStamp, Guid? ActorId, string? ActorName, Guid? ActorStamp);

    private sealed class TransactionBoundaryInterceptor(Func<CancellationToken, Task> beforeStart) : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public int TransactionsStarted { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                TransactionsStarted++;
                Enabled = false;
                await beforeStart(cancellationToken);
            }
            return result;
        }
    }
}
