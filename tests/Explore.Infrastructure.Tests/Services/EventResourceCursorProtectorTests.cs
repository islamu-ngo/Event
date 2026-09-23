using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Services;

public sealed class EventResourceCursorProtectorTests
{
    [Test]
    public async Task PositionIsOpaqueAndBoundToTenantEventSubjectAnonymousAndMachineIdentity()
    {
        var clock = new Clock();
        var service = new EventResourceCursorProtector(new EphemeralDataProtectionProvider(), clock);
        var scope = new EventResourceCursorScope(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), false);
        var position = new EventResourceCursorPosition(17, Guid.CreateVersion7());
        var token = service.Protect(scope, position);
        await Assert.That(token.Length).IsLessThanOrEqualTo(2048);
        await Assert.That(token.Contains(position.ResourceId.ToString("D"), StringComparison.Ordinal)).IsFalse();
        await Assert.That(service.TryUnprotect(token, scope, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(position);
        EventResourceCursorScope[] wrongScopes =
        [
            scope with { TenantId = Guid.CreateVersion7() }, scope with { EventId = Guid.CreateVersion7() },
            scope with { SubjectUserId = Guid.CreateVersion7() }, scope with { SubjectUserId = null },
            scope with { IsMachineCaller = true }
        ];
        foreach (var wrong in wrongScopes)
        {
            await Assert.That(service.TryUnprotect(token, wrong, out decoded)).IsFalse();
            await Assert.That(decoded).IsNull();
        }
        var anonymous = scope with { SubjectUserId = null };
        var anonymousToken = service.Protect(anonymous, position);
        await Assert.That(service.TryUnprotect(anonymousToken, scope, out _)).IsFalse();
        await Assert.That(service.TryUnprotect(anonymousToken, anonymous, out _)).IsTrue();
    }

    [Test]
    public async Task ExpiryIsHalfOpenAndMalformedWrongVersionAndLostKeysRequireRefresh()
    {
        var clock = new Clock();
        var provider = new EphemeralDataProtectionProvider();
        var service = new EventResourceCursorProtector(provider, clock);
        var scope = new EventResourceCursorScope(Guid.CreateVersion7(), Guid.CreateVersion7(), null, false);
        var position = new EventResourceCursorPosition(0, Guid.CreateVersion7());
        var token = service.Protect(scope, position);
        clock.Now = clock.Now.AddMinutes(15).AddTicks(-1);
        await Assert.That(service.TryUnprotect(token, scope, out _)).IsTrue();
        clock.Now = clock.Now.AddTicks(1);
        await Assert.That(service.TryUnprotect(token, scope, out _)).IsFalse();
        var wrongVersion = provider.CreateProtector("EventResource.AudienceCursor.v1").Protect(JsonSerializer.Serialize(
            new { Version = 2, Scope = scope, Position = position, ExpiresAt = clock.Now.AddMinutes(1) }));
        foreach (var invalid in new[] { "", "invalid", token + "broken", wrongVersion })
            await Assert.That(service.TryUnprotect(invalid, scope, out _)).IsFalse();
        var restarted = new EventResourceCursorProtector(new EphemeralDataProtectionProvider(), clock);
        await Assert.That(restarted.TryUnprotect(service.Protect(scope, position), scope, out _)).IsFalse();
    }

    [Test]
    public async Task OversizedInputIsRejectedBeforeCryptography()
    {
        bool invoked = false;
        var protector = Substitute.For<IDataProtector>();
        protector.Unprotect(Arg.Any<byte[]>()).Returns(call => { invoked = true; return call.Arg<byte[]>(); });
        var provider = Substitute.For<IDataProtectionProvider>();
        provider.CreateProtector(Arg.Any<string>()).Returns(protector);
        var service = new EventResourceCursorProtector(provider, new Clock());
        var scope = new EventResourceCursorScope(Guid.CreateVersion7(), Guid.CreateVersion7(), null, false);
        await Assert.That(service.TryUnprotect(new string('A', 2049), scope, out var decoded)).IsFalse();
        await Assert.That(decoded).IsNull();
        await Assert.That(invoked).IsFalse();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
