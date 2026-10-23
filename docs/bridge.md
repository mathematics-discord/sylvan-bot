# Bridge

Bridge is an authenticated WebSocket API hosted inside Sylvan. It uses Sylvan's existing Discord client and bot identity. The connecting application receives eligible new messages and can request typing or replies to those messages. Sylvan keeps its Discord token; clients use a separate Bridge credential.

## Configuration and hosting

Bridge runs during normal bot operation, including when no channels are configured. There is no enable switch. Configuration uses the existing .NET configuration providers, including `.env` and environment variables. A missing or invalid Bridge credential fails startup. Supply a **separate random secret**, at least 32 printable ASCII characters without spaces; do not reuse the Discord token.

```dotenv
DISCORD__TOKEN=<discord-bot-token-kept-in-sylvan>
BRIDGE__CREDENTIAL=<separate-random-bridge-secret-at-least-32-characters>
BRIDGE__ADDRESS=127.0.0.1
BRIDGE__PORT=8765

# Optional non-privileged Discord configuration:
DISCORD__GATEWAYINTENTS=Guilds,GuildMessages
DISCORD__ALWAYSDOWNLOADUSERS=false
```

| .NET configuration key | Default / requirement |
| --- | --- |
| `Bridge:Credential` | Required, dedicated random secret, at least 32 printable ASCII characters without spaces |
| `Bridge:Address` | `127.0.0.1`; IPv4 or IPv6 literal |
| `Bridge:Port` | `8765`; integer 1–65535 |
| `Discord:GatewayIntents` | Existing defaults: `Guilds,GuildMembers,GuildMessages,GuildMessageReactions,MessageContent` |
| `Discord:AlwaysDownloadUsers` | Existing default: `true`; set `false` when omitting `GuildMembers` |

Bridge needs only `Guilds,GuildMessages`. It never requests member lists or relies on a complete guild member cache. Other features may need additional intents or complete membership information; configuring the shared client changes their available input too. Existing deployments retain their Discord defaults unless these options are supplied. `AlwaysDownloadUsers=true` with no `GuildMembers` intent fails validation.

The listener uses Kestrel within the existing host, DI container, and shutdown lifetime. The runtime requires the ASP.NET Core shared framework (included with the SDK and the Docker image). Default URL: `ws://127.0.0.1:8765/bridge`. This is the only endpoint. Other paths return HTTP 404. Address/port come from `Bridge`, not `ASPNETCORE_URLS`.

The production Compose file binds **host loopback only**, publishing `${BRIDGE__PORT:-8765}` to container port 8765. Inside the container `BRIDGE__ADDRESS` defaults to `0.0.0.0` so publishing works; `BRIDGE__CREDENTIAL` is required. Set the two `DISCORD__...` options in the Compose environment if using the minimal intent configuration. Keep the port private, use an SSH tunnel, or terminate TLS at a suitable authenticated network boundary before using a non-loopback address. A plain `ws://` connection does not encrypt the credential or messages.

Startup applies the additive `AddBridgeChannels` EF migration through the existing migration service, then loads configured channels before subscribing to message events. Only channel IDs are persisted. Message content, action requests, results, and origin bookkeeping are never stored by Bridge.

## Discord management

Exactly these management commands are provided:

- `/bridge add [channel]`: enable Bridge; defaults to the invoking channel.
- `/bridge remove [channel]`: disable Bridge; defaults to the invoking channel.
- `/bridge list`: show the invoking guild's configured channels.

Commands are guild-only, require `Administrator`, and enforce it both in their default member permissions and at execution using the existing permission precondition. `ManageGuild` alone does not grant access. Responses are ephemeral and database operations are deferred. Adding an existing membership or removing an absent membership succeeds and reports the resulting state. Changes update the in-memory filtering view immediately after persistence, without restart. Use these commands to change membership; external database edits are loaded on restart.

Supported destinations are text channels, announcement channels, and public/private/announcement threads in the invoking guild. Forum containers, voice/stage channels, categories, and DMs are unsupported. A forum post's thread can be configured. Membership is the **exact channel ID**: adding a parent does not add its threads. List retains configured IDs even if Discord no longer resolves a deleted channel.

## Eligible messages

Only `MessageReceived` events for human-authored guild messages in configured channels enter Bridge. A message must both:

