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

Anyone who possesses the complete link can read its limited status. Do not post
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
check-in credential authority. This status surface does not add confirmed-order
self-cancellation.

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
