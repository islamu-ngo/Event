using System.Collections.Immutable;
using System.Data.Common;
using System.Text;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.ConfigurationManifest;
using Explore.Persistence.Entities;
using Explore.Persistence;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class HumanImportSessionOwnershipTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicSessionRetainsCallerInputsBeforeOwnedStorage(bool preview)
    {
        var mutation = new SessionInputMutation();
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync(mutation);
        fixture.Context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        if (!await fixture.Context.PaidEventPolicyVersions.AnyAsync())
            fixture.Context.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
        await fixture.Context.SaveChangesAsync();
        var executor = fixture.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
        var snapshot = await executor.ReadSnapshotAsync(ConfigurationManifestExportView.Portable, CancellationToken.None);
        byte[] artifact = snapshot.Utf8Json.ToArray();
        string originalDigest = ConfigurationImportDigest.ComputeBytes(artifact);
        if (!preview)
        {
            mutation.Arm(() => Array.Fill(artifact, (byte)'X'));
            var created = await executor.CreateAsync(artifact, CancellationToken.None);
            await using var observer = fixture.CreateIndependentContext();
            var session = await observer.Set<ConfigurationImportSession>()
                .SingleAsync(row => row.SessionId == created.SessionId);
            await Assert.That(session.ArtifactDigest).IsEqualTo(originalDigest);
        }
        else
        {
            var created = await executor.CreateAsync(artifact, CancellationToken.None);
            var sections = new List<string> { "instance.settings" };
            var mappings = new Dictionary<string, string>();
            var approvals = new List<string>();
            mutation.Arm(() =>
            {
                sections[0] = "unsupported.section";
                mappings["caller-replaced"] = "unsupported.target";
                approvals.Add("caller-replaced");
            });
            var result = await executor.PreviewAsync(created.SessionId, created.AccessToken,
                new ConfigurationImportPreviewRequest
                {
                    SelectedSectionKeys = sections,
                    Mappings = mappings,
                    ApplyMode = ConfigurationImportApplyMode.ApplySelected,
                    GrantedApprovalCodes = approvals
                }, CancellationToken.None);
            await Assert.That(result.IsApplyReady).IsTrue();
            await using var observer = fixture.CreateIndependentContext();
            await Assert.That(await observer.Set<ConfigurationImportSession>()
                .Where(row => row.SessionId == created.SessionId).Select(row => row.State).SingleAsync())
                .IsEqualTo(ConfigurationImportSessionState.PreviewReady);
        }
        await Assert.That(mutation.Triggered).IsTrue();
    }

    [Test]
    public async Task PublicInstanceSessionCreatePreviewAndCancelSettleBeforeReturning()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        fixture.Context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        if (!await fixture.Context.PaidEventPolicyVersions.AnyAsync())
            fixture.Context.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
        await fixture.Context.SaveChangesAsync();
        var executor = fixture.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
        var snapshot = await executor.ReadSnapshotAsync(ConfigurationManifestExportView.Portable, CancellationToken.None);
        var created = await executor.CreateAsync(snapshot.Utf8Json, CancellationToken.None);
        var before = await MutationCounts.ReadAsync(fixture.Context);
        var preview = await executor.PreviewAsync(created.SessionId, created.AccessToken,
            new ConfigurationImportPreviewRequest
            {
                SelectedSectionKeys = ["instance.settings"],
                Mappings = new Dictionary<string, string>(),
                ApplyMode = ConfigurationImportApplyMode.ApplySelected,
                GrantedApprovalCodes = []
            }, CancellationToken.None);
        await Assert.That(preview.IsApplyReady).IsTrue();
        await Assert.That(await MutationCounts.ReadAsync(fixture.Context)).IsEqualTo(before);
        await using (var observer = fixture.CreateIndependentContext())
            await Assert.That(await observer.Set<ConfigurationImportSession>()
                .Where(row => row.SessionId == created.SessionId).Select(row => row.State).SingleAsync())
                .IsEqualTo(ConfigurationImportSessionState.PreviewReady);
        await executor.CancelAsync(created.SessionId, created.AccessToken, CancellationToken.None);
        await using var cancelled = fixture.CreateIndependentContext();
        await Assert.That(await cancelled.Set<ConfigurationImportSession>()
            .Where(row => row.SessionId == created.SessionId).Select(row => row.State).SingleAsync())
            .IsEqualTo(ConfigurationImportSessionState.Cancelled);
        await Assert.That(await cancelled.Set<ConfigurationImportStoredArtifact>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task SetupLiveKeepsItsSeparateEnrollmentBoundTransactionPath()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        var manager = fixture.Services.GetRequiredService<ConfigurationImportSessionManager>();
        var target = ConfigurationImportTarget.ForSetupLiveTenant(
            fixture.TenantId, Guid.CreateVersion7(), 1, fixture.UserId);
        DateTime now = fixture.Clock.GetUtcNow().UtcDateTime;
        var created = await manager.CreateSetupLiveAsync(target, Encoding.UTF8.GetBytes("{}"),
            now, TimeSpan.FromMinutes(20), CancellationToken.None);
        var source = await manager.ReadSetupLiveArtifactAsync(
            created.Session.SessionId, target, created.AccessToken, now, CancellationToken.None);
        await Assert.That(Encoding.UTF8.GetString(source.Bytes.Span)).IsEqualTo("{}");
        var input = new ConfigurationImportPreviewInput(target, source.Digest,
            ConfigurationImportDigest.Compute(["setup-target"]),
            [new ConfigurationImportSectionSnapshot("tenant.settings",
                ConfigurationImportDigest.Compute(["setup-source"]), ConfigurationPortabilityClass.Portable,
                supportsPreview: true, supportsDiff: true, requiresExternalSetup: false)],
            [], ["tenant.settings"], [], ConfigurationImportApplyMode.ApplySelected, [], [], source.ExpiresAt);
        var preview = await manager.PrepareSetupLivePreviewAsync(created.Session.SessionId, target,
            created.AccessToken, input, now, CancellationToken.None);
        await Assert.That(preview.IsApplyReady).IsTrue();
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateSetupLiveAsync(
            ConfigurationImportTarget.ForTenant(fixture.TenantId), Encoding.UTF8.GetBytes("{}"),
            now, TimeSpan.FromMinutes(20), CancellationToken.None));
        await Assert.That(fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject).IsNull();
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        var persisted = await observer.Set<ConfigurationImportSession>().AsNoTracking().SingleAsync();
        await Assert.That(persisted.TargetAuthorityKey).IsEqualTo(target.GetSessionStorageKey());
        await Assert.That(persisted.State).IsEqualTo(ConfigurationImportSessionState.PreviewReady);
        await Assert.That(await observer.Set<ConfigurationImportStoredArtifact>().CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GenericNestedTransactionsStillRefuseWithoutExecutingTheirBody(bool serializable)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        var unitOfWork = fixture.Services.GetRequiredService<IUnitOfWork>();
        bool entered = false;
        Task<bool> Body(CancellationToken _)
        {
            entered = true;
            return Task.FromResult(true);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => serializable
            ? unitOfWork.ExecuteSerializableAsync(Body, CancellationToken.None)
            : unitOfWork.ExecuteInTransactionAsync(Body, CancellationToken.None));
        await Assert.That(entered).IsFalse();
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task SessionHandleEndsBeforeCommitFinalizersCanReuseIt()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        var services = fixture.Services;
        var issuer = services.GetRequiredService<IHumanConfigurationImportAdmission>();
        var manager = services.GetRequiredService<ConfigurationImportSessionManager>();
        var target = ConfigurationImportTarget.ForTenant(fixture.TenantId);
        bool finalizerReached = false;
        var attempt = issuer.ExecuteObservationAsync(new HumanConfigurationImportObservation<bool>(
            services.GetRequiredService<IHumanCommitScopePreparation>().Subject!, target,
            HumanConfigurationImportObservationAccess.Update, async (owner, token) =>
            {
                var created = await manager.CreateAsync(owner, target, Encoding.UTF8.GetBytes("{}"),
                    fixture.Clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(20), token);
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    finalizerReached = true;
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.CancelAsync(
                        owner, created.Session.SessionId, target, created.AccessToken,
                        fixture.Clock.GetUtcNow().UtcDateTime, finalToken));
                }, _ => { });
                return true;
            }), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => attempt);
        await Assert.That(finalizerReached).IsTrue();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.Set<ConfigurationImportSession>().AnyAsync()).IsFalse();
        await Assert.That(await observer.Set<ConfigurationImportStoredArtifact>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ActiveHandleCannotOutliveItsAcquiredPhysicalTransaction()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        var services = fixture.Services;
        var target = ConfigurationImportTarget.ForTenant(fixture.TenantId);
        bool staleAttemptRejected = false;
        var attempt = services.GetRequiredService<IHumanConfigurationImportAdmission>().ExecuteObservationAsync(
            new HumanConfigurationImportObservation<bool>(
                services.GetRequiredService<IHumanCommitScopePreparation>().Subject!, target,
                HumanConfigurationImportObservationAccess.Update, async (owner, token) =>
                {
                    await fixture.Context.Database.CurrentTransaction!.DisposeAsync();
                    await Assert.ThrowsAsync<InvalidOperationException>(() => services
                        .GetRequiredService<ConfigurationImportSessionManager>().CreateAsync(
                            owner, target, Encoding.UTF8.GetBytes("{}"), fixture.Clock.GetUtcNow().UtcDateTime,
                            TimeSpan.FromMinutes(20), token));
                    staleAttemptRejected = true;
                    return true;
                }), CancellationToken.None);
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() => attempt);
        await Assert.That(staleAttemptRejected).IsTrue();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.Set<ConfigurationImportSession>().AnyAsync()).IsFalse();
        await Assert.That(await observer.Set<ConfigurationImportStoredArtifact>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task RollbackSessionPersistsSnapshotCopyUnderOriginalObservationOwner()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        var target = ConfigurationImportTarget.ForTenant(fixture.TenantId);
        DateTime now = fixture.Clock.GetUtcNow().UtcDateTime;
        var artifacts = fixture.Services.GetRequiredService<IConfigurationImportArtifactStore>();
        var snapshot = await artifacts.StoreAsync(new(Guid.CreateVersion7()), Encoding.UTF8.GetBytes("{}"),
            now, now.AddDays(1), CancellationToken.None);
        string digest = ConfigurationImportDigest.Compute(["rollback-source"]);
        var source = ConfigurationImportOperation.CreateApplied(
            Guid.CreateVersion7(), Guid.CreateVersion7(), target.AuthorityKey, target.TenantId, fixture.UserId,
            null, digest, digest, digest, digest, digest, (int)ConfigurationImportApplyMode.ApplySelected,
            ["tenant.settings"], snapshot.Handle.Id, snapshot.Sha256Digest, snapshot.ExpiresAt,
            Guid.CreateVersion7(), true, digest, [], now, now);
        await fixture.Services.GetRequiredService<IConfigurationImportOperationRepository>()
            .AddAsync(source, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.Services.GetRequiredService<ConfigurationImportApplyService>()
            .CreateRollbackSessionAsync(source.Id, target, CancellationToken.None);

        await Assert.That(result.SourceOperationId).IsEqualTo(source.Id);
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        var persisted = await observer.Set<ConfigurationImportSession>().AsNoTracking()
            .SingleAsync(row => row.SessionId == result.Session.SessionId);
        await Assert.That(persisted.Artifact.Handle.Id).IsNotEqualTo(snapshot.Handle.Id);
        await Assert.That(persisted.ArtifactDigest).IsEqualTo(snapshot.Sha256Digest);
        await Assert.That(persisted.HasAccess(target,
            ConfigurationImportSessionManager.DigestToken(result.Session.AccessToken[37..]))).IsTrue();
        await Assert.That(result.Session.AccessToken[..37]).IsEqualTo($"{source.Id:D}.");
        await Assert.That(Encoding.UTF8.GetString((await artifacts.ReadAsync(
            persisted.Artifact.Handle, CancellationToken.None)).Span)).IsEqualTo("{}");
        await Assert.That(await observer.Set<ConfigurationImportStoredArtifact>().CountAsync()).IsEqualTo(2);
        await Assert.That(await observer.ConfigurationImportOperations.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("cancel")]
    [Arguments("expire")]
    public async Task ManagerDeletesProtectedBytesAndRetainsSessionEvidence(string action)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        var services = fixture.Services;
        var human = services.GetRequiredService<IHumanCommitScopePreparation>();
        var issuer = services.GetRequiredService<IHumanConfigurationImportAdmission>();
        var manager = services.GetRequiredService<ConfigurationImportSessionManager>();
        var target = ConfigurationImportTarget.ForTenant(fixture.TenantId);
        DateTime now = fixture.Clock.GetUtcNow().UtcDateTime;
        var created = await issuer.ExecuteObservationAsync(new HumanConfigurationImportObservation<ConfigurationImportSessionCreated>(
            human.Subject!, target, HumanConfigurationImportObservationAccess.Update,
            (owner, token) => manager.CreateAsync(owner, target, Encoding.UTF8.GetBytes("{}"),
                now, TimeSpan.FromMinutes(5), token)), CancellationToken.None);
        await Assert.That(created.ToString()).DoesNotContain(created.AccessToken);
        if (action == "cancel")
            await services.GetRequiredService<ConfigurationImportSessionApplicationService>()
                .CancelTenantAsync(fixture.TenantId, created.Session.SessionId, created.AccessToken, CancellationToken.None);
        else
            await Assert.That(await manager.ExpireAsync(now.AddMinutes(6), 10, CancellationToken.None)).IsEqualTo(1);
        var missing = await Assert.That(async () =>
            await services.GetRequiredService<IConfigurationImportArtifactStore>()
                .ReadAsync(created.Session.Artifact.Handle, CancellationToken.None))
            .Throws<ConfigurationImportSessionException>();
        await Assert.That(missing?.FailureCode).IsEqualTo(ConfigurationImportFailureCodes.ArtifactMissing);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.Set<ConfigurationImportSession>()
            .Where(row => row.SessionId == created.Session.SessionId).Select(row => row.State).SingleAsync())
            .IsEqualTo(action == "cancel" ? ConfigurationImportSessionState.Cancelled : ConfigurationImportSessionState.Expired);
        await Assert.That(await observer.Set<ConfigurationImportStoredArtifact>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task ManagerPreviewMutatesOnlySessionMetadataUnderItsObservation()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        var services = fixture.Services;
        var human = services.GetRequiredService<IHumanCommitScopePreparation>();
        var issuer = services.GetRequiredService<IHumanConfigurationImportAdmission>();
        var manager = services.GetRequiredService<ConfigurationImportSessionManager>();
        var target = ConfigurationImportTarget.ForTenant(fixture.TenantId);
        DateTime now = fixture.Clock.GetUtcNow().UtcDateTime;
        var created = await issuer.ExecuteObservationAsync(new HumanConfigurationImportObservation<ConfigurationImportSessionCreated>(
            human.Subject!, target, HumanConfigurationImportObservationAccess.Update,
            (owner, token) => manager.CreateAsync(owner, target, Encoding.UTF8.GetBytes("{}"),
                now, TimeSpan.FromMinutes(20), token)), CancellationToken.None);
        var before = await MutationCounts.ReadAsync(fixture.Context);
        var input = new ConfigurationImportPreviewInput(target, created.Session.ArtifactDigest,
            ConfigurationImportDigest.Compute(["target-revision"]),
            [new ConfigurationImportSectionSnapshot("tenant.settings",
                ConfigurationImportDigest.Compute(["source-settings"]), ConfigurationPortabilityClass.Portable,
                supportsPreview: true, supportsDiff: true, requiresExternalSetup: false)],
            [], ["tenant.settings"], [], ConfigurationImportApplyMode.ApplySelected, [], [], now.AddMinutes(15));
        var preview = await issuer.ExecuteObservationAsync(new HumanConfigurationImportObservation<ConfigurationImportPreview>(
            human.Subject!, target, HumanConfigurationImportObservationAccess.Update,
            (owner, token) => manager.PreparePreviewAsync(owner, created.Session.SessionId, target,
                created.AccessToken, input, now.AddMinutes(1), token)), CancellationToken.None);
        await Assert.That(preview.Items.Select(item => item.SectionKey)).Contains("tenant.settings");
        await Assert.That(await MutationCounts.ReadAsync(fixture.Context)).IsEqualTo(before);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.Set<ConfigurationImportSession>()
            .Where(row => row.SessionId == created.Session.SessionId).Select(row => row.State).SingleAsync())
            .IsEqualTo(ConfigurationImportSessionState.PreviewReady);
    }

    [Test]
    [Arguments("commit")]
    [Arguments("revoked-after-body")]
    [Arguments("expired-after-body")]
    [Arguments("caught-storage-failure")]
    public async Task SessionAndArtifactShareOriginalSettlementDespiteCaughtStorageFailure(string outcome)
    {
        var failure = new SessionStorageFailure();
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync(failure);
        Guid grantId = await GrantTenantAsync(fixture);
        var services = fixture.Services;
        var human = services.GetRequiredService<IHumanCommitScopePreparation>();
        var issuer = services.GetRequiredService<IHumanConfigurationImportAdmission>();
        var manager = services.GetRequiredService<ConfigurationImportSessionManager>();
        var target = ConfigurationImportTarget.ForTenant(fixture.TenantId);
        failure.Enabled = outcome == "caught-storage-failure";
        bool bodyFinished = false;
        var observation = new HumanConfigurationImportObservation<ConfigurationImportSessionCreated?>(
            human.Subject!, target, HumanConfigurationImportObservationAccess.Update, async (owner, token) =>
            {
                ConfigurationImportSessionCreated? created = null;
                try
                {
                    created = await manager.CreateAsync(owner, target, Encoding.UTF8.GetBytes("{}"),
                        fixture.Clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(20), token);
                }
                catch (InvalidOperationException) when (outcome == "caught-storage-failure")
                {
                    // The artifact has been saved; catching the session write failure must not commit it.
                }
                bodyFinished = true;
                if (outcome == "revoked-after-body")
                    await fixture.Context.TenantUserRoleGrants.Where(row => row.Id == grantId).ExecuteDeleteAsync(token);
                if (outcome == "expired-after-body")
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                    {
                        fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew)
                            .AddTicks(1) - fixture.Clock.GetUtcNow());
                        return Task.CompletedTask;
                    }, _ => { });
                return created;
            });
        var attempt = issuer.ExecuteObservationAsync(observation, CancellationToken.None);
        if (outcome == "commit")
        {
            var created = await attempt;
            await Assert.That(created).IsNotNull();
            await Assert.That(created!.ToString()).DoesNotContain(created.AccessToken);
            await Assert.That(Encoding.UTF8.GetString((await services
                .GetRequiredService<IConfigurationImportArtifactStore>()
                .ReadAsync(created.Session.Artifact.Handle, CancellationToken.None)).Span)).IsEqualTo("{}");
        }
        else if (outcome == "caught-storage-failure")
            await Assert.ThrowsAsync<InvalidOperationException>(() => attempt);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => attempt);
        await Assert.That(bodyFinished).IsTrue();
        await Assert.That(failure.Triggered).IsEqualTo(outcome == "caught-storage-failure");
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.Set<ConfigurationImportSession>().CountAsync())
            .IsEqualTo(outcome == "commit" ? 1 : 0);
        await Assert.That(await observer.Set<ConfigurationImportStoredArtifact>().CountAsync())
            .IsEqualTo(outcome == "commit" ? 1 : 0);
        await Assert.That(await observer.TenantUserRoleGrants.AnyAsync(row => row.Id == grantId)).IsTrue();
    }

    [Test]
    [Arguments("counterfeit")]
    [Arguments("foreign")]
    [Arguments("wrong-target")]
    [Arguments("read-only")]
    public async Task InvalidSessionParticipationCannotPersistEvenWhenCaught(string misuse)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        var services = fixture.Services;
        var human = services.GetRequiredService<IHumanCommitScopePreparation>();
        var issuer = services.GetRequiredService<IHumanConfigurationImportAdmission>();
        var foreign = ActivatorUtilities.CreateInstance<HumanConfigurationImportAdmission>(services);
        var manager = misuse == "foreign"
            ? ActivatorUtilities.CreateInstance<ConfigurationImportSessionManager>(services, foreign)
            : services.GetRequiredService<ConfigurationImportSessionManager>();
        var target = ConfigurationImportTarget.ForTenant(fixture.TenantId);
        var observation = new HumanConfigurationImportObservation<bool>(human.Subject!, target,
            misuse == "read-only" ? HumanConfigurationImportObservationAccess.Read
                : HumanConfigurationImportObservationAccess.Update, async (owner, token) =>
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.CreateAsync(
                    misuse == "counterfeit" ? new Counterfeit(owner.Authority) : owner,
                    misuse == "wrong-target" ? ConfigurationImportTarget.ForInstance() : target,
                    Encoding.UTF8.GetBytes("{}"), fixture.Clock.GetUtcNow().UtcDateTime,
                    TimeSpan.FromMinutes(20), token));
                return true;
            });
        // A misuse of the genuine issuer's active handle poisons that owner even if caught.
        var attempt = issuer.ExecuteObservationAsync(observation, CancellationToken.None);
        if (misuse is "wrong-target" or "read-only")
            await Assert.ThrowsAsync<InvalidOperationException>(() => attempt);
        else
            await Assert.That(await attempt).IsTrue();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.Set<ConfigurationImportSession>().AnyAsync()).IsFalse();
        await Assert.That(await observer.Set<ConfigurationImportStoredArtifact>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task AcquiredNonCallbackAndEndedCallbackHandlesCannotJoinAnotherTransaction()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantTenantAsync(fixture);
        var services = fixture.Services;
        var human = services.GetRequiredService<IHumanCommitScopePreparation>();
        var issuer = services.GetRequiredService<IHumanConfigurationImportAdmission>();
        var manager = services.GetRequiredService<ConfigurationImportSessionManager>();
        var target = ConfigurationImportTarget.ForTenant(fixture.TenantId);
        var inactive = await issuer.PrepareAsync(human.Subject!, target, null, CancellationToken.None);
        Task<ConfigurationImportSessionCreated> Create(IPreparedHumanConfigurationImport handle) =>
            manager.CreateAsync(handle, target, Encoding.UTF8.GetBytes("{}"), fixture.Clock.GetUtcNow().UtcDateTime,
                TimeSpan.FromMinutes(20), CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(inactive));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                await inactive.AcquireAsync(token);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(inactive));
                return true;
            }));
        IPreparedHumanConfigurationImport? retained = null;
        await issuer.ExecuteObservationAsync(new HumanConfigurationImportObservation<bool>(
            human.Subject!, target, HumanConfigurationImportObservationAccess.Update, (owner, _) =>
            {
                retained = owner;
                return Task.FromResult(true);
            }), CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(retained!));
        await issuer.ExecuteObservationAsync(new HumanConfigurationImportObservation<bool>(
            human.Subject!, target, HumanConfigurationImportObservationAccess.Update, async (_, _) =>
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(retained!));
                return true;
            }), CancellationToken.None);
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.Set<ConfigurationImportSession>().AnyAsync()).IsFalse();
        await Assert.That(await observer.Set<ConfigurationImportStoredArtifact>().AnyAsync()).IsFalse();
    }

    private static async Task<Guid> GrantTenantAsync(ResourceVisitorAuthenticatedFixture fixture)
    {
        var member = await fixture.Context.TenantUsers.SingleAsync(
            row => row.TenantId == fixture.TenantId && row.UserId == fixture.UserId);
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        fixture.Context.TenantUserRoleGrants.Add(grant);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        return grant.Id;
    }

    private sealed class Counterfeit(HumanConfigurationImportAuthority authority) : IPreparedHumanConfigurationImport
    {
        public HumanConfigurationImportAuthority Authority => authority;
        public Task AcquireAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImmutableArray<SettingChangedNotification>> ExecuteApplyAsync(
            HumanConfigurationImportApply apply, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A copied authority must never execute.");
    }

    private sealed class SessionInputMutation : DbCommandInterceptor
    {
        private Action? mutate;
        internal bool Triggered { get; private set; }
        internal void Arm(Action action) => mutate = action;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            string table = eventData.Context!.Model.FindEntityType(typeof(ConfigurationImportSession))!.GetTableName()!;
            if (mutate is not null && command.CommandText.Contains($"\"{table}\"", StringComparison.Ordinal))
            {
                var action = mutate;
                mutate = null;
                Triggered = true;
                action();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SessionStorageFailure : SaveChangesInterceptor
    {
        internal bool Enabled { get; set; }
        internal bool Triggered { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<ConfigurationImportSession>()
                .Any(entry => entry.State == EntityState.Added))
            {
                Triggered = true;
                throw new InvalidOperationException("Injected session storage failure after artifact save.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed record MutationCounts(
        int Tenants, int SystemSettings, int TenantSettings, int SettingsDocuments, int SuccessOperations, int OutboxMessages)
    {
        internal static async Task<MutationCounts> ReadAsync(ExploreDbContext context) =>
            new(await context.Tenants.CountAsync(), await context.SystemSettings.CountAsync(),
                await context.TenantSettingOverrides.CountAsync(), await context.TenantSettingsDocuments.CountAsync(),
                await context.ConfigurationManifestOperations.CountAsync(), await context.OutboxMessages.CountAsync());
    }
}
