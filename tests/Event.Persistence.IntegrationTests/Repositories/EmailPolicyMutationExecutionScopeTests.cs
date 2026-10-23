using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PolicyMutationExecutionScopeSqliteFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PolicyMutationExecutionScopeSqlite")]
public sealed class EmailPolicyMutationExecutionScopeTests(PolicyMutationExecutionScopeSqliteFixture fixture)
{
    [Test]
    public async Task BorrowedReaderCannotAcquireEmailPurposeEvenForTheSameTenant()
    {
        await using var caller = fixture.OpenCaller();
        await using var reader = await caller.ServiceProvider
            .GetRequiredService<IConfigurationManifestTenantReadScopeFactory>()
            .CreateAsync(fixture.CallerTenantId, default);
        var context = reader.Services.GetRequiredService<ExploreDbContext>();
        await Assert.That(async () =>
        {
            await using var rejected = await Factory(reader.Services)
                .CreateAsync(PolicyMutationScope.EmailTenant(fixture.CallerTenantId), default);
        }).Throws<InvalidOperationException>();
        await Assert.That(reader.Services.GetRequiredService<ExploreDbContext>()).IsSameReferenceAs(context);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryOwnerCannotBePromotedToEmailPurpose(bool tenant)
    {
        await using var caller = fixture.OpenCaller();
        var ordinary = tenant ? new PolicyMutationScope(fixture.TargetTenantId) : PolicyMutationScope.Instance;
        await using var owner = await Factory(caller.ServiceProvider).CreateAsync(ordinary, default);
        var context = owner.Services.GetRequiredService<ExploreDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await Assert.That(async () =>
        {
            await using var rejected = await Factory(owner.Services).CreateAsync(
                tenant ? PolicyMutationScope.EmailTenant(fixture.TargetTenantId) : PolicyMutationScope.EmailInstance,
                default);
        }).Throws<InvalidOperationException>();

        await Assert.That(owner.Services.GetRequiredService<ExploreDbContext>()).IsSameReferenceAs(context);
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task DirectWriterCannotEscapeAnAlreadyStartedOrdinaryTransaction()
    {
        await using var caller = fixture.OpenCaller();
        var context = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var writer = caller.ServiceProvider.GetRequiredService<IEmailDeliverySettingsWriter>();

        await Assert.That(async () => await writer.ApplyAsync(
            [new(null, GovernanceSettingKeys.Email.FromName, EmailDeliverySettingMutationKind.SetValue, "\"rejected\"")],
            actorUserId: null)).Throws<InvalidOperationException>();
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EmailOwnerReusesExactlyItsPurposeAndTenant(bool tenant)
    {
        await using var caller = fixture.OpenCaller();
        var selection = tenant
            ? PolicyMutationScope.EmailTenant(fixture.TargetTenantId) : PolicyMutationScope.EmailInstance;
        await using var owner = await Factory(caller.ServiceProvider).CreateAsync(selection, default);
        var context = owner.Services.GetRequiredService<ExploreDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await using var nested = await Factory(owner.Services).CreateAsync(selection, default);

        await Assert.That(nested.Services).IsSameReferenceAs(owner.Services);
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(selection.TenantId);
        await Assert.That(async () =>
        {
            await using var rejected = await Factory(owner.Services).CreateAsync(
                tenant ? PolicyMutationScope.EmailInstance : PolicyMutationScope.EmailTenant(fixture.TargetTenantId),
                default);
        }).Throws<InvalidOperationException>();
        await Assert.That(async () =>
        {
            await using var rejected = await Factory(owner.Services).CreateAsync(new(selection.TenantId), default);
        }).Throws<InvalidOperationException>();
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task SettingsAndRevisionRollBackWithTheEmailOwner()
    {
        await using var caller = fixture.OpenCaller();
        await using var owner = await Factory(caller.ServiceProvider).CreateAsync(PolicyMutationScope.EmailInstance, default);
        var context = owner.Services.GetRequiredService<ExploreDbContext>();
        var before = await context.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost);
        long revision = await context.EmailDispatchProcessorStates.AsNoTracking()
            .Select(row => row.DeliveryPolicyRevision).SingleAsync();
        var writer = owner.Services.GetRequiredService<IEmailDeliverySettingsWriter>();
        var mutationLock = owner.Services.GetRequiredService<ISettingMutationLock>();
        var unitOfWork = owner.Services.GetRequiredService<IUnitOfWork>();
        string value = $"\"smtp-{Guid.CreateVersion7():N}.example.test\"";

        await Assert.That(async () => await mutationLock.ExecuteOrderedGroupsAsync(
            [EmailDeliverySettingKeys.All], token => unitOfWork.ExecuteSerializableAsync<bool>(async inner =>
            {
                var result = await writer.ApplyAsync(
                    [new(null, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetValue, value)],
                    actorUserId: null, inner);
                await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
                await Assert.That(await context.EmailDispatchProcessorStates.AsNoTracking()
                    .Select(row => row.DeliveryPolicyRevision).SingleAsync(inner)).IsEqualTo(revision + 1);
                throw new RollbackProbeException();
            }, token))).Throws<RollbackProbeException>();

        context.ChangeTracker.Clear();
        await Assert.That((await context.SystemSettings.AsNoTracking().SingleAsync(row => row.Id == before.Id)).Value)
            .IsEqualTo(before.Value);
        await Assert.That(await context.EmailDispatchProcessorStates.AsNoTracking()
            .Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(revision);
    }

    [Test]
    public async Task NativeInstanceBatchKeepsEmailAndOrdinarySettingsOnOneOwner()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        var original = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        fixture.EmailConstructions.Clear();
        fixture.Constructions.Clear();
        var handler = caller.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto>>();
        await Assert.That(fixture.EmailConstructions).IsEmpty();

        var result = await handler.ExecuteAsync(new()
        {
            Category = "TenantDelegation",
            Scope = SettingScope.Instance,
            Mode = BatchUpdateMode.Strict,
            Values = new Dictionary<string, string>
            {
                [GovernanceSettingKeys.TenantDelegation.LockSmtp] = "false",
                [GovernanceSettingKeys.TenantDelegation.LockStorage] = "false"
            }
        }, default);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Results.All(entry => entry.Applied)).IsTrue();
        var email = fixture.EmailConstructions.Single();
        await Assert.That(email.Context).IsSameReferenceAs(fixture.Constructions.Single().Context);
        await Assert.That(ReferenceEquals(email.Context, original)).IsFalse();
        await Assert.That(email.TenantId).IsNull();
        await Assert.That(email.Purpose).IsEqualTo((PolicyMutationPurpose?)PolicyMutationPurpose.EmailDelivery);
        await Assert.That(await original.SystemSettings.AsNoTracking()
            .Where(row => row.SettingKey == GovernanceSettingKeys.TenantDelegation.LockStorage)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("false");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeUpdateLockUnlockAndResetPreserveInstanceOrAuthenticatedTenant(bool tenant)
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        await EnableTenantDelegationAsync(caller.ServiceProvider);
        fixture.EmailConstructions.Clear();
        const string key = GovernanceSettingKeys.Email.FromName;
        SettingScope scope = tenant ? SettingScope.Tenant : SettingScope.Instance;
        var update = await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { Key = key, Value = "Scoped sender", Scope = scope }, default);
        await Assert.That(update.IsSuccess).IsTrue();
        var locked = await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { Key = key, Scope = scope }, default);
        await Assert.That(locked.IsSuccess).IsTrue();
        var unlocked = await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { Key = key, Scope = scope }, default);
        await Assert.That(unlocked.IsSuccess).IsTrue();
        var reset = await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { Key = key, Scope = scope }, default);
        // Instance reset remains forbidden by the existing handler, before invoking a writer.
        await Assert.That(reset.IsSuccess).IsEqualTo(tenant);
        await Assert.That(fixture.EmailConstructions.Count).IsEqualTo(tenant ? 4 : 3);
        Guid? expectedTenant = tenant ? fixture.CallerTenantId : null;
        await Assert.That(fixture.EmailConstructions.All(entry => entry.TenantId == expectedTenant
            && entry.Purpose == PolicyMutationPurpose.EmailDelivery)).IsTrue();
    }

    [Test]
    public async Task InstanceTargetCommandsKeepThePersistedTargetSeparateFromCaller()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        await EnableTenantDelegationAsync(caller.ServiceProvider);
        fixture.EmailConstructions.Clear();
        const string key = GovernanceSettingKeys.Email.FromName;
        var set = await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<SetInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new(fixture.TargetTenantId, key, "Target sender"), default);
        await Assert.That(set.IsSuccess).IsTrue();
        var locked = await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<LockInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new(fixture.TargetTenantId, key), default);
        await Assert.That(locked.IsSuccess).IsTrue();
        var unlocked = await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<UnlockInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new(fixture.TargetTenantId, key), default);
        await Assert.That(unlocked.IsSuccess).IsTrue();
        await Assert.That(fixture.EmailConstructions.Count).IsEqualTo(3);
        await Assert.That(fixture.EmailConstructions.All(entry => entry.TenantId == fixture.TargetTenantId)).IsTrue();
        await Assert.That(caller.ServiceProvider.GetRequiredService<ITenantContextAccessor>().TenantId)
            .IsEqualTo((Guid?)fixture.CallerTenantId);
    }

    [Test]
    public async Task DirectSmtpAndGovernanceServicesUseDedicatedEmailOwners()
    {
        await using var caller = fixture.OpenCaller();
        var original = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        fixture.EmailConstructions.Clear();
        await caller.ServiceProvider.GetRequiredService<IInstanceSmtpSettingService>()
            .ApplySettingsAsync(settings: null, enableDelivery: true);
        await caller.ServiceProvider.GetRequiredService<IInstanceGovernanceSettingService>()
            .ApplyTenantDelegationSettingsAsync(new() { LockTenantSmtp = false }, actorUserId: null);

        await Assert.That(fixture.EmailConstructions.Count).IsEqualTo(2);
        await Assert.That(fixture.EmailConstructions.All(entry =>
            entry.TenantId is null && !ReferenceEquals(entry.Context, original))).IsTrue();
    }

    [Test]
    public async Task NativeDelegationPatchSelectsEmailBeforeItsOuterTransaction()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        fixture.EmailConstructions.Clear();
        var result = await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<UpdateTenantDelegationSettingsCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new()
            {
                UserId = fixture.AdministratorId,
                Patch = new()
                {
                    LockTenantSmtp = OptionalUpdate<bool>.Set(false),
                    LockTenantStorage = OptionalUpdate<bool>.Set(false)
                }
            }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(fixture.EmailConstructions.Single().TenantId).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeDisableUsesThePreviewedTenantOrInstancePurpose(bool tenant)
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        await EnableTenantDelegationAsync(caller.ServiceProvider);
        Guid? target = tenant ? fixture.CallerTenantId : null;
        var preview = await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<PreviewEmailDeliveryDisableCommand, BaseCommandResponse<EmailDeliveryDisablePreviewDto>>>()
            .ExecuteAsync(new(target), default);
        await Assert.That(preview.IsSuccess).IsTrue();
        await Assert.That(preview.Id!.ConfirmationToken).IsNotNull();
        fixture.EmailConstructions.Clear();
        try
        {
            var result = await caller.ServiceProvider
                .GetRequiredService<ICommandHandler<DisableEmailDeliveryCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new(target, preview.Id.ExpectedRevision,
                    DisableEmailDeliveryCommand.RequiredAcknowledgement, preview.Id.ConfirmationToken), default);
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(fixture.EmailConstructions.Single().TenantId).IsEqualTo(target);
        }
        finally
        {
            var restored = await caller.ServiceProvider.GetRequiredService<IEmailDeliverySettingsWriter>().ApplyAsync(
                [new(target, GovernanceSettingKeys.Email.DeliveryEnabled, EmailDeliverySettingMutationKind.SetValue, "true")],
                actorUserId: null);
            await Assert.That(restored.IsAccepted()).IsTrue();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PersistedPlanContentSelectsEmailOnlyWhenTheVersionContainsEmail(bool email)
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        await EnableTenantDelegationAsync(caller.ServiceProvider);
        var context = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var now = DateTime.UtcNow;
        var plan = new TenantPlan
        {
            Id = Guid.CreateVersion7(),
            Key = $"purpose-{Guid.CreateVersion7():N}",
            DisplayName = "Purpose composition",
            CreatedAt = now
        };
        var version = new TenantPlanVersion
        {
            Id = Guid.CreateVersion7(),
            TenantPlan = plan,
            TenantPlanId = plan.Id,
            VersionNumber = 1,
            TenantPlanStatusId = (int)TenantPlanStatusEnum.Published,
            CurrencyCode = "EUR",
            BillingPeriod = "monthly",
            IsActiveForProvisioning = true,
            CreatedAt = now,
            Settings =
            [
                new() { Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.AiAssistant.Enabled,
                    JsonValue = "true", CreatedAt = now }
            ]
        };
        if (email)
            version.Settings.Add(new()
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.Email.FromName,
                JsonValue = "\"Plan purpose sender\"",
                CreatedAt = now
            });
        var assignment = new TenantPlanAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.CallerTenantId,
            Tenant = null!,
            TenantPlan = plan,
            TenantPlanId = plan.Id,
            TenantPlanVersion = version,
            TenantPlanVersionId = version.Id,
            TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
            AssignedByUserId = fixture.AdministratorId,
            AssignedAt = now,
            CreatedAt = now
        };
        context.TenantPlanAssignments.Add(assignment);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        fixture.EmailConstructions.Clear();
        fixture.Constructions.Clear();
        try
        {
            var result = await caller.ServiceProvider
                .GetRequiredService<ICommandHandler<ApplyInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new(fixture.CallerTenantId, assignment.Id, fixture.AdministratorId), default);
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(fixture.EmailConstructions.Count).IsEqualTo(email ? 1 : 0);
            await Assert.That(fixture.Constructions.Single().Purpose).IsEqualTo(
                (PolicyMutationPurpose?)(email ? PolicyMutationPurpose.EmailDelivery : PolicyMutationPurpose.Standard));
            if (email)
                await Assert.That(fixture.EmailConstructions.Single().TenantId).IsEqualTo((Guid?)fixture.CallerTenantId);
            var applied = await context.TenantPlanAssignments.AsNoTracking().SingleAsync(row => row.Id == assignment.Id);
            await Assert.That(applied.UpdatedBy).IsEqualTo((Guid?)fixture.AdministratorId);
        }
        finally
        {
            await context.TenantPlanAssignments.Where(row => row.Id == assignment.Id).ExecuteDeleteAsync();
            await context.TenantPlanVersionSettings.Where(row => row.TenantPlanVersionId == version.Id).ExecuteDeleteAsync();
            await context.TenantPlanVersions.Where(row => row.Id == version.Id).ExecuteDeleteAsync();
            await context.TenantPlans.Where(row => row.Id == plan.Id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task DisableCommandRejectsAnotherTenantBeforeConstructingTheWriter()
    {
        await using var caller = fixture.OpenCaller();
        fixture.EmailConstructions.Clear();
        await Assert.That(async () => await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<DisableEmailDeliveryCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new(fixture.TargetTenantId, 0, null, null), default))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.EmailConstructions).IsEmpty();
    }

    private static async Task EnableTenantDelegationAsync(IServiceProvider services)
    {
        var result = await services.GetRequiredService<IEmailDeliverySettingsWriter>().ApplyAsync(
            [new(null, GovernanceSettingKeys.TenantDelegation.LockSmtp, EmailDeliverySettingMutationKind.SetValue, "false")],
            actorUserId: null);
        await Assert.That(result.IsAccepted()).IsTrue();
    }

    private static IPolicyMutationExecutionScopeFactory Factory(IServiceProvider services) =>
        services.GetRequiredService<IPolicyMutationExecutionScopeFactory>();

    private sealed class RollbackProbeException : Exception;
}
