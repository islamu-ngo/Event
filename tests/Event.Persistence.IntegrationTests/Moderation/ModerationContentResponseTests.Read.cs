using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Responses;
using Explore.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NoticeDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationNoticeReadTests.NoticeDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentResponseTests
{
    [Test]
    public async Task Independent_current_reviewer_gets_the_original_statement_once_under_custody_and_its_buffer_is_zeroed()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var keys = new ObservedResponseProtection(new EphemeralDataProtectionProvider());
        var command = Command(fixture);
        await using (var submit = new ResponseSession(fixture, fixture.Database.SubjectId, protection: keys))
            await Assert.That((await submit.Handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        await using var reviewer = new ResponseSession(fixture, fixture.Database.ReviewerId, protection: keys);
        var query = new GetModerationContentResponseStatementQueryHandler(reviewer.Repository, fixture.Database);
        var pending = await query.QueryAsync(new(command.OperationId));
        string? disclosed = null;
        var outcome = await pending.DiscloseAsync(async (bytes, _) =>
        {
            await Assert.That(reviewer.Context.Database.CurrentTransaction).IsNotNull();
            disclosed = Encoding.UTF8.GetString(bytes.Span);
        }, default);
        await Assert.That(outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(disclosed).IsEqualTo(command.Statement);
        await Assert.That(keys.Plaintext!.All(value => value == 0)).IsTrue();
        await Assert.That(await pending.DiscloseAsync((_, _) => throw new InvalidOperationException(), default))
            .IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That(reviewer.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("administrator")]
    [Arguments("subject")]
    [Arguments("withdrawn")]
    [Arguments("role")]
    [Arguments("unresolved")]
    [Arguments("erasure")]
    [Arguments("retention")]
    [Arguments("tenant")]
    public async Task Current_reviewer_and_original_custody_are_required_before_private_decryption(string denial)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var keys = new ObservedResponseProtection(new EphemeralDataProtectionProvider());
        var command = Command(fixture);
        await using (var submit = new ResponseSession(fixture, fixture.Database.SubjectId, protection: keys))
            await Assert.That((await submit.Handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        if (denial is "withdrawn" or "role" or "unresolved")
            await fixture.MutateAsync(async context =>
            {
                if (denial == "role")
                {
                    await context.TenantUserRoleGrants
                        .Where(row => row.TenantUser.UserId == fixture.Database.ReviewerId).ExecuteDeleteAsync();
                    return;
                }
                var assignment = await context.ModerationReviewerAssignments
                    .SingleAsync(row => row.ReviewerHumanId == fixture.Database.ReviewerId);
                if (denial == "withdrawn")
                    assignment.Withdraw(assignment.Revision, fixture.Database.AdministratorId, fixture.Clock.UtcNow);
                else
                    assignment.QualifyController(assignment.Revision, assignment.ControllerHumanId!.Value,
                        Guid.CreateVersion7(), fixture.Database.AdministratorId, assignment.QualifiedUntil!.Value,
                        fixture.Clock.UtcNow);
            });
        if (denial == "erasure")
        {
            await using var primary = fixture.Database.Context();
            await fixture.Privacy.Authority(primary, fixture.Clock).AppendAsync(new(
                Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, fixture.Database.ReviewerId,
                PrivacyErasureReasonCode.AccountDeletion, 1));
        }
        if (denial == "retention") fixture.Clock.UtcNow = fixture.Approval.RetainUntil;
        var caller = denial switch
        {
            "administrator" => fixture.Database.AdministratorId,
            "subject" => fixture.Database.SubjectId,
            _ => fixture.Database.ReviewerId
        };
        await using var reviewer = new ResponseSession(fixture, caller, protection: keys);
        var wrote = false;
        var outcome = await reviewer.Repository.DiscloseContentResponseStatementAsync(
            denial == "tenant" ? Guid.CreateVersion7() : fixture.Database.TenantId,
            command.OperationId, (_, _) => { wrote = true; return Task.CompletedTask; }, default);
        await Assert.That(outcome == ModerationQualificationWriteOutcome.Known).IsFalse();
        await Assert.That(wrote).IsFalse();
        await Assert.That(keys.Plaintext).IsNull();
    }

    [Test]
    public async Task Original_deadline_expiring_at_decryption_cannot_release_response_text()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var keys = new ObservedResponseProtection(new EphemeralDataProtectionProvider(),
            () => fixture.Clock.UtcNow = fixture.Approval.RetainUntil);
        var command = Command(fixture);
        await using (var submit = new ResponseSession(fixture, fixture.Database.SubjectId, protection: keys))
            await Assert.That((await submit.Handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        await using var reviewer = new ResponseSession(fixture, fixture.Database.ReviewerId, protection: keys);
        var wrote = false;
        var result = await reviewer.Repository.DiscloseContentResponseStatementAsync(fixture.Database.TenantId,
            command.OperationId, (_, _) => { wrote = true; return Task.CompletedTask; }, default);
        await Assert.That(result).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        await Assert.That(wrote).IsFalse();
        await Assert.That(keys.Plaintext!.All(value => value == 0)).IsTrue();
    }

    [Test]
    public async Task Transport_failure_is_not_retried_or_swallowed_and_zeroes_private_response_text()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var keys = new ObservedResponseProtection(new EphemeralDataProtectionProvider());
        var command = Command(fixture);
        await using (var submit = new ResponseSession(fixture, fixture.Database.SubjectId, protection: keys))
            await Assert.That((await submit.Handler.ExecuteAsync(command)).IsSuccess).IsTrue();
        await using var reviewer = new ResponseSession(fixture, fixture.Database.ReviewerId, protection: keys);
        var writes = 0;
        await Assert.ThrowsAsync<CryptographicException>(async () =>
            await reviewer.Repository.DiscloseContentResponseStatementAsync(fixture.Database.TenantId,
                command.OperationId, (_, _) =>
                {
                    writes++;
                    throw new CryptographicException("Transport failed after disclosure began.");
                }, default));
        await Assert.That(writes).IsEqualTo(1);
        await Assert.That(keys.Plaintext!.All(value => value == 0)).IsTrue();
        await Assert.That(reviewer.Context.Database.CurrentTransaction).IsNull();
    }

    private sealed class ObservedResponseProtection(IDataProtectionProvider inner, Action? afterUnprotect = null)
        : IDataProtectionProvider
    {
        private Action? AfterUnprotect { get; } = afterUnprotect;
        public byte[]? Plaintext { get; private set; }
        public IDataProtector CreateProtector(string purpose) => new Protector(inner.CreateProtector(purpose), this);
        private sealed class Protector(IDataProtector actual, ObservedResponseProtection observer) : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) => new Protector(actual.CreateProtector(purpose), observer);
            public byte[] Protect(byte[] plaintext) => actual.Protect(plaintext);
            public byte[] Unprotect(byte[] protectedData)
            {
                var plaintext = actual.Unprotect(protectedData);
                observer.Plaintext = plaintext;
                observer.AfterUnprotect?.Invoke();
                return plaintext;
            }
        }
    }
}
