using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Event.Standalone.IntegrationTests.Fixtures;
using Explore.API.Models;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Standalone.IntegrationTests;

public sealed partial class CombinedEventResourceTransportTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeCombinedFinalLeaseDeniesWithdrawnPreparedBytes(bool withdraw)
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        await using var host = deployment.CreateHost();
        Guid resourceId = Guid.CreateVersion7();
        var observation = new FileReadObservation();
        using var selected = host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStorageProviderBindingService>();
            services.AddScoped<IStorageProviderBindingService>(provider => new ObservedBindings(
                ActivatorUtilities.CreateInstance<StorageProviderBindingService>(provider), observation));
            services.Configure<MvcOptions>(options =>
                options.Filters.Add(new FinalResourceWithdrawal(resourceId, withdraw)));
        }));
        using var client = selected.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = deployment.Subject.ToString("D"), password = deployment.InitialPassword }))
        {
            login.EnsureSuccessStatusCode();
            using var challenge = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                challenge.RootElement.GetProperty("replacementChallenge").GetProperty("token").GetString());
        }
        string password = NativeEmailOptionalStandaloneFixture.NewPassword();
        using (var replace = await client.PostAsJsonAsync("/api/auth/local/credential-replacement", new { newPassword = password }))
            replace.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = null;
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login",
            new { identifier = deployment.Subject.ToString("D"), password }))
        {
            login.EnsureSuccessStatusCode();
            using var token = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                token.RootElement.GetProperty("token").GetString());
        }
        await ActivateResourceDirectoryAsync(client);

        string marker = Guid.CreateVersion7().ToString("N");
        byte[] bytes = Encoding.UTF8.GetBytes($"%PDF-1.7\n{marker}\n%%EOF");
        await using (var scope = selected.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EnableTenantFilterBypass("Seed one combined final-lease resource and its captured private file.");
            var actor = await db.Actors.SingleAsync(row => row.UserId == deployment.Subject);
            var unscanned = await db.SystemSettings.SingleOrDefaultAsync(row =>
                row.SettingKey == GovernanceSettingKeys.EventResources.AllowUnscannedDocuments);
            if (unscanned is null)
                db.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                    Value = "true",
                    ValueType = SettingValueType.Boolean,
                    Category = "EventResources"
                });
            else unscanned.Value = "true";
            Guid eventId = Guid.CreateVersion7(), storageId = Guid.CreateVersion7();
            var parent = new Explore.Domain.Event
            {
                Id = eventId,
                Title = "Combined final-lease file",
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                ActorId = actor.Id,
                Actor = actor,
                OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventStatus = null!
            };
            parent.Publish(DateTime.UtcNow);
            db.Events.Add(parent);
            var bindings = scope.ServiceProvider.GetRequiredService<IStorageProviderBindingService>();
            var binding = await bindings.CaptureAsync(StorageProviders.Local, PlatformDefaults.DefaultTenantId, default);
            await db.SaveChangesAsync();
            var provider = await bindings.ResolveAsync(binding.Id, default);
            using var content = new MemoryStream(bytes);
            var written = await provider.WriteAsync(new FileStorageWriteInput(
                PlatformDefaults.DefaultTenantId, content, "application/pdf", "lease.pdf", "pdf", bytes.Length, bytes.Length),
                default);
            var storage = new StorageObject
            {
                Id = storageId,
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                FileTypeId = (int)FileTypeEnum.Document,
                FileType = null!,
                ObjectKey = written.ObjectKey,
                Provider = written.Provider,
                StorageProviderBindingId = binding.Id,
                FullName = "lease.pdf",
                SafeDisplayName = "lease.pdf",
                Extension = "pdf",
                ContentType = "application/pdf",
                Size = written.SizeBytes,
                Sha256Checksum = written.Sha256Checksum!,
                Purpose = StorageObjectPurposes.EventResource,
                Visibility = StorageObjectVisibilities.PrivateOwner,
                OwningResourceKind = StorageOwningResourceKinds.EventResource,
                OwningResourceId = resourceId,
                LifecycleState = StorageObjectLifecycleStates.Active,
                CreatedBy = deployment.Subject
            };
            storage.RecordEventResourceInspection(storageId, storage.Sha256Checksum);
            db.StorageObjects.Add(storage);
            var resource = EventResource.CreateDraft(resourceId, PlatformDefaults.DefaultTenantId, eventId, null,
                new EventResourceMetadata
                {
                    Title = "Public final-lease handout",
                    Kind = EventResourceKindEnum.GeneralDocument,
                    DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                }, EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId,
                    EventResourceAudienceKindEnum.Public)], deployment.Subject, DateTime.UtcNow);
            resource.SetStoredFile(storageId, resource.ConcurrencyStamp, deployment.Subject, DateTime.UtcNow);
            resource.Publish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
                false, true, null, false, new(null, null, null, null)), true,
                resource.ConcurrencyStamp, deployment.Subject, DateTime.UtcNow);
            db.EventResources.Add(resource);
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.GetAsync($"/api/eventresource/{resourceId}/content");
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        await observation.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(observation.Opened).IsEqualTo(1);
        await Assert.That(observation.Disposed).IsTrue();
        if (withdraw)
        {
            await Assert.That(observation.BytesRead).IsEqualTo(0L);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(response.Content.Headers.ContentDisposition).IsNull();
            await Assert.That(Encoding.UTF8.GetString(body)).DoesNotContain(marker);
            await Assert.That(Encoding.UTF8.GetString(body)).DoesNotContain("%PDF");
            await using var verification = selected.Services.CreateAsyncScope();
            var db = verification.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EnableTenantFilterBypass("Verify committed withdrawal happened after preparation, before final lease.");
            await Assert.That((await db.EventResources.AsNoTracking().SingleAsync(row => row.Id == resourceId)).PublicationStateId)
                .IsEqualTo((int)EventResourcePublicationStateEnum.Withdrawn);
        }
        else
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body).IsEquivalentTo(bytes);
            await Assert.That(observation.BytesRead).IsEqualTo((long)bytes.Length);
            await Assert.That(response.Content.Headers.ContentDisposition!.DispositionType).IsEqualTo("attachment");
        }
    }

    private sealed class FileReadObservation
    {
        public int Opened;
        public long BytesRead;
        public bool Disposed;
        public TaskCompletionSource Disposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ObservedBindings(IStorageProviderBindingService inner, FileReadObservation observation)
        : IStorageProviderBindingService
    {
        public Task<StorageProviderBinding> CaptureAsync(string provider, Guid tenantId, CancellationToken cancellationToken) =>
            inner.CaptureAsync(provider, tenantId, cancellationToken);
        public async Task<IFileStorageProvider> ResolveAsync(Guid bindingId, CancellationToken cancellationToken) =>
            new ObservedProvider(await inner.ResolveAsync(bindingId, cancellationToken), observation);
    }

    private sealed class ObservedProvider(IFileStorageProvider inner, FileReadObservation observation) : IFileStorageProvider
    {
        public string Provider => inner.Provider;
        public Task<FileStorageWriteResult> WriteAsync(FileStorageWriteInput input, CancellationToken cancellationToken) =>
            inner.WriteAsync(input, cancellationToken);
        public Task<bool> ExistsAsync(FileStorageExistsInput input, CancellationToken cancellationToken) =>
            inner.ExistsAsync(input, cancellationToken);
        public Task<FileStorageDeleteResult> DeleteAsync(FileStorageDeleteInput input, CancellationToken cancellationToken) =>
            inner.DeleteAsync(input, cancellationToken);
        public Task<FileStorageProviderStatus> TestAsync(CancellationToken cancellationToken, bool testWritePermissions = false) =>
            inner.TestAsync(cancellationToken, testWritePermissions);
        public async Task<FileStorageReadResult> OpenReadAsync(FileStorageReadInput input, CancellationToken cancellationToken)
        {
            var opened = await inner.OpenReadAsync(input, cancellationToken);
            observation.Opened++;
            return opened with { Content = new ObservedFileStream(opened.Content, observation) };
        }
    }

    private sealed class ObservedFileStream(Stream inner, FileReadObservation observation) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            observation.BytesRead += read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer, cancellationToken);
            observation.BytesRead += read;
            return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => inner.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                observation.Disposed = true;
                observation.Disposal.TrySetResult();
            }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            observation.Disposed = true;
            observation.Disposal.TrySetResult();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class FinalResourceWithdrawal(Guid resourceId, bool withdraw) : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (withdraw && context.Result is EventResourceFileResult)
            {
                await using var scope = context.HttpContext.RequestServices.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                db.EnableTenantFilterBypass("Commit one exact resource withdrawal at the native result boundary.");
                var resource = await db.EventResources.SingleAsync(row => row.Id == resourceId);
                resource.Withdraw(resource.ConcurrencyStamp, resource.CreatedBy!.Value, DateTime.UtcNow);
                await db.SaveChangesAsync();
            }
            await next();
        }
    }
}
