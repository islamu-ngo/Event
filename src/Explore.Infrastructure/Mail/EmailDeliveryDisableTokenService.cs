// ABOUTME: Protects five-minute email-disable previews with purpose-isolated ASP.NET Core Data Protection.
// ABOUTME: Binds the actor and canonical impact digest without placing unbounded scope lists in tokens.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Mail;

public sealed class EmailDeliveryDisableTokenService : IEmailDeliveryDisableTokenService
{
    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeProvider _timeProvider;

    public EmailDeliveryDisableTokenService(IDataProtectionProvider dataProtectionProvider)
        : this(dataProtectionProvider, TimeProvider.System)
    {
    }

    internal EmailDeliveryDisableTokenService(IDataProtectionProvider dataProtectionProvider, TimeProvider timeProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("ISLAMU.Email.DeliveryDisable.v1")
            .ToTimeLimitedDataProtector();
        _timeProvider = timeProvider;
    }

    public EmailDeliveryDisableToken Issue(Guid actorUserId, EmailDeliveryDisableImpactSnapshot snapshot)
    {
        if (!IsValid(actorUserId, snapshot))
        {
            throw new ArgumentException("A valid actor and disableable impact snapshot are required.");
        }

        var expiresAtUtc = _timeProvider.GetUtcNow().AddMinutes(5);
        var token = _protector.Protect(Convert.ToHexString(ComputeDigest(actorUserId, snapshot)), expiresAtUtc);
        return new EmailDeliveryDisableToken(token, expiresAtUtc);
    }

    public bool Matches(string? token, Guid actorUserId, EmailDeliveryDisableImpactSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048 || !IsValid(actorUserId, snapshot))
        {
            return false;
        }

        try
        {
            var actual = Encoding.UTF8.GetBytes(_protector.Unprotect(token));
            var expected = Encoding.UTF8.GetBytes(Convert.ToHexString(ComputeDigest(actorUserId, snapshot)));
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool IsValid(Guid actorUserId, EmailDeliveryDisableImpactSnapshot? snapshot)
    {
        if (actorUserId == Guid.Empty || snapshot is null || !snapshot.CanDisable
            || snapshot.TenantId == Guid.Empty || snapshot.Revision < 0)
        {
            return false;
        }

        var scopeIds = new HashSet<Guid?>();
        return snapshot.AffectedScopes.All(scope => scope is not null && scope.TenantId != Guid.Empty
            && scope.Revision >= 0 && scopeIds.Add(scope.TenantId));
    }

    private static byte[] ComputeDigest(Guid actorUserId, EmailDeliveryDisableImpactSnapshot snapshot) =>
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorUserId = actorUserId,
            snapshot.TenantId,
            snapshot.Revision,
            snapshot.IsLocked,
            AffectedScopes = snapshot.AffectedScopes.OrderBy(scope => scope.TenantId).ToArray()
        }));
}
