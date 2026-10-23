using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
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
using Explore.Persistence.Identity;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Real validated Local credentials, original selected scopes, persisted source fences and the
/// actual committing owner. Only the external Cerbos transport is substituted.
/// </summary>
[NotInParallel]
public sealed class HumanEventProviderPreparationTests
{
    [Test]
    [Arguments("local", false)]
    [Arguments("local", true)]
    [Arguments("cerbos", false)]
    [Arguments("cerbos", true)]
    public async Task BindingHandlerRequiresCurrentEventDecisionBeforeObservingSubordinateTarget(string provider, bool revoke)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (eventId, assignmentId, _) = await SeedAsync(fixture);
        if (revoke)
        {
            await using var writer = fixture.Native.CreateDatabase();
            await writer.EventRoleAssignments.Where(row => row.Id == assignmentId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, EventRoleAssignmentStatus.Revoked));
        }
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, provider, transport);
        var handler = new DeleteRegistrationProviderBindingCommandHandler(
            request.Services.GetRequiredService<IRegistrationProviderRepository>(), fixture.Clock, preparation);
        var command = new DeleteRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7());
        if (revoke && provider == "local")
            await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(command));
        else
        {
            var result = await handler.ExecuteAsync(command);
            await Assert.That(result.FailureCode).IsEqualTo("registration_provider_binding_not_found");
        }
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "cerbos" ? 1 : 0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("local", "unchanged")]
    [Arguments("cerbos", "unchanged")]
    [Arguments("local", "revoke_before_acquire")]
    [Arguments("cerbos", "revoke_before_acquire")]
    [Arguments("local", "own_revocation")]
    [Arguments("cerbos", "own_revocation")]
    [Arguments("local", "expire_after_last_read")]
    [Arguments("cerbos", "expire_after_last_read")]
    [Arguments("local", "credential_expiry_after_last_read")]
    [Arguments("cerbos", "credential_expiry_after_last_read")]
    [Arguments("local", "own_expiry_extension_after_last_read")]
    [Arguments("cerbos", "own_expiry_extension_after_last_read")]
    [Arguments("local", "replace_target")]
    [Arguments("cerbos", "replace_target")]
    [Arguments("local", "own_binding_change")]
    [Arguments("cerbos", "own_binding_change")]
    [Arguments("local", "own_organizer_change")]
    [Arguments("cerbos", "own_organizer_change")]
    [Arguments("local", "own_route_change")]
    [Arguments("cerbos", "own_route_change")]
    [Arguments("local", "irrelevant_route_write")]
    [Arguments("cerbos", "irrelevant_route_write")]
    [Arguments("local", "irrelevant_assignment_write")]
    [Arguments("cerbos", "irrelevant_assignment_write")]
    public async Task OriginalProviderInputsMustSurviveAcquisitionOwnWritesAndFinalTime(string provider, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        var (eventId, assignmentId, expires) = await SeedAsync(fixture);
        var originalLifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        if (change is "expire_after_last_read" or "credential_expiry_after_last_read" or "own_expiry_extension_after_last_read")
        {
            var credentialEnd = originalLifetime.ExpiresAt.Add(originalLifetime.ClockSkew);
            expires = change == "credential_expiry_after_last_read"
                ? credentialEnd.AddHours(1)
                : fixture.Clock.GetUtcNow().Add((credentialEnd - fixture.Clock.GetUtcNow()) / 2);
            await using var seed = fixture.Native.CreateDatabase();
            await seed.EventRoleAssignments.Where(row => row.Id == assignmentId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expires.UtcDateTime));
            if (change != "credential_expiry_after_last_read")
                await Assert.That(originalLifetime.IsValidAt(expires)).IsTrue();
        }
        var command = new DeleteRegistrationProviderBindingCommand(
            PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7());
        var transport = new Transport(context);
        var preparation = CreatePreparation(services, fixture.Clock, provider, transport);
        var prepared = await preparation.PrepareAsync(command);
        if (change == "revoke_before_acquire")
        {
            await using var revoke = fixture.Native.CreateDatabase();
            await revoke.EventRoleAssignments.Where(row => row.Id == assignmentId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, EventRoleAssignmentStatus.Revoked));
        }
        string key = $"event-provider-preparation-{Guid.NewGuid():N}";
        var execution = services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, token);
            if (change == "own_revocation")
                await context.EventRoleAssignments.Where(row => row.Id == assignmentId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, EventRoleAssignmentStatus.Revoked), token);
            if (change == "own_expiry_extension_after_last_read")
                await context.EventRoleAssignments.Where(row => row.Id == assignmentId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expires.AddHours(1).UtcDateTime), token);
            if (change == "replace_target")
                await context.Events.Where(row => row.Id == eventId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()), token);
            if (change == "own_binding_change")
                await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, Guid.NewGuid().ToString()), token);
            if (change == "own_organizer_change")
            {
                Guid actor = await context.Actors.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
                    .Select(row => row.Id).SingleAsync(token);
                await context.Events.Where(row => row.Id == eventId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.OrganizerActorId, actor), token);
            }
            if (change == "irrelevant_assignment_write")
                await context.EventRoleAssignments.Where(row => row.Id == assignmentId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAtUtc, expires.UtcDateTime), token);
            if (change is "own_route_change" or "irrelevant_route_write")
            {
                (string Key, string Value)[] settings = change == "own_route_change"
                    ? [(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "true"),
                        (GovernanceSettingKeys.Cerbos.Mode, "\"custom_endpoint\""),
                        (GovernanceSettingKeys.Cerbos.CustomEndpoint, "\"http://changed.prepared.test:3593\"")]
                    : [(GovernanceSettingKeys.Cerbos.GrpcEndpoint, "\"http://unused.prepared.test:3593\"")];
                foreach (var setting in settings)
                {
                    var row = await context.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == setting.Key, token);
                    if (row is null)
                        context.SystemSettings.Add(new SystemSetting { SettingKey = setting.Key, Value = setting.Value });
                    else
                        row.Value = setting.Value;
                }
            }
            // This admission is deliberately registered after the preparation's source callback.
            // Every admission's reads must finish before ANY admission samples trusted time.
            services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
            {
                await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == assignmentId, finalToken);
                if (change is "expire_after_last_read" or "own_expiry_extension_after_last_read")
                    fixture.Clock.Advance(expires - fixture.Clock.GetUtcNow());
                if (change == "credential_expiry_after_last_read")
                    fixture.Clock.Advance(originalLifetime.ExpiresAt.Add(originalLifetime.ClockSkew)
                        .AddTicks(1) - fixture.Clock.GetUtcNow());
            }, _ => { });
            context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await context.SaveChangesAsync(token);
            return true;
        });
        bool commits = change is "unchanged" or "irrelevant_assignment_write" or "irrelevant_route_write";
        if (commits)
            await Assert.That(await execution).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsEqualTo(commits);
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "cerbos" ? 1 : 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.AcquireAsync(command));
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, true, false)]
    [Arguments(true, true, true)]
    public async Task LocalAuthorityIsNativeEventAuthorityNotAnInstanceShortcut(
        bool instanceAdmin, bool tenantAdmin, bool organizer)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (eventId, assignmentId, _) = await SeedAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            if (instanceAdmin)
            {
                int role = await seed.Roles.Where(row => row.MasterCode == "platform.admin"
                    && row.RoleScopeId == (int)RoleScopeEnum.Platform).Select(row => row.Id).SingleAsync();
                seed.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = fixture.Binding.LocalSubjectId,
                    User = null!,
                    RoleId = role,
                    Role = null!,
                    GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
            }
            if (tenantAdmin)
            {
                var member = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = fixture.Binding.LocalSubjectId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
                };
                seed.TenantUsers.Add(member);
                seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = member.TenantId,
                    Tenant = null!,
                    TenantUserId = member.Id,
                    TenantUser = member,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant
                });
            }
            if (organizer)
            {
                Guid actor = await seed.Actors.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
                    .Select(row => row.Id).SingleAsync();
                await seed.Events.Where(row => row.Id == eventId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.OrganizerActorId, actor));
            }
            await seed.SaveChangesAsync();
        }
        var command = new DeleteRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7());
        var preparation = CreatePreparation(request.Services, fixture.Clock, "local",
            new Transport(request.Services.GetRequiredService<ExploreDbContext>()));
        bool accepted = organizer || !tenantAdmin;
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var facts = await request.Services.GetRequiredService<IHumanEventAssignmentSourceFactory>()
            .Create(command.TenantId, [eventId]).ObserveAsync();
        var original = await request.Services.GetRequiredService<FallbackAuthorizationService>().AuthorizeAsync(
            new AuthorizationRequest(ResourceKinds.Event, eventId.ToString("D"),
                AuthorizationActions.Events.ManageRegistrationChannels,
                Facts: HumanEventAssignmentProjection.Resource(facts, eventId)));
        await Assert.That(original.IsAllowed).IsEqualTo(accepted);
        if (accepted)
            await preparation.ExecuteAsync(command, _ => Task.FromResult(true));
        else
            await Assert.ThrowsAsync<AuthorizationException>(() => preparation.PrepareAsync(command));
        if (!organizer)
        {
            await using var revoke = fixture.Native.CreateDatabase();
            await revoke.EventRoleAssignments.Where(row => row.Id == assignmentId).ExecuteDeleteAsync();
            await Assert.ThrowsAsync<AuthorizationException>(() => preparation.PrepareAsync(command));
        }
    }

    [Test]
    public async Task ExactCommandTargetCannotBeReplacedAndConnectionAdministrationIsExcluded()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (eventId, _, _) = await SeedAsync(fixture);
        var command = new DeleteRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7());
        var preparation = CreatePreparation(request.Services, fixture.Clock, "local",
            new Transport(request.Services.GetRequiredService<ExploreDbContext>()));
        var prepared = await preparation.PrepareAsync(command);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(command with { BindingId = Guid.CreateVersion7() }, token);
                return true;
            }));
        await Assert.ThrowsAsync<ArgumentException>(() => preparation.PrepareAsync(
            new DeleteRegistrationProviderConnectionCommand(command.TenantId, eventId, command.BindingId)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            preparation.PrepareAsync(command with { TenantId = Guid.CreateVersion7() }));
    }

    [Test]
    public async Task EveryExactManagementCommandCanAcquireOnlyItsOriginalEventCapability()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (eventId, _, _) = await SeedAsync(fixture);
        Guid tenant = PlatformDefaults.DefaultTenantId, binding = Guid.CreateVersion7(), workflow = Guid.CreateVersion7(),
            requirement = Guid.CreateVersion7(), channel = Guid.CreateVersion7(), connection = Guid.CreateVersion7();
        ICommand<BaseCommandResponse<Guid>>[] commands =
        [
            new PollRegistrationProviderReconciliationCommand(tenant, eventId, binding, fixture.Clock.GetUtcNow().UtcDateTime),
            new QueueManualRegistrationProviderImportCommand(tenant, eventId, binding, "storage", "source"),
            new RetryRegistrationProviderParkedItemCommand(tenant, eventId, null, Guid.CreateVersion7(), 1, "retry"),
            new ResolveRegistrationProviderQueueItemCommand(tenant, eventId, Guid.CreateVersion7(), null, "resolved", "note"),
            new CreateRegistrationProviderBindingCommand(tenant, eventId, new RegistrationProviderBindingRequestDto()),
            new UpdateRegistrationProviderBindingCommand(tenant, eventId, binding, new RegistrationProviderBindingRequestDto()),
            new DeleteRegistrationProviderBindingCommand(tenant, eventId, binding),
            new PublishEventRegistrationProviderBindingCommand(tenant, eventId, binding),
            new ReplaceEventDraftRegistrationProviderMappingsCommand(tenant, eventId, binding, new ReplaceRegistrationProviderMappingsRequestDto()),
            new ImportExternalRegistrationProviderFormVersionCommand(tenant, eventId, connection, new ImportExternalRegistrationProviderFormVersionRequestDto()),
            new UpsertRegistrationChannelCommand(tenant, eventId, workflow, requirement, channel, new RegistrationChannelRequestDto()),
            new DeleteRegistrationChannelCommand(tenant, eventId, workflow, requirement, channel)
        ];
        var preparation = CreatePreparation(request.Services, fixture.Clock, "local",
            new Transport(request.Services.GetRequiredService<ExploreDbContext>()));
        foreach (var command in commands)
            await Assert.That(await preparation.ExecuteAsync(command, _ => Task.FromResult(true))).IsTrue();
    }

    [Test]
    public async Task ExternalLocalIdentityCannotBorrowApplicationSources()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            topology: IdentityDatabaseTopology.External);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (eventId, _, _) = await SeedAsync(fixture);
        var command = new DeleteRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7());
        var transport = new Transport(request.Services.GetRequiredService<ExploreDbContext>());
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        await Assert.ThrowsAsync<NotSupportedException>(() => preparation.PrepareAsync(command));
        await Assert.That(transport.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task OrdinaryConcurrencyFailureDoesNotReplayTheBusinessCallbackOrProviderDecision()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (eventId, _, _) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        var command = new DeleteRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7());
        string key = $"event-provider-no-replay-{Guid.NewGuid():N}";
        int invocations = 0;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => preparation.ExecuteAsync<bool>(command, async token =>
        {
            invocations++;
            context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await context.SaveChangesAsync(token);
            throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                "A business conflict is not an acknowledged owner rollback receipt.");
        }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsFalse();
        await Assert.That(invocations).IsEqualTo(1);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RejectedBusinessResultRollsBackPersistedWorkAndPreservesItsFailure()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (eventId, _, _) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        var command = new DeleteRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7());
        var rejected = BaseCommandResponse.Failure<Guid>("registration_provider_binding_update_failed",
            "Domain transition rejected.", id: command.BindingId);
        string key = $"event-provider-rejected-{Guid.NewGuid():N}";
        var result = await preparation.ExecuteAsync(command, async token =>
        {
            context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "uncommitted" });
            await context.SaveChangesAsync(token);
            return rejected;
        });
        await Assert.That(result).IsSameReferenceAs(rejected);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsFalse();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task RejectedBusinessResultCannotCertifyAnEndedOwnersRollback()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (eventId, _, _) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        var command = new DeleteRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7());
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() => preparation.ExecuteAsync(command, async _ =>
        {
            await context.Database.CurrentTransaction!.DisposeAsync();
            return BaseCommandResponse.Failure<Guid>("registration_provider_binding_update_failed");
        }));
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BusinessSuppliedRollbackReceiptCannotCertifyRetryOfTheOriginalOwner(bool nested)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (eventId, _, _) = await SeedAsync(fixture);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, "cerbos", transport);
        var command = new DeleteRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7());
        string key = $"event-provider-foreign-receipt-{Guid.NewGuid():N}";
        int invocations = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.ExecuteAsync<bool>(command, async token =>
        {
            invocations++;
            context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "staged" });
            await context.SaveChangesAsync(token);
            // Adversarial callback input, NOT a simulated successful source/owner.
            // The real owner's recursive exception classifier must not turn it into permission
            // to retry this operation. The actual owner must still roll back the persisted marker.
            var receipt = new HumanMutationAttemptRolledBackException(new SqliteException("Untrusted callback receipt", 5));
            if (nested) throw new AggregateException(new InvalidOperationException("Nested callback receipt", receipt));
            throw receipt;
        }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsFalse();
        await Assert.That(invocations).IsEqualTo(1);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CerbosSeesPersistedEventProjectionAndItsOriginalOrderedPrincipal()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var (eventId, _, _) = await SeedAsync(fixture);
        var services = request.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(services, fixture.Clock, "cerbos", transport);
        var command = new DeleteRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, eventId, Guid.CreateVersion7());
        var prepared = await preparation.PrepareAsync(command);
        var sent = transport.Requests.Single();
        var originalBuilder = services.GetRequiredService<CerbosPrincipalBuilder>();
        var principal = await originalBuilder.BuildSdkPrincipalAsync(fixture.Binding.LocalSubjectId);
        await originalBuilder.EnrichWithEventAssignmentsAsync(principal, fixture.Binding.LocalSubjectId,
            command.TenantId, [eventId], CancellationToken.None);
        // The original builder samples DateTime.UtcNow, not this fixture's trusted clock.
        // Normalize only that independently sampled scalar; compare its actual unsorted lists.
        principal.WithAttribute("nowUtc",
            Cerbos.Sdk.Builder.AttributeValue.StringValue(sent.Principal.Attr["nowUtc"].StringValue));
        var assignments = await services.GetRequiredService<IHumanEventAssignmentSourceFactory>()
            .Create(command.TenantId, [eventId]).ObserveAsync();
        var expected = Cerbos.Sdk.Builder.CheckResourcesRequest.NewInstance().WithPrincipal(principal)
            .ToCheckResourcesRequest().Principal;
        await Assert.That(sent.Principal.Equals(expected)).IsTrue();
        await Assert.That(sent.Resources.Single().Actions.Single()).IsEqualTo(AuthorizationActions.Events.ManageRegistrationChannels);
        await Assert.That(sent.Resources.Single().Resource.Id).IsEqualTo(eventId.ToString("D"));
        await Assert.That(sent.Resources.Single().Resource.Attr["actorId"].StringValue)
            .IsEqualTo(assignments.Events.Single().ActorId.ToString());
        await request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, token);
            return true;
        });
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    private static HumanEventProviderMutationPreparation CreatePreparation(IServiceProvider services,
        TimeProvider clock, string provider, Transport transport)
    {
        var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://cerbos.prepared.test:3593" });
        var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(
            services, transport.Client, transport.Factory, options);
        return new(services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<ITenantContext>(), clock, cerbos,
            Options.Create(new AuthorizationProviderDeploymentOptions { Provider = provider }), options,
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanEventAssignmentSourceFactory>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => services.GetRequiredService<ISerializableAttemptOwner>(),
            () => services.GetRequiredService<IHumanEventProviderSourceFactory>());
    }

    private static async Task<(Guid EventId, Guid AssignmentId, DateTimeOffset Expires)> SeedAsync(
        LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var seed = fixture.Native.CreateDatabase();
        Guid actor = await seed.Actors.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
            .Select(row => row.Id).SingleAsync();
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            Title = "Prepared provider event",
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
        var expiry = fixture.Clock.GetUtcNow().AddHours(1);
        var assignment = EventRoleAssignment.Create(parent.TenantId, parent.Id, fixture.Binding.LocalSubjectId,
            (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active,
            fixture.Clock.GetUtcNow().AddDays(-1).UtcDateTime, expiry.UtcDateTime, fixture.Binding.LocalSubjectId);
        seed.EventRoleAssignments.Add(assignment);
        var permission = await seed.Permissions.SingleAsync(row => row.MasterCode == PermissionCodes.EventRegistrationManage);
        if (!await seed.RolePermissions.AnyAsync(row => row.RoleId == (int)RoleEnum.EventManager && row.PermissionId == permission.Id))
            seed.RolePermissions.Add(new RolePermission
            {
                RoleId = (int)RoleEnum.EventManager,
                Role = null!,
                PermissionId = permission.Id,
                Permission = null!
            });
        await seed.SaveChangesAsync();
        return (parent.Id, assignment.Id, expiry);
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        internal DefaultHttpContext Context { get; }
        internal IServiceProvider Services => _scope.ServiceProvider;

        internal Request(IServiceProvider services)
        {
            _scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = Services };
            Context.Request.Path = "/event-provider-preparation";
            _accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }

        internal Task AuthenticateAsync(string token)
        {
            _accessor.HttpContext = Context;
            Context.Request.Headers.Authorization = "Bearer " + token;
            return CompleteAuthenticationAsync();
        }

        private async Task CompleteAuthenticationAsync()
        {
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
        }

        public async ValueTask DisposeAsync()
        {
            _accessor.HttpContext = null;
            await _scope.DisposeAsync();
        }
    }

    private sealed class Transport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        internal List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];

        internal Transport(ExploreDbContext owner)
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(Client);
            Client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    if (owner.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("A PDP must never be called under the source locks.");
                    var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    Requests.Add(sent);
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new()
                        {
                            Kind = sent.Resources.Single().Resource.Kind,
                            Id = sent.Resources.Single().Resource.Id
                        }
                    };
                    result.Actions.Add(sent.Resources.Single().Actions.Single(), Effect.Allow);
                    response.Results.Add(result);
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }
    }
}
