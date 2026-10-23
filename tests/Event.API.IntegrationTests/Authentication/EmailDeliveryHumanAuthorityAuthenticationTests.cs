using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class EmailDeliveryHumanAuthorityAuthenticationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConsentCannotAuthorizeDirectWriterAfterPersistedRevocation(bool tenantTarget)
    {
        var notifications = new Notifications();
        await using var fixture = await CreateFixtureAsync(!tenantTarget, tenantTarget, notifications);
        var command = Command(await PreviewAsync(fixture, tenantTarget));
        await RevokeAsync(fixture, tenantTarget);
        string before = await StateAsync(fixture);
        await fixture.ExecuteAsync(tenantTarget, async services =>
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                services.GetRequiredService<EmailDeliverySettingsWriter>().DisableAsync(
                    new(command.TenantId, fixture.Subject.PrincipalId, command.ExpectedRevision,
                        command.ConfirmationToken, command.Acknowledgement))));
        await Assert.That(await StateAsync(fixture)).IsEqualTo(before);
        await Assert.That(notifications.Published).IsEmpty();
    }

    [Test]
    public async Task OrdinaryTenantEmailSettingDoesNotBorrowDisableInstanceAdministratorShortcut()
    {
        await using var fixture = await CreateFixtureAsync(instance: true, tenant: false);
        string before = await StateAsync(fixture);
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                services.GetRequiredService<EmailDeliverySettingsWriter>().ApplyAsync(
                    [new(fixture.TenantId, GovernanceSettingKeys.Email.FromName,
                        EmailDeliverySettingMutationKind.SetValue, "\"Not permitted\"")], fixture.Subject.PrincipalId)));
        await Assert.That(await StateAsync(fixture)).IsEqualTo(before);
    }

    [Test]
    public async Task ExplicitInstanceTenantEmailActionRequiresCurrentInstanceAuthority()
    {
        await using var fixture = await CreateFixtureAsync(instance: true, tenant: false);
        var mutations = System.Collections.Immutable.ImmutableArray.Create(
            new EmailDeliverySettingMutation(fixture.TenantId, GovernanceSettingKeys.Email.FromName,
                EmailDeliverySettingMutationKind.SetValue, "\"Instance tenant sender\""));
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
            await Assert.That((await services.GetRequiredService<EmailDeliverySettingsWriter>()
                .ApplyInstanceTenantAsync(mutations, fixture.Subject.PrincipalId)).Status)
                .IsEqualTo(EmailDeliverySettingsWriteStatus.Applied));
        await using (var observer = fixture.CreateIndependentContext())
            await Assert.That((await observer.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking()
                .SingleAsync(row => row.TenantId == fixture.TenantId
                    && row.SettingKey == GovernanceSettingKeys.Email.FromName)).Value)
                .IsEqualTo("\"Instance tenant sender\"");
        Guid auditAdministrator = await SeedAuditAdministratorAsync(fixture);
        await RevokeAsync(fixture, tenant: false);
        string before = await StateAsync(fixture);
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<EmailDeliverySettingsWriter>()
                .ApplyInstanceTenantAsync(mutations, auditAdministrator)));
        await Assert.That(await StateAsync(fixture)).IsEqualTo(before);
    }

    [Test]
    public async Task TenantAdministratorCannotBorrowExplicitInstanceTenantEmailAction()
    {
        await using var fixture = await CreateFixtureAsync(instance: false, tenant: true);
        Guid auditAdministrator = await SeedAuditAdministratorAsync(fixture);
        string before = await StateAsync(fixture);
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<EmailDeliverySettingsWriter>()
                .ApplyInstanceTenantAsync([new(fixture.TenantId, GovernanceSettingKeys.Email.FromName,
                    EmailDeliverySettingMutationKind.SetValue, "\"Not instance owned\"")], auditAdministrator)));
        await Assert.That(await StateAsync(fixture)).IsEqualTo(before);
    }

    [Test]
    public async Task EmailSettingsUseBoundHumanRatherThanSuppliedAuditAdministrator()
    {
        await using var fixture = await CreateFixtureAsync(instance: true, tenant: false);
        Guid auditAdministrator = await SeedAuditAdministratorAsync(fixture);
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
            await Assert.That((await services.GetRequiredService<EmailDeliverySettingsWriter>()
                .ApplyAsync([new(null, GovernanceSettingKeys.Email.FromName,
                    EmailDeliverySettingMutationKind.SetValue, "\"Bound sender\"")], auditAdministrator)).Status)
                .IsEqualTo(EmailDeliverySettingsWriteStatus.Applied));
        await using (var observer = fixture.CreateIndependentContext())
            await Assert.That((await observer.SystemSettings.AsNoTracking()
                .SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.FromName)).UpdatedBy)
                .IsEqualTo(fixture.Subject.PrincipalId);
        await RevokeAsync(fixture, tenant: false);
        string before = await StateAsync(fixture);
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<EmailDeliverySettingsWriter>()
                .ApplyAsync([new(null, GovernanceSettingKeys.Email.FromName,
                    EmailDeliverySettingMutationKind.SetValue, "\"Revoked sender\"")], auditAdministrator)));
        await Assert.That(await StateAsync(fixture)).IsEqualTo(before);
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Arguments(false, false)]
    [Arguments(false, true)]
    public async Task PersistedRevocationDuringOriginalOwnerAcquisitionDeniesEmailAction(
        bool previewAction, bool tenantTarget)
    {
        var notifications = new Notifications();
        await using var fixture = await CreateFixtureAsync(!tenantTarget, tenantTarget, notifications);
        var command = Command(await PreviewAsync(fixture, tenantTarget));
        var gate = new EmailOwnerAcquisitionGate();
        string? failure = null;
        Task action = fixture.ExecuteAsync(tenantTarget, async services =>
        {
            failure = previewAction
                ? (await services.GetRequiredService<ICommandHandler<PreviewEmailDeliveryDisableCommand,
                    BaseCommandResponse<EmailDeliveryDisablePreviewDto>>>()
                    .ExecuteAsync(new(command.TenantId), CancellationToken.None)).FailureCode
                : (await services.GetRequiredService<ICommandHandler<DisableEmailDeliveryCommand,
                    BaseCommandResponse<Guid>>>().ExecuteAsync(command, CancellationToken.None)).FailureCode;
        }, gate);
        try
        {
            Task first = await Task.WhenAny(gate.Reached.Task, action).WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(first).IsSameReferenceAs(gate.Reached.Task);
            await Assert.That(action.IsCompleted).IsFalse();
            await RevokeAsync(fixture, tenantTarget);
            string before = await StateAsync(fixture);
            gate.Release.TrySetResult();
            await action.WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(failure).IsEqualTo(FailureCodes.AdminRequired);
            await Assert.That(await StateAsync(fixture)).IsEqualTo(before);
            await Assert.That(notifications.Published).IsEmpty();
        }
        finally
        {
            gate.Release.TrySetResult();
            await action;
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task BoundEmailOwnerPreservesExistingAdministratorDisjunctionAndUnrelatedControls(
        bool tenantTarget, bool tenantActor)
    {
        var notifications = new Notifications();
        await using var fixture = await CreateFixtureAsync(!tenantActor, tenantActor, notifications);
        var command = Command(await PreviewAsync(fixture, tenantTarget));
        await fixture.ExecuteAsync(tenantTarget, async services =>
            await Assert.That((await services.GetRequiredService<ICommandHandler<DisableEmailDeliveryCommand,
                BaseCommandResponse<Guid>>>().ExecuteAsync(command, CancellationToken.None)).IsSuccess).IsTrue());
        await using var observer = fixture.CreateIndependentContext();
        var targetSetting = tenantTarget
            ? await observer.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking()
                .Where(row => row.TenantId == fixture.TenantId
                    && row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled).Select(row => row.Value).SingleAsync()
            : await observer.SystemSettings.AsNoTracking()
                .Where(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled).Select(row => row.Value).SingleAsync();
        var otherSetting = tenantTarget
            ? await observer.SystemSettings.AsNoTracking()
                .Where(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled).Select(row => row.Value).SingleAsync()
            : await observer.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking()
                .Where(row => row.TenantId == fixture.TenantId
                    && row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled).Select(row => row.Value).SingleAsync();
        await Assert.That(targetSetting).IsEqualTo("false");
        await Assert.That(otherSetting).IsEqualTo("true");
        await Assert.That(notifications.Published.Single().ActorUserId).IsEqualTo(fixture.Subject.PrincipalId);
    }

    private static async Task<SmtpAuthenticatedFixture> CreateFixtureAsync(
        bool instance, bool tenant, Notifications? notifications = null)
    {
        var fixture = await SmtpAuthenticatedFixture.CreateAsync(instance, tenant, services =>
        {
            if (notifications is not null)
                services.AddSingleton<INotificationHandler<SettingChangedNotification>>(notifications);
        });
        try
        {
            await using var context = fixture.CreateIndependentContext();
            var writer = new InternalEmailDeliverySettingsWriter(context,
                new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)), new EfCoreUnitOfWork(context));
            var result = await writer.ApplyInternalAsync([new(fixture.TenantId, GovernanceSettingKeys.Email.DeliveryEnabled,
                EmailDeliverySettingMutationKind.SetValue, "true")], null);
            await Assert.That(result.Status is EmailDeliverySettingsWriteStatus.Applied
                or EmailDeliverySettingsWriteStatus.NoChange).IsTrue();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private static async Task<EmailDeliveryDisablePreviewDto> PreviewAsync(
        SmtpAuthenticatedFixture fixture, bool tenantTarget)
    {
        EmailDeliveryDisablePreviewDto preview = null!;
        await fixture.ExecuteAsync(tenantTarget, async services =>
        {
            var result = await services.GetRequiredService<ICommandHandler<PreviewEmailDeliveryDisableCommand,
                BaseCommandResponse<EmailDeliveryDisablePreviewDto>>>()
                .ExecuteAsync(new(tenantTarget ? fixture.TenantId : null), CancellationToken.None);
            await Assert.That(result.IsSuccess).IsTrue();
            preview = result.Id!;
            await Assert.That(preview.ConfirmationToken).IsNotNull();
        });
        return preview;
    }

    private static DisableEmailDeliveryCommand Command(EmailDeliveryDisablePreviewDto preview) =>
        new(preview.TenantId, preview.ExpectedRevision, DisableEmailDeliveryCommand.RequiredAcknowledgement,
            preview.ConfirmationToken);

    private static async Task RevokeAsync(SmtpAuthenticatedFixture fixture, bool tenant)
    {
        await using var context = fixture.CreateIndependentContext();
        if (tenant)
            await context.TenantUserRoleGrants.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .Where(row => row.TenantId == fixture.TenantId && row.TenantUser.UserId == fixture.Subject.PrincipalId)
                .ExecuteDeleteAsync();
        else
            await context.PlatformUserRoles.Where(row => row.UserId == fixture.Subject.PrincipalId).ExecuteDeleteAsync();
    }

    private static async Task<Guid> SeedAuditAdministratorAsync(SmtpAuthenticatedFixture fixture)
    {
        await using var context = fixture.CreateIndependentContext();
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"email-audit-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Audit",
                LastName = "Administrator"
            },
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            User = user,
            UserId = user.Id,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<string> StateAsync(SmtpAuthenticatedFixture fixture)
    {
        await using var context = fixture.CreateIndependentContext();
        return JsonSerializer.Serialize(new
        {
            System = await context.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey)
                .Select(row => new
                {
                    row.Id,
                    row.SettingKey,
                    row.Value,
                    row.IsLocked,
                    row.CreatedAt,
                    row.CreatedBy,
                    row.UpdatedAt,
                    row.UpdatedBy,
                    row.Description,
                    row.Category
                }).ToArrayAsync(),
            Tenant = await context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking()
                .OrderBy(row => row.TenantId).ThenBy(row => row.SettingKey)
                .Select(row => new
                {
                    row.Id,
                    row.TenantId,
                    row.SettingKey,
                    row.Value,
                    row.IsLocked,
                    row.CreatedAt,
                    row.CreatedBy,
                    row.UpdatedAt,
                    row.UpdatedBy
                }).ToArrayAsync(),
            Processor = await context.EmailDispatchProcessorStates.AsNoTracking().OrderBy(row => row.ProcessorCode).ToArrayAsync(),
            Control = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking()
                .OrderBy(row => row.TenantId).ToArrayAsync()
        });
    }

    private sealed class Notifications : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Published { get; } = [];
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class EmailOwnerAcquisitionGate : DbTransactionInterceptor
    {
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (eventData.IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("Email authority requires its original Serializable owner.");
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
