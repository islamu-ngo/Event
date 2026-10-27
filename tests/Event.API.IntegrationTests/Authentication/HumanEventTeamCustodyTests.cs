using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class HumanEventTeamCustodyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AssignmentAndAuditShareTheOriginalOwner(bool rejectAfterBody)
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        Guid created = Guid.Empty;
        request.Admission.AfterNativeSources = async token =>
        {
            created = await context.EventRoleAssignments.AsNoTracking().Where(row =>
                row.EventId == target.Event && row.RoleId == (int)RoleEnum.CheckInStaff).Select(row => row.Id).SingleAsync(token);
            await Assert.That(await context.AuditLogs.AsNoTracking().AnyAsync(row =>
                row.EntityId == created.ToString() && row.Action == "EventRoleAssignmentCreated", token)).IsTrue();
            if (rejectAfterBody) throw new InvalidOperationException("Rejected before owner settlement.");
        };
        var handler = ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(request.Services);
        var command = Assignment(fixture, target.Event);
        if (rejectAfterBody)
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(command));
        else
            await Assert.That((await handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        await Assert.That(request.Admission.NativeSourcesReached).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.AsNoTracking().AnyAsync(row => row.Id == created))
            .IsEqualTo(!rejectAfterBody);
        await Assert.That(await observer.AuditLogs.AsNoTracking().AnyAsync(row =>
            row.EntityId == created.ToString() && row.Action == "EventRoleAssignmentCreated")).IsEqualTo(!rejectAfterBody);
    }

    private static Task<LocalIdentityLifecycleHttpFixture> CreateFixture() =>
        LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: services =>
        {
            var registration = services.Single(service => service.ServiceType == typeof(IHumanActionCommitFinalizer));
            services.Remove(registration);
            services.AddScoped(provider => new CommitAdmissionProbe(
                (IHumanActionCommitFinalizer)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!)));
            services.AddScoped<IHumanActionCommitFinalizer>(provider => provider.GetRequiredService<CommitAdmissionProbe>());
        });

    private static AssignEventRoleCommand Assignment(LocalIdentityLifecycleHttpFixture fixture, Guid eventId) => new()
    {
        TenantId = PlatformDefaults.DefaultTenantId,
        EventId = eventId,
        TargetUserId = fixture.Binding.LocalSubjectId,
        RoleId = (int)RoleEnum.CheckInStaff,
        StartsAtUtc = fixture.Clock.GetUtcNow().AddMinutes(-1).UtcDateTime
    };

    private static async Task<(Guid Event, Guid Assignment)> Seed(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var seed = fixture.Native.CreateDatabase();
        Guid actor = await seed.Actors.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
            .Select(row => row.Id).SingleAsync();
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            Title = "Original team custody",
            ActorId = actor,
            Actor = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            SessionCount = 1,
            FirstSessionStartUtc = fixture.Clock.GetUtcNow().AddDays(1),
            LastSessionEndUtc = fixture.Clock.GetUtcNow().AddDays(1).AddHours(1),
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        seed.Events.Add(parent);
        var assignment = EventRoleAssignment.Create(parent.TenantId, parent.Id, fixture.Binding.LocalSubjectId,
            (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active,
            fixture.Clock.GetUtcNow().AddDays(-1).UtcDateTime, null, fixture.Binding.LocalSubjectId);
        seed.EventRoleAssignments.Add(assignment);
        // The genuine manager ceiling includes every requested CheckInStaff permission.
        var staffPermissions = await seed.RolePermissions.Where(row => row.RoleId == (int)RoleEnum.CheckInStaff)
            .Select(row => row.PermissionId).ToListAsync();
        staffPermissions.Add(await seed.Permissions.Where(row => row.MasterCode == PermissionCodes.EventManageTeam)
            .Select(row => row.Id).SingleAsync());
        foreach (int permission in staffPermissions.Distinct())
            if (!await seed.RolePermissions.AnyAsync(row => row.RoleId == (int)RoleEnum.EventManager && row.PermissionId == permission))
                seed.RolePermissions.Add(new RolePermission
                {
                    RoleId = (int)RoleEnum.EventManager,
                    Role = null!,
                    PermissionId = permission,
                    Permission = null!
                });
        await seed.Permissions.Where(row => staffPermissions.Contains(row.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsFiltered, false).SetProperty(row => row.IsActive, true));
        foreach (var (key, value) in new[]
        {
            (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\""),
            (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\"")
        })
        {
            var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
            else setting.Value = value;
        }
        await seed.SaveChangesAsync();
        return (parent.Id, assignment.Id);
    }

    private sealed class CommitAdmissionProbe(IHumanActionCommitFinalizer inner) : IHumanActionCommitFinalizer
    {
        private int registrations;
        internal Func<CancellationToken, Task>? AfterNativeSources { get; set; }
        internal Func<CancellationToken, Task>? AfterAllSources { get; set; }
        internal bool NativeSourcesReached { get; private set; }

        public void Register(Func<CancellationToken, Task> validateSources, Action<DateTimeOffset> validateAssignments)
        {
            int position = ++registrations;
            inner.Register(async token =>
            {
                await validateSources(token);
                // Native admission registers first; inject only after its receipt read,
                // before retained human sources validate the same physical transaction.
                if (position == 1 && AfterNativeSources is { } afterNative)
                {
                    NativeSourcesReached = true;
                    await afterNative(token);
                }
                if (position == registrations && AfterAllSources is { } afterAll) await afterAll(token);
            }, validateAssignments);
        }
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        private readonly IHttpContextAccessor accessor;
        internal IServiceProvider Services => scope.ServiceProvider;
        internal CommitAdmissionProbe Admission => Services.GetRequiredService<CommitAdmissionProbe>();
        internal DefaultHttpContext Context { get; }

        internal Request(IServiceProvider services)
        {
            scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = Services };
            accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }

        internal Task Authenticate(string token)
        {
            accessor.HttpContext = Context;
            Context.Request.Headers.Authorization = "Bearer " + token;
            return AuthenticateOriginalRequest();
        }

        private async Task AuthenticateOriginalRequest()
        {
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
            await Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        }

        internal void SubstituteSubject(string subject)
        {
            var identity = (ClaimsIdentity)Context.User.Identity!;
            foreach (var claim in identity.FindAll("sub").Concat(identity.FindAll(ClaimTypes.NameIdentifier)).ToArray())
                identity.RemoveClaim(claim);
            identity.AddClaim(new Claim("sub", subject));
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
        }

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
        }
    }
}
