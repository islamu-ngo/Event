using System.ComponentModel.DataAnnotations.Schema;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Explore.Persistence.Services;

/// <summary>
/// Resolves the email dispatch recipient projection through the native
/// <c>email_delivery_recipient</c> SECURITY DEFINER function on PostgreSQL, keeping
/// users/user_pii reads behind the email recipient owner role; other providers use an
/// equivalent entity projection. Both paths feed
/// <see cref="RecipientEmailAddressResolver.Resolve(EmailRecipient?, Guid)"/>.
/// </summary>
internal static class EmailDeliveryRecipientReader
{
    private sealed record RecipientRow(
        [property: Column("user_id")] Guid UserId,
        [property: Column("is_deleted")] bool IsDeleted,
        [property: Column("contact_email")] string? ContactEmail,
        [property: Column("claim_id")] Guid? ClaimId,
        [property: Column("normalized_email")] string? ClaimEmail,
        [property: Column("claim_verified")] bool ClaimVerified);

    public static async Task<EmailRecipient?> ReadAsync(
        ExploreDbContext context, Guid recipientUserId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql())
        {
            return await context.Users
                .AsNoTracking()
                .Where(user => user.Id == recipientUserId)
                .Select(user => new EmailRecipient(
                    user.Id,
                    user.IsDeleted,
                    user.Pii != null ? user.Pii.Email : null,
                    user.IdentityEmailClaims
                        .Where(claim => claim.Evidence.Any(evidence =>
                            evidence.UserId == recipientUserId
                            && evidence.ClaimId == claim.Id
                            && evidence.IsActive))
                        .Select(claim => new EmailRecipientClaim(claim.Id, claim.NormalizedEmail))
                        .ToList()))
                .SingleOrDefaultAsync(cancellationToken);
        }

        // PostgreSQL: users/user_pii are function-mediated for every application login.
        string query = $"""
            SELECT r.user_id, r.is_deleted, r.contact_email,
                r.claim_id, r.normalized_email, r.claim_verified
            FROM {PostgresEmailDeliveryPolicyContract.RecipientFunctionSql(context)}(@recipient_id) r
            """;
        var rows = await context.Database.SqlQueryRaw<RecipientRow>(query,
                new NpgsqlParameter("recipient_id", recipientUserId))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return null;

        RecipientRow head = rows[0];
        return new EmailRecipient(
            head.UserId,
            head.IsDeleted,
            head.ContactEmail,
            rows.Where(row => row.ClaimId is not null && row.ClaimVerified)
                .Select(row => new EmailRecipientClaim(row.ClaimId!.Value, row.ClaimEmail!))
                .ToList());
    }
}
