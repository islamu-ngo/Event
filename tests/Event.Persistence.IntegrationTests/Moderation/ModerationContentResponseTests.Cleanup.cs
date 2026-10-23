using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoticeDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationNoticeReadTests.NoticeDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentResponseTests
{
    [Test]
    public async Task Original_source_cleanup_physically_deletes_response_ciphertext_and_its_receipt_at_R()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var request = Command(fixture);
        await using (var response = new ResponseSession(fixture, fixture.Database.SubjectId))
            await Assert.That((await response.Handler.ExecuteAsync(request)).IsSuccess).IsTrue();
        var keyId = ApiKeyHashing.CreateKeyId();
        await using (var setup = fixture.Database.Context())
        {
            var host = await setup.ModerationHostBindings.SingleAsync();
            setup.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(), KeyId = keyId, Name = "Content response purpose cleanup",
                SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()), Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin, OwnerId = fixture.Database.AdministratorId,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active, ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None, ExternalApiKeyCreditPeriod = null!
            });
            setup.ModerationMachineSubjectBindings.Add(ModerationMachineSubjectBinding.Establish(
                Guid.CreateVersion7(), host.Id, ModerationMachineCredentialSource.Persisted, keyId, null,
                ExternalApiKeyOwnerType.InstanceAdmin, fixture.Database.AdministratorId, host.MaintenanceServicePrincipalId));
            await setup.SaveChangesAsync();
        }
        fixture.Clock.UtcNow = fixture.Approval.RetainUntil;
        await using (var context = fixture.Database.Context())
        {
            var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
            machine.SetPrincipal(new ApiKeyPrincipalContext(keyId, null, ExternalApiKeyOwnerType.InstanceAdmin,
                fixture.Database.AdministratorId, ["events:read"]) { CredentialSource = ApiKeyCredentialSource.Persisted });
            var admin = new AdminContext(new HttpContextAccessor(), new PlatformUserRoleRepository(context),
                new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var keys = new ExternalApiKeyRepository(context);
            var bindings = new ModerationMachineSubjectBindingRepository(context, admin, machine, keys,
                new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), fixture.Clock);
            var cleanup = await new ModerationNoticeCleanupRepository(context, machine, bindings, keys, fixture.Clock)
                .PruneAsync(null, 10);
            await Assert.That(cleanup.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
            await Assert.That(cleanup.Purged).IsEqualTo(1);
            // This fixture deliberately has no original operation receipt. Deleting its
            // response must not launder that incomplete original write into completion proof.
            await Assert.That(cleanup.OriginalWritesProven).IsEqualTo(0);
        }
        await using var inspect = Read(fixture);
        await Assert.That(await inspect.Set<ModerationContentResponse>().AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationNoticeSources.AnyAsync()).IsFalse();
        await Assert.That(await inspect.ModerationNotices.AnyAsync()).IsFalse();
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
        await using var retry = new ResponseSession(fixture, fixture.Database.SubjectId);
        await Assert.That((await retry.Handler.ExecuteAsync(request)).IsSuccess).IsFalse();
    }
}