1. Contain `<@BOT_USER_ID>` or `<@!BOT_USER_ID>` in its body.
2. Include that same bot ID in Discord's structured mentioned-user IDs.

This uses Discord's [documented message-content exception for messages mentioning the app](https://docs.discord.com/developers/events/gateway#message-content-intent). The check applies even if another deployment enables `MessageContent`. A reply reference or reply notification without a body mention is insufficient. Bot/webhook messages, DMs, edits, other channels, and other mentions are ignored before queuing content.

Events contain the original message text and available attachment metadata. Display name comes from the message author's available guild display name, then global name or username. No member-list download is needed. References contain IDs only: Bridge does not copy nested referenced-message content, fetch channel history, or download attachments. Attachment URLs can expire and availability is determined by Discord.

## Authentication and connection

Send this header on the WebSocket HTTP upgrade:

```http
Authorization: Bearer <bridge-credential>
```

Credentials in query strings, frames, or WebSocket subprotocols are not accepted. Missing/incorrect authentication returns HTTP 401 before upgrade. An authenticated request without a WebSocket upgrade returns HTTP 400. Only **one active connection** is allowed; an additional authenticated upgrade receives HTTP 409. A client needs WebSocket header support, such as .NET `ClientWebSocket`; the browser WebSocket constructor cannot set this header.

Every frame below is one UTF-8 JSON **WebSocket message** (transport fragmentation is supported). Field names and action types are case-sensitive. Discord IDs are decimal strings, including all IDs in attachments and references. Timestamps use ISO 8601. Protocol version is `1`.

The server first sends:

```json
{
  "version": 1,
  "type": "connected",
  "limits": {
    "outboundQueue": 64,
    "actionQueue": 16,
    "origins": 256,
    "requests": 1024,
    "frameBytes": 65536,
    "replyCharacters": 16000
  }
}
```

Connection confirmation means the Bridge transport is available, not that Discord has a fully populated member cache. Discord action availability is checked when dispatching.

## Message events

```json
{
  "version": 1,
  "type": "message",
  "message": {
    "messageId": "123456789012345678",
    "guildId": "223456789012345678",
    "channelId": "323456789012345678",
    "timestamp": "2026-10-23T12:00:00+00:00",
    "authorId": "423456789012345678",
    "displayName": "Example member",
    "text": "<@523456789012345678> Hello Bridge",
    "replyReference": {
      "messageId": "623456789012345678",
      "channelId": "323456789012345678",
      "guildId": "223456789012345678"
    },
    "attachments": [
      {
        "id": "723456789012345678",
        "filename": "example.png",
        "url": "https://cdn.discordapp.com/attachments/example",
        "proxyUrl": "https://media.discordapp.net/attachments/example",
        "size": 12345,
        "contentType": "image/png",
        "width": 640,
        "height": 480,
        "description": null,
        "ephemeral": false
      }
    ]
  }
}
```

`replyReference` is `null` without a reference. Its optional `messageId` and `guildId` can be `null`. Unknown attachment content type, dimensions, and description are `null`; no attachments produces `[]`.

## Client actions and results

Actions must identify a message delivered through the **current connection**, still retained among its latest 256 origins. The server resolves guild/channel from that origin; client-supplied `channelId`, `guildId`, or other unknown properties are rejected. Every action requires version 1, a unique nonblank `requestId` of 1–64 characters, and a nonzero canonical decimal `messageId` string. Duplicate JSON properties are rejected.

Typing sends one Discord typing notification (no persistent timer):

```json
{"version":1,"type":"typing","requestId":"typing-1","messageId":"123456789012345678"}
```

Reply text must be nonblank and at most 16,000 UTF-16 code units:

```json
{"version":1,"type":"reply","requestId":"reply-1","messageId":"123456789012345678","text":"Hello from Bridge."}
```

Replies reference the originating message, fail if it no longer exists, and disable all allowed mention types with `MentionRepliedUser=false` to suppress reply-author pings and user/role/everyone mentions. Long replies are split within Discord's 2,000-character limit, preferring line/word boundaries, preserving surrogate pairs, and closing/reopening ordinary triple-backtick fences. Whitespace-only chunks are omitted because Discord cannot send them. Complex Markdown constructs may span chunks. Every part references the same original message.

