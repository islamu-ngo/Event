using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class LocalAdministrationNativeAuthenticationTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    public async Task PrivateManagerCreatesAndDisclosesOnlyTheAcknowledgedNativeCredential()
    {
        Guid tenant = await SeedTenant();
        await using var fixture = await TenantBrandingNativeAuthenticationFixture.CreateAsync(postgres, tenant);
        var command = new CreateLocalIdentityCommand(Guid.CreateVersion7(),
            $"{Guid.CreateVersion7():N}@example.test", "Native", "Credential");
        LocalCredentialCreateResult created = await fixture.InOriginalRequestAsync(async () =>
        {
            var preparation = fixture.Services.GetRequiredService<ILocalAdministrationPreparation>();
            var administration = fixture.Services.GetRequiredService<ILocalHumanCredentialAdministration>();
            var original = await preparation.PrepareAsync(command, fixture.Subject.PrincipalId, default);
            var result = await administration.CreatePendingAsync(original, command, default);
            await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            var snapshot = (await administration.ReadProvisioningAsync(original, command, default))!;
            await Assert.That((await administration.BindAsync(original, command, snapshot, default)).IsSuccess).IsTrue();
            await Assert.That(await administration.ActivateAsync(original, command, snapshot, default))
                .IsEqualTo(LocalCredentialActivationOutcome.Activated);
            var status = (await administration.ReadOperationAsync(original, command, default))!;
            await Assert.That(status.IsCurrent).IsTrue();
            await Assert.That(status.CredentialState).IsEqualTo(LocalCredentialState.ChangeRequired);

            var replay = await preparation.PrepareAsync(command, fixture.Subject.PrincipalId, default);
            var repeated = await administration.CreatePendingAsync(replay, command, default);
            await Assert.That(repeated.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Replayed);
            await Assert.That(repeated.TemporaryPassword).IsNull();
            await Assert.That((await administration.ReadOperationAsync(replay, command, default))!.IsCurrent).IsTrue();
            return result;
        });
        await using var observer = postgres.CreateDbContext();
        var user = await observer.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == created.Receipt!.LocalSubjectId);
        var hasher = fixture.Services.GetRequiredService<IPasswordHasher<LocalIdentityUser>>();
        await Assert.That(hasher.VerifyHashedPassword(user, user.PasswordHash!, created.TemporaryPassword!))
            .IsNotEqualTo(PasswordVerificationResult.Failed);
        await Assert.That(await observer.UserExternalLogins.AsNoTracking()
            .AnyAsync(row => row.Id == created.Receipt!.ExternalLoginId && row.UserId == user.Id)).IsTrue();
        await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task OriginalLocalPrincipalCanDiscloseItsExactNativeSelfReset()
    {
        Guid tenant = await SeedTenant();
        await using var fixture = await TenantBrandingNativeAuthenticationFixture.CreateAsync(postgres, tenant);
        LocalIdentityCredentialOperation previous;
        LocalIdentityUser before;
        await using (var observer = postgres.CreateDbContext())
        {
            previous = await observer.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleAsync(row => row.LocalSubjectId == fixture.Subject.PrincipalId
                    && row.Stage == LocalCredentialOperationStage.Replaced);
            before = await observer.Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == fixture.Subject.PrincipalId);
        }
        var command = new ResetLocalCredentialCommand(Guid.CreateVersion7(), fixture.Subject.PrincipalId,
            previous.Id, previous.ConcurrencyStamp, "Original native self-reset");
        var result = await fixture.InOriginalRequestAsync(async () =>
        {
            var original = await fixture.Services.GetRequiredService<ILocalAdministrationPreparation>()
                .PrepareAsync(command, fixture.Subject.PrincipalId, default);
            var administration = fixture.Services.GetRequiredService<ILocalHumanCredentialAdministration>();
            var reset = await administration.ResetAsync(original, command, default);
            await Assert.That(reset.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
            var status = (await administration.ReadOperationAsync(original, command, default))!;
            await Assert.That(status.IsCurrent).IsTrue();
            await Assert.That(status.CredentialState).IsEqualTo(LocalCredentialState.ChangeRequired);
            await Assert.That(status.VerifiedAt).IsEqualTo(previous.VerifiedAt);
            await Assert.That(status.VerifiedByApplicationUserId).IsEqualTo(previous.VerifiedByApplicationUserId);
            return reset;
        });
        await using var after = postgres.CreateDbContext();
        var user = await after.Set<LocalIdentityUser>().AsNoTracking().SingleAsync(row => row.Id == before.Id);
        await Assert.That(user.SecurityStamp).IsNotEqualTo(before.SecurityStamp);
        await Assert.That(user.EmailConfirmed).IsEqualTo(before.EmailConfirmed);
        await Assert.That(user.LockoutEnabled).IsEqualTo(before.LockoutEnabled);
        await Assert.That(fixture.Services.GetRequiredService<IPasswordHasher<LocalIdentityUser>>()
            .VerifyHashedPassword(user, user.PasswordHash!, result.TemporaryPassword!))
            .IsNotEqualTo(PasswordVerificationResult.Failed);
        await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePendingGroupCannotOutliveTheOriginalCredential(bool expire)
    {
        Guid tenant = await SeedTenant();
        var clock = new ControlledClock();
        var terminal = new ExpireAfterSources(clock);
        await using var fixture = await TenantBrandingNativeAuthenticationFixture.CreateAsync(
            postgres, tenant, clock, terminal.Decorate);
        terminal.Expiry = expire
            ? fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1) : null;
        var command = new CreateLocalIdentityCommand(Guid.CreateVersion7(),
            $"{Guid.CreateVersion7():N}@example.test", "Native", "Deadline");
        await fixture.InOriginalRequestAsync(async () =>
        {
            var original = await fixture.Services.GetRequiredService<ILocalAdministrationPreparation>()
                .PrepareAsync(command, fixture.Subject.PrincipalId, default);
            Task<LocalCredentialCreateResult> Create() => fixture.Services
                .GetRequiredService<ILocalHumanCredentialAdministration>().CreatePendingAsync(original, command, default);
            if (expire)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create());
            else
                await Assert.That((await Create()).Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            return true;
        });
        await using var observer = postgres.CreateDbContext();
        await Assert.That(await observer.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .AnyAsync(row => row.Id == command.OperationId)).IsEqualTo(!expire);
        await Assert.That(await observer.Set<LocalIdentityUser>().AsNoTracking()
            .AnyAsync(row => row.Email == command.Email)).IsEqualTo(!expire);
        await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
    }

    private async Task<Guid> SeedTenant()
    {
        await using var setup = postgres.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Local native custody",
            Slug = $"local-custody-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        setup.Tenants.Add(tenant);
        await setup.SaveChangesAsync();
        return tenant.Id;
    }

    private sealed class ControlledClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ExpireAfterSources(ControlledClock clock)
    {
        internal DateTimeOffset? Expiry { get; set; }
        internal void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IHumanActionCommitFinalizer));
            services.Remove(descriptor);
            services.AddScoped<IHumanActionCommitFinalizer>(provider => new Finalizer(
                (IHumanActionCommitFinalizer)(descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), this, clock));
        }

        private sealed class Finalizer(IHumanActionCommitFinalizer inner, ExpireAfterSources terminal,
            ControlledClock clock) : IHumanActionCommitFinalizer
        {
            public void Register(Func<CancellationToken, Task> validateSources, Action<DateTimeOffset> validateAssignments) =>
                inner.Register(async token =>
                {
                    await validateSources(token);
                    if (terminal.Expiry is { } expiry) clock.Now = expiry;
                }, validateAssignments);
        }
    }
}
