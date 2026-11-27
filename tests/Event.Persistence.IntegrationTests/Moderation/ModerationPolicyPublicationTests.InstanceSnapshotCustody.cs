using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Snapshot_rejects_same_account_principal_replacement_during_owner_acquisition()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        await database.PublishAsync(database.Request());
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var clock = new ImportExportOwnerClock();
        var boundary = new SnapshotCredentialBoundary();
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(), clock,
            services =>
            {
                var registration = services.Last(service =>
                    service.ServiceType == typeof(IGlobalExploreDbContextFactory));
                var original = (IGlobalExploreDbContextFactory)registration.ImplementationInstance!;
                services.Remove(registration);
                services.AddSingleton<IGlobalExploreDbContextFactory>(
                    new ReplacingSnapshotPrincipalFactory(original, boundary));
            });
        await human.InitializeAsync();
        boundary.Request = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;

        await Assert.That(async () => await human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
            .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides,
                ConfigurationManifestExportScope.Instance, CancellationToken.None))
            .Throws<UnauthorizedAccessException>();
        await Assert.That(boundary.Reached).IsTrue();
    }

    [Test]
    public async Task Snapshot_rejects_credential_expiry_during_successful_child_disposal()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        await database.PublishAsync(database.Request());
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var clock = new ImportExportOwnerClock();
        var boundary = new SnapshotCredentialBoundary();
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(), clock,
            services =>
            {
                services.AddScoped(provider => new SnapshotDisposalExpiry(
                    provider.GetRequiredService<ExploreDbContext>(), clock, boundary));
                var registration = services.Last(service =>
                    service.ServiceType == typeof(IInstanceAdministratorCommitAuthority));
                var original = registration.ImplementationFactory!;
                services.Remove(registration);
                services.AddScoped<IInstanceAdministratorCommitAuthority>(provider =>
                {
                    var authority = (IInstanceAdministratorCommitAuthority)original(provider);
                    _ = provider.GetRequiredService<SnapshotDisposalExpiry>();
                    return authority;
                });
            });
        await human.InitializeAsync();
        var principal = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User;
        boundary.Lifetime = await human.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(principal, CancellationToken.None)
            ?? throw new InvalidOperationException("The witness requires the genuine validated credential lifetime.");

        await Assert.That(async () => await human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
            .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides,
                ConfigurationManifestExportScope.Instance, CancellationToken.None))
            .Throws<UnauthorizedAccessException>();
        await Assert.That(boundary.Reached).IsTrue();
    }

    private sealed class SnapshotCredentialBoundary
    {
        internal HttpContext? Request { get; set; }
        internal HumanAuthenticationLifetime? Lifetime { get; set; }
        internal bool Reached { get; set; }
    }

    private sealed class ReplacingSnapshotPrincipalFactory(
        IGlobalExploreDbContextFactory original, SnapshotCredentialBoundary boundary)
        : IGlobalExploreDbContextFactory
    {
        public async Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            var context = await original.CreateDbContextAsync(cancellationToken);
            if (boundary.Request is { } request && !boundary.Reached)
            {
                boundary.Reached = true;
                request.User = new ClaimsPrincipal(request.User.Identities);
            }
            return context;
        }
    }

    private sealed class SnapshotDisposalExpiry(
        ExploreDbContext context, ImportExportOwnerClock clock, SnapshotCredentialBoundary boundary)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (context.IsConfigurationInstanceImportOwner && boundary.Lifetime is { } lifetime)
            {
                await Task.Yield();
                clock.Current = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                boundary.Reached = true;
            }
        }
    }
}
