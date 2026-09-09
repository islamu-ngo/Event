---
description: "Keep a private guest status link when registration does not rely on email."
---
<!-- ABOUTME: Explains private post-confirmation guest status and explicit bookmark preservation. -->
<!-- ABOUTME: Separates status access from checkout, attendee credentials and public calendar export. -->

# Guest Participation Without Email

Guest registration does not require an email-delivery promise. Before a new guest
reservation, the event must have a finite authoritative session end so the server
can establish a bounded private status window. Normal ticket, capacity, approval,
payment and browser-challenge rules still apply.

## Save Your Private Status Link

After confirmation, use the offered guest-status action, then explicitly copy or
download its private link. The current browser's in-memory checkout capability is
not a backup. Save the link somewhere private before closing the page.

Anyone who possesses the complete link can read its limited status and can cancel
an eligible free guest confirmation when the server offers that action. Do not post
it publicly, include it in analytics or send it in support tickets. If you lose
the link and the browser's capability, there is no email-based or guessed-identity
recovery path.

The private part of the link is a URL fragment. The page removes that fragment
before analytics or network initialization; later status requests send the
capability only in the protected request header. It is not placed in query
strings, server-rendered links or the calendar file.

## Status Window And Rescheduling

The initial status window is the event's authoritative last session end plus
30 days, recorded with the guest reservation. It is separate from the checkout
hold deadline: a confirmed guest can inspect status after the hold has expired.

An earlier reschedule or a temporarily missing end cannot shorten an existing
live promise. An authorized status read can extend a still-live promise when the
current event ends later. The page displays the server's current access deadline;
check it while access is live after a reschedule. Expired promises do not revive,
and records created without a status promise do not gain one by guessing a link.

The page shows only safe order/event lifecycle facts and the access deadline.
Event cancellation and order cancellation are distinct facts; an event marked
cancelled does not itself prove that every downstream action has finished.
The status link does not grant checkout, payment, form-editing, attendee-data or
check-in credential authority.

## Cancelling An Eligible Free Guest Confirmation

Use cancellation only when the private status page offers it, and confirm the
exact registration shown. Dismissing the confirmation sends no cancellation.
The server checks current eligibility again when the POST arrives; a previously
visible action can become unavailable.

This operation is limited to free anonymous confirmations with no paid-order
history and no relevant check-in history. A zero displayed balance alone is not
proof that an order is free. Paid/refunded orders, account-owned registrations,
staff corrections and unsupported states retain their existing processes.
A recorded check-in prevents this guest cancellation even if it was later undone.
If check-in and cancellation overlap, the server checks committed admission
history rather than relying on the status page's earlier eligibility result.

Successful cancellation changes the order, revokes its eligible admission and
releases consumed capacity in one transaction. Repeating the same successful
operation does not release places twice. Opening or refreshing the status page
does not cancel anything. If the server reports a conflict, use the refreshed
status rather than assuming cancellation succeeded.

The existing finite private-status window still applies. An unconfirmed order
does not gain post-confirmation cancellation authority from its checkout link.
Contact the organizer for situations outside this narrow self-service action.

## Anonymous Data And Retention

When the ticket requires no attendee information, registration creates unnamed
participants without contact details. A required entry name does not require a
purchaser email address.

Anonymous names and answers are available only until their event-purpose deadline.
The default is the event's end plus seven days; an authorized instance or tenant
administrator can set `anonymous_registration.retention_days` from 0 through 30
for new registrations. Zero means the event end. A field's shorter deadline still
applies.

The deadline is fixed when the guest registration starts. Editing a name,
claiming the registration with an account, changing the setting, cancelling or
rescheduling does not extend it. The private status link can remain usable longer
because it contains no attendee names or answers.

Expired data becomes unavailable before the background deletion sweep runs.
Legal holds may preserve required records without keeping them available through
ordinary attendee, organizer or export access. Consent and audit evidence follow
their separate policies. Existing guest data without a recorded original deadline
is unavailable rather than receiving a new retention window.

Registration exports stored by Event obey the same access boundary. An external
provider or a person who already downloaded data may retain their own copy;
Event's expiry does not prove deletion of those copies.
An internal queued email is not an external delivery: an expired registration
contact cannot be decrypted and newly sent just because it was queued earlier.
Already-started external delivery retains its existing outcome; expiry does not
retract a message already handed to the provider.

## Public Calendar Export

When the server offers a calendar action, it uses the existing public event
calendar export. The `.ics` contains public event/location information, never the
guest capability or attendee data. A private or cancelled event may have no public
calendar action while its limited private status remains available.

A downloaded calendar is a static file, not a subscription or a delivery
guarantee. Recheck public event information for schedule updates. Keep your private
status bookmark separately; the calendar is not a way to recover it.

## Related Guides

* [Visitor policy and anonymous challenges](modular-event-aspects.md)
* [Ticketing and check-in](ticketing-and-check-in.md)
* [Authentication](../security-and-identity/authentication.md)
