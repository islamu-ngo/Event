// ABOUTME: Exercises native protected anonymous authority, proof binding, and recovery expiry across service instances.
// ABOUTME: Uses real Data Protection and cryptographic work with an injected clock, without repository mocks or sleeps.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Services.Registration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AnonymousRegistrationChallengeServiceTests
{
    [Test]
    public async Task ExactProof_ReconstructsSameOrderAndCapabilityAcrossServiceInstancesWithoutPersistentPlaintext()
    {
        using var provider = CreateProvider();
        var clock = new Clock();
        IAnonymousRegistrationChallengeService issuer = CreateService(provider, clock);
        IAnonymousRegistrationChallengeService consumer = CreateService(provider, clock);
        var binding = Binding();
        var challenge = issuer.Issue(binding, 16);
        string nonce = Solve(challenge);
        var first = consumer.Validate(binding, challenge.ProtectedChallenge, nonce);
        var retry = CreateService(provider, clock).Validate(binding, challenge.ProtectedChallenge, nonce);

        await Assert.That(first).IsNotNull();
        await Assert.That(retry).IsNotNull();
        await Assert.That(first!.OrderId).IsNotEqualTo(Guid.Empty);
        await Assert.That(first.OrderId.Version).IsEqualTo(7);
        await Assert.That(retry!.OrderId).IsEqualTo(first.OrderId);
        await Assert.That(retry.GuestCapabilityToken).IsEqualTo(first.GuestCapabilityToken);
        await Assert.That(first.TenantId).IsEqualTo(binding.TenantId);
        await Assert.That(first.EventId).IsEqualTo(binding.EventId);
        await Assert.That(new GuestCapabilityTokenService().Matches(first.GuestCapabilityToken, first.GuestAccessTokenHash)).IsTrue();
        await Assert.That(challenge.ExpiresAt).IsEqualTo(clock.GetUtcNow().AddSeconds(120));
        await Assert.That(first.IsFresh(clock.GetUtcNow())).IsTrue();
        await Assert.That(JsonSerializer.Serialize(first)).IsEqualTo("{}");
        await Assert.That(challenge.ProtectedChallenge.Contains(first.GuestCapabilityToken, StringComparison.Ordinal)).IsFalse();
        await Assert.That(challenge.ToString().Contains(challenge.ProtectedChallenge, StringComparison.Ordinal)).IsFalse();
        await Assert.That(first.ToString().Contains(first.GuestCapabilityToken, StringComparison.Ordinal)).IsFalse();
        await Assert.That(binding.ToString().Contains(binding.IdempotencyKey, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Expiry_StopsFreshAllocationExactlyButAllowsOnlyBoundedCommittedRecovery()
    {
        using var provider = CreateProvider();
        var clock = new Clock();
        var service = CreateService(provider, clock);
        var binding = Binding();
        var challenge = service.Issue(binding, 16);
        var nonce = Solve(challenge);
        var authority = service.Validate(binding, challenge.ProtectedChallenge, nonce);
        await Assert.That(authority).IsNotNull();
        clock.Now = challenge.ExpiresAt.AddTicks(-1);
        await Assert.That(authority!.IsFresh(clock.Now)).IsTrue();
        clock.Now = challenge.ExpiresAt;
        await Assert.That(authority.IsFresh(clock.Now)).IsFalse();
        var recovered = service.Validate(binding, challenge.ProtectedChallenge, nonce);
        await Assert.That(recovered).IsNotNull();
        await Assert.That(recovered!.OrderId).IsEqualTo(authority.OrderId);
        await Assert.That(recovered.ExpiresAt).IsEqualTo(challenge.ExpiresAt);
        clock.Now = challenge.ExpiresAt.AddHours(24).AddTicks(-1);
        await Assert.That(service.Validate(binding, challenge.ProtectedChallenge, nonce)).IsNotNull();
        clock.Now = challenge.ExpiresAt.AddHours(24);
        await Assert.That(service.Validate(binding, challenge.ProtectedChallenge, nonce)).IsNull();
    }

    [Test]
    public async Task WrongScopeBodyKeyNonceAndSignature_NeverRevealAuthority()
    {
        using var provider = CreateProvider();
        var clock = new Clock();
        var service = CreateService(provider, clock);
        var binding = Binding();
        var challenge = service.Issue(binding, 16);
        string nonce = Solve(challenge);
        var invalidBindings = new[]
        {
            new AnonymousRegistrationChallengeBinding(Guid.CreateVersion7(), binding.EventId, binding.CanonicalRequestDigest, binding.IdempotencyKey),
            new AnonymousRegistrationChallengeBinding(binding.TenantId, Guid.CreateVersion7(), binding.CanonicalRequestDigest, binding.IdempotencyKey),
            new AnonymousRegistrationChallengeBinding(binding.TenantId, binding.EventId, Convert.ToHexString(SHA256.HashData(RandomNumberGenerator.GetBytes(32))), binding.IdempotencyKey),
            new AnonymousRegistrationChallengeBinding(binding.TenantId, binding.EventId, binding.CanonicalRequestDigest, Guid.CreateVersion7().ToString("N"))
        };
        foreach (var invalid in invalidBindings)
            await Assert.That(service.Validate(invalid, challenge.ProtectedChallenge, nonce)).IsNull();
        foreach (string? invalidNonce in new string?[] { null, "", "0", "00000000000000000", "000000000000000G", "000000000000000A", new('0', 4097) })
            await Assert.That(service.Validate(binding, challenge.ProtectedChallenge, invalidNonce)).IsNull();
        string unsolvedNonce = Enumerable.Range(0, 100).Select(i => i.ToString("x16", CultureInfo.InvariantCulture))
            .First(candidate => !Satisfies16(challenge.ProtectedChallenge, candidate));
        await Assert.That(service.Validate(binding, challenge.ProtectedChallenge, unsolvedNonce)).IsNull();
        await Assert.That(challenge.ProtectedChallenge.Length).IsGreaterThan(50);
        char replacement = challenge.ProtectedChallenge[50] == 'A' ? 'B' : 'A';
        string tampered = challenge.ProtectedChallenge[..50] + replacement + challenge.ProtectedChallenge[51..];
        await Assert.That(service.Validate(binding, tampered, nonce)).IsNull();
        await Assert.That(service.Validate(binding, new string('A', 4097), nonce)).IsNull();
        await Assert.That(service.Validate(binding, null, nonce)).IsNull();
        await Assert.That(service.Validate(binding, "malformed", nonce)).IsNull();
        using var foreignProvider = CreateProvider("another-application");
        await Assert.That(CreateService(foreignProvider, clock).Validate(binding, challenge.ProtectedChallenge, nonce)).IsNull();
        clock.Now = clock.Now.AddTicks(-1);
        await Assert.That(service.Validate(binding, challenge.ProtectedChallenge, nonce)).IsNull();
    }

    [Test]
    public async Task Issue_DefaultAndDifficultyBoundsAreEnforced()
    {
        using var provider = CreateProvider();
        var service = CreateService(provider, new Clock());
        var binding = Binding();
        await Assert.That(service.Issue(binding).Difficulty).IsEqualTo(18);
        await Assert.That(service.Issue(binding, 16).Difficulty).IsEqualTo(16);
        await Assert.That(service.Issue(binding, 22).Difficulty).IsEqualTo(22);
        await Assert.That(() => service.Issue(binding, 15)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => service.Issue(binding, 23)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => service.Issue(binding, int.MaxValue)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(18)]
    [Arguments(22)]
    public async Task PublicWorkerEncoding_VerifiesNonByteAlignedDifficulty(int difficulty)
    {
        using var provider = CreateProvider();
        var service = CreateService(provider, new Clock());
        var binding = Binding();
        var challenge = service.Issue(binding, difficulty);
        await Assert.That(service.Validate(binding, challenge.ProtectedChallenge, Solve(challenge))).IsNotNull();
    }

    [Test]
    public async Task RealKeyRingSurvivesProviderRestartButRejectsOtherApplicationAndPurpose()
    {
        var directory = Directory.CreateTempSubdirectory("anonymous-challenge-keys-");
        try
        {
            var clock = new Clock();
            var binding = Binding();
            AnonymousRegistrationChallengeDto challenge;
            using (var first = PersistentProvider(directory, "islamu-event"))
                challenge = CreateService(first, clock).Issue(binding, 16);
            string nonce = Solve(challenge);
            using var restarted = PersistentProvider(directory, "islamu-event");
            await Assert.That(CreateService(restarted, clock).Validate(binding, challenge.ProtectedChallenge, nonce)).IsNotNull();
            using var otherApplication = PersistentProvider(directory, "another-application");
            await Assert.That(CreateService(otherApplication, clock).Validate(binding, challenge.ProtectedChallenge, nonce)).IsNull();
            var wrongPurpose = restarted.GetRequiredService<IDataProtectionProvider>().CreateProtector("different-purpose");
            await Assert.That(() => wrongPurpose.Unprotect(challenge.ProtectedChallenge)).Throws<CryptographicException>();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task AuthenticatedEnvelopeStillRequiresVersionOperationAndOriginalBoundedLifetime()
    {
        using var provider = CreateProvider();
        var service = CreateService(provider, new Clock());
        var binding = Binding();
        var challenge = service.Issue(binding, 16);
        var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Explore.AnonymousRegistrationChallenge.v1");
        var original = JsonNode.Parse(protector.Unprotect(challenge.ProtectedChallenge))!;
        var mutations = new (string Field, JsonNode? Value)[]
        {
            ("Version", JsonValue.Create(2)), ("Operation", JsonValue.Create("another-operation")),
            ("Difficulty", JsonValue.Create(15)), ("Difficulty", JsonValue.Create(23)),
            ("ExpiresAt", JsonValue.Create(challenge.ExpiresAt.AddSeconds(1))),
            ("OrderId", JsonValue.Create(Guid.Empty)), ("GuestCapabilityToken", null)
        };
        foreach (var mutation in mutations)
        {
            var changed = original.DeepClone();
            changed[mutation.Field] = mutation.Value;
            string token = protector.Protect(changed.ToJsonString());
            // Solve the valid public 16-bit work even for a semantically invalid signed envelope.
            string proof = Solve(new AnonymousRegistrationChallengeDto(token, challenge.ExpiresAt, 16, 1));
            await Assert.That(service.Validate(binding, token, proof)).IsNull();
        }
        string invalidJson = protector.Protect("{");
        await Assert.That(service.Validate(binding, invalidJson, "0000000000000000")).IsNull();
    }

    [Test]
    public async Task ApplicationConsumptionSnapshotsBusinessIntentAndRejectsInternalCommandMutation()
    {
        using var provider = CreateProvider();
        var service = CreateService(provider, new Clock());
        var binding = Binding();
        var challenge = service.Issue(binding, 16);
        string nonce = Solve(challenge);
        var lines = new[] { new RegistrationOrderLineSelection(Guid.CreateVersion7(), 1, null) };
        var intended = new StartGuestRegistrationOrderCommand(binding.EventId, Guid.CreateVersion7(),
            BookingPartyTypeEnum.Individual, lines);
        var raw = service.Validate(binding, challenge.ProtectedChallenge, nonce);
        await Assert.That(raw).IsNotNull();
        await Assert.That(raw!.Matches(intended)).IsFalse();
        var handler = new ConsumeAnonymousRegistrationChallengeCommandHandler(new TenantScope(binding.TenantId), service);
        var command = new ConsumeAnonymousRegistrationChallengeCommand(binding.EventId, binding.CanonicalRequestDigest,
            binding.IdempotencyKey, challenge.ProtectedChallenge, nonce, intended);
        var authority = await handler.Handle(command, CancellationToken.None);
        await Assert.That(authority).IsNotNull();
        await Assert.That(authority!.Matches(intended)).IsTrue();
        await Assert.That(authority.Matches(intended with { EventId = Guid.CreateVersion7() })).IsFalse();
        await Assert.That(authority.Matches(intended with { TicketCatalogVersionId = Guid.CreateVersion7() })).IsFalse();
        await Assert.That(authority.Matches(intended with { BookingPartyType = BookingPartyTypeEnum.Household })).IsFalse();
        await Assert.That(authority.Matches(intended with { PlatformContributionBasisPoints = 100 })).IsFalse();
        await Assert.That(authority.Matches(intended with { Lines = [lines[0] with { ChosenUnitPriceMinor = 500 }] })).IsFalse();
        var create = new CreateRegistrationOrderWithHoldCommand
        {
            EventId = intended.EventId, TicketCatalogVersionId = intended.TicketCatalogVersionId,
            BookingPartyType = intended.BookingPartyType, Lines = lines, GuestAccessTokenHash = authority.GuestAccessTokenHash
        };
        await Assert.That(authority.Matches(create)).IsTrue();
        await Assert.That(authority.Matches(create with { AccountUserId = Guid.CreateVersion7() })).IsFalse();
        await Assert.That(authority.Matches(create with { PurchaserActorId = Guid.CreateVersion7() })).IsFalse();
        await Assert.That(authority.Matches(create with { VerifiedContactNormalizedEmail = Guid.CreateVersion7().ToString("N") })).IsFalse();
        await Assert.That(authority.Matches(create with { GuestAccessTokenHash = new GuestCapabilityTokenService().Issue().Hash })).IsFalse();
        lines[0] = lines[0] with { Quantity = 2 };
        await Assert.That(authority.Matches(intended)).IsFalse();
        await Assert.That(authority.Matches(create)).IsTrue();
        await Assert.That(authority.Matches(create with { Lines = lines })).IsFalse();
        await Assert.That(JsonSerializer.Serialize(authority)).IsEqualTo("{}");
        await Assert.That(() => JsonSerializer.Deserialize<AnonymousRegistrationChallengeAuthority>("{}")).Throws<NotSupportedException>();
        await Assert.That(typeof(AnonymousRegistrationChallengeAuthority).GetConstructors()).IsEmpty();
        await Assert.That(await handler.Handle(command with { EventId = Guid.CreateVersion7() }, CancellationToken.None)).IsNull();
        await Assert.That(await handler.Handle(command with { CanonicalRequestDigest = string.Empty }, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task SameKeyAndBodyFreshIssuanceCannotAcquireOriginalOrderCapability()
    {
        using var provider = CreateProvider();
        var service = CreateService(provider, new Clock());
        var binding = Binding();
        var original = service.Issue(binding, 16);
        var replacement = service.Issue(binding, 16);
        var first = service.Validate(binding, original.ProtectedChallenge, Solve(original));
        var second = service.Validate(binding, replacement.ProtectedChallenge, Solve(replacement));
        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNotNull();
        await Assert.That(second!.OrderId).IsNotEqualTo(first!.OrderId);
        await Assert.That(second.GuestAccessTokenHash).IsNotEqualTo(first.GuestAccessTokenHash);
        await Assert.That(second.GuestCapabilityToken).IsNotEqualTo(first.GuestCapabilityToken);
    }

    private static ServiceProvider PersistentProvider(DirectoryInfo directory, string applicationName)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().PersistKeysToFileSystem(directory).SetApplicationName(applicationName);
        return services.BuildServiceProvider();
    }

    private sealed record TenantScope(Guid TenantId) : ITenantContext;

    private static ServiceProvider CreateProvider(string applicationName = "islamu-event")
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider().SetApplicationName(applicationName);
        return services.BuildServiceProvider();
    }

    private static AnonymousRegistrationChallengeService CreateService(IServiceProvider provider, Clock clock) =>
        new(provider.GetRequiredService<IDataProtectionProvider>(), new GuestCapabilityTokenService(), clock);

    private static AnonymousRegistrationChallengeBinding Binding() => new(Guid.CreateVersion7(), Guid.CreateVersion7(),
        Convert.ToHexString(SHA256.HashData(RandomNumberGenerator.GetBytes(32))), Guid.CreateVersion7().ToString("N"));

    private static string Solve(AnonymousRegistrationChallengeDto challenge)
    {
        for (ulong nonce = 0; nonce < ulong.MaxValue; nonce++)
        {
            string encoded = nonce.ToString("x16", CultureInfo.InvariantCulture);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("islamu-event:anonymous-registration:v1\n" + challenge.ProtectedChallenge + "\n" + encoded));
            // Independent worker condition for the published 16..22 interval: two zero bytes,
            // then a numeric third-byte threshold (rather than the server's bit-shift loop).
            if (hash[0] == 0 && hash[1] == 0 && (challenge.Difficulty == 16 || hash[2] < 1 << (24 - challenge.Difficulty)))
                return encoded;
        }
        throw new InvalidOperationException("The nonce space has no solution.");
    }

    private static bool Satisfies16(string challenge, string nonce)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("islamu-event:anonymous-registration:v1\n" + challenge + "\n" + nonce));
        return hash[0] == 0 && hash[1] == 0;
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
