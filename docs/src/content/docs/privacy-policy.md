---
title: Sylvan Privacy Policy
description: How Sylvan and SylvanMail handle your information and how to contact the operator about your data.
---

Last updated 10 November 2026

**ratgirl** operates Sylvan and SylvanMail for the Mathematics Discord server and is the data controller for the information these bots handle. For a
copy of your data, a correction, deletion or a privacy complaint, email [rattus-ratgirl@protonmail.com](mailto:rattus-ratgirl@protonmail.com). You can
use this address after leaving the server. Email goes to the operator. A DM to SylvanMail goes to the moderation team.

## Server messages and automatic moderation

Sylvan receives server messages and edits through Discord. Where automatic moderation is enabled, it checks members’ message text against rules set by
staff. A match can cause Sylvan to delete the message, time out its author or ban them. The reason recorded for a timeout or ban can include an
excerpt of the message. A separate spam check counts attachments posted by the same member across several channels within a short period. It can issue
a ban based on that pattern without inspecting the files. You can ask a moderator to review an automatic action
through [SylvanMail](https://discord.com/users/1567888927048863784).

In channels covered by edit or deletion logging, Sylvan takes a temporary snapshot of members’ messages. It includes the text, author ID, server,
channel and message IDs, timestamps, and attachment links, types and sizes. Excluded channels and logging destinations are skipped. The snapshot
contains no attachment files. **The current window is 24 hours from the time the original message was sent.** Edits do not restart the clock. The
operator may extend the window to a maximum of **14 days** and will update this policy before a longer period takes effect. Once a snapshot expires,
it cannot be used to produce a log, even if a database outage delays cleanup.

A log already posted to Discord can outlast the snapshot. An edit log can show the old and new text. A deletion log can include the deleted text and
copies of attachments Sylvan could recover. A bulk deletion can produce a transcript with usernames or IDs, timestamps, channels and text. Anyone who
can read the destination log channel can read what was posted there.

## Moderation tickets

A ticket records a staff note or a moderation action, such as a ban, kick, timeout, voice mute or deafen, or a tracked role change. It can contain the
member’s and moderators’ IDs, the reason, comments and evidence links, dates, duration, review decisions, changes to the record, and references to
Discord messages and audit entries. Staff can add a note without notifying the member. The database also contains tickets and their history from
Mathematics’ previous moderation bot.

Staff use this history to explain decisions, check earlier action and manage restrictions. Records can remain relevant after a restriction ends or a
member leaves. Hiding a ticket removes its ordinary listing and managed posts. The record remains available to authorised staff, and earlier lookup
replies can remain on Discord.

Ticket details are normally posted to designated channels. An authorised moderator can also use `/ticket show` or `/ticket showhidden` to post a
result in the channel where they run the command. Anyone with access to that channel can read the result, including any attached comment.

## Direct messages to SylvanMail

When you DM SylvanMail, it relays your message and supported attachments into a staff forum conversation identified by your username and Discord ID.
Staff with access to that conversation can read it. A new conversation can also show your recent ticket history. Staff can reply under their username
or as “staff”. If a reply is signed “staff”, the recipient does not see the moderator’s name. Staff and the operator can still identify who sent it.

Discord holds the messages and relayed files. Sylvan’s database links users to conversations and records sender, channel and message IDs, times and
delivery status to route replies and prevent duplicate sends. It does not keep a separate transcript or attachment archive as part of the mail
delivery record. Deleting a message on one side does not recall a copy already delivered to the other.

## Roles, preferences and diagnostics

Sylvan uses Discord IDs, roles, permissions and reactions to handle role choices and display colours. It can remember selected roles when you leave
and restore them if you rejoin. If you use staff Working Hours, it saves your user ID, schedule, time zone or offset, and whether the schedule is
enabled. Switching it off stops the schedule and leaves the saved times in the database.

To diagnose errors and delayed actions, Sylvan records technical information such as command names, times, outcomes, and user, channel, server and
message IDs. When a command targets a member or their message, the log can also identify that member by ID and username.

## Who receives the information and how long it is kept

The operator runs the production database, technical logs and backups on a private VPS hosted by **OVHcloud in Milan, Italy**. Italy is covered by the
UK’s adequacy arrangements for the European Economic Area. Discord hosts the server, DMs, staff posts and files. Discord may process them in the
United States and other countries under the safeguards described in its [Privacy Policy](https://discord.com/privacy). If you email the operator,
Proton Mail handles your address and correspondence.

| Information                                                   | Retention                                                                                                                  |
|---------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------|
| Live message snapshots for edit and deletion logging          | Currently 24 hours from message creation. The maximum permitted window is 14 days.                                         |
| Technical logs and backups on the VPS                         | Retained for 28 days. A backup taken before deletion can retain a copy until that backup expires.                          |
| Tickets, staff notes and case history                         | No automatic expiry. Kept while needed to manage restrictions, review decisions or understand relevant moderation history. |
| ModMail conversation and delivery records                     | No automatic expiry. Kept while needed to route correspondence, check delivery or resolve a case.                          |
| Published Discord logs, ticket lookups, staff threads and DMs | Remain on Discord until removed. The VPS log and backup periods do not remove these messages.                              |
| Role and schedule data                                        | Kept while needed to apply a preference or restore a role when a member rejoins.                                           |
| Privacy correspondence                                        | Kept while needed to verify, resolve and document the request.                                                             |

## Why we use this information

Under UK data protection law, the operator relies on legitimate interests to moderate Mathematics, give members a way to contact staff, provide role
features they use, and keep the bots reliable. These interests apply only where the processing is necessary and your rights and interests do not
override them. The operator also uses information as needed to comply with legal obligations, including responding to privacy requests.

**You can object** to processing based on legitimate interests. Email the operator and explain how the processing affects you so the operator can
consider your circumstances.

## Access, correction and deletion

Email [rattus-ratgirl@protonmail.com](mailto:rattus-ratgirl@protonmail.com) to ask what Sylvan holds about you, get a copy, correct an error, request
deletion or restrict how your data is used. Include your Discord user ID if available and enough detail to locate the records. The operator may ask
you to verify that the account is yours and will protect other people’s information when responding.

The operator will promptly delete your Discord API data on request unless the law requires retention. This includes identifying copies the operator
controls in the database and on Discord. Earlier backups expire on the 28-day schedule above. A hidden ticket is still stored, so you can request
deletion even if its channel post has disappeared. If you want staff to reconsider a sanction, say so explicitly so they can review the decision as
well.

If you are unhappy with how your information or request was handled, you can complain to the operator at the same address. You can also complain to
the [UK Information Commissioner’s Office](https://ico.org.uk/make-a-complaint/) or your local data protection authority.

Changes to this policy will appear on this page with a revised date.
