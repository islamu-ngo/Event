using System.Security.Claims;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Features.Moderation.Responses.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoticeDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationNoticeReadTests.NoticeDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentResponseTests
{
    [Test]
    [Arguments(RoleEnum.EventOwner)]
    [Arguments(RoleEnum.EventManager)]
    public async Task Original_representative_response_preserves_Content_and_does_not_acknowledge_or_change_findings(RoleEnum role)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true, representativeRole: role);
        await using var session = new ResponseSession(fixture, fixture.Database.SubjectId);
        var command = Command(fixture);
        var accepted = await session.Handler.ExecuteAsync(command);
        var replay = await session.Handler.ExecuteAsync(command);
        await Assert.That(accepted.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        await using var inspect = Read(fixture);
        var response = await inspect.Set<ModerationContentResponse>().SingleAsync();
        await Assert.That(response.Id).IsEqualTo(command.OperationId);
        await Assert.That(response.RepresentativeHumanId).IsEqualTo(fixture.Database.SubjectId);
        await Assert.That(response.ContentSubjectId).IsNotEqualTo(response.RepresentativeHumanId);
        await Assert.That(response.RetainUntil).IsEqualTo(fixture.Approval.RetainUntil);
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationDecisions.SingleAsync()).SubjectId).IsEqualTo(response.ContentSubjectId);
        await Assert.That((await inspect.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(5L);
        await Assert.That((await inspect.ModerationFindings.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That((await inspect.ModerationSanctions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
        await Assert.That(await inspect.ModerationQualificationEvidence
            .AnyAsync(row => row.Id == command.OperationId)).IsFalse();
        await Assert.That((await session.Handler.ExecuteAsync(command with { Statement = "A different statement." })).IsSuccess)
            .IsFalse();
        await Assert.That(await inspect.Set<ModerationContentResponse>().CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("replacement")]
    [Arguments("version")]
    [Arguments("reviewer")]
    [Arguments("administrator")]
    [Arguments("human")]
    public async Task Historical_occurrence_requires_its_exact_original_Content_assignment(string change)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: change != "human");
        if (change is "replacement" or "version")
            await fixture.MutateAsync(async context =>
            {
                var assignment = await context.EventRoleAssignments.SingleAsync();
                if (change == "version")
                    assignment.UpdateValidityWindow(assignment.StartsAtUtc,
                        fixture.Clock.UtcNow.AddDays(2).UtcDateTime, fixture.Clock.UtcNow.UtcDateTime);
                else
                {
                    assignment.Revoke(fixture.Database.AdministratorId, fixture.Clock.UtcNow.UtcDateTime);
                    context.EventRoleAssignments.Add(EventRoleAssignment.Create(fixture.Database.TenantId,
                        assignment.EventId, assignment.UserId, (int)RoleEnum.EventManager,
                        EventRoleAssignmentStatus.Active, fixture.Clock.UtcNow.UtcDateTime,
                        fixture.Clock.UtcNow.AddDays(1).UtcDateTime, fixture.Database.AdministratorId));
                }
            });
        var caller = change switch
        {
            "reviewer" => fixture.Database.ReviewerId,
            "administrator" => fixture.Database.AdministratorId,
            _ => fixture.Database.SubjectId
        };
        await using var session = new ResponseSession(fixture, caller);
        var command = Command(fixture);
        await Assert.That((await session.Handler.ExecuteAsync(command)).IsSuccess).IsFalse();
        await using var inspect = Read(fixture);
        await Assert.That(await inspect.Set<ModerationContentResponse>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Retained_erasure_rejects_private_capture_before_a_primary_saga_exists()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await using (var primary = fixture.Database.Context())
            await fixture.Privacy.Authority(primary, fixture.Clock).AppendAsync(new(
                Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, fixture.Database.SubjectId,
                PrivacyErasureReasonCode.AccountDeletion, 1));
        await using var session = new ResponseSession(fixture, fixture.Database.SubjectId);
        await Assert.That((await session.Handler.ExecuteAsync(Command(fixture))).IsSuccess).IsFalse();
        await using var inspect = Read(fixture);
        await Assert.That(await inspect.PrivacyErasureSagas.AnyAsync()).IsFalse();
        await Assert.That(await inspect.Set<ModerationContentResponse>().AnyAsync()).IsFalse();
    }

    private static SubmitModerationContentResponseCommand Command(NoticeDatabase fixture)
    {
        var representative = fixture.Approval.Intent.ContentRepresentatives.SingleOrDefault();
        return new(fixture.SubjectNoticeId, Guid.CreateVersion7(), fixture.SnapshotHash,
            representative?.AssignmentId ?? Guid.CreateVersion7(), representative?.AssignmentRevision ?? 1,
            "A finite protected statement about the original Content decision.");
    }

    private static ExploreDbContext Read(NoticeDatabase fixture)
    {
        var context = fixture.Database.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        return context;
    }

    private sealed class ResponseSession : IAsyncDisposable
    {
        public ExploreDbContext Context { get; }
        public ModerationQualificationRepository Repository { get; }
        public SubmitModerationContentResponseCommandHandler Handler { get; }

        public ResponseSession(NoticeDatabase fixture, Guid caller,
            ExploreDbContext? context = null, IPrivacyIdentityFenceAuthority? privacy = null,
            IDataProtectionProvider? protection = null)
        {
            Context = context ?? fixture.Database.Context();
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, caller.ToString())], "response-native-witness"));
            var identity = new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(Context)),
                new OrganizationMemberRepository(Context),
                new GroupMemberRepository(Context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(Context), null), NullLogger<AdminContext>.Instance);
            Repository = new(Context, identity, new MachinePrincipalAccessor(http), null!,
                new ModerationNativeActivationQualification(Context, fixture.Database.Inventory,
                    null, new ModerationQualificationPolicySourceOwner(Context, fixture.Clock), fixture.Clock),
                fixture.Clock, privacy ?? fixture.Privacy.Authority(Context, fixture.Clock),
                responseProtection: protection ?? new EphemeralDataProtectionProvider());
            Handler = new(Repository, fixture.Database);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
