using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Exceptions;
using Explore.Application.Features.Actors.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed class ModerationCommandAuthorityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeCommandSuspendsWithOneAuditAcrossRepeatedSuspension(bool identityOnly)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        DateTime now = await context.Actors.Where(row => row.Id == fixture.ActorId)
            .Select(row => row.CreatedAt).SingleAsync();
        int adminRole = await context.Roles.Where(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin")
            .Select(row => row.Id).SingleAsync();
        context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = adminRole,
            Role = null!,
            GrantedAt = now
        });
        Guid identityId = Guid.CreateVersion7();
        context.AtprotoIdentities.Add(new AtprotoIdentity(AtprotoDid.Parse("did:plc:handler-authority"))
        {
            Id = identityId,
            ActorId = fixture.ActorId,
            Actor = null!,
            PdsHost = "https://pds.example.test",
            IsActive = true,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var moderation = new GlobalModerationRequest
        {
            Action = GlobalModerationAction.Suspend,
            ReasonCode = "policy_violation"
        };
        BaseCommandResponse<Guid> first;
        BaseCommandResponse<Guid> repeated;
        if (identityOnly)
        {
            var command = new ModerateAtprotoIdentityCommand { AtprotoIdentityId = identityId, Moderation = moderation };
            first = await fixture.ExecuteCommandAsync<ModerateAtprotoIdentityCommand, BaseCommandResponse<Guid>>(command);
            repeated = await fixture.ExecuteCommandAsync<ModerateAtprotoIdentityCommand, BaseCommandResponse<Guid>>(command);
        }
        else
        {
            var command = new ModerateActorCommand { ActorId = fixture.ActorId, Moderation = moderation };
            first = await fixture.ExecuteCommandAsync<ModerateActorCommand, BaseCommandResponse<Guid>>(command);
            repeated = await fixture.ExecuteCommandAsync<ModerateActorCommand, BaseCommandResponse<Guid>>(command);
        }
        await Assert.That(first.Id).IsEqualTo(identityOnly ? identityId : fixture.ActorId);
        await Assert.That(repeated.Id).IsEqualTo(first.Id);
        context.ChangeTracker.Clear();
        bool suspended = identityOnly
            ? await context.AtprotoIdentities.Where(row => row.Id == identityId)
                .Select(row => row.IsSuspended).SingleAsync()
            : await context.Actors.IgnoreAutoIncludes().Where(row => row.Id == fixture.ActorId)
                .Select(row => row.IsSuspended).SingleAsync();
        int audits = identityOnly
            ? await context.Set<AtprotoIdentityModerationRecord>().CountAsync(row => row.AtprotoIdentityId == identityId)
            : await context.Set<ActorModerationRecord>().CountAsync(row => row.ActorId == fixture.ActorId);
        await Assert.That(suspended).IsTrue();
        await Assert.That(audits).IsEqualTo(1);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(context.ChangeTracker.Entries<ActorPii>()).IsEmpty();

        await context.PlatformUserRoles.Where(row => row.UserId == fixture.UserId).ExecuteDeleteAsync();
        var reinstatement = new GlobalModerationRequest
        {
            Action = GlobalModerationAction.Reinstate,
            ReasonCode = moderation.ReasonCode
        };
        if (identityOnly)
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                fixture.ExecuteCommandAsync<ModerateAtprotoIdentityCommand, BaseCommandResponse<Guid>>(
                    new() { AtprotoIdentityId = identityId, Moderation = reinstatement }));
        else
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                fixture.ExecuteCommandAsync<ModerateActorCommand, BaseCommandResponse<Guid>>(
                    new() { ActorId = fixture.ActorId, Moderation = reinstatement }));
        await Assert.That(identityOnly
            ? await context.AtprotoIdentities.Where(row => row.Id == identityId)
                .Select(row => row.IsSuspended).SingleAsync()
            : await context.Actors.IgnoreAutoIncludes().Where(row => row.Id == fixture.ActorId)
                .Select(row => row.IsSuspended).SingleAsync()).IsTrue();
        await Assert.That(identityOnly
            ? await context.Set<AtprotoIdentityModerationRecord>().CountAsync(row => row.AtprotoIdentityId == identityId)
            : await context.Set<ActorModerationRecord>().CountAsync(row => row.ActorId == fixture.ActorId)).IsEqualTo(1);
    }
}
