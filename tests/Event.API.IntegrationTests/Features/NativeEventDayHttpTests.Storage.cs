using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Features.EventDays.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventDayHttpTests
{
    [Test]
    public async Task Images_RequireActivePublicRasterMetadata_AndPreservePatchPresence()
    {
        await using var factory = await DayFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        var image = await factory.ImageAsync();
        foreach (var invalid in new[]
        {
            await factory.ImageAsync(visibility: StorageObjectVisibilities.PrivateOwner),
            await factory.ImageAsync(state: StorageObjectLifecycleStates.Quarantined),
            await factory.ImageAsync(contentType: "image/svg+xml", extension: "svg"),
            await factory.ImageAsync(foreign: true),
            Guid.CreateVersion7()
        })
        {
            using var rejected = await owner.PostAsJsonAsync("/api/eventday", new
            { eventId = factory.PublicEventId, localDate = "2027-03-01", bannerImageId = invalid });
            await ProblemAsync(rejected, HttpStatusCode.BadRequest);
        }
        using var created = await owner.PostAsJsonAsync("/api/eventday", new
        { eventId = factory.PublicEventId, localDate = "2027-03-01", bannerImageId = image });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var before = await DetailAsync(owner, id);
        await Assert.That(before.GetProperty("bannerImageId").GetGuid()).IsEqualTo(image);
        using (var omitted = await PatchAsync(owner, id, new { sortOrder = new { value = 3 } },
            $"\"{before.GetProperty("concurrencyStamp").GetGuid()}\""))
            await Assert.That(omitted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var preserved = await DetailAsync(owner, id);
        await Assert.That(preserved.GetProperty("bannerImageId").GetGuid()).IsEqualTo(image);
        using (var cleared = await PatchAsync(owner, id, new { bannerImage = new { value = new { hasValue = true, value = (Guid?)null } } },
            $"\"{preserved.GetProperty("concurrencyStamp").GetGuid()}\""))
            await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await DetailAsync(owner, id)).TryGetProperty("bannerImageId", out _)).IsFalse();
    }

    [Test]
    public async Task StorageLookupFailureAndCancellation_DoNotCreateADay_AndRemainObservable()
    {
        await using var factory = await DayFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        var image = await factory.ImageAsync();
        var command = new CreateEventDayCommand { EventDayDto = new CreateEventDayDto
        { EventId = factory.PublicEventId, LocalDate = new DateOnly(2027, 4, 1), BannerImageId = image } };
        var failure = new IOException("Injected storage metadata read failure.");
        factory.StorageReads.Failure = () => failure;
        var observed = await Assert.That(async () => { await factory.CreateDayAsync(command); }).Throws<IOException>();
        await Assert.That(ReferenceEquals(observed, failure)).IsTrue();
        using (var response = await owner.PostAsJsonAsync("/api/eventday", command.EventDayDto))
            await ProblemAsync(response, HttpStatusCode.InternalServerError);
        using var cancellation = new CancellationTokenSource();
        factory.StorageReads.Failure = () =>
        {
            cancellation.Cancel();
            return new OperationCanceledException(cancellation.Token);
        };
        var cancelled = await Assert.That(async () => { await factory.CreateDayAsync(command, cancellation.Token); }).Throws<OperationCanceledException>();
        await Assert.That(cancelled!.CancellationToken).IsEqualTo(cancellation.Token);
        factory.StorageReads.Failure = null;
        await Assert.That(Items(await CollectionAsync(owner, Managed(factory.PublicEventId))).Length).IsEqualTo(2);
        using var recovered = await owner.PostAsJsonAsync("/api/eventday", command.EventDayDto);
        await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.Created);
    }

    private sealed partial class DayFactory
    {
        public StorageReadFailure StorageReads { get; } = new();

        public async Task<Guid> ImageAsync(string visibility = StorageObjectVisibilities.PublicImage,
            string state = StorageObjectLifecycleStates.Active, string contentType = "image/png", string extension = "png", bool foreign = false)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var tenant = foreign ? ForeignTenantId : PlatformDefaults.DefaultTenantId;
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenant);
            var image = new StorageObject
            {
                Id = Guid.CreateVersion7(), TenantId = tenant, Tenant = null!, FileTypeId = (int)FileTypeEnum.Image, FileType = null!,
                Uri = "https://images.example.test/day.png", Provider = "legacy_external", FullName = "day.png", SafeDisplayName = "day.png",
                Extension = extension, ContentType = contentType, Visibility = visibility, Purpose = StorageObjectPurposes.EventImage, LifecycleState = state
            };
            db.StorageObjects.Add(image);
            await db.SaveChangesAsync();
            return image.Id;
        }

        public async Task<BaseCommandResponse<Guid>> CreateDayAsync(CreateEventDayCommand command, CancellationToken cancellationToken = default)
        {
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", OwnerId.ToString())], "Test")) };
            try
            {
                return await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventDayCommand, BaseCommandResponse<Guid>>>()
                    .ExecuteAsync(command, cancellationToken);
            }
            finally
            {
                accessor.HttpContext = previous;
            }
        }
    }

    private sealed class StorageReadFailure : DbCommandInterceptor
    {
        public Func<Exception>? Failure { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Failure is not null && command.CommandText.Contains(eventData.Context!.Model.FindEntityType(typeof(StorageObject))!.GetTableName()!, StringComparison.Ordinal))
                throw Failure();
            return ValueTask.FromResult(result);
        }
    }
}
