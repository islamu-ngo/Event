using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Tests.Services;

public sealed class LocalVisitorEnrollmentIntentProtectorTests
{
    [Test]
    public async Task RoundtripPreservesEveryBindingFieldAndUtcExpiryWithoutExposingMaterial()
    {
        var clock = new Clock();
        ILocalVisitorEnrollmentIntentProtector service =
            new LocalVisitorEnrollmentIntentProtector(new EphemeralDataProtectionProvider(), clock);
        var intent = CreateIntent(clock);

        var token = service.Protect(intent);
        var restored = service.Unprotect(token);

        await Assert.That(restored).IsEqualTo(intent);
        await Assert.That(restored!.ExpiresAt.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(token.Length).IsLessThanOrEqualTo(2048);
        foreach (var material in new[] { intent.OperationId.ToString("D"), intent.TenantId.ToString("D"), intent.ProofDigest })
            await Assert.That(token.Contains(material, StringComparison.Ordinal)).IsFalse();
        await Assert.That(intent.ToString()).IsEqualTo(nameof(LocalVisitorEnrollmentIntent));
        await Assert.That(restored.ToString()).IsEqualTo(nameof(LocalVisitorEnrollmentIntent));
    }

    [Test]
    public async Task ModifiedWrongKeyAndOtherWorkflowTokensCannotOpen()
    {
        var clock = new Clock();
        var provider = new EphemeralDataProtectionProvider();
        var service = new LocalVisitorEnrollmentIntentProtector(provider, clock);
        var intent = CreateIntent(clock);
        var token = service.Protect(intent);
        var middle = token.Length / 2;
        var modified = token[..middle] + (token[middle] == 'A' ? 'B' : 'A') + token[(middle + 1)..];
        var otherWorkflowToken = new EventReportEvidenceProtector(provider).Protect(JsonSerializer.Serialize(intent));
        var otherKeys = new LocalVisitorEnrollmentIntentProtector(new EphemeralDataProtectionProvider(), clock);

        await Assert.That(service.Unprotect(modified)).IsNull();
        await Assert.That(otherKeys.Unprotect(token)).IsNull();
        await Assert.That(service.Unprotect(otherWorkflowToken)).IsNull();
        await Assert.That(() => provider.CreateProtector("ISLAMU.EventReporting.Evidence.v1").Unprotect(token))
            .Throws<CryptographicException>();
    }

    [Test]
    public async Task ReprotectingReservationPreservesOriginalDeadlineAndExpiresAtExactTick()
    {
        var clock = new Clock();
        var service = new LocalVisitorEnrollmentIntentProtector(new EphemeralDataProtectionProvider(), clock);
        var intent = CreateIntent(clock);
        var original = service.Protect(intent);
        clock.Now = clock.Now.AddMinutes(5);

        var replay = service.Protect(intent);

        await Assert.That(service.Unprotect(replay)).IsEqualTo(intent);
        clock.Now = new DateTimeOffset(intent.ExpiresAt.AddTicks(-1));
        await Assert.That(service.Unprotect(original)).IsEqualTo(intent);
        await Assert.That(service.Unprotect(replay)).IsEqualTo(intent);
        clock.Now = clock.Now.AddTicks(1);
        await Assert.That(service.Unprotect(original)).IsNull();
        await Assert.That(service.Unprotect(replay)).IsNull();
        await Assert.That(() => service.Protect(intent)).Throws<ArgumentException>();
        clock.Now = clock.Now.AddTicks(1);
        await Assert.That(service.Unprotect(original)).IsNull();
    }

    [Test]
    public async Task MalformedEmptyAndOversizedProtectedInputsReturnNull()
    {
        var clock = new Clock();
        var service = new LocalVisitorEnrollmentIntentProtector(new EphemeralDataProtectionProvider(), clock);
        var token = service.Protect(CreateIntent(clock));

        foreach (var invalid in new[] { null!, "", " ", "invalid", "%%%", token + "broken", new string('A', 2049) })
            await Assert.That(service.Unprotect(invalid)).IsNull();
    }

    [Test]
    public async Task AuthenticatedMalformedPayloadsReturnNull()
    {
        var clock = new Clock();
        var provider = new EphemeralDataProtectionProvider();
        var service = new LocalVisitorEnrollmentIntentProtector(provider, clock);
        var writer = provider.CreateProtector("ISLAMU.LocalVisitorEnrollment.Intent.v1");

        foreach (var plaintext in new[] { "", "{", "null", "{}", "[]", "{\"ExpiresAt\":\"invalid\"}", "{\"OperationId\":17}" })
            await Assert.That(service.Unprotect(writer.Protect(plaintext))).IsNull();
    }

    [Test]
    public async Task InvalidBindingsCannotBeMintedOrReturnedEvenWhenAuthenticated()
    {
        var clock = new Clock();
        var provider = new EphemeralDataProtectionProvider();
        var service = new LocalVisitorEnrollmentIntentProtector(provider, clock);
        var writer = provider.CreateProtector("ISLAMU.LocalVisitorEnrollment.Intent.v1");
        var valid = CreateIntent(clock);
        LocalVisitorEnrollmentIntent[] invalidBindings =
        [
            valid with { OperationId = Guid.Empty },
            valid with { TenantId = Guid.Empty },
            valid with { ProofDigest = null! },
            valid with { ProofDigest = "" },
            valid with { ProofDigest = valid.ProofDigest[..63] },
            valid with { ProofDigest = valid.ProofDigest + "0" },
            valid with { ProofDigest = "A" + valid.ProofDigest[1..] },
            valid with { ProofDigest = "g" + valid.ProofDigest[1..] },
            valid with { ProofDigest = "\u0660" + valid.ProofDigest[1..] },
            valid with { ExpiresAt = clock.Now.UtcDateTime },
            valid with { ExpiresAt = clock.Now.UtcDateTime.AddTicks(-1) },
            valid with { ExpiresAt = clock.Now.UtcDateTime.AddMinutes(15).AddTicks(1) },
            valid with { ExpiresAt = DateTime.SpecifyKind(valid.ExpiresAt, DateTimeKind.Unspecified) },
            valid with { ExpiresAt = DateTime.SpecifyKind(valid.ExpiresAt, DateTimeKind.Local) }
        ];

        foreach (var invalid in invalidBindings)
        {
            var failure = await Assert.That(() => service.Protect(invalid)).Throws<ArgumentException>();
            foreach (var material in new[] { valid.ProofDigest, valid.OperationId.ToString("D"), valid.TenantId.ToString("D") })
                await Assert.That(failure!.ToString().Contains(material, StringComparison.Ordinal)).IsFalse();
            await Assert.That(service.Unprotect(writer.Protect(JsonSerializer.Serialize(invalid)))).IsNull();
        }
        await Assert.That(() => service.Protect(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ClockFailuresAndCancellationPropagateInsteadOfBecomingInvalidInput()
    {
        var clock = new Clock();
        var service = new LocalVisitorEnrollmentIntentProtector(new EphemeralDataProtectionProvider(), clock);
        var intent = CreateIntent(clock);
        var token = service.Protect(intent);
        clock.Failure = new InvalidOperationException();

        await Assert.That(() => service.Protect(intent)).Throws<InvalidOperationException>();
        await Assert.That(() => service.Unprotect(token)).Throws<InvalidOperationException>();
        clock.Failure = new OperationCanceledException();
        await Assert.That(() => service.Protect(intent)).Throws<OperationCanceledException>();
        await Assert.That(() => service.Unprotect(token)).Throws<OperationCanceledException>();
        clock.Failure = new FormatException();
        await Assert.That(() => service.Unprotect(token)).Throws<FormatException>();
        clock.Failure = new CryptographicException();
        await Assert.That(() => service.Unprotect(token)).Throws<CryptographicException>();
    }

    private static LocalVisitorEnrollmentIntent CreateIntent(Clock clock) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Convert.ToHexStringLower(SHA256.HashData(RandomNumberGenerator.GetBytes(32))),
            clock.Now.UtcDateTime.AddMinutes(15));

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public Exception? Failure { get; set; }
        public override DateTimeOffset GetUtcNow() => Failure is { } failure ? throw failure : Now;
    }
}
