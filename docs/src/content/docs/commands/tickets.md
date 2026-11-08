---
title: Ticket commands
description: Syntax and examples for /ticket, ticket message actions and /config tickets.
---

For the staff workflow and permission table, see [Moderation tickets](../../features/tickets/). The examples below use ticket `1234`. Substitute the
number printed on your ticket.

## Look up tickets

| Command              | Options                                                                                                                                     |
|----------------------|---------------------------------------------------------------------------------------------------------------------------------------------|
| `/ticket show`       | Choose one of `user:` (mention or user ID), `id:` (ticket number) or `message:` (Sylvan ticket message link). Add `page:` to a user lookup. |
| `/ticket showhidden` | Same selectors, with access to hidden records for senior staff.                                                                             |

## Create and edit

| Command                                                             | Use                                                                                                                   |
|---------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------|
| `/ticket note user:@member`                                         | Opens a comment editor. Supply `comment:` to save the note directly. A user ID works for a departed member.           |
| `/ticket edit id:1234`                                              | Opens the editor with the existing details.                                                                           |
| `/ticket set id:1234 duration:7 days`                               | Replaces the total duration. You can also supply `comment:` to replace the comment. Omitted fields keep their values. |
| `/ticket append id:1234 comment:Member contacted staff by ModMail.` | Adds text to the existing comment.                                                                                    |
| `/ticket top`                                                       | Requests redelivery of your newest outstanding ticket prompt.                                                         |

Enter a positive duration such as `90 minutes`, `2 days 6 hours` or `3 weeks`. A month is 30 days and a year is 365 days. Use `permanent` for an
open-ended ban, voice restriction or tracked role. A timeout needs a finite duration. Notes and kicks have no duration.

## Review and reverse

| Command                            | Use                                                        |
|------------------------------------|------------------------------------------------------------|
| `/ticket review`                   | Opens your private review queue.                           |
| `/ticket revert id:1234`           | Lifts the active restriction and keeps the ticket visible. |
| `/ticket hide id:1234`             | Lifts an active restriction before hiding its record.      |
| `/ticket set id:1234 hidden:False` | Restores the record.                                       |

On a Sylvan ticket message, open **Apps** for **Manage Ticket**, **Approve Ticket**, **Revert Ticket** or **Hide Ticket**. If a send or Discord change
is reported as unconfirmed, use `/ticket show id:1234` and check the existing message and live restriction before trying again.

## Administrator configuration

Run `/config tickets` with no options to display the current settings. To enable capture, supply a staff text channel. For example,
`/config tickets channel:#tickets enabled:True`. Choose a channel where staff can read ticket history and Sylvan can read history, send messages and
embed links. Audit capture also requires View Audit Log.

| Option                                       | Effect                                                                                                                     |
|----------------------------------------------|----------------------------------------------------------------------------------------------------------------------------|
| `enabled:`                                   | Enables or pauses ticket management for this server. Disabling pauses automated handling, including long-timeout renewals. |
| `channel:`                                   | Sets the destination for ticket messages and the review summary.                                                           |
| `reminder-minutes:`                          | Sets the DM reminder interval in whole minutes. Minimum 1, default 1,440.                                                  |
| `exempt-role-add:` / `exempt-role-remove:`   | Changes which acting moderators are exempt from review for newly captured tickets.                                         |
| `tracked-role-add:` / `tracked-role-remove:` | Changes which ordinary role grants create tickets. Role tracking and role persistence are configured separately.           |
