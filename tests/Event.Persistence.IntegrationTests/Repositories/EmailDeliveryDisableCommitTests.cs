
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Handlers.Commands;
using Explore.Application.Features.EmailDispatch.Handlers.Queries;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Mail;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryDisableCommitTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfirmedDisable_CommitsOnlySelectedIntentAndRetainsConfiguration(bool tenantScope)
    {
        string path = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(path);
            Guid? target = tenantScope ? scenario.TenantId : null;
            await using var context = CreateContext(path);
            using var fixture = new InstanceSettingsCommandFixture(context: context,
                userId: tenantScope ? scenario.TenantActorId : scenario.PlatformActorId);
            var handlers = Handlers(fixture, target, Tokens());
            var before = await ReadStateAsync(context, target);
            var otherBefore = await ReadStateAsync(context, tenantScope ? null : scenario.TenantId);
            var inheritedBefore = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == scenario.InheritedTenantId);
            var retainedBefore = await ReadRetainedMetadataAsync(context);
            var preview = await PreviewAsync(handlers.Preview, target);
            await Assert.That(await ReadStateAsync(context, target)).IsEqualTo(before);
            await Assert.That(preview.AffectedScopes.Any(scope => scope.TenantId == target)).IsTrue();
            if (!tenantScope)
            {
                await Assert.That(preview.AffectedScopes.Any(scope => scope.TenantId == scenario.TenantId)).IsFalse();
                await Assert.That(preview.AffectedScopes.Any(scope => scope.TenantId == scenario.InheritedTenantId)).IsTrue();
            }

            var response = await handlers.Disable.Handle(Command(preview), CancellationToken.None);

            await Assert.That(response.IsSuccess).IsTrue();
            await using var observer = CreateContext(path);
            var after = await ReadStateAsync(observer, target);
            await Assert.That(after.Value).IsEqualTo("false");
            await Assert.That(after.Revision).IsEqualTo(before.Revision + 1);
            await Assert.That(after.SuppressedThroughRevision).IsEqualTo(after.Revision);
            await Assert.That(after.SuppressedThroughUtc).IsNotNull();
            await Assert.That(await ReadStateAsync(observer, tenantScope ? null : scenario.TenantId)).IsEqualTo(otherBefore);
            var inheritedAfter = await observer.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == scenario.InheritedTenantId);
            await Assert.That(inheritedAfter.DeliveryPolicyRevision).IsEqualTo(
                inheritedBefore.DeliveryPolicyRevision + (tenantScope ? 0 : 1));
            await Assert.That(inheritedAfter.OptionalSuppressedThroughRevision).IsEqualTo(
                tenantScope ? inheritedBefore.OptionalSuppressedThroughRevision : inheritedAfter.DeliveryPolicyRevision);
            await Assert.That((await ReadRetainedMetadataAsync(observer)).SequenceEqual(retainedBefore)).IsTrue();
            await Assert.That(fixture.Notifications.Published.Single().NewValue).IsEqualTo("false");
            await Assert.That(fixture.Notifications.Published.Single().TenantId).IsEqualTo(target);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChangedRevisionOrAffectedMembership_RejectsOldConfirmation(bool addAffectedTenant)
    {
        string path = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(path);
            await using var context = CreateContext(path);
            using var fixture = new InstanceSettingsCommandFixture(context: context, userId: scenario.PlatformActorId);
            var handlers = Handlers(fixture, target: null, Tokens());
            var preview = await PreviewAsync(handlers.Preview, target: null);
            await using (var writer = CreateContext(path))
            {
                if (addAffectedTenant)
                    await AddTenantAsync(writer);
                else
                    await SetEmailSettingAsync(writer, GovernanceSettingKeys.Email.FromName, "\"New sender name\"");
            }
            var fresh = await PreviewAsync(handlers.Preview, target: null);
            if (addAffectedTenant)
            {
                await Assert.That(fresh.ExpectedRevision).IsEqualTo(preview.ExpectedRevision);
                await Assert.That(fresh.AffectedScopes.Length).IsEqualTo(preview.AffectedScopes.Length + 1);
            }
            else
                await Assert.That(fresh.ExpectedRevision).IsGreaterThan(preview.ExpectedRevision);
            var before = await ReadStateAsync(context, target: null);

            var response = await handlers.Disable.Handle(Command(preview), CancellationToken.None);

            await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
            await using var observer = CreateContext(path);
            await Assert.That(await ReadStateAsync(observer, target: null)).IsEqualTo(before);
            await Assert.That(fixture.Notifications.Published.Count).IsEqualTo(0);
        }
        finally { DeleteDatabase(path); }
    }

    public enum InvalidConfirmation { Acknowledgement, DifferentActor, RevokedTenantRole, DifferentCurrentTenant }

    [Test]
    [Arguments(InvalidConfirmation.Acknowledgement)]
    [Arguments(InvalidConfirmation.DifferentActor)]
    [Arguments(InvalidConfirmation.RevokedTenantRole)]
    [Arguments(InvalidConfirmation.DifferentCurrentTenant)]
    public async Task Confirmation_DoesNotReplaceExactAcknowledgementOrFreshAuthority(InvalidConfirmation invalid)
    {
        string path = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(path);
            bool tenantScope = invalid is InvalidConfirmation.RevokedTenantRole or InvalidConfirmation.DifferentCurrentTenant;
            Guid? target = tenantScope ? scenario.TenantId : null;
            await using var context = CreateContext(path);
            using var fixture = new InstanceSettingsCommandFixture(context: context,
                userId: tenantScope ? scenario.TenantActorId : scenario.PlatformActorId);
            var tokenService = Tokens();
            var handlers = Handlers(fixture, target, tokenService);
            var preview = await PreviewAsync(handlers.Preview, target);
            var before = await ReadStateAsync(context, target);
            var request = Command(preview);
            BaseCommandResponse<Guid> response;
            if (invalid == InvalidConfirmation.DifferentActor)
            {
                using var differentActor = new InstanceSettingsCommandFixture(context: context, userId: scenario.OtherPlatformActorId);
                response = await Handlers(differentActor, target, tokenService).Disable.Handle(request, CancellationToken.None);
                await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
                await Assert.That(differentActor.Notifications.Published.Count).IsEqualTo(0);
            }
            else
            {
                if (invalid == InvalidConfirmation.Acknowledgement)
                    request = request with { Acknowledgement = DisableEmailDeliveryCommand.RequiredAcknowledgement + " " };
                else if (invalid == InvalidConfirmation.RevokedTenantRole)
                {
                    await using var writer = CreateContext(path);
                    var grant = await writer.TenantUserRoleGrants
                        .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                        .SingleAsync(row => row.TenantId == scenario.TenantId && row.RoleId == (int)RoleEnum.TenantAdmin);
                    grant.RevokedAt = DateTime.UtcNow;
                    await writer.SaveChangesAsync();
                }
                else
                    handlers = Handlers(fixture, scenario.InheritedTenantId, tokenService);

                response = await handlers.Disable.Handle(request, CancellationToken.None);
                if (tenantScope)
                {
                    await Assert.That(response.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
                    var deniedPreview = await handlers.Preview.Handle(new PreviewEmailDeliveryDisableQuery(TenantId: target), CancellationToken.None);
                    await Assert.That(deniedPreview.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
                }
                else
                    await Assert.That(response.Errors).IsNotNull();
            }
            await Assert.That(response.IsSuccess).IsFalse();
            await using var observer = CreateContext(path);
            await Assert.That(await ReadStateAsync(observer, target)).IsEqualTo(before);
            await Assert.That(fixture.Notifications.Published.Count).IsEqualTo(0);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfirmedToken_CannotDisableAgainAfterReenable(bool tenantScope)
    {
        string path = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(path);
            Guid? target = tenantScope ? scenario.TenantId : null;
            await using var context = CreateContext(path);
            using var fixture = new InstanceSettingsCommandFixture(context: context,
                userId: tenantScope ? scenario.TenantActorId : scenario.PlatformActorId);
            var handlers = Handlers(fixture, target, Tokens());
            var preview = await PreviewAsync(handlers.Preview, target);
            await Assert.That((await handlers.Disable.Handle(Command(preview), CancellationToken.None)).IsSuccess).IsTrue();
            await using (var writer = CreateContext(path))
            {
                if (target is Guid tenantId)
                    await SetEmailSettingAsync(writer, GovernanceSettingKeys.Email.DeliveryEnabled, "true", tenantId: tenantId);
                else
                {
                    await SetEmailSettingAsync(writer, GovernanceSettingKeys.Email.DeliveryEnabled, "true");
                }
            }
            var reenabled = await ReadStateAsync(context, target);

            var replay = await handlers.Disable.Handle(Command(preview), CancellationToken.None);

            await Assert.That(replay.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
            await using var observer = CreateContext(path);
            await Assert.That(reenabled.Value).IsEqualTo("true");
            await Assert.That(await ReadStateAsync(observer, target)).IsEqualTo(reenabled);
            await Assert.That(fixture.Notifications.Published.Count).IsEqualTo(1);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RevisionWriteFailure_RollsBackConfirmedSettingAndSuppression(bool tenantScope)
    {
        string path = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(path);
            Guid? target = tenantScope ? scenario.TenantId : null;
            await using var context = CreateContext(path);
            using var fixture = new InstanceSettingsCommandFixture(context: context,
                userId: tenantScope ? scenario.TenantActorId : scenario.PlatformActorId);
            var handlers = Handlers(fixture, target, Tokens());
            var preview = await PreviewAsync(handlers.Preview, target);
            var before = await ReadStateAsync(context, target);
            var retainedBefore = await ReadRetainedMetadataAsync(context);
            var entity = context.Model.FindEntityType(tenantScope ? typeof(EmailDispatchTenantControl) : typeof(EmailDispatchProcessorState))!;
            string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!);
            await context.Database.ExecuteSqlRawAsync($"CREATE TRIGGER reject_confirmed_disable AFTER UPDATE ON {table} BEGIN SELECT RAISE(ABORT, 'disable_revision_rejected'); END");

            bool rejected = false;
            try { await handlers.Disable.Handle(Command(preview), CancellationToken.None); }
            catch (Exception exception) when (exception is SqliteException or DbUpdateException) { rejected = true; }

            await Assert.That(rejected).IsTrue();
            await using var observer = CreateContext(path);
            await Assert.That(await ReadStateAsync(observer, target)).IsEqualTo(before);
            await Assert.That((await ReadRetainedMetadataAsync(observer)).SequenceEqual(retainedBefore)).IsTrue();
            await Assert.That(fixture.Notifications.Published.Count).IsEqualTo(0);
        }
        finally { DeleteDatabase(path); }
    }

    [Test]
    public async Task NewlyLockedTenantDelegation_ConflictsWithPreviouslyValidDisablePreview()
    {
        string path = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(path);
            await using var context = CreateContext(path);
            using var fixture = new InstanceSettingsCommandFixture(context: context, userId: scenario.TenantActorId);
            var handlers = Handlers(fixture, scenario.TenantId, Tokens());
            var preview = await PreviewAsync(handlers.Preview, scenario.TenantId);
            await using (var writer = CreateContext(path))
                await SetEmailSettingAsync(writer, GovernanceSettingKeys.TenantDelegation.LockSmtp, "true");
            var before = await ReadStateAsync(context, scenario.TenantId);
            var metadata = await ReadRetainedMetadataAsync(context);

            var result = await handlers.Disable.Handle(Command(preview), CancellationToken.None);

            await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
            await using var observer = CreateContext(path);
            await Assert.That(await ReadStateAsync(observer, scenario.TenantId)).IsEqualTo(before);
            await Assert.That((await ReadRetainedMetadataAsync(observer)).SequenceEqual(metadata)).IsTrue();
            await Assert.That(fixture.Notifications.Published).IsEmpty();
        }
        finally { DeleteDatabase(path); }
    }

    private static (PreviewEmailDeliveryDisableQueryHandler Preview, DisableEmailDeliveryCommandHandler Disable) Handlers(
        InstanceSettingsCommandFixture fixture, Guid? target, EmailDeliveryDisableTokenService tokenService)
    {
        var tenantContext = new BoundTenantContext(TenantId: target ?? Guid.Empty);
        fixture.Context.TenantContext = tenantContext;
        var impact = new EmailDeliveryDisableImpactReader(context: fixture.Context);
        var platformRoles = new PlatformUserRoleRepository(fixture.Context);
        var tenantRoles = new TenantUserRoleGrantRepository(fixture.Context);
        return (new PreviewEmailDeliveryDisableQueryHandler(
            adminContext: fixture.AdminContext, tenantContext: tenantContext, impactReader: impact,
            tokenService: tokenService, mutationLock: fixture.MutationLock, unitOfWork: fixture.UnitOfWork,
            platformRoles: platformRoles, tenantRoles: tenantRoles),
            new DisableEmailDeliveryCommandHandler(
                adminContext: fixture.AdminContext, tenantContext: tenantContext,
                emailSettingsWriter: CreateEmailSettingsWriter(fixture.Context, fixture.MutationLock, tokenService),
                mutationLock: fixture.MutationLock, unitOfWork: fixture.UnitOfWork,
                publisher: fixture.Mediator, platformRoles: platformRoles, tenantRoles: tenantRoles));
    }

    private static async Task<EmailDeliveryDisablePreviewDto> PreviewAsync(PreviewEmailDeliveryDisableQueryHandler handler, Guid? target)
    {
        var result = await handler.Handle(new PreviewEmailDeliveryDisableQuery(TenantId: target), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.CanDisable).IsTrue();
        await Assert.That(result.Id.ConfirmationToken).IsNotNull();
        return result.Id;
    }

    private static DisableEmailDeliveryCommand Command(EmailDeliveryDisablePreviewDto preview) =>
        new(TenantId: preview.TenantId, ExpectedRevision: preview.ExpectedRevision,
            Acknowledgement: DisableEmailDeliveryCommand.RequiredAcknowledgement, ConfirmationToken: preview.ConfirmationToken);

    private static async Task<Scenario> SeedAsync(string path)
    {
        await CreateDatabaseAsync(path);
        await using var context = CreateContext(path);
        await SetEmailSettingAsync(context, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false");
        Guid platformActor = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
        Guid otherPlatformActor = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
        Guid tenantId = await AddTenantAsync(context);
        Guid inheritedTenantId = await AddTenantAsync(context);
        DateTime now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = now,
            Pii = new UserPii { Email = $"disable-admin-{Guid.CreateVersion7():N}@example.test", FirstName = "Tenant", LastName = "Admin" }
        };
        var membership = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = now,
            CreatedAt = now
        };
        context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            TenantUserId = membership.Id,
            TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = now,
            CreatedAt = now
        });
        await context.SaveChangesAsync();
        await ApplyEmailSettingsAsync(context,
            [new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.DeliveryEnabled, Kind: EmailDeliverySettingMutationKind.SetValue, Value: "true", IsLocked: true),
             new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.SmtpHost, Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"smtp.independent.test\""),
             new(TenantId: tenantId, Key: GovernanceSettingKeys.Email.FromAddress, Kind: EmailDeliverySettingMutationKind.SetValue, Value: "\"events@independent.test\"")],
            actorUserId: user.Id);
        var processor = await context.EmailDispatchProcessorStates.SingleAsync();
        processor.IsPaused = true;
        processor.PauseReason = "Operator maintenance";
        processor.PausedAt = now;
        processor.GlobalSmtpRateLimitPerMinuteOverride = 23;
        processor.OptionalRemindersDeferred = true;
        processor.SmtpAvailableTokens = 7;
        processor.SmtpRefillAt = now;
        foreach (Guid id in new[] { tenantId, inheritedTenantId })
        {
            var control = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleOrDefaultAsync(row => row.TenantId == id);
            if (control is null)
            {
                control = new EmailDispatchTenantControl { Id = Guid.CreateVersion7(), TenantId = id, CreatedAt = now };
                context.EmailDispatchTenantControls.Add(control);
            }
            control.IsPaused = true;
            control.PauseReason = "Tenant maintenance";
            control.PausedAt = now;
            control.SmtpAvailableTokens = 3;
            control.SmtpRefillAt = now;
        }
        await context.SaveChangesAsync();
        return new Scenario(TenantId: tenantId, InheritedTenantId: inheritedTenantId,
            TenantActorId: user.Id, PlatformActorId: platformActor, OtherPlatformActorId: otherPlatformActor);
    }

    private static async Task<Guid> AddTenantAsync(ExploreDbContext context)
    {
        Guid id = Guid.CreateVersion7();
        context.Tenants.Add(new Tenant
        {
            Id = id,
            FullName = "Confirmed disable tenant",
            Slug = $"disable-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static async Task<PolicyState> ReadStateAsync(ExploreDbContext context, Guid? target)
    {
        if (target is Guid tenantId)
        {
            var setting = await context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == tenantId && row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
            var control = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == tenantId);
            return new(setting.Value, control.DeliveryPolicyRevision, control.OptionalSuppressedThroughRevision, control.OptionalSuppressedThroughUtc);
        }
        var globalSetting = await context.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
        var processor = await context.EmailDispatchProcessorStates.AsNoTracking().SingleAsync();
        return new(globalSetting.Value, processor.DeliveryPolicyRevision, processor.OptionalSuppressedThroughRevision, processor.OptionalSuppressedThroughUtc);
    }

    private static async Task<string[]> ReadRetainedMetadataAsync(ExploreDbContext context)
    {
        // Compare detached scalar projections, excluding only the intended value and policy/audit mutation fields.
        var system = await context.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey)
            .Select(row => new
            {
                row.Id,
                row.SettingKey,
                Value = row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled ? null : row.Value,
                row.IsLocked,
                row.CreatedAt,
                row.CreatedBy,
                row.Category,
                row.Description,
                row.DisplayOrder,
                row.AllowedValues,
                row.SettingValueTypeId
            }).ToArrayAsync();
        var tenants = await context.TenantSettingOverrides
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                row.TenantId,
                row.SettingKey,
                Value = row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled ? null : row.Value,
                row.IsLocked,
                row.CreatedAt,
                row.CreatedBy
            }).ToArrayAsync();
        var processor = await context.EmailDispatchProcessorStates.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                row.IsPaused,
                row.PauseReason,
                row.PausedAt,
                row.PausedBy,
                row.GlobalSmtpRateLimitPerMinuteOverride,
                row.OptionalRemindersDeferred,
                row.SmtpAvailableTokens,
                row.SmtpRefillAt
            }).ToArrayAsync();
        var controls = await context.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.TenantId, row.IsPaused, row.PauseReason, row.PausedAt, row.PausedBy, row.SmtpAvailableTokens, row.SmtpRefillAt }).ToArrayAsync();
        return [System.Text.Json.JsonSerializer.Serialize(system), System.Text.Json.JsonSerializer.Serialize(tenants),
            System.Text.Json.JsonSerializer.Serialize(processor), System.Text.Json.JsonSerializer.Serialize(controls)];
    }

    private static EmailDeliveryDisableTokenService Tokens() => new(dataProtectionProvider: new EphemeralDataProtectionProvider());
    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"email-disable-commit-{Guid.CreateVersion7():N}.db");
    private static void DeleteDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
    }

    private sealed record BoundTenantContext(Guid TenantId) : ITenantContext;
    private sealed record Scenario(Guid TenantId, Guid InheritedTenantId, Guid TenantActorId, Guid PlatformActorId, Guid OtherPlatformActorId);
    private sealed record PolicyState(string Value, long Revision, long? SuppressedThroughRevision, DateTime? SuppressedThroughUtc);
}
