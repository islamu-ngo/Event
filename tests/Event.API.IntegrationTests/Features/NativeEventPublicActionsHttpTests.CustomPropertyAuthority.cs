using Explore.Application.Exceptions;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;
using Explore.Application.Features.EventCustomProperties.Handlers.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task DirectDefinitionDeletionRejectsNonAdministrator(int kind)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, kind);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            DeleteOwnedDefinition(scope.ServiceProvider, kind, id));

        await Assert.That(await DefinitionStillActive(db, kind, id)).IsTrue();
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task CurrentAdministratorCanDeleteExactDefinition(int kind, bool instance)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, kind);
        if (instance)
            await GrantTemplateAdministrator(db, data.OutsiderId);
        else
        {
            var member = await db.TenantUsers.SingleAsync(row => row.UserId == data.OutsiderId);
            db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await Assert.That(await DeleteOwnedDefinition(scope.ServiceProvider, kind, id)).IsTrue();
        await Assert.That(await DefinitionStillActive(db, kind, id)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CommittedAdministratorRevocationPreventsDefinitionDeletion(int kind)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, kind);
        Guid grantId = await GrantTemplateAdministrator(db, data.OutsiderId);
        boundary.Arm(db, async token =>
        {
            await using var revocation = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            revocation.PlatformUserRoles.Remove(
                await revocation.PlatformUserRoles.SingleAsync(row => row.Id == grantId, token));
            await revocation.SaveChangesAsync(token);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            DeleteOwnedDefinition(scope.ServiceProvider, kind, id, deadline.Token));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(await DefinitionStillActive(db, kind, id)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task TrackedTenantSubstitutionCannotChangeDefinitionDeletionTarget(int kind)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, kind);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        switch (kind)
        {
            case 0: db.CustomPropertyDefinitions.Local.Single(row => row.Id == id).TenantId = data.ForeignTenantId; break;
            case 1: db.EventCustomPropertyDefinitions.Local.Single(row => row.Id == id).TenantId = data.ForeignTenantId; break;
            case 2: db.EventSessionCustomPropertyDefinitions.Local.Single(row => row.Id == id).TenantId = data.ForeignTenantId; break;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeleteOwnedDefinition(scope.ServiceProvider, kind, id));

        await Assert.That(await DefinitionStillActive(db, kind, id)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task DirectDefinitionUpdateRejectsNonAdministrator(int kind)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, kind);
        var before = await DefinitionState(db, kind, id);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            UpdateOwnedDefinition(scope.ServiceProvider, kind, id, before.Stamp));

        await Assert.That(await DefinitionState(db, kind, id)).IsEqualTo(before);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CurrentAdministratorUpdatesPersistedDefinitionDespitePostedTenantHint(int kind)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, kind);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var before = await DefinitionState(db, kind, id);

        var response = await UpdateOwnedDefinition(scope.ServiceProvider, kind, id, before.Stamp);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That((await DefinitionState(db, kind, id)).Name).IsEqualTo("Updated authority");
        await Assert.That(await DefinitionStillActive(db, kind, id)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CommittedAdministratorRevocationPreventsDefinitionUpdate(int kind)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, kind);
        Guid grantId = await GrantTemplateAdministrator(db, data.OutsiderId);
        var before = await DefinitionState(db, kind, id);
        boundary.Arm(db, async token =>
        {
            await using var revocation = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            revocation.PlatformUserRoles.Remove(
                await revocation.PlatformUserRoles.SingleAsync(row => row.Id == grantId, token));
            await revocation.SaveChangesAsync(token);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            UpdateOwnedDefinition(scope.ServiceProvider, kind, id, before.Stamp, deadline.Token));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(await DefinitionState(db, kind, id)).IsEqualTo(before);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task DirectDefinitionCreationRejectsNonAdministrator(int kind)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid parentId = await PrepareDefinitionParent(db, data.PublicId, kind);
        string key = $"create_{Guid.CreateVersion7():N}";

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            CreateOwnedDefinition(scope.ServiceProvider, kind, parentId, key));

        await Assert.That(await CreatedDefinitionExists(db, kind, key)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CurrentAdministratorCreatesDefinitionInExecutionTenant(int kind)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid parentId = await PrepareDefinitionParent(db, data.PublicId, kind);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        string key = $"create_{Guid.CreateVersion7():N}";

        var response = await CreateOwnedDefinition(scope.ServiceProvider, kind, parentId, key);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(await DefinitionStillActive(db, kind, response.Id)).IsTrue();
        await Assert.That(await CreatedDefinitionExists(db, kind, key)).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CommittedAdministratorRevocationPreventsDefinitionCreation(int kind)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid parentId = await PrepareDefinitionParent(db, data.PublicId, kind);
        Guid grantId = await GrantTemplateAdministrator(db, data.OutsiderId);
        string key = $"create_{Guid.CreateVersion7():N}";
        boundary.Arm(db, async token =>
        {
            await using var revocation = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            revocation.PlatformUserRoles.Remove(
                await revocation.PlatformUserRoles.SingleAsync(row => row.Id == grantId, token));
            await revocation.SaveChangesAsync(token);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateOwnedDefinition(scope.ServiceProvider, kind, parentId, key, deadline.Token));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(await CreatedDefinitionExists(db, kind, key)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task DirectDefinitionPurgeRejectsNonAdministrator(int kind)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, kind);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            PurgeOwnedDefinition(scope.ServiceProvider, kind, id));

        await Assert.That(await DefinitionStillActive(db, kind, id)).IsTrue();
        await Assert.That(await db.AuditLogs.AnyAsync(row => row.EntityId == id.ToString())).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CurrentAdministratorPurgesExactDefinitionWithAtomicAudit(int kind)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, kind);
        await GrantTemplateAdministrator(db, data.OutsiderId);

        var response = await PurgeOwnedDefinition(scope.ServiceProvider, kind, id);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsNotNull();
        var result = response.Id!;
        await Assert.That(result.Purged).IsTrue();
        await Assert.That(await DefinitionStillActive(db, kind, id)).IsFalse();
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(row => row.Id == result.AuditLogId);
        await Assert.That(audit.ActorId).IsEqualTo(data.OutsiderId);
        await Assert.That(audit.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(audit.EntityId).IsEqualTo(id.ToString());
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task CommittedAdministratorRevocationPreventsDefinitionPurgeAndAudit(int kind)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, kind);
        Guid grantId = await GrantTemplateAdministrator(db, data.OutsiderId);
        boundary.Arm(db, async token =>
        {
            await using var revocation = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            revocation.PlatformUserRoles.Remove(
                await revocation.PlatformUserRoles.SingleAsync(row => row.Id == grantId, token));
            await revocation.SaveChangesAsync(token);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PurgeOwnedDefinition(scope.ServiceProvider, kind, id, deadline.Token));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(await DefinitionStillActive(db, kind, id)).IsTrue();
        await Assert.That(await db.AuditLogs.AnyAsync(row => row.EntityId == id.ToString())).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DirectValueMutationRejectsNonAdministrator(bool session, bool multi)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var target = await PrepareValueDefinition(db, data.PublicId, session, multi);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            SetOwnedDefinitionValue(scope.ServiceProvider, session, multi, target.Id, target.ParentId));

        await Assert.That(await DefinitionValueCount(db, session, target.Id)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CurrentAdministratorWritesDefinitionValues(bool session, bool multi)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var target = await PrepareValueDefinition(db, data.PublicId, session, multi);
        await GrantTemplateAdministrator(db, data.OutsiderId);

        var response = await SetOwnedDefinitionValue(
            scope.ServiceProvider, session, multi, target.Id, target.ParentId);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(await DefinitionValueCount(db, session, target.Id)).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CommittedAdministratorRevocationPreventsValueMutation(bool session, bool multi)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync(boundary);
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var target = await PrepareValueDefinition(db, data.PublicId, session, multi);
        Guid grantId = await GrantTemplateAdministrator(db, data.OutsiderId);
        boundary.Arm(db, async token =>
        {
            await using var revocation = await scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
            revocation.PlatformUserRoles.Remove(
                await revocation.PlatformUserRoles.SingleAsync(row => row.Id == grantId, token));
            await revocation.SaveChangesAsync(token);
        });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => SetOwnedDefinitionValue(
            scope.ServiceProvider, session, multi, target.Id, target.ParentId, deadline.Token));

        await Assert.That(boundary.Applied).IsTrue();
        await Assert.That(await DefinitionValueCount(db, session, target.Id)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TrackedValueParentCannotReplacePersistedMutationTarget(bool session)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var target = await PrepareValueDefinition(db, data.PublicId, session, false);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var initial = await SetOwnedDefinitionValue(scope.ServiceProvider, session, false, target.Id, target.ParentId);
        await Assert.That(initial.IsSuccess).IsTrue();
        if (session)
        {
            Guid otherSessionId = await PrepareDefinitionParent(db, data.OtherId, 2);
            var value = await db.EventSessionCustomPropertyValues.SingleAsync(row => row.Id == initial.Id);
            value.EventSessionId = otherSessionId;
        }
        else
        {
            var value = await db.EventCustomPropertyValues.SingleAsync(row => row.Id == initial.Id);
            value.EventId = data.OtherId;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SetOwnedDefinitionValue(scope.ServiceProvider, session, false, target.Id, target.ParentId));

        Guid storedParent = session
            ? await db.EventSessionCustomPropertyValues.AsNoTracking().Where(row => row.Id == initial.Id)
                .Select(row => row.EventSessionId).SingleAsync()
            : await db.EventCustomPropertyValues.AsNoTracking().Where(row => row.Id == initial.Id)
                .Select(row => row.EventId).SingleAsync();
        await Assert.That(storedParent).IsEqualTo(target.ParentId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TrackedOptionActivationCannotBecomeAnUnsubmittedDefinitionPatch(bool session)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, session ? 2 : 1);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        Guid optionId = Guid.CreateVersion7();
        if (session)
        {
            (await db.EventSessionCustomPropertyDefinitions.SingleAsync(row => row.Id == id)).PropertyType = PropertyType.Option;
            db.EventSessionCustomPropertyOptions.Add(new EventSessionCustomPropertyOption
            {
                Id = optionId,
                EventSessionCustomPropertyDefinitionId = id,
                Namespace = "tenant",
                Key = "inactive",
                DisplayName = "Inactive",
                Value = "inactive",
                IsActive = false
            });
        }
        else
        {
            (await db.EventCustomPropertyDefinitions.SingleAsync(row => row.Id == id)).PropertyType = PropertyType.Option;
            db.EventCustomPropertyOptions.Add(new EventCustomPropertyOption
            {
                Id = optionId,
                EventCustomPropertyDefinitionId = id,
                Namespace = "tenant",
                Key = "inactive",
                DisplayName = "Inactive",
                Value = "inactive",
                IsActive = false
            });
        }
        await db.SaveChangesAsync();
        var before = await DefinitionState(db, session ? 2 : 1, id);
        if (session)
            db.EventSessionCustomPropertyOptions.Local.Single(row => row.Id == optionId).IsActive = true;
        else
            db.EventCustomPropertyOptions.Local.Single(row => row.Id == optionId).IsActive = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateOwnedDefinition(scope.ServiceProvider, session ? 2 : 1, id, before.Stamp));

        bool storedActive = session
            ? await db.EventSessionCustomPropertyOptions.AsNoTracking().Where(row => row.Id == optionId)
                .Select(row => row.IsActive).SingleAsync()
            : await db.EventCustomPropertyOptions.AsNoTracking().Where(row => row.Id == optionId)
                .Select(row => row.IsActive).SingleAsync();
        await Assert.That(storedActive).IsFalse();
        await Assert.That(await DefinitionState(db, session ? 2 : 1, id)).IsEqualTo(before);
    }

    private static async Task<(Guid Id, Guid ParentId)> PrepareValueDefinition(
        ExploreDbContext db, Guid eventId, bool session, bool multi)
    {
        Guid id = await SeedOwnedDefinition(db, eventId, session ? 2 : 1);
        Guid parentId;
        if (session)
        {
            var definition = await db.EventSessionCustomPropertyDefinitions.SingleAsync(row => row.Id == id);
            definition.IsMulti = multi;
            parentId = definition.EventSessionId;
        }
        else
        {
            var definition = await db.EventCustomPropertyDefinitions.SingleAsync(row => row.Id == id);
            definition.IsMulti = multi;
            parentId = definition.EventId;
        }
        await db.SaveChangesAsync();
        return (id, parentId);
    }

    private static Task<int> DefinitionValueCount(ExploreDbContext db, bool session, Guid id) => session
        ? db.EventSessionCustomPropertyValues.CountAsync(row => row.EventSessionCustomPropertyDefinitionId == id)
        : db.EventCustomPropertyValues.CountAsync(row => row.EventCustomPropertyDefinitionId == id);

    private static async Task<BaseCommandResponse<Guid>> SetOwnedDefinitionValue(
        IServiceProvider services, bool session, bool multi, Guid id, Guid parentId, CancellationToken token = default)
    {
        await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(token);
        if (session)
            return await (multi
                ? ActivatorUtilities.CreateInstance<SetEventSessionCustomPropertyMultiValuesCommandHandler>(services).ExecuteAsync(new()
                {
                    DefinitionId = id,
                    EventSessionId = parentId,
                    Values = [new()
                    {
                        EventSessionCustomPropertyDefinitionId = id, EventSessionId = parentId, TextValue = "authority"
                    }]
                }, token)
                : ActivatorUtilities.CreateInstance<SetEventSessionCustomPropertyValueCommandHandler>(services).ExecuteAsync(new()
                {
                    ValueDto = new()
                    {
                        EventSessionCustomPropertyDefinitionId = id,
                        EventSessionId = parentId,
                        TextValue = "authority"
                    }
                }, token));
        return await (multi
            ? ActivatorUtilities.CreateInstance<SetEventCustomPropertyMultiValuesCommandHandler>(services).ExecuteAsync(new()
            {
                DefinitionId = id,
                EventId = parentId,
                Values = [new()
                {
                    EventCustomPropertyDefinitionId = id, EventId = parentId, TextValue = "authority"
                }]
            }, token)
            : ActivatorUtilities.CreateInstance<SetEventCustomPropertyValueCommandHandler>(services).ExecuteAsync(new()
            {
                ValueDto = new()
                {
                    EventCustomPropertyDefinitionId = id,
                    EventId = parentId,
                    TextValue = "authority"
                }
            }, token));
    }

    private static async Task<BaseCommandResponse<CustomPropertyPurgeResultDto>> PurgeOwnedDefinition(
        IServiceProvider services, int kind, Guid id, CancellationToken token = default)
    {
        await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(token);
        return await (kind switch
        {
            0 => ActivatorUtilities.CreateInstance<PurgeCustomPropertyDefinitionCommandHandler>(services)
                .ExecuteAsync(new() { Id = id, Reason = "Unused definition" }, token),
            1 => ActivatorUtilities.CreateInstance<PurgeEventCustomPropertyDefinitionCommandHandler>(services)
                .ExecuteAsync(new() { Id = id, Reason = "Unused definition" }, token),
            2 => ActivatorUtilities.CreateInstance<PurgeEventSessionCustomPropertyDefinitionCommandHandler>(services)
                .ExecuteAsync(new() { Id = id, Reason = "Unused definition" }, token),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
    }

    private static async Task<BaseCommandResponse<Guid>> CreateOwnedDefinition(
        IServiceProvider services, int kind, Guid parentId, string key, CancellationToken token = default)
    {
        await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(token);
        return await (kind switch
        {
            0 => ActivatorUtilities.CreateInstance<CreateCustomPropertyDefinitionCommandHandler>(services).ExecuteAsync(new()
            {
                DefinitionDto = new()
                {
                    EntityTypeName = EntityTypeName.Organization,
                    Namespace = "tenant.community",
                    Key = key,
                    DisplayName = "Created authority",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.Public
                }
            }, token),
            1 => ActivatorUtilities.CreateInstance<CreateEventCustomPropertyDefinitionCommandHandler>(services).ExecuteAsync(new()
            {
                DefinitionDto = new()
                {
                    EventId = parentId,
                    Namespace = "tenant.community",
                    Key = key,
                    DisplayName = "Created authority",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.Public
                }
            }, token),
            2 => ActivatorUtilities.CreateInstance<CreateEventSessionCustomPropertyDefinitionCommandHandler>(services).ExecuteAsync(new()
            {
                DefinitionDto = new()
                {
                    EventSessionId = parentId,
                    Namespace = "tenant.community",
                    Key = key,
                    DisplayName = "Created authority",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.Public
                }
            }, token),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
    }

    private static async Task<Guid> PrepareDefinitionParent(ExploreDbContext db, Guid eventId, int kind)
    {
        if (kind != 2)
            return eventId;
        Guid sessionId = Guid.CreateVersion7();
        db.EventSessions.Add(new EventSession
        {
            Id = sessionId,
            EventId = eventId,
            Event = null!,
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            Title = "Definition creation"
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private static Task<bool> CreatedDefinitionExists(ExploreDbContext db, int kind, string key) => kind switch
    {
        0 => db.CustomPropertyDefinitions.AsNoTracking().AnyAsync(row => row.Key == key),
        1 => db.EventCustomPropertyDefinitions.AsNoTracking().AnyAsync(row => row.Key == key),
        2 => db.EventSessionCustomPropertyDefinitions.AsNoTracking().AnyAsync(row => row.Key == key),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static async Task<BaseCommandResponse<Guid>> UpdateOwnedDefinition(
        IServiceProvider services, int kind, Guid id, Guid stamp, CancellationToken token = default)
    {
        await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(token);
        return await (kind switch
        {
            0 => ActivatorUtilities.CreateInstance<UpdateCustomPropertyDefinitionCommandHandler>(services).ExecuteAsync(new()
            {
                DefinitionId = id,
                ExpectedConcurrencyStamp = stamp,
                TenantId = Guid.CreateVersion7(),
                DefinitionDto = new() { Metadata = new() { DisplayName = "Updated authority" } }
            }, token),
            1 => ActivatorUtilities.CreateInstance<UpdateEventCustomPropertyDefinitionCommandHandler>(services).ExecuteAsync(new()
            {
                DefinitionId = id,
                ExpectedConcurrencyStamp = stamp,
                TenantId = Guid.CreateVersion7(),
                DefinitionDto = new() { Metadata = new() { DisplayName = "Updated authority" } }
            }, token),
            2 => ActivatorUtilities.CreateInstance<UpdateEventSessionCustomPropertyDefinitionCommandHandler>(services).ExecuteAsync(new()
            {
                DefinitionId = id,
                ExpectedConcurrencyStamp = stamp,
                TenantId = Guid.CreateVersion7(),
                DefinitionDto = new() { Metadata = new() { DisplayName = "Updated authority" } }
            }, token),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
    }

    private static async Task<(string Name, Guid Stamp)> DefinitionState(ExploreDbContext db, int kind, Guid id)
    {
        if (kind == 0)
        {
            var definition = await db.CustomPropertyDefinitions.AsNoTracking().SingleAsync(row => row.Id == id);
            return (definition.DisplayName, definition.ConcurrencyStamp);
        }
        if (kind == 1)
        {
            var definition = await db.EventCustomPropertyDefinitions.AsNoTracking().SingleAsync(row => row.Id == id);
            return (definition.DisplayName, definition.ConcurrencyStamp);
        }
        var sessionDefinition = await db.EventSessionCustomPropertyDefinitions.AsNoTracking().SingleAsync(row => row.Id == id);
        return (sessionDefinition.DisplayName, sessionDefinition.ConcurrencyStamp);
    }

    private static async Task<bool> DeleteOwnedDefinition(
        IServiceProvider services, int kind, Guid id, CancellationToken token = default)
    {
        await services.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(token);
        return await (kind switch
        {
            0 => ActivatorUtilities.CreateInstance<DeleteCustomPropertyDefinitionCommandHandler>(services)
                .ExecuteAsync(new() { Id = id }, token),
            1 => ActivatorUtilities.CreateInstance<DeleteEventCustomPropertyDefinitionCommandHandler>(services)
                .ExecuteAsync(new() { Id = id }, token),
            2 => ActivatorUtilities.CreateInstance<DeleteEventSessionCustomPropertyDefinitionCommandHandler>(services)
                .ExecuteAsync(new() { Id = id }, token),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
    }

    private static async Task<Guid> SeedOwnedDefinition(ExploreDbContext db, Guid eventId, int kind)
    {
        Guid id = Guid.CreateVersion7();
        Guid tenantId = PlatformDefaults.DefaultTenantId;
        switch (kind)
        {
            case 0:
                db.CustomPropertyDefinitions.Add(new CustomPropertyDefinition
                {
                    Id = id,
                    TenantId = tenantId,
                    EntityTypeName = EntityTypeName.Organization,
                    Namespace = "tenant",
                    Key = "authority",
                    DisplayName = "Authority",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.Public,
                    IsActive = true
                });
                break;
            case 1:
                db.EventCustomPropertyDefinitions.Add(new EventCustomPropertyDefinition
                {
                    Id = id,
                    TenantId = tenantId,
                    EventId = eventId,
                    Namespace = "tenant",
                    Key = "authority",
                    DisplayName = "Authority",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.Public,
                    IsActive = true
                });
                break;
            case 2:
                Guid sessionId = Guid.CreateVersion7();
                db.EventSessions.Add(new EventSession
                {
                    Id = sessionId,
                    EventId = eventId,
                    Event = null!,
                    TenantId = tenantId,
                    Tenant = null!,
                    Title = "Definition authority"
                });
                db.EventSessionCustomPropertyDefinitions.Add(new EventSessionCustomPropertyDefinition
                {
                    Id = id,
                    TenantId = tenantId,
                    EventSessionId = sessionId,
                    Namespace = "tenant",
                    Key = "authority",
                    DisplayName = "Authority",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.Public,
                    IsActive = true
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        await db.SaveChangesAsync();
        return id;
    }

    private static Task<bool> DefinitionStillActive(ExploreDbContext db, int kind, Guid id) => kind switch
    {
        0 => db.CustomPropertyDefinitions.AsNoTracking()
            .AnyAsync(row => row.Id == id && row.TenantId == PlatformDefaults.DefaultTenantId && !row.IsDeleted),
        1 => db.EventCustomPropertyDefinitions.AsNoTracking()
            .AnyAsync(row => row.Id == id && row.TenantId == PlatformDefaults.DefaultTenantId && !row.IsDeleted),
        2 => db.EventSessionCustomPropertyDefinitions.AsNoTracking()
            .AnyAsync(row => row.Id == id && row.TenantId == PlatformDefaults.DefaultTenantId && !row.IsDeleted),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
