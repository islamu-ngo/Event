using Explore.Application.Models;
using Explore.Domain;

namespace Explore.Application.Contracts.Notifications;

/// <summary>Only durable dispatch coordinates cross into the final SMTP authority owner.</summary>
public sealed record ModerationNoticeEmailRequest(
    Guid TenantId, Guid OutboxId, Guid ProcessingLeaseToken, int AttemptNumber);

/// <summary>Transient original calendar guard, never an SMTP-derived acknowledgement or new retention clock.</summary>
public sealed record ModerationNoticeEmailWindow(DateTimeOffset RetainUntil, int? UnacknowledgedCalendarMonths)
{
    public bool Allows(DateTimeOffset now) => now.Offset == TimeSpan.Zero && now < RetainUntil
        && (UnacknowledgedCalendarMonths is not { } months || ModerationNoticeRetention.CanNotifyAt(now, months, RetainUntil));
}

/// <summary>
/// Executes the terminal transport once under current original-notice, recipient, erasure and
/// governance authority. The callback must start SMTP immediately and honor DisclosureUntilUtc.
/// Neither admission nor transport acceptance is subject acknowledgement.
/// </summary>
public interface IModerationNoticeEmailAuthority
{
    Task<EmailResult> SendAsync(ModerationNoticeEmailRequest request,
        Func<EmailMessage, CancellationToken, Task<EmailResult>> send,
        CancellationToken cancellationToken);
}