Membership is checked again before typing and before **each reply part**. Removing a channel rejects pending actions; already dispatched Discord requests may complete. Channel resolution may use Discord REST, but never channel history or a member list. Discord.NET handles rate limits. Timeout/502 retries are disabled for these actions to avoid repeating sends with unknown outcomes. Requests have a 15-second Discord request timeout and a 60-second overall action budget.

Success echoes the correlation ID and action, with known sent IDs (empty for typing):

```json
{"version":1,"type":"result","requestId":"reply-1","action":"reply","sentMessageIds":["823456789012345678"]}
```

Errors use the same envelope, including IDs from successful earlier parts:

```json
{"version":1,"type":"error","requestId":"reply-1","code":"partial_send","message":"Discord action failed. Do not automatically retry unknown outcomes.","sentMessageIds":["823456789012345678"],"outcomeUnknown":true}
```

`outcomeUnknown=true` means a Discord call was dispatched but its outcome could not be confirmed. Never automatically retry that action: Discord may have accepted an unreported part. Even `outcomeUnknown=false` can accompany earlier sent IDs, for example removal between reply parts. Do not resend those parts. If a connection disappears during a send, its result is unavailable and its outcome may be unknown. Reconnecting does not recover it.

| Error code | Meaning |
| --- | --- |
| `invalid_frame` | Invalid JSON, version, fields, IDs, or reply text; `requestId` can be `null` when unavailable/invalid |
| `duplicate_request` | A valid action already used this ID on this connection; it is not dispatched again |
| `unknown_origin` | Message was not delivered on this connection or its origin was evicted |
| `busy` | Action queue full; this action was not dispatched |
| `channel_removed` | The originating channel is no longer configured; earlier parts can appear in `sentMessageIds` |
| `channel_unavailable` | Originating channel cannot be resolved as a supported channel in its original guild |
| `discord_error` | Discord operation failed before any confirmed reply sends |
| `partial_send` | Discord operation failed after one or more confirmed reply sends |

Errors include `sentMessageIds` (normally `[]`) and `outcomeUnknown` (normally `false`). Invalid frames do not consume request IDs. Every syntactically valid action consumes its ID, including rejected origins, removed channels, and `busy`. Clients should generate a new ID for a deliberate new action; duplicate IDs produce an error, not replayed results.

## Bounds and disconnects

- One shared **connection-local outbound queue of 64 frames**, including events/results/errors, and one **action queue of 16**. One sender, one receiver, and one sequential Discord action worker run outside gateway callbacks. A current item can also be in flight in each worker.
- Gateway callbacks only filter, copy available message metadata, and try to enqueue. A full outbound queue drops **new messages** and logs message/guild/channel IDs without bodies. Results/errors that cannot be queued terminate the connection instead of silently disappearing. No gateway callback waits for a client or Discord action.
- The latest **256 successfully delivered message origins** are retained, oldest first, as IDs only. There is no history replay or age-based retention. Queued events for removed channels are discarded before delivery.
- At most **1,024 valid action request IDs** are retained per connection. Duplicates do not dispatch. Attempting another distinct action after this limit terminates the connection; reconnect to obtain a fresh budget. No automatic action retry or cross-connection deduplication is provided.
- Incoming messages are limited to **65,536 bytes** and JSON depth 8. Binary/oversized messages terminate the connection. Invalid JSON or schemas within the size bound receive `invalid_frame`. Slow consumers are bounded by a **10-second send timeout**. Ping interval and pong timeout are each 20 seconds.
- With no connected client, incoming work is discarded. Disconnect or application shutdown cancels transport/actions, observes connection tasks, and discards all queued content, origins, and correlation IDs. A new connection receives future events only.

Routine Bridge logs contain connection state, IDs, counts, failure types, and unknown-outcome flags. They do not contain message/reply bodies, attachment URLs, request IDs, or credentials.

## Verification boundaries

The Bridge tests use ordinary eligibility/protocol/text data and a real loopback Kestrel endpoint with `ClientWebSocket`. The endpoint checks use a real, unconnected Discord.NET client; they never log in or manufacture Discord.NET substitutes. PostgreSQL integration checks require Docker. Live Discord validation still needs a test guild: use only `Guilds,GuildMessages`, disable member downloading, configure a channel, then check body mentions versus reply-only notifications, attachment metadata, typing, split replies, mention suppression, and removal during queued actions.
