using System.Data.Common;
using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain.Enums;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Native_instance_import_snapshot_exports_authorized_definitions_without_preparing_the_parent_tenant()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        var publication = RebindExportRequest(database.Request(), database.Scope.Parent!);
        await Assert.That((await database.PublishAsync(publication)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(),
            new ImportExportOwnerClock());
        await human.InitializeAsync();
        var parent = human.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await Assert.That(parent.Subject).IsNull();

        var artifact = await human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
            .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides, CancellationToken.None);
        var manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(artifact.Utf8Json);

        await Assert.That(manifest.Spec.Instance.Moderation).IsNotNull();
        await Assert.That(manifest.Spec.Tenants.Single(row => row.Metadata.Name == $"export-{foreign.TenantId:N}")
            .Spec.Moderation).IsNotNull();
        await Assert.That(manifest.Spec.Tenants.Single(row => row.Metadata.Name == $"publication-{database.TenantId:N}")
            .Spec.Moderation).IsNull();
        await Assert.That(parent.Subject).IsNull();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
    }

    [Test]
    [Arguments("unauthenticated")]
    [Arguments("machine")]
    [Arguments("tenant-administrator")]
    [Arguments("expired-credential")]
    public async Task Native_instance_import_snapshot_denies_callers_without_current_platform_human_authority(string caller)
    {
        await using var database = await Database.CreateAsync();
        await database.PublishAsync(database.Request());
        if (caller == "tenant-administrator")
            await DemoteExportAdministratorAsync(database);
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var clock = new ImportExportOwnerClock();
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(),
            clock);
        await human.InitializeAsync(authenticate: caller != "unauthenticated");
        if (caller == "expired-credential")
        {
            var principal = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User;
            var lifetime = await human.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(principal, CancellationToken.None)
                ?? throw new InvalidOperationException("The regression requires the genuine validated credential lifetime.");
            clock.Current = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
        }
        if (caller == "machine")
        {
            var principal = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User;
            var identity = (ClaimsIdentity)principal.Identity!;
            identity.AddClaims([
                new(ApiAuthenticationClaimTypes.ApiKeyId, Guid.CreateVersion7().ToString("N")),
                new(ApiAuthenticationClaimTypes.OwnerType, nameof(ExternalApiKeyOwnerType.InstanceAdmin)),
                new(ApiAuthenticationClaimTypes.OwnerId, database.AdminId.ToString("D"))
            ]);
            await Assert.That(human.Machine.IsMachineCaller).IsTrue();
        }

        try
        {
            await human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
                .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides, CancellationToken.None);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            || caller != "expired-credential" && exception is ModerationConfigurationImportException
            { Outcome: ModerationPolicyPublicationOutcome.Denied or ModerationPolicyPublicationOutcome.Unauthenticated })
        {
            await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
            await Assert.That(context.IsTenantFilterBypassed).IsFalse();
            return;
        }
        throw new InvalidOperationException("Instance definitions must not be disclosed without current platform human authority.");
    }

    [Test]
    public async Task Null_tenant_global_context_without_native_import_selection_cannot_export_definitions()
    {
        await using var database = await Database.CreateAsync();
        await database.PublishAsync(database.Request());
        await using var context = database.Context();
        var clock = new ImportExportOwnerClock();
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(), clock);
        await human.InitializeAsync();
        await using var global = await human.Services.GetRequiredService<IGlobalExploreDbContextFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        await Assert.That(global.TenantFilterTenantId).IsNull();
        var publications = new ModerationPolicyPublicationRepository(global, human.Admin, human.Machine,
            database.Inventory, null, clock);
        var exporter = new ModerationConfigurationExportService(publications, new EfCoreUnitOfWork(global));

        await Assert.That(async () => await exporter.ReadAsync(
            ConfigurationImportTarget.ForInstance(), CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
        await Assert.That(global.IsTenantFilterBypassed).IsFalse();
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
    }

    [Test]
    public async Task Credential_expiry_after_the_final_native_authority_wait_prevents_instance_disclosure()
    {
        await using var database = await Database.CreateAsync();
        await database.PublishAsync(database.Request());
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var clock = new ImportExportOwnerClock();
        var expiry = new FinalExportExpiry();
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(), clock,
            services =>
            {
                var descriptor = services.Last(row => row.ServiceType == typeof(IInstanceAdministratorCommitAuthority));
                var nativeFactory = descriptor.ImplementationFactory
                    ?? throw new InvalidOperationException("The regression requires the native scoped authority factory.");
                services.Remove(descriptor);
                services.AddScoped<IInstanceAdministratorCommitAuthority>(provider =>
                    new ExpiringAfterNativeAuthority((IInstanceAdministratorCommitAuthority)nativeFactory(provider),
                        provider.GetRequiredService<ExploreDbContext>(), clock, expiry));
            });
        await human.InitializeAsync();
        var principal = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User;
        expiry.Lifetime = await human.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(principal, CancellationToken.None)
            ?? throw new InvalidOperationException("The regression requires the original validated credential lifetime.");

        await Assert.That(async () => await human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
            .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides, CancellationToken.None))
            .Throws<UnauthorizedAccessException>();
        await Assert.That(expiry.ReachedFinalAuthority).IsTrue();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("expiry")]
    [Arguments("principal")]
    public async Task Credential_change_during_a_later_snapshot_read_prevents_returning_the_artifact(string change)
    {
        await using var database = await Database.CreateAsync();
        await database.PublishAsync(database.Request());
        var clock = new ImportExportOwnerClock();
        var boundary = new LateSnapshotCredentialChange(clock, change);
        await using var context = database.Context(boundary);
        await SeedExportPaidPolicyAsync(context);
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(), clock);
        await human.InitializeAsync();
        boundary.Request = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        boundary.Lifetime = await human.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(boundary.Request.User, CancellationToken.None)
            ?? throw new InvalidOperationException("The regression requires the original validated credential lifetime.");

        await Assert.That(async () => await human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
            .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides, CancellationToken.None))
            .Throws<UnauthorizedAccessException>();
        await Assert.That(boundary.ReachedLaterRead).IsTrue();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private sealed class LateSnapshotCredentialChange(ImportExportOwnerClock clock, string change) : DbCommandInterceptor
    {
        internal HttpContext? Request { get; set; }
        internal HumanAuthenticationLifetime? Lifetime { get; set; }
        internal bool ReachedLaterRead { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!ReachedLaterRead && Request is { } request && Lifetime is { } lifetime
                && eventData.Context is ExploreDbContext
                {
                    IsConfigurationInstanceImportOwner: true,
                    ModerationTrustDomainId: null
                } owner
                && owner.Model.FindEntityType(typeof(LegalDocument))?.GetTableName() is { } table
                && command.CommandText.Contains(table, StringComparison.Ordinal))
            {
                ReachedLaterRead = true;
                if (change == "expiry")
                    clock.Current = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                else
                    request.User = new ClaimsPrincipal(request.User.Identities);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FinalExportExpiry
    {
        internal HumanAuthenticationLifetime? Lifetime { get; set; }
        internal bool ReachedFinalAuthority { get; set; }
    }

    private sealed class ExpiringAfterNativeAuthority(
        IInstanceAdministratorCommitAuthority inner, ExploreDbContext owner,
        ImportExportOwnerClock clock, FinalExportExpiry expiry) : IInstanceAdministratorCommitAuthority
    {
        public async Task<bool> IsInstanceAdministratorForCommitAsync(Guid principalId, CancellationToken cancellationToken)
        {
            bool allowed = await inner.IsInstanceAdministratorForCommitAsync(principalId, cancellationToken);
            if (allowed && owner.ModerationTrustDomainId is not null)
            {
                var lifetime = expiry.Lifetime
                    ?? throw new InvalidOperationException("Original credential custody was not observed.");
                expiry.ReachedFinalAuthority = true;
                clock.Current = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
            }
            return allowed;
        }
    }
}
