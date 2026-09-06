// ABOUTME: Specifies private transient Application validation independently of HTTP and authentication.
// ABOUTME: Guards exact expiry ceilings, UTF-8 bounds and the sole tenant-free OAuth read exception.

using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Explore.Application.Features.Authentication.Atproto.Validators;
using Explore.Domain;

namespace Event.Application.UnitTests.Features.Authentication;

public sealed class AtprotoTransientValidationTests
{
    private static readonly TimeProvider Clock = new FixedClock();
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    [Arguments(AtprotoTransientPurpose.OAuthState, 600)]
    [Arguments(AtprotoTransientPurpose.TenantHandoff, 120)]
    public async Task CreateLifetime_HasExactPurposeCeiling(AtprotoTransientPurpose purpose, int seconds)
    {
        var validator = new CreateAtprotoTransientCommandValidator(Clock);
        long now = Clock.GetUtcNow().ToUnixTimeMilliseconds();
        var valid = new CreateAtprotoTransientCommand(purpose, Digest, Guid.CreateVersion7(), "opaque", now + seconds * 1000);
        await Assert.That((await validator.ValidateAsync(valid)).IsValid).IsTrue();
        await Assert.That((await validator.ValidateAsync(valid with { ExpiresAtUnixMilliseconds = now + seconds * 1000 + 1 })).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(valid with { ExpiresAtUnixMilliseconds = now })).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(valid with { ExpiresAtUnixMilliseconds = long.MaxValue })).IsValid).IsFalse();
    }

    [Test]
    public async Task ProtectedPayloadBound_CountsUtf8Bytes_NotCharacters()
    {
        var validator = new CreateAtprotoTransientCommandValidator(Clock);
        var request = new CreateAtprotoTransientCommand(AtprotoTransientPurpose.OAuthState, Digest,
            Guid.CreateVersion7(), new string('\u00e9', 32768), Clock.GetUtcNow().AddMinutes(1).ToUnixTimeMilliseconds());
        await Assert.That((await validator.ValidateAsync(request)).IsValid).IsTrue();
        await Assert.That((await validator.ValidateAsync(request with { ProtectedPayload = request.ProtectedPayload + "a" })).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(request with { ProtectedPayload = "" })).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(request with { TokenDigest = Digest.ToUpperInvariant() })).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(request with { TenantId = Guid.Empty })).IsValid).IsFalse();
    }

    [Test]
    public async Task OnlyOAuthReadCanOmitTenant_AndNoOrdinaryOperationAcceptsHealthProbe()
    {
        var read = new ReadAtprotoTransientQuery(AtprotoTransientPurpose.OAuthState, Digest, null);
        var readValidator = new ReadAtprotoTransientQueryValidator();
        await Assert.That((await readValidator.ValidateAsync(read)).IsValid).IsTrue();
        await Assert.That((await readValidator.ValidateAsync(read with { ExpectedTenantId = Guid.Empty })).IsValid).IsFalse();
        await Assert.That((await readValidator.ValidateAsync(read with { Purpose = AtprotoTransientPurpose.TenantHandoff })).IsValid).IsFalse();
        await Assert.That((await readValidator.ValidateAsync(read with { Purpose = AtprotoTransientPurpose.HealthProbe })).IsValid).IsFalse();
        var consume = new ConsumeAtprotoTransientCommand(Guid.CreateVersion7(), AtprotoTransientPurpose.OAuthState, Digest, Guid.Empty);
        await Assert.That((await new ConsumeAtprotoTransientCommandValidator().ValidateAsync(consume)).IsValid).IsFalse();
        consume = consume with { ExpectedTenantId = Guid.CreateVersion7(), Purpose = AtprotoTransientPurpose.HealthProbe };
        await Assert.That((await new ConsumeAtprotoTransientCommandValidator().ValidateAsync(consume)).IsValid).IsFalse();
        var create = new CreateAtprotoTransientCommand(AtprotoTransientPurpose.HealthProbe, Digest, Guid.CreateVersion7(), "opaque",
            Clock.GetUtcNow().AddSeconds(30).ToUnixTimeMilliseconds());
        await Assert.That((await new CreateAtprotoTransientCommandValidator(Clock).ValidateAsync(create)).IsValid).IsFalse();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
    }
}
