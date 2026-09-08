using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Geocoding;
using Explore.Application.Features.Locations.Handlers.Commands;
using Explore.Application.Features.Locations.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Geocoding;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Repositories;

internal static class LocationUnicodeWriteAtomicityTests
{
    internal static async Task AssertRejectedWritesAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture, Guid tenantId, Guid actorId)
    {
        await using ExploreDbContext context = fixture.CreateTenantContext(tenantId);
        Tenant tenant = await context.Tenants.SingleAsync(row => row.Id == tenantId);
        context.Set<TenantSetting>().Add(new TenantSetting
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = tenant,
            SettingKey = GovernanceSettingKeys.AddressGovernance.CreationMode,
            Value = "\"OpenWithModeration\"", CreatedAt = DateTime.UnixEpoch
        });
        await context.SaveChangesAsync();
        var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var settings = new HierarchicalSettingsResolver(
            new SystemSettingRepository(context, mutationLock), new TenantSettingRepository(context, mutationLock),
            new OrganizationSettingRepository(context), new GroupSettingRepository(context),
            new GroupTenantRepository(context), new UserPreferenceRepository(context),
            context.TenantContext!, mutationLock, cache, NullLogger<HierarchicalSettingsResolver>.Instance,
            EmailDispatchSqliteFixture.CreateEmailSettingsWriter(context, mutationLock));
        // Cerbos is the external authorization boundary; all local policy and persistence behavior is real.
        var authorization = Substitute.For<IAuthorizationProvider>();
        authorization.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos));
        var governance = new AddressGovernancePolicyResolver(settings, authorization);
        var protector = new DataProtectionAddressSelectionProtector(
            new EphemeralDataProtectionProvider(), TimeProvider.System,
            Options.Create(new PhotonGeocodingOptions()));
        var user = new UserContext(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", actorId.ToString())], "test"))
            }
        });

        foreach (int scenario in Enumerable.Range(0, 4))
        {
            var location = new Location
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, FullName = "Original venue",
                Country = "BE", City = "Brussels", ConcurrencyStamp = Guid.CreateVersion7()
            };
            location.SetManualAddress("Original café address", "1000");
            context.Locations.Add(location);
            if (scenario == 3)
            {
                context.Entry(location.Pii!).Property(pii => pii.Address).CurrentValue = "Stored \uFDD0 address";
            }
            await context.SaveChangesAsync();
            await context.Entry(location).ReloadAsync();
            await context.Entry(location.Pii!).ReloadAsync();
            string? token = null;
            if (scenario == 2)
            {
                token = (await protector.ProtectAsync(new ProtectedAddressSelection
                {
                    DisplayName = "Changed provider name", Address = "Invalid\0provider address", Postcode = "2000",
                    City = "Paris", Country = "FR", Latitude = 48.8, Longitude = 2.3,
                    Attribution = "Synthetic provider", Provenance = new ProtectedAddressProvenance { Provider = "Photon" }
                }, new AddressSelectionContext
                {
                    TenantId = tenantId, ActorId = actorId, Purpose = AddressSelectionPurpose.UpdateLocation,
                    Target = new AddressSelectionTarget { LocationId = location.Id, ExpectedConcurrencyStamp = location.ConcurrencyStamp },
                    ConfigurationFingerprint = protector.ConfigurationFingerprint
                }, CancellationToken.None)).Value;
            }
            var patch = scenario switch
            {
                0 => new UpdateLocationDto { FullName = new() { Value = "Changed name" }, Address = new() { Value = "Invalid\0address" } },
                1 => new UpdateLocationDto { FullName = new() { Value = "Invalid\uFDD0name" }, Address = new() { Value = "Changed address" } },
                2 => new UpdateLocationDto { AddressSelectionToken = token },
                _ => new UpdateLocationDto { Postcode = new() { Value = "2000" } }
            };
            var before = context.Entry(location).CurrentValues.Clone();
            var beforePii = context.Entry(location.Pii!).CurrentValues.Clone();
            var handler = new UpdateLocationCommandHandler(new LocationRepository(context), protector,
                context.TenantContext!, user, governance, TimeProvider.System);

            var response = await handler.Handle(new UpdateLocationCommand
            {
                LocationId = location.Id, ExpectedConcurrencyStamp = location.ConcurrencyStamp, UpdateLocationDto = patch
            }, CancellationToken.None);

            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.FailureCode).IsEqualTo(Explore.Application.Responses.FailureCodes.AddressSelectionInvalid);
            await Assert.That(before.Properties.Where(property => !Equals(before[property], context.Entry(location).CurrentValues[property]))
                .Select(property => property.Name)).IsEmpty();
            await Assert.That(beforePii.Properties.Where(property => !Equals(beforePii[property], context.Entry(location.Pii!).CurrentValues[property]))
                .Select(property => property.Name)).IsEmpty();
            await context.Entry(location).ReloadAsync();
            await context.Entry(location.Pii!).ReloadAsync();
            await Assert.That(before.Properties.Where(property => !Equals(before[property], context.Entry(location).CurrentValues[property]))
                .Select(property => property.Name)).IsEmpty();
            await Assert.That(beforePii.Properties.Where(property => !Equals(beforePii[property], context.Entry(location.Pii!).CurrentValues[property]))
                .Select(property => property.Name)).IsEmpty();
        }
    }
}
