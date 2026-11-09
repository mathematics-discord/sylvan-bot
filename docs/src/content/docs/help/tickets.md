---
title: Troubleshoot a ticket
description: Check missing prompts and ticket posts, pending Discord changes, and pauses in enforcement or expiry.
---

If Sylvan reports a problem after a save, hide, or reversal, check the saved
ticket and the member’s current restriction in Discord. A change
can succeed in one place before Sylvan confirms it in the other.

## The ticket is missing

- **No DM arrived.** Run `/ticket top` for your newest outstanding prompt,
  or [find the ticket](../../staff/tickets/find-a-ticket/) and use
  `/ticket edit` in the server. A completed ban or timeout may have no
  reminder left to resend.
- **A lookup found nothing.** Check which member, ticket number, or ticket
  message you supplied. `/ticket show` opens visible tickets by number.
  Senior staff can use `/ticket showhidden` for a hidden record. Both slash
  commands post their replies in the channel.
- **The ticket was saved but publication is unconfirmed.** Look up the
  saved number and check the configured destination. Creating the same note
  again can produce a duplicate. Sylvan retries updates to known posts.
  If it cannot confirm the first post, it won’t automatically send another.
  Ask an administrator to investigate if the post never appears.
- **Some channel copies could not be synchronised.** The saved record
  remains. Check which posts still show the old details. Sylvan retries
  their update or removal.

## A Discord change is pending or unconfirmed

**Pending** means Sylvan is waiting to confirm a Discord change. A ticket
with a pending operation cannot be approved or have its details edited.
Check the member’s live restriction, give Sylvan time to read the audit log,
then reopen the record.

| What you see | What to check |
| --- | --- |
| Saving could not be confirmed | Read the saved fields again. For a reversal or hide, also check whether the restriction was already lifted. |
| Discord rejected a timeout change | Check the member, role hierarchy, and Sylvan’s timeout permission. Read the comment and duration separately. A comment submitted with a rejected duration can still be saved. |
| A newer change awaits audit reconciliation | Another action may have changed the restriction. Let Sylvan read that change, then reopen the ticket. |
| The editor says the ticket changed | Reopen the editor to see the latest details before resubmitting your correction. |

## The restriction is still active after expiry

Check **Expires** on the ticket. Durations run from the original action.
Ban, voice, and tracked-role expiry depend on Sylvan confirming which action
is current. A voice restriction may have to wait for the member to reconnect
to voice.

**Enforcement paused** on a timeout or **Expiry paused** on another
restriction means Sylvan needs confirmation before continuing. Check the
live restriction and intended expiry. Once audit recovery is complete,
open **Edit**, confirm the intended duration, and save. If the pause persists,
ask an administrator to investigate with the ticket number and the exact
reply Sylvan gave.
