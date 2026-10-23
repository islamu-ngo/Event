using Explore.Application.Authentication;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class AtprotoParticipationMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task The_real_onboarding_alias_invalidates_a_new_admin_membership_but_not_its_replay(bool organization)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var seed = await SeedAsync(database, organization);
        var personalId = Guid.CreateVersion7();
        await using (var setup = database.Context())
        {
            var user = await setup.Users.SingleAsync(row => row.Id == database.SubjectId);
            setup.Actors.Add(new Actor
            {
                Id = personalId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = user.Id,
                User = user,
                Pii = new ActorPii { DisplayName = "Personal onboarding witness" }
            });
            await setup.SaveChangesAsync();
        }
        long initial;
        await using (var before = database.Context())
            initial = await before.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        for (var pass = 0; pass < 2; pass++)
        {
            await using var context = database.Context();
            var operation = new AtprotoSubjectOnboardingOperation(new UserExternalLoginRepository(context),
                new AtprotoIdentityRepository(context), new ActorRepository(context), new ActorTypeRepository(context),
                new TenantRepository(context), new TenantUserRepository(context), new TenantUserRoleGrantRepository(context),
                new OrganizationRepository(context), new OrganizationTenantRepository(context), new OrganizationMemberRepository(context),
                new GroupRepository(context), new GroupTenantRepository(context), new GroupMemberRepository(context),
                new ActorReferenceConsolidationRepository(context), new GenericRepository<ActorMerge, Guid>(context));
            var did = AtprotoDid.Parse(seed.Account.Value);
            var request = new BootstrapAtprotoSessionCommand(did, "https://pds.example.test", "participant-test",
                organization ? AtprotoSubjectClassification.Organization : AtprotoSubjectClassification.Group, ReadOnlyMemory<byte>.Empty);
            var verified = new AtprotoVerifiedOAuthSession(did, "participant.example.test", new Uri(request.ExpectedPdsUri),
                request.OAuthClientKeyId, ReadOnlyMemory<byte>.Empty);
            await MutateAsync(database, context, seed, async (participant, token) =>
            {
                var result = await operation.ExecuteAsync(request, verified,
                    await context.Users.SingleAsync(row => row.Id == database.SubjectId, token),
                    await context.Actors.SingleAsync(row => row.Id == personalId, token), database.TenantId,
                    DateTime.UtcNow, participant, token);
                await Assert.That(result.Success).IsTrue();
                await Assert.That(result.ActorId).IsEqualTo(seed.ActorId);
                await Assert.That(result.ParticipationId).IsEqualTo(seed.ParticipationId);
            });
        }
        await using var check = database.Context();
        await Assert.That(await check.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(initial + 1);
        await Assert.That(organization ? await check.OrganizationMembers.CountAsync() : await check.GroupMembers.CountAsync()).IsEqualTo(1);
        await Assert.That(organization
            ? await check.OrganizationTenants.Select(row => row.ApprovalStatusId).SingleAsync()
            : await check.GroupTenants.Select(row => row.ApprovalStatusId).SingleAsync()).IsEqualTo((int)ApprovalStatusEnum.Pending);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_verified_new_membership_advances_qualification_once_without_promoting_participation(bool organization)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var seed = await SeedAsync(database, organization);
        await using var context = database.Context();
        var initial = await context.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        await MutateAsync(database, context, seed, async (participant, token) =>
        {
            await participant.FenceActorAsync(seed.ActorId, token);
            var memberId = await AddMemberAsync(context, database, seed, database.SubjectId, token);
            await participant.RecordMembershipAsync(memberId, token);
            await participant.RecordMembershipAsync(memberId, token);
        });
        await using var check = database.Context();
        await Assert.That((await check.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(initial.Revision + 1);
        var approval = organization
            ? await check.OrganizationTenants.Select(row => row.ApprovalStatusId).SingleAsync()
            : await check.GroupTenants.Select(row => row.ApprovalStatusId).SingleAsync();
        await Assert.That(approval).IsEqualTo((int)ApprovalStatusEnum.Pending);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Replaying_an_existing_membership_does_not_renew_qualification()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var seed = await SeedAsync(database, false);
        await using (var seedContext = database.Context())
            await AddMemberAsync(seedContext, database, seed, database.SubjectId, CancellationToken.None);
        await using var context = database.Context();
        var revision = await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        await MutateAsync(database, context, seed, (participant, token) => participant.FenceActorAsync(seed.ActorId, token));
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision);
    }

    [Test]
    public async Task A_retained_erasure_fence_denies_membership_before_primary_cleanup()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var seed = await SeedAsync(database, false);
        await using var context = database.Context();
        await database.Privacy.Authority(context, TimeProvider.System).AppendAsync(PrivacyErasureRequest.Create(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, database.SubjectId,
            PrivacyErasureReasonCode.SubjectErasureRequest, 1), CancellationToken.None);
        await Assert.That(async () => await MutateAsync(database, context, seed, (_, _) => Task.CompletedTask))
            .Throws<InvalidOperationException>();
        await Assert.That(await context.GroupMembers.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task A_verified_account_cannot_record_membership_for_another_human()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var seed = await SeedAsync(database, false);
        await using var context = database.Context();
        var revision = await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        await Assert.That(async () => await MutateAsync(database, context, seed, async (participant, token) =>
        {
            await participant.FenceActorAsync(seed.ActorId, token);
            await participant.RecordMembershipAsync(await AddMemberAsync(context, database, seed, database.ReviewerId, token), token);
        })).Throws<InvalidOperationException>();
        await Assert.That(await context.GroupMembers.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_changed_verified_login_rolls_back_membership_and_qualification_at_the_real_commit(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var seed = await SeedAsync(database, false);
        await using var context = database.Context();
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var revision = await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        var observedOwner = false;
        await Assert.That(async () => await MutateAsync(database, context, seed, async (participant, token) =>
        {
            observedOwner = context.IdentityFenceOwnsTransaction;
            await participant.FenceActorAsync(seed.ActorId, token);
            await participant.RecordMembershipAsync(await AddMemberAsync(context, database, seed, database.SubjectId, token), token);
            (await context.UserExternalLogins.SingleAsync(token)).ProviderKey = $"did:plc:{Guid.CreateVersion7():N}";
            await context.SaveChangesAsync(token);
        }, shared?.Authority(context, TimeProvider.System))).Throws<InvalidOperationException>();
        await using var check = database.Context();
        await Assert.That(observedOwner).IsEqualTo(colocated);
        await Assert.That(await check.GroupMembers.AnyAsync()).IsFalse();
        await Assert.That(await check.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision);
        await Assert.That((await check.UserExternalLogins.SingleAsync()).ProviderKey).IsEqualTo(seed.Account.Value);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Final_qualification_cannot_outlive_its_admin_membership_or_represented_actor(bool changeActor)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var seed = await SeedAsync(database, false);
        await using var context = database.Context();
        var revision = await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync();
        await Assert.That(async () => await MutateAsync(database, context, seed, async (participant, token) =>
        {
            await participant.FenceActorAsync(seed.ActorId, token);
            var id = await AddMemberAsync(context, database, seed, database.SubjectId, token);
            await participant.RecordMembershipAsync(id, token);
            if (changeActor) (await context.Actors.SingleAsync(row => row.Id == seed.ActorId, token)).IsSuspended = true;
            else (await context.GroupMembers.SingleAsync(row => row.Id == id, token)).RoleId = (int)RoleEnum.GroupMember;
            await context.SaveChangesAsync(token);
        })).Throws<InvalidOperationException>();
        await using var check = database.Context();
        await Assert.That(await check.GroupMembers.AnyAsync()).IsFalse();
        await Assert.That(await check.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(revision);
    }

    private static async Task MutateAsync(SourceDatabase database, ExploreDbContext context, Seed seed,
        Func<IAtprotoParticipationMutation, CancellationToken, Task> operation, IPrivacyIdentityFenceAuthority? authority = null)
    {
        authority ??= database.Privacy.Authority(context, TimeProvider.System);
        IAtprotoParticipationMutation? participant = null;
        Task CheckAsync(CancellationToken token) => participant?.EnsureCurrentAsync(token) ?? Task.CompletedTask;
        await authority.ExecuteEnrollmentSerializedAsync(async token =>
            await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async inner =>
            {
                participant = await new AtprotoParticipationMutationFactory(context, authority).AcquireAsync(
                    seed.Account, database.SubjectId, seed.LoginId, database.TenantId, inner);
                await operation(participant, inner);
                return true;
            }, CheckAsync, token), CheckAsync, CancellationToken.None);
    }

    private static async Task<Guid> AddMemberAsync(ExploreDbContext context, SourceDatabase database, Seed seed, Guid userId, CancellationToken token)
    {
        var id = Guid.CreateVersion7();
        if (seed.Organization)
            context.OrganizationMembers.Add(new OrganizationMember
            {
                Id = id,
                TenantId = database.TenantId,
                Tenant = null!,
                OrganizationTenantId = seed.ParticipationId,
                OrganizationTenant = null!,
                UserId = userId,
                User = null!,
                RoleId = (int)RoleEnum.OrgAdmin,
                Role = null!
            });
        else
            context.GroupMembers.Add(new GroupMember
            {
                Id = id,
                TenantId = database.TenantId,
                Tenant = null!,
                GroupTenantId = seed.ParticipationId,
                GroupTenant = null!,
                UserId = userId,
                User = null!,
                RoleId = (int)RoleEnum.GroupAdmin,
                Role = null!
            });
        await context.SaveChangesAsync(token);
        return id;
    }

    private static async Task<Seed> SeedAsync(SourceDatabase database, bool organization)
    {
        await using var context = database.Context();
        var account = PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(AtprotoDid.Parse($"did:plc:{Guid.CreateVersion7():N}"));
        var loginId = Guid.CreateVersion7();
        context.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = loginId,
            UserId = database.SubjectId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Atproto,
            AuthenticationProvider = null!,
            ProviderKey = account.Value
        });
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorType = null!,
            ActorTypeId = (int)(organization ? ActorTypeEnum.Organization : ActorTypeEnum.Group),
            Pii = new ActorPii { DisplayName = "Verified membership witness" }
        };
        var participationId = Guid.CreateVersion7();
        if (organization)
        {
            var owner = new Organization { Id = Guid.CreateVersion7(), Pii = new OrganizationPii { FullName = "Verified organization" } };
            actor.Organization = owner; actor.OrganizationId = owner.Id;
            context.OrganizationTenants.Add(new OrganizationTenant
            {
                Id = participationId,
                Organization = owner,
                OrganizationId = owner.Id,
                TenantId = database.TenantId,
                Tenant = null!,
                ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                ApprovalStatus = null!
            });
        }
        else
        {
            var owner = new Group { Id = Guid.CreateVersion7(), FullName = "Verified group" };
            actor.Group = owner; actor.GroupId = owner.Id;
            context.GroupTenants.Add(new GroupTenant
            {
                Id = participationId,
                Group = owner,
                GroupId = owner.Id,
                TenantId = database.TenantId,
                Tenant = null!,
                ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
                ApprovalStatus = null!
            });
        }
        context.AtprotoIdentities.Add(new AtprotoIdentity(AtprotoDid.Parse(account.Value))
        {
            Id = Guid.CreateVersion7(),
            Actor = actor,
            ActorId = actor.Id,
            PdsHost = "https://pds.example.test",
            IsActive = true
        });
        await context.SaveChangesAsync();
        return new(account, loginId, actor.Id, participationId, organization);
    }

    private sealed record Seed(ProviderAccountKey Account, Guid LoginId, Guid ActorId, Guid ParticipationId, bool Organization);
}
