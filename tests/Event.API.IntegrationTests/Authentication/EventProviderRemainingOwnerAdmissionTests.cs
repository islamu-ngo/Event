using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Exceptions;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Authenticated admission regressions for the five remote and mapping business owners.
/// Concrete handlers are intentional: a preliminary authorization decorator must not stand
/// in for the requested owner admission. These source-authored cases exercise original-human
/// admission, not complete final-boundary assurance, and remain unrun.
/// </summary>
[NotInParallel]
public sealed class EventProviderRemainingOwnerAdmissionTests
{
    [Test]
    [Arguments("valid")]
    [Arguments("revoke")]
    [Arguments("expire")]
    [Arguments("replace")]
    [Arguments("final-revoke")]
    [Arguments("conflict")]
    public async Task PreparedRemoteAdmissionRetainsOriginalSourcesAndDoesNotReplay(string change)
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string credential = await fixture.SignInAsync();
        var (eventId, assignmentId) = await SeedAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "local")));
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContextAccessor>()
            .BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Authorization = "Bearer " + credential;
        accessor.HttpContext = context;
        try
        {
            var authenticated = await context.AuthenticateAsync();
            await Assert.That(authenticated.Succeeded).IsTrue();
            context.User = authenticated.Principal!;
            var command = new PollRegistrationProviderReconciliationCommand(
                PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7(),
                fixture.Clock.GetUtcNow().UtcDateTime);
            var preparation = services.GetRequiredService<IHumanEventProviderMutationPreparation>();
            var prepared = await preparation.PrepareAsync(command, token);
            if (change == "replace")
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    prepared.ValidateAsync(command with { }, token));
                return;
            }
            if (change == "revoke")
            {
                await using var writer = fixture.Native.CreateDatabase();
                await writer.EventRoleAssignments.Where(row => row.Id == assignmentId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status,
                        EventRoleAssignmentStatus.Revoked), token);
                await Assert.ThrowsAsync<AuthorizationException>(() => prepared.ValidateAsync(command, token));
                return;
            }
            if (change == "expire")
            {
                var lifetime = await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                    .ObserveAsync(context.User, token);
                await Assert.That(lifetime).IsNotNull();
                fixture.Clock.Advance(lifetime!.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => prepared.ValidateAsync(command, token));
                return;
            }
            await prepared.ValidateAsync(command, token);
            var owner = services.GetRequiredService<ExploreDbContext>();
            int bodies = 0;
            bool changedOwnGrant = false;
            Task<BaseCommandResponse<Guid>> Execute() =>
                preparation.ExecutePreparedAsync(command, prepared, async currentToken =>
                {
                    bodies++;
                    await Assert.That(owner.Database.CurrentTransaction).IsNotNull();
                    if (change == "final-revoke")
                    {
                        await owner.EventRoleAssignments.Where(row => row.Id == assignmentId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status,
                                EventRoleAssignmentStatus.Revoked), currentToken);
                        changedOwnGrant = true;
                    }
                    if (change == "conflict")
                        throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                            "Original prepared attempt conflict.");
                    return BaseCommandResponse.Success(eventId, "Prepared source admission completed.");
                }, token);
            if (change == "final-revoke")
            {
                Exception? admissionFailure = null;
                try { await Execute(); }
                catch (AuthorizationException failure) { admissionFailure = failure; }
                catch (HumanMutationAttemptRolledBackException failure) { admissionFailure = failure; }
                await Assert.That(admissionFailure).IsNotNull();
                await Assert.That(changedOwnGrant).IsTrue();
            }
            else if (change == "conflict")
                await Assert.ThrowsAsync<HumanMutationAttemptRolledBackException>(Execute);
            else
                await Assert.That((await Execute()).IsSuccess).IsTrue();
            await Assert.That(bodies).IsEqualTo(1);
            await Assert.That(owner.Database.CurrentTransaction).IsNull();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => prepared.ValidateAsync(command, token));
            await using var observer = fixture.Native.CreateDatabase();
            await Assert.That(await observer.EventRoleAssignments.Where(row => row.Id == assignmentId)
                .Select(row => row.Status).SingleAsync(token)).IsEqualTo(EventRoleAssignmentStatus.Active);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Test]
    [Arguments("poll", false)]
    [Arguments("poll", true)]
    [Arguments("manual", false)]
    [Arguments("manual", true)]
    [Arguments("publish", false)]
    [Arguments("publish", true)]
    [Arguments("mapping", false)]
    [Arguments("mapping", true)]
    [Arguments("schema", false)]
    [Arguments("schema", true)]
    public async Task OriginalEventGrantMustPrecedeSubordinateObservation(string consumer, bool revoked)
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string credential = await fixture.SignInAsync();
        var (eventId, assignmentId) = await SeedAsync(fixture);
        if (revoked)
        {
            await using var writer = fixture.Native.CreateDatabase();
            await writer.EventRoleAssignments.Where(row => row.Id == assignmentId)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    row => row.Status, EventRoleAssignmentStatus.Revoked), token);
        }

        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.Configure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "local")));
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContextAccessor>()
            .BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/event-provider-remaining-owner-admission";
        context.Request.Headers.Authorization = "Bearer " + credential;
        accessor.HttpContext = context;
        try
        {
            var authenticated = await context.AuthenticateAsync();
            await Assert.That(authenticated.Succeeded).IsTrue();
            context.User = authenticated.Principal!;
            Guid targetId = Guid.CreateVersion7();
            ICommand<BaseCommandResponse<Guid>> command = consumer switch
            {
                "poll" => new PollRegistrationProviderReconciliationCommand(
                    PlatformDefaults.DefaultTenantId, eventId, targetId,
                    fixture.Clock.GetUtcNow().UtcDateTime),
                "manual" => new QueueManualRegistrationProviderImportCommand(
                    PlatformDefaults.DefaultTenantId, eventId, targetId,
                    Guid.CreateVersion7().ToString("D"), "owner-admission"),
                "publish" => new PublishEventRegistrationProviderBindingCommand(
                    PlatformDefaults.DefaultTenantId, eventId, targetId),
                "mapping" => new ReplaceEventDraftRegistrationProviderMappingsCommand(
                    PlatformDefaults.DefaultTenantId, eventId, targetId, new()),
                "schema" => new ImportExternalRegistrationProviderFormVersionCommand(
                    PlatformDefaults.DefaultTenantId, eventId, targetId,
                    new ImportExternalRegistrationProviderFormVersionRequestDto
                    {
                        ProviderSurveyId = "owner-admission",
                        Key = "owner-admission",
                        Name = "Owner admission"
                    }),
                _ => throw new ArgumentOutOfRangeException(nameof(consumer))
            };

            var preparation = services.GetRequiredService<IHumanEventProviderMutationPreparation>();
            if (revoked)
            {
                // Independently establish the actual source denial, not a mocked permission.
                await Assert.ThrowsAsync<AuthorizationException>(() => preparation.PrepareAsync(command, token));
                await Assert.ThrowsAsync<AuthorizationException>(() => ExecuteOwnerAsync(services, command, token));
            }
            else
            {
                // A real owner can admit this exact original subject/action. Use a separate
                // preparation for the eventual handler; capabilities are never borrowed.
                var prepared = await preparation.PrepareAsync(command, token);
                await services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async ownerToken =>
                {
                    await prepared.AcquireAsync(command, ownerToken);
                    return true;
                }, token);
                BaseCommandResponse<Guid> response = await ExecuteOwnerAsync(services, command, token);
                await Assert.That(response.IsSuccess).IsFalse();
                await Assert.That(response.FailureCode).IsEqualTo(consumer == "schema"
                    ? "registration_provider_connection_not_found"
                    : "registration_provider_binding_not_found");
            }
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static Task<BaseCommandResponse<Guid>> ExecuteOwnerAsync(IServiceProvider services,
        ICommand<BaseCommandResponse<Guid>> command, CancellationToken token) => command switch
        {
            PollRegistrationProviderReconciliationCommand poll =>
                ActivatorUtilities.CreateInstance<PollRegistrationProviderReconciliationCommandHandler>(services)
                    .ExecuteAsync(poll, token),
            QueueManualRegistrationProviderImportCommand manual =>
                ActivatorUtilities.CreateInstance<QueueManualRegistrationProviderImportCommandHandler>(services)
                    .ExecuteAsync(manual, token),
            PublishEventRegistrationProviderBindingCommand publish =>
                ActivatorUtilities.CreateInstance<PublishEventRegistrationProviderBindingCommandHandler>(services)
                    .ExecuteAsync(publish, token),
            ReplaceEventDraftRegistrationProviderMappingsCommand mapping =>
                ActivatorUtilities.CreateInstance<ReplaceEventDraftRegistrationProviderMappingsCommandHandler>(services)
                    .ExecuteAsync(mapping, token),
            ImportExternalRegistrationProviderFormVersionCommand schema =>
                ActivatorUtilities.CreateInstance<ImportExternalRegistrationProviderFormVersionCommandHandler>(services)
                    .ExecuteAsync(schema, token),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };

    private static async Task<(Guid EventId, Guid AssignmentId)> SeedAsync(
        LocalIdentityLifecycleHttpFixture fixture)
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using ExploreDbContext seed = fixture.Native.CreateDatabase();
        Guid actor = await seed.Actors.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
            .Select(row => row.Id).SingleAsync(token);
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            Title = "Remaining provider owner",
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
            fixture.Clock.GetUtcNow().AddDays(-1).UtcDateTime,
            fixture.Clock.GetUtcNow().AddHours(1).UtcDateTime, fixture.Binding.LocalSubjectId);
        seed.EventRoleAssignments.Add(assignment);
        var permission = await seed.Permissions.SingleAsync(
            row => row.MasterCode == PermissionCodes.EventRegistrationManage, token);
        if (!await seed.RolePermissions.AnyAsync(
            row => row.RoleId == (int)RoleEnum.EventManager && row.PermissionId == permission.Id, token))
        {
            seed.RolePermissions.Add(new RolePermission
            {
                RoleId = (int)RoleEnum.EventManager,
                Role = null!,
                PermissionId = permission.Id,
                Permission = null!
            });
        }
        await seed.SaveChangesAsync(token);
        return (parent.Id, assignment.Id);
    }
}
