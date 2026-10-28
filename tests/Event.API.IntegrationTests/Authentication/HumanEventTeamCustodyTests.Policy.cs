using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Features.EventRoleAssignments.Handlers.Queries;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanEventTeamCustodyTests
{
    [Test]
    public async Task NonAdministratorKeepsLocalTeamDisclosureAndOriginalRoleCeiling()
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var team = await ActivatorUtilities.CreateInstance<GetEventTeamListRequestHandler>(request.Services)
            .QueryAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, EventId = target.Event });
        await Assert.That(team.Single().AssignmentId).IsEqualTo(target.Assignment);
        var presets = await ActivatorUtilities.CreateInstance<GetAssignableEventRolePresetsRequestHandler>(request.Services)
            .QueryAsync(new()
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                EventId = target.Event,
                AssignerUserId = fixture.Binding.LocalSubjectId
            });
        await Assert.That(presets.Any(row => row.RoleId == (int)RoleEnum.CheckInStaff)).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.UserId == fixture.Binding.LocalSubjectId)).IsFalse();
        await Assert.That(await observer.TenantUserRoleGrants.AnyAsync(row =>
            row.TenantUser.UserId == fixture.Binding.LocalSubjectId && row.RoleId == (int)RoleEnum.TenantAdmin)).IsFalse();
    }

    [Test]
    public async Task FilteredRequestedPermissionStaysOutsideTheDelegableCeiling()
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            int permission = await seed.RolePermissions.Where(row => row.RoleId == (int)RoleEnum.CheckInStaff)
                .Select(row => row.PermissionId).FirstAsync();
            await seed.Permissions.Where(row => row.Id == permission)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsFiltered, true));
        }
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var result = await ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(request.Services)
            .ExecuteAsync(Assignment(fixture, target.Event));
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_role_authority_ceiling_exceeded");
        var presets = await ActivatorUtilities.CreateInstance<GetAssignableEventRolePresetsRequestHandler>(request.Services)
            .QueryAsync(new()
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                EventId = target.Event,
                AssignerUserId = fixture.Binding.LocalSubjectId
            });
        await Assert.That(presets.Any(row => row.RoleId == (int)RoleEnum.CheckInStaff)).IsFalse();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.CountAsync(row => row.EventId == target.Event)).IsEqualTo(1);
        await Assert.That(await observer.AuditLogs.AnyAsync(row => row.EntityId == target.Event.ToString()
            && row.Action == "EventRoleAssignmentDenied")).IsTrue();
    }

    [Test]
    public async Task OwnerAssignmentRetainsTheSeparateTransferInvariant()
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var result = await ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(request.Services)
            .ExecuteAsync(Assignment(fixture, target.Event) with { RoleId = (int)RoleEnum.EventOwner });
        await Assert.That(result.FailureCode).IsEqualTo("event_owner_transfer_required");
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.CountAsync(row => row.EventId == target.Event)).IsEqualTo(1);
        await Assert.That(await observer.AuditLogs.AnyAsync(row => row.EntityId == target.Event.ToString()
            && row.Action == "EventRoleAssignmentDenied")).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreparedEmailCannotResolveAReplacementTargetOrAcquireAnEqualRequest(bool equalRequest)
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var original = new AssignEventRoleByEmailCommand
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            EventId = target.Event,
            TargetUserEmail = fixture.Login.Identifier,
            RoleId = (int)RoleEnum.CheckInStaff
        };
        var prepared = await request.Services.GetRequiredService<IHumanEventTeamActionPreparation>().PrepareAsync(original, default);
        string changedEmail = $"changed-{Guid.CreateVersion7():N}@team.test";
        if (!equalRequest)
        {
            await using var writer = fixture.Native.CreateDatabase();
            var user = await writer.Users.Include(row => row.Pii).SingleAsync(row => row.Id == fixture.Binding.LocalSubjectId);
            user.Email = changedEmail;
            await writer.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(equalRequest ? original with { } : original, token);
                return true;
            }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.CountAsync(row => row.EventId == target.Event)).IsEqualTo(1);
        await Assert.That(await observer.Users.Where(row => row.Id == fixture.Binding.LocalSubjectId)
            .Select(row => row.Pii.Email).SingleAsync()).IsEqualTo(equalRequest ? fixture.Login.Identifier : changedEmail);
    }

    [Test]
    [Arguments("actor")]
    [Arguments("tenant")]
    [Arguments("event")]
    public async Task DirectAssignmentCannotSubstituteTheOriginalSelectedActorOrTarget(string change)
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var command = Assignment(fixture, target.Event);
        if (change == "actor") request.SubstituteSubject(Guid.CreateVersion7().ToString("D"));
        command = change switch
        {
            "actor" => command,
            "tenant" => command with { TenantId = Guid.CreateVersion7() },
            _ => command with { EventId = Guid.CreateVersion7() }
        };
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(request.Services).ExecuteAsync(command));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.CountAsync(row => row.EventId == target.Event)).IsEqualTo(1);
        await Assert.That(await observer.AuditLogs.AnyAsync(row => row.EntityType == nameof(EventRoleAssignment)
            && row.ActorId == fixture.Binding.LocalSubjectId)).IsFalse();
    }
}
