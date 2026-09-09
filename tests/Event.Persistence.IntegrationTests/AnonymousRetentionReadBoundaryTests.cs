using System.Data.Common;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Admissions;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Features.StorageObjects.Handlers.Commands;
using Explore.Application.Responses;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Application.Services.Registration;
using Explore.Application.Telemetry;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class AnonymousRetentionReadBoundaryTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task HistoricalAnonymousParticipantWithoutOriginalBoundHidesHeldContactButKeepsMutationAuthority()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var order = await SeedHistoricalOrderAsync(fixture);
        var participant = RegistrationParticipant.Create(fixture.TenantId, order.Id, null, ParticipantTypeEnum.Adult, null);
        participant.SetPii(RegistrationParticipantPii.Create(participant.Id, fixture.TenantId,
            "Held attendee", "held@example.test", "+32000000001", (int)RegistrationRetentionPolicyEnum.LegalHold, Now));
        fixture.Context.Add(participant);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var result = await fixture.ExecuteAsync<GetRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?>(new(order.Id));

        await Assert.That(result).IsNotNull();
        var disclosed = result!.Participants.Single();
        await Assert.That(disclosed.DisplayName).IsNull();
        await Assert.That(disclosed.Email).IsNull();
        await Assert.That(disclosed.Phone).IsNull();
        var mutable = await fixture.Services.GetRequiredService<IRegistrationParticipantRepository>()
            .GetParticipantForUpdateAsync(participant.Id, order.Id, fixture.TenantId, CancellationToken.None);
        await Assert.That(mutable!.Pii!.DisplayName).IsEqualTo("Held attendee");
        await Assert.That(await fixture.Context.RegistrationParticipantPii.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task RegistrationCsvWithMissingSubmissionCannotDiscloseProviderBytes()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var storage = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!,
            Uri = "unused",
            ObjectKey = "missing.csv",
            Provider = StorageProviders.Local,
            FullName = "private.csv",
            SafeDisplayName = "private.csv",
            Extension = ".csv",
            ContentType = "text/csv",
            Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            Purpose = StorageObjectPurposes.Document,
            LifecycleState = StorageObjectLifecycleStates.Active,
            OwningResourceKind = "registration_submission_sink",
            OwningResourceId = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        fixture.Context.Add(storage);
        await fixture.Context.SaveChangesAsync();
        var provider = fixture.Services.GetRequiredService<IFileStorageProviderResolver>().GetRequired(StorageProviders.Local);
        await using var content = new MemoryStream("private answer"u8.ToArray());
        var written = await provider.WriteAsync(new(fixture.TenantId, content, "text/csv", "private.csv", ".csv",
            content.Length, 1024, $"tenants/{fixture.TenantId:N}/retention-tests/{storage.Id:N}.csv"), CancellationToken.None);
        storage.ObjectKey = written.ObjectKey;
        await fixture.Context.SaveChangesAsync();
        try
        {
            var opened = await fixture.Services.GetRequiredService<IStorageObjectContentReader>()
                .OpenAsync(storage.Id, false, CancellationToken.None);
            if (opened is not null) await opened.Content.DisposeAsync();
            await Assert.That(opened).IsNull();
        }
        finally
        {
            await provider.DeleteAsync(new(written.ObjectKey), CancellationToken.None);
        }
    }

    [Test]
    [Arguments(-1, true)]
    [Arguments(0, false)]
    [Arguments(1, false)]
    public async Task ExactExpiryStopsHeldParticipantTicketContactAnalyticsAndCsvBeforeCleanup(long ticks, bool allowed)
    {
        var clock = new Clock();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var scope = await SeedReadScopeAsync(fixture, legalHold: true);
        try
        {
            clock.Now = new DateTimeOffset(scope.Deadline.AddTicks(ticks));
            var participants = await fixture.ExecuteAsync<GetRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?>(new(scope.OrderId));
            await Assert.That(participants!.Participants.Single().DisplayName).IsEqualTo(allowed ? "Held attendee" : null);
            var presentations = await fixture.Services.GetRequiredService<IAdmissionTicketPresentationResolver>()
                .ResolveAsync(fixture.TenantId, [scope.TicketId], CancellationToken.None);
            await Assert.That(presentations[scope.TicketId].HolderDisplayName).IsEqualTo(allowed ? "Held attendee" : null);
            var identity = await fixture.Services.GetRequiredService<IAdmissionRecoveryIdentityResolver>()
                .FindAsync(new(fixture.TenantId, "held@example.test", AdmissionRecoveryPurpose.TicketRecovery), CancellationToken.None);
            await Assert.That(identity.IdentityPresent).IsEqualTo(allowed);
            var delivery = await fixture.Services.GetRequiredService<IAdmissionRecoveryDeliveryStager>()
                .StageAsync(new(fixture.TenantId, Guid.CreateVersion7(), scope.TicketId,
                    AdmissionRecoveryPurpose.TicketRecovery, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), CancellationToken.None);
            await Assert.That(delivery.Outcome).IsEqualTo(allowed ? AdmissionRecoveryDeliveryOutcome.Accepted : AdmissionRecoveryDeliveryOutcome.Pending);
            var issuance = await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
                token => fixture.Services.GetRequiredService<IAdmissionIssuanceRepository>().LoadAsync(
                    new(fixture.TenantId, scope.OrderId, scope.EffectId, AdmissionIssuanceAuthority.ConfirmedFreeOrder), token), CancellationToken.None);
            await Assert.That(issuance!.DeliveryAddress).IsEqualTo(allowed ? "held@example.test" : string.Empty);
            var analytics = await fixture.Services.GetRequiredService<IRegistrationAnswerAnalyticsRepository>()
                .GetEventFormVersionAnalyticsAsync(fixture.TenantId, scope.EventId, scope.FormId, scope.VersionId, 1, CancellationToken.None);
            await Assert.That(analytics!.Fields.Count).IsEqualTo(allowed ? 1 : 0);
            var content = await fixture.Services.GetRequiredService<IStorageObjectContentReader>()
                .OpenAsync(scope.Storage.Id, false, CancellationToken.None);
            if (content is not null)
            {
                using var reader = new StreamReader(content.Content);
                await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("private answer");
            }
            await Assert.That(content is not null).IsEqualTo(allowed);
            await Assert.That(await fixture.ExecuteAsync<GetPresignedDownloadUrlRequest, PresignedDownloadUrlResponseDto?>(
                new() { Id = scope.Storage.Id, ExpirationMinutes = 1 })).IsNull();
            var cleanup = await fixture.Services.GetRequiredService<IRegistrationRetentionCleanupRepository>()
                .CleanupTenantAsync(fixture.TenantId, clock.Now.UtcDateTime, 10, CancellationToken.None);
            await Assert.That(cleanup.AnswersDeleted).IsEqualTo(0);
            await Assert.That(await fixture.Context.RegistrationParticipantPii.CountAsync()).IsEqualTo(1);
            await Assert.That(await fixture.Context.RegistrationAnswers.CountAsync()).IsEqualTo(1);
            await Assert.That(await fixture.Context.StorageObjects.AsNoTracking().Where(item => item.Id == scope.Storage.Id)
                .Select(item => item.LifecycleState).SingleAsync()).IsEqualTo(StorageObjectLifecycleStates.Active);
        }
        finally { await DeleteStorageAsync(fixture, scope.Storage); }
    }

    [Test]
    public async Task ExpiredAnonymousCsvCannotBeDisclosedOrLoseCleanupLineageThroughMetadataReparenting()
    {
        var clock = new Clock();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var source = await SeedReadScopeAsync(fixture, legalHold: false);
        var target = await SeedReadScopeAsync(fixture, legalHold: false, anonymous: false);
        try
        {
            clock.Now = new DateTimeOffset(source.Deadline);
            var update = await fixture.ExecuteAsync<UpdateStorageObjectCommand, BaseCommandResponse<Guid>>(new()
            {
                StorageObjectId = source.Storage.Id,
                StorageObjectDto = new()
                {
                    Metadata = new() { FullName = "reparented.csv" },
                    Ownership = new()
                    {
                        OwningResourceKind = source.Storage.OwningResourceKind,
                        OwningResourceId = target.Storage.OwningResourceId
                    }
                }
            });
            fixture.Context.ChangeTracker.Clear();

            var opened = await fixture.Services.GetRequiredService<IStorageObjectContentReader>()
                .OpenAsync(source.Storage.Id, false, CancellationToken.None);
            if (opened is not null) await opened.Content.DisposeAsync();
            await Assert.That(opened).IsNull();
            var persisted = await fixture.Services.GetRequiredService<IStorageObjectRepository>().GetById(source.Storage.Id);
            await Assert.That(persisted!.OwningResourceId).IsEqualTo(source.Storage.OwningResourceId);
            await Assert.That(persisted.OwningResourceKind).IsEqualTo(source.Storage.OwningResourceKind);
            await Assert.That(persisted.RegistrationContentRetentionUntilUtc).IsEqualTo(source.Deadline);
            await Assert.That(persisted.FullName).IsEqualTo("private.csv");
            await Assert.That(update.IsSuccess).IsFalse();

            await fixture.Services.GetRequiredService<IRegistrationRetentionCleanupRepository>()
                .CleanupTenantAsync(fixture.TenantId, source.Deadline, 10, CancellationToken.None);
            fixture.Context.ChangeTracker.Clear();
            persisted = await fixture.Services.GetRequiredService<IStorageObjectRepository>().GetById(source.Storage.Id);
            await Assert.That(persisted!.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
        }
        finally
        {
            await DeleteStorageAsync(fixture, source.Storage);
            await DeleteStorageAsync(fixture, target.Storage);
        }
    }

    [Test]
    [Arguments(-1, true)]
    [Arguments(0, false)]
    [Arguments(1, false)]
    public async Task PersistedCsvDeadlineRemainsAuthoritativeWithNonanonymousOrder(long ticks, bool allowed)
    {
        var clock = new Clock();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var scope = await SeedReadScopeAsync(fixture, legalHold: false, anonymous: false);
        try
        {
            // Independently exercise the persisted artifact bound, not the order's anonymous policy.
            clock.Now = new DateTimeOffset(scope.Deadline.AddTicks(ticks));
            var opened = await fixture.Services.GetRequiredService<IStorageObjectContentReader>()
                .OpenAsync(scope.Storage.Id, false, CancellationToken.None);
            if (opened is not null)
            {
                using var reader = new StreamReader(opened.Content);
                await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("private answer");
            }
            await Assert.That(opened is not null).IsEqualTo(allowed);
        }
        finally { await DeleteStorageAsync(fixture, scope.Storage); }
    }

    [Test]
    [Arguments("clear")]
    [Arguments("kind")]
    [Arguments("actor")]
    [Arguments("deadline-only")]
    [Arguments("kind-only")]
    public async Task GenericOwnershipUpdatesCannotEraseRegistrationProvenance(string scenario)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var scope = await SeedReadScopeAsync(fixture, legalHold: false);
        try
        {
            if (scenario == "deadline-only")
                await fixture.Context.StorageObjects.Where(item => item.Id == scope.Storage.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.OwningResourceKind, (string?)null)
                    .SetProperty(item => item.OwningResourceId, (Guid?)null));
            if (scenario == "kind-only")
                await fixture.Context.StorageObjects.Where(item => item.Id == scope.Storage.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.RegistrationContentRetentionUntilUtc, (DateTime?)null));
            fixture.Context.ChangeTracker.Clear();
            var repository = fixture.Services.GetRequiredService<IStorageObjectRepository>();
            var before = await repository.GetById(scope.Storage.Id);
            string? originalKind = before!.OwningResourceKind;
            Guid? originalId = before.OwningResourceId;
            var response = await fixture.ExecuteAsync<UpdateStorageObjectCommand, BaseCommandResponse<Guid>>(new()
            {
                StorageObjectId = scope.Storage.Id,
                StorageObjectDto = new()
                {
                    Ownership = scenario is "clear" or "kind-only" ? new() : new()
                    {
                        OwningResourceKind = scenario == "actor" ? originalKind : "event",
                        OwningResourceId = originalId ?? scope.EventId,
                        ActorId = scenario == "actor" ? fixture.ActorId : null
                    }
                }
            });
            fixture.Context.ChangeTracker.Clear();
            var after = await repository.GetById(scope.Storage.Id);
            await Assert.That(after!.OwningResourceKind).IsEqualTo(originalKind);
            await Assert.That(after.OwningResourceId).IsEqualTo(originalId);
            await Assert.That(after.ActorId).IsNull();
            await Assert.That(response.IsSuccess).IsFalse();
        }
        finally { await DeleteStorageAsync(fixture, scope.Storage); }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task OrdinaryMetadataUpdatesAndUnchangedRegistrationOwnershipRemainFunctional(bool registrationOwned)
    {
        var clock = new Clock();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var scope = await SeedReadScopeAsync(fixture, legalHold: false);
        try
        {
            if (!registrationOwned)
                await fixture.Context.StorageObjects.Where(item => item.Id == scope.Storage.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.OwningResourceKind, "event")
                    .SetProperty(item => item.OwningResourceId, scope.EventId)
                    .SetProperty(item => item.RegistrationContentRetentionUntilUtc, (DateTime?)null));
            fixture.Context.ChangeTracker.Clear();
            Guid targetId = registrationOwned ? scope.Storage.OwningResourceId!.Value : Guid.CreateVersion7();
            var response = await fixture.ExecuteAsync<UpdateStorageObjectCommand, BaseCommandResponse<Guid>>(new()
            {
                StorageObjectId = scope.Storage.Id,
                StorageObjectDto = new()
                {
                    Metadata = new() { FullName = "renamed.csv" },
                    Access = new() { Purpose = StorageObjectPurposes.Attachment, Visibility = StorageObjectVisibilities.AuthenticatedTenant },
                    Ownership = new()
                    {
                        OwningResourceKind = registrationOwned ? scope.Storage.OwningResourceKind : "event",
                        OwningResourceId = targetId,
                        ActorId = registrationOwned ? null : fixture.ActorId
                    }
                }
            });
            fixture.Context.ChangeTracker.Clear();
            var persisted = await fixture.Services.GetRequiredService<IStorageObjectRepository>().GetById(scope.Storage.Id);
            await Assert.That(persisted!.OwningResourceId).IsEqualTo(targetId);
            await Assert.That(persisted.ActorId).IsEqualTo(registrationOwned ? (Guid?)null : fixture.ActorId);
            await Assert.That(persisted.Purpose).IsEqualTo(StorageObjectPurposes.Attachment);
            await Assert.That(persisted.RegistrationContentRetentionUntilUtc).IsEqualTo(registrationOwned ? scope.Deadline : (DateTime?)null);
            var opened = await fixture.Services.GetRequiredService<IStorageObjectContentReader>()
                .OpenAsync(scope.Storage.Id, false, CancellationToken.None);
            await Assert.That(opened).IsNotNull();
            using var reader = new StreamReader(opened!.Content);
            await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("private answer");
            await Assert.That(opened.SafeDisplayName).IsEqualTo("renamed.csv");
            await Assert.That(response.IsSuccess).IsTrue();
        }
        finally { await DeleteStorageAsync(fixture, scope.Storage); }
    }

    [Test]
    public async Task ForeignTenantCannotUpdateTrackedRegistrationArtifact()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var scope = await SeedReadScopeAsync(fixture, legalHold: false);
        try
        {
            var repository = fixture.Services.GetRequiredService<IStorageObjectRepository>();
            var storage = await repository.GetById(scope.Storage.Id);
            var handler = new UpdateStorageObjectCommandHandler(repository,
                fixture.Services.GetRequiredService<IActorRepository>(), new TenantScope(Guid.CreateVersion7()));
            var response = await handler.Handle(new()
            {
                StorageObjectId = scope.Storage.Id,
                StorageObjectDto = new() { Metadata = new() { FullName = "foreign.csv" } }
            }, CancellationToken.None);
            await Assert.That(storage!.FullName).IsEqualTo("private.csv");
            fixture.Context.ChangeTracker.Clear();
            await Assert.That((await repository.GetById(scope.Storage.Id))!.FullName).IsEqualTo("private.csv");
            await Assert.That(response.IsSuccess).IsFalse();
        }
        finally { await DeleteStorageAsync(fixture, scope.Storage); }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ProviderOpenCrossingExpiryDisposesAlreadyOpenedStream(bool anonymous)
    {
        var clock = new Clock();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var scope = await SeedReadScopeAsync(fixture, legalHold: true, anonymous: anonymous);
        var provider = new OpenBarrier(fixture.Services.GetRequiredService<IFileStorageProviderResolver>().GetRequired(StorageProviders.Local));
        var reader = new StorageObjectContentReader(fixture.Services.GetRequiredService<IStorageObjectRepository>(), provider,
            fixture.Services.GetRequiredService<ICurrentUserService>(), fixture.Services.GetRequiredService<ILogger<StorageObjectContentReader>>(),
            fixture.Services.GetRequiredService<BusinessMetrics>(), clock);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            clock.Now = new DateTimeOffset(scope.Deadline.AddTicks(-1));
            Task<FileStorageReadResult> openedSignal = provider.Opened.Task;
            var pending = reader.OpenAsync(scope.Storage.Id, false, timeout.Token);
            FileStorageReadResult opened = await openedSignal.WaitAsync(timeout.Token);
            clock.Now = new DateTimeOffset(scope.Deadline);
            provider.Release.TrySetResult();
            await Assert.That(await pending.WaitAsync(timeout.Token)).IsNull();
            await Assert.That(opened.Content.CanRead).IsFalse();
        }
        finally
        {
            provider.Release.TrySetResult();
            await DeleteStorageAsync(fixture, scope.Storage);
        }
    }

    [Test]
    [Arguments("missing-artifact-bound")]
    [Arguments("missing-order-bound")]
    [Arguments("foreign-submission")]
    [Arguments("claimed-expired")]
    public async Task AnonymousCsvCannotRecoverDisclosureFromMissingOrChangedAuthority(string scenario)
    {
        var clock = new Clock();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var scope = await SeedReadScopeAsync(fixture, legalHold: true);
        try
        {
            if (scenario == "missing-artifact-bound")
                await fixture.Context.StorageObjects.Where(item => item.Id == scope.Storage.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RegistrationContentRetentionUntilUtc, (DateTime?)null));
            if (scenario == "missing-order-bound")
                await fixture.Context.RegistrationOrders.Where(item => item.Id == scope.OrderId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AnonymousPiiRetentionUntilUtc, (DateTime?)null));
            if (scenario == "foreign-submission")
            {
                Guid foreignTenantId = Guid.CreateVersion7();
                fixture.Context.Tenants.Add(new Tenant
                {
                    Id = foreignTenantId,
                    FullName = "Foreign retention tenant",
                    Slug = $"retention-{foreignTenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                });
                await fixture.Context.SaveChangesAsync();
                await fixture.Context.StorageObjects.Where(item => item.Id == scope.Storage.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.TenantId, foreignTenantId));
                fixture.Context.TenantContext = new TenantScope(foreignTenantId);
            }
            if (scenario == "claimed-expired")
            {
                var order = await fixture.Context.RegistrationOrders.Include(item => item.Pii).SingleAsync(item => item.Id == scope.OrderId);
                await Assert.That(order.TryLinkGuestOrderToAccount(fixture.UserId, "held@example.test")).IsTrue();
                await fixture.Context.SaveChangesAsync();
                clock.Now = new DateTimeOffset(scope.Deadline);
            }
            fixture.Context.ChangeTracker.Clear();
            await Assert.That(await fixture.Services.GetRequiredService<IStorageObjectContentReader>()
                .OpenAsync(scope.Storage.Id, false, CancellationToken.None)).IsNull();
        }
        finally { await DeleteStorageAsync(fixture, scope.Storage); }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CsvCleanupRequiresPublishedNoHoldAuthorityAndPreservesEvidence(bool publishedAuthority)
    {
        var clock = new Clock();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var scope = await SeedReadScopeAsync(fixture, legalHold: false);
        try
        {
            if (!publishedAuthority)
                await fixture.Context.RegistrationFormVersions.Where(version => version.Id == scope.VersionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(version => version.PublishedAt, (DateTime?)null));
            clock.Now = new DateTimeOffset(scope.Deadline);
            var cleanup = await fixture.Services.GetRequiredService<IRegistrationRetentionCleanupRepository>()
                .CleanupTenantAsync(fixture.TenantId, scope.Deadline, 1, CancellationToken.None);
            await Assert.That(cleanup.AnswersDeleted).IsEqualTo(1);
            await Assert.That(await fixture.Context.StorageObjects.AsNoTracking().Where(item => item.Id == scope.Storage.Id)
                .Select(item => item.LifecycleState).SingleAsync()).IsEqualTo(publishedAuthority
                    ? StorageObjectLifecycleStates.DeleteRequested : StorageObjectLifecycleStates.Active);
            await Assert.That(await fixture.Context.RegistrationSubmissions.CountAsync()).IsEqualTo(1);
            await Assert.That(await fixture.Context.AdmissionTickets.CountAsync()).IsEqualTo(1);
            await Assert.That(await fixture.Services.GetRequiredService<IFileStorageProviderResolver>().GetRequired(StorageProviders.Local)
                .ExistsAsync(new(scope.Storage.ObjectKey!), CancellationToken.None)).IsTrue();
        }
        finally { await DeleteStorageAsync(fixture, scope.Storage); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReleasedAnswerFilePreservesSubmissionOrderAuthorityAcrossMetadataUpdates(bool legacyOwnership)
    {
        var clock = new Clock();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var scope = await SeedReadScopeAsync(fixture, legalHold: true);
        try
        {
            var field = await fixture.Context.RegistrationFormFields.SingleAsync(item => item.RegistrationFormVersionId == scope.VersionId && item.Key == "document");
            Guid submissionId = scope.Storage.OwningResourceId!.Value;
            scope.Storage.OwningResourceKind = null;
            scope.Storage.OwningResourceId = null;
            scope.Storage.RegistrationContentRetentionUntilUtc = null;
            var file = RegistrationAnswerFile.Create(fixture.TenantId, submissionId, field, scope.Storage, Now);
            var release = file.ReleaseManually(fixture.UserId, "Operator review", Now);
            if (legacyOwnership)
            {
                scope.Storage.OwningResourceKind = null;
                scope.Storage.OwningResourceId = null;
            }
            fixture.Context.StorageObjects.Update(scope.Storage);
            fixture.Context.AddRange(file, release);
            await fixture.Context.SaveChangesAsync();
            fixture.Context.ChangeTracker.Clear();
            var update = await fixture.ExecuteAsync<UpdateStorageObjectCommand, BaseCommandResponse<Guid>>(new()
            {
                StorageObjectId = scope.Storage.Id,
                StorageObjectDto = new()
                {
                    Ownership = new() { OwningResourceKind = "event", OwningResourceId = scope.EventId }
                }
            });
            fixture.Context.ChangeTracker.Clear();
            clock.Now = new DateTimeOffset(scope.Deadline.AddTicks(-1));
            var reader = fixture.Services.GetRequiredService<IStorageObjectContentReader>();
            var content = await reader.OpenAsync(scope.Storage.Id, false, CancellationToken.None);
            await Assert.That(content).IsNotNull();
            await content!.Content.DisposeAsync();
            await Assert.That(await fixture.ExecuteAsync<GetPresignedDownloadUrlRequest, PresignedDownloadUrlResponseDto?>(
                new() { Id = scope.Storage.Id, ExpirationMinutes = 1 })).IsNull();
            clock.Now = new DateTimeOffset(scope.Deadline);
            await Assert.That(await reader.OpenAsync(scope.Storage.Id, false, CancellationToken.None)).IsNull();
            await Assert.That(await fixture.Context.RegistrationAnswerFiles.CountAsync()).IsEqualTo(1);
            await Assert.That(await fixture.Context.RegistrationAnswerFileReleases.CountAsync()).IsEqualTo(1);
            var persisted = await fixture.Services.GetRequiredService<IStorageObjectRepository>().GetById(scope.Storage.Id);
            await Assert.That(persisted!.OwningResourceKind).IsEqualTo(scope.Storage.OwningResourceKind);
            await Assert.That(persisted.OwningResourceId).IsEqualTo(scope.Storage.OwningResourceId);
            await Assert.That(update.IsSuccess).IsFalse();
        }
        finally { await DeleteStorageAsync(fixture, scope.Storage); }
    }

    [Test]
    [Arguments("participant", "registration_ticket_assignments")]
    [Arguments("ticket", "ticket_type_entitlements")]
    [Arguments("analytics", "GROUP BY")]
    public async Task FinalPreparatoryReadCannotCarryNamesOrAggregatesAcrossExpiry(string surface, string sqlBoundary)
    {
        var clock = new Clock();
        var boundary = new ReadBoundary(clock, sqlBoundary);
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(boundary));
        });
        var scope = await SeedReadScopeAsync(fixture, legalHold: true);
        try
        {
            clock.Now = new DateTimeOffset(scope.Deadline.AddTicks(-1));
            boundary.ExpireAt = new DateTimeOffset(scope.Deadline);
            if (surface == "participant")
            {
                var result = await fixture.ExecuteAsync<GetRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?>(new(scope.OrderId));
                await Assert.That(result!.Participants.Single().DisplayName).IsNull();
            }
            else if (surface == "ticket")
            {
                var result = await fixture.Services.GetRequiredService<IAdmissionTicketPresentationResolver>()
                    .ResolveAsync(fixture.TenantId, [scope.TicketId], CancellationToken.None);
                await Assert.That(result[scope.TicketId].HolderDisplayName).IsNull();
            }
            else
            {
                var result = await fixture.Services.GetRequiredService<IRegistrationAnswerAnalyticsRepository>()
                    .GetEventFormVersionAnalyticsAsync(fixture.TenantId, scope.EventId, scope.FormId, scope.VersionId, 1, CancellationToken.None);
                await Assert.That(result!.Fields).IsEmpty();
            }
            await Assert.That(boundary.Triggered).IsTrue();
        }
        finally { await DeleteStorageAsync(fixture, scope.Storage); }
    }

    [Test]
    public async Task CleanupBatchIsBoundedAndLaterCsvDeletionStillUsesPreservedAuthorityAfterAnswerErasure()
    {
        var clock = new Clock();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var first = await SeedReadScopeAsync(fixture, legalHold: false);
        var second = await SeedReadScopeAsync(fixture, legalHold: false);
        try
        {
            var cleanup = fixture.Services.GetRequiredService<IRegistrationRetentionCleanupRepository>();
            await fixture.Context.RegistrationAnswers.ExecuteDeleteAsync();
            await cleanup.CleanupTenantAsync(fixture.TenantId, first.Deadline, 1, CancellationToken.None);
            await Assert.That(await fixture.Context.StorageObjects.CountAsync(item => item.LifecycleState == StorageObjectLifecycleStates.DeleteRequested)).IsEqualTo(1);
            await cleanup.CleanupTenantAsync(fixture.TenantId, first.Deadline, 1, CancellationToken.None);
            await Assert.That(await fixture.Context.StorageObjects.CountAsync(item => item.LifecycleState == StorageObjectLifecycleStates.DeleteRequested)).IsEqualTo(2);
            await Assert.That(await fixture.Context.RegistrationSubmissions.CountAsync()).IsEqualTo(2);
            await Assert.That(await fixture.Context.AdmissionTickets.CountAsync()).IsEqualTo(2);
            var provider = fixture.Services.GetRequiredService<IFileStorageProviderResolver>().GetRequired(StorageProviders.Local);
            await Assert.That(await provider.ExistsAsync(new(first.Storage.ObjectKey!), CancellationToken.None)).IsTrue();
            await Assert.That(await provider.ExistsAsync(new(second.Storage.ObjectKey!), CancellationToken.None)).IsTrue();
        }
        finally
        {
            await DeleteStorageAsync(fixture, first.Storage);
            await DeleteStorageAsync(fixture, second.Storage);
        }
    }

    [Test]
    [Arguments(-1, true)]
    [Arguments(0, false)]
    public async Task EarlierFiniteRowAndCsvDeadlinesRemainAuthoritativeBeforeOrderExpiry(long ticks, bool allowed)
    {
        var clock = new Clock();
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock));
        var scope = await SeedReadScopeAsync(fixture, legalHold: false);
        DateTime rowDeadline = Now.AddHours(2);
        try
        {
            await fixture.Context.RegistrationParticipantPii.ExecuteUpdateAsync(setters => setters.SetProperty(pii => pii.RetentionUntil, rowDeadline));
            await fixture.Context.RegistrationOrderPii.ExecuteUpdateAsync(setters => setters.SetProperty(pii => pii.RetentionUntil, rowDeadline));
            await fixture.Context.RegistrationAnswers.ExecuteUpdateAsync(setters => setters.SetProperty(answer => answer.RetentionUntil, rowDeadline));
            await fixture.Context.StorageObjects.Where(storage => storage.Id == scope.Storage.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(storage => storage.RegistrationContentRetentionUntilUtc, rowDeadline));
            fixture.Context.ChangeTracker.Clear();
            clock.Now = new DateTimeOffset(rowDeadline.AddTicks(ticks));
            var participants = await fixture.ExecuteAsync<GetRegistrationOrderParticipantsQuery, RegistrationOrderParticipantsDto?>(new(scope.OrderId));
            await Assert.That(participants!.Participants.Single().DisplayName).IsEqualTo(allowed ? "Held attendee" : null);
            var tickets = await fixture.Services.GetRequiredService<IAdmissionTicketPresentationResolver>()
                .ResolveAsync(fixture.TenantId, [scope.TicketId], CancellationToken.None);
            await Assert.That(tickets[scope.TicketId].HolderDisplayName).IsEqualTo(allowed ? "Held attendee" : null);
            var identity = await fixture.Services.GetRequiredService<IAdmissionRecoveryIdentityResolver>()
                .FindAsync(new(fixture.TenantId, "held@example.test", AdmissionRecoveryPurpose.TicketRecovery), CancellationToken.None);
            await Assert.That(identity.IdentityPresent).IsEqualTo(allowed);
            var analytics = await fixture.Services.GetRequiredService<IRegistrationAnswerAnalyticsRepository>()
                .GetEventFormVersionAnalyticsAsync(fixture.TenantId, scope.EventId, scope.FormId, scope.VersionId, 1, CancellationToken.None);
            await Assert.That(analytics!.Fields.Count).IsEqualTo(allowed ? 1 : 0);
            var content = await fixture.Services.GetRequiredService<IStorageObjectContentReader>()
                .OpenAsync(scope.Storage.Id, false, CancellationToken.None);
            if (content is not null) await content.Content.DisposeAsync();
            await Assert.That(content is not null).IsEqualTo(allowed);
            await Assert.That(await fixture.Context.RegistrationParticipantPii.CountAsync()).IsEqualTo(1);
            await Assert.That(await fixture.Context.RegistrationAnswers.CountAsync()).IsEqualTo(1);
        }
        finally { await DeleteStorageAsync(fixture, scope.Storage); }
    }

    private sealed record TenantScope(Guid TenantId) : ITenantContext;

    private static async Task<ReadScope> SeedReadScopeAsync(EventVisitorCapabilitySqliteFixture fixture, bool legalHold, bool anonymous = true)
    {
        DateTime deadline = Now.AddDays(1);
        var order = await SeedHistoricalOrderAsync(fixture, deadline, anonymous);
        int retentionPolicy = (int)(legalHold ? RegistrationRetentionPolicyEnum.LegalHold : RegistrationRetentionPolicyEnum.StandardOperational);
        order.SetPii(RegistrationOrderPii.CreateFromVerifiedContact(order.Id, fixture.TenantId, "Held attendee", "held@example.test",
            null, null, "held@example.test", retentionPolicy, Now));
        var catalog = await fixture.Context.EventTicketCatalogVersions.Include(item => item.TicketTypes)
            .SingleAsync(item => item.Id == order.TicketCatalogVersionId);
        var type = catalog.TicketTypes.Single();
        var line = RegistrationOrderLine.Create(catalog, type, order.Id, 1, null, null);
        order.AddLine(line);
        var participant = RegistrationParticipant.Create(fixture.TenantId, order.Id, null, ParticipantTypeEnum.Adult, null);
        participant.SetPii(RegistrationParticipantPii.Create(participant.Id, fixture.TenantId, "Held attendee", "held@example.test", null,
            retentionPolicy, Now));
        var assignment = RegistrationTicketAssignment.Create(fixture.TenantId, order.Id, line.Id, 1, participant.Id,
            AssignmentStatusEnum.Assigned, null, Now);
        fixture.Context.AddRange(participant, assignment);
        var workflow = RegistrationWorkflow.Create(fixture.TenantId, order.EventId, "RETENTION", Now);
        var requirement = RegistrationRequirement.Create(workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now);
        var channel = RegistrationChannel.Create(requirement, 1, true, null, Now);
        requirement.AddChannel(channel);
        workflow.AddRequirement(requirement);
        var form = RegistrationForm.Create(fixture.TenantId, order.EventId, "native", "retention", "Retention", Now);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now);
        var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", Now);
        version.AddSection(section);
        var field = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1, "native", "attending", "Attending",
            RegistrationFieldTypeEnum.Boolean, retentionPolicy, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, false, null, true, true, Now);
        version.AddField(section, field);
        var fileField = RegistrationFormField.Create(Guid.CreateVersion7(), section, 2, "native", "document", "Document",
            RegistrationFieldTypeEnum.File, retentionPolicy, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, false, Now);
        version.AddField(section, fileField);
        new FormSchemaArtifactPublicationService(new FormSchemaArtifactGenerator()).Publish(version, Now);
        form.AddVersion(version);
        fixture.Context.AddRange(workflow, form);
        await fixture.Context.SaveChangesAsync();
        await fixture.Context.RegistrationOrders.Where(item => item.Id == order.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RegistrationWorkflowVersionId, workflow.Id));
        fixture.Context.ChangeTracker.Clear();
        var attempt = RegistrationAttempt.Create(fixture.TenantId, order.EventId, order.Id, workflow.Id, requirement.Id,
            channel.Id, form.Id, version.Id, CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            null, null, Now, Now.AddHours(1));
        fixture.Context.Add(attempt);
        await fixture.Context.SaveChangesAsync();
        var submission = RegistrationSubmission.Create(attempt,
            RegistrationEvidenceHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), Now, null, null, null, null);
        var answer = RegistrationAnswer.CreateBoolean(submission, field, requirement, RegistrationAnswerSubjectTypeEnum.RegistrationOrder,
            order.Id, 1, true, Now, anonymousUpperBoundUtc: deadline);
        fixture.Context.AddRange(submission, answer);
        await fixture.Context.SaveChangesAsync();
        await fixture.Context.RegistrationOrders.Where(item => item.Id == order.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.RegistrationOrderStatusId, (int)RegistrationOrderStatusEnum.Confirmed)
            .SetProperty(item => item.ConfirmedAt, Now));
        fixture.Context.ChangeTracker.Clear();
        order = await fixture.Context.RegistrationOrders.Include(item => item.Lines).ThenInclude(item => item.Assignments)
            .Include(item => item.Participants).SingleAsync(item => item.Id == order.Id);
        catalog = await fixture.Context.EventTicketCatalogVersions.Include(item => item.TicketTypes).SingleAsync(item => item.Id == catalog.Id);
        var ticket = AdmissionTicket.Issue(order, order.Lines.Single(), order.Lines.Single().Assignments.Single(), order.Participants.Single(),
            catalog, catalog.TicketTypes.Single(), Guid.CreateVersion7(), "RETENTION-TEST", Guid.CreateVersion7(), 1, 1,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), Now);
        var effect = RegistrationFinalizationEffect.Create(order, Now);
        var storage = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document,
            FileType = null!,
            Uri = "unused",
            Provider = StorageProviders.Local,
            FullName = "private.csv",
            SafeDisplayName = "private.csv",
            Extension = ".csv",
            ContentType = "text/csv",
            Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            Purpose = StorageObjectPurposes.Document,
            LifecycleState = StorageObjectLifecycleStates.Active,
            OwningResourceKind = "registration_submission_sink",
            OwningResourceId = submission.Id,
            RegistrationContentRetentionUntilUtc = deadline,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        await using var content = new MemoryStream("private answer"u8.ToArray());
        var written = await fixture.Services.GetRequiredService<IFileStorageProviderResolver>().GetRequired(StorageProviders.Local)
            .WriteAsync(new(fixture.TenantId, content, "text/csv", "private.csv", ".csv", content.Length, 1024,
                $"tenants/{fixture.TenantId:N}/retention-tests/{storage.Id:N}.csv"), CancellationToken.None);
        storage.ObjectKey = written.ObjectKey;
        fixture.Context.AddRange(ticket, effect, storage);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return new(order.Id, order.EventId, form.Id, version.Id, ticket.Id, effect.Id, deadline, storage);
    }

    private static Task<FileStorageDeleteResult> DeleteStorageAsync(EventVisitorCapabilitySqliteFixture fixture, StorageObject storage) =>
        fixture.Services.GetRequiredService<IFileStorageProviderResolver>().GetRequired(StorageProviders.Local)
            .DeleteAsync(new(storage.ObjectKey!), CancellationToken.None);

    private sealed record ReadScope(Guid OrderId, Guid EventId, Guid FormId, Guid VersionId, Guid TicketId,
        Guid EffectId, DateTime Deadline, StorageObject Storage);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(AnonymousRetentionReadBoundaryTests.Now);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ReadBoundary(Clock clock, string sqlBoundary) : DbCommandInterceptor
    {
        public DateTimeOffset? ExpireAt { get; set; }
        public bool Triggered { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (ExpireAt is { } deadline && command.CommandText.Contains(sqlBoundary, StringComparison.OrdinalIgnoreCase))
            {
                Triggered = true;
                clock.Now = deadline;
                ExpireAt = null;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class OpenBarrier(IFileStorageProvider inner) : IFileStorageProvider, IFileStorageProviderResolver
    {
        public TaskCompletionSource<FileStorageReadResult> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Provider => inner.Provider;
        public IFileStorageProvider GetRequired(string provider) => this;
        public async Task<FileStorageReadResult> OpenReadAsync(FileStorageReadInput input, CancellationToken cancellationToken)
        {
            var opened = await inner.OpenReadAsync(input, cancellationToken);
            Opened.TrySetResult(opened);
            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return opened;
            }
            catch
            {
                await opened.Content.DisposeAsync();
                throw;
            }
        }
        public Task<FileStorageWriteResult> WriteAsync(FileStorageWriteInput input, CancellationToken token) => inner.WriteAsync(input, token);
        public Task<bool> ExistsAsync(FileStorageExistsInput input, CancellationToken token) => inner.ExistsAsync(input, token);
        public Task<FileStorageDeleteResult> DeleteAsync(FileStorageDeleteInput input, CancellationToken token) => inner.DeleteAsync(input, token);
        public Task<FileStorageProviderStatus> TestAsync(CancellationToken token, bool testWritePermissions = false) => inner.TestAsync(token, testWritePermissions);
    }

    private static async Task<RegistrationOrder> SeedHistoricalOrderAsync(EventVisitorCapabilitySqliteFixture fixture, DateTime? deadline = null, bool anonymous = true)
    {
        var target = await fixture.SeedEventAsync(published: true);
        var catalog = await fixture.SeedTicketAsync(target.Id);
        var token = fixture.Services.GetRequiredService<IGuestCapabilityTokenService>().Issue();
        var order = RegistrationOrder.Create(fixture.TenantId, target.Id, anonymous ? null : fixture.UserId, null, BookingPartyTypeEnum.Individual,
            catalog.CatalogId, RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(),
                (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
                (int)(anonymous ? IdentityAccessModeEnum.GuestAllowed : IdentityAccessModeEnum.AccountRequired),
                anonymous ? GuestRecoveryPolicyEnum.EmailOptional : null),
            null, anonymous ? token.Hash : null, "USD", Now, Now.AddHours(1), anonymous ? deadline : null);
        fixture.Context.Add(order);
        await fixture.Context.SaveChangesAsync();
        return order;
    }
}
