# Ticket-managed long timeouts

Edit an active timeout ticket's duration through the existing ticket commands,
edit form or moderator DM flow. For example, `90 days` means 90 days from the
original timeout, not from the edit. Existing ticket permissions still apply.
Permanent timeouts remain unsupported. Editing an ended ticket only corrects
its historical details.

The ticket stores the full duration. Sylvan applies native Discord windows of
at most 28 days minus one minute and checks active timeouts daily. When a window
has seven days or less remaining, Sylvan renews it. The final window ends at the
ticket's exact expiry. Renewals do not add moderator history or reset approval.

A direct Discord timeout change replaces the managed duration; a direct removal
cancels it. A member leaving does not cancel their ticket. Sylvan checks it on
rejoin and reapplies the remaining restriction after audit reconciliation.
Startup and reconnect checks recover missed join notifications.

Ticket management must stay enabled, and Sylvan needs Moderate Members, View
Audit Log and sufficient role hierarchy. Disabling ticket management pauses
renewals. If Sylvan cannot renew before a native window expires, enforcement can
lapse until recovery. After more than 45 days without a successful audit scan,
automatic enforcement pauses because intervening moderator actions cannot be
verified. An authorised duration edit can confirm the restriction after audit
recovery. Discord failures are logged with the ticket, guild and member IDs.

## Staging validation

Use a non-production guild and an eligible test member. These checks require
Discord and are separate from the application-owned automated tests:

- Apply a native timeout, then edit its ticket to `90 days`. Check the full
  duration on the ticket and a native expiry within Discord's 28-day limit.
- Exercise each existing edit path and its permission checks.
- In a temporary staging build, shorten the native window, renewal threshold
  and daily check interval together. Observe multiple renewals without changing
  the final expiry, approval or moderator history, or creating another ticket.
- Shorten a long timeout into a final native window, then into the past. Verify
  natural expiry and immediate removal respectively.
- Change and remove the timeout directly in Discord; ensure Sylvan respects
  both actions, including around a renewal attempt.
- Leave and rejoin before final expiry, including while Sylvan is disconnected.
  Check that the remaining restriction is reapplied. Return after final expiry
  and confirm that no timeout is applied.
- Restart during an uncertain timeout request and exercise delayed audit
  delivery. Confirm recovery preserves the intended final expiry.
- Remove Sylvan's timeout permission, observe a logged renewal failure, restore
  it and verify recovery. Verify hidden tickets still renew, disabled management
  pauses work, and an unverifiable audit gap suspends enforcement.

The forward migration adds timing fields and backfills existing timeout and
pending-operation timestamps. It does not change existing intended expiries.
Generating it does not apply it to a database.
