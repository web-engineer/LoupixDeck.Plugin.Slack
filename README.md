# LoupixDeck.Plugin.Slack
<img width="128" height="128" align="right" src="./icon.png"/>

A [LoupixDeck](https://github.com/RadiatorTwo/LoupixDeck) plugin for Slack:

- keys and dials for Slack's own keyboard shortcuts (getting around, reading, writing, formatting, huddles);
- an unread key that mirrors Slack's badge;
- keys that open a given channel or DM;
- status presets, a pause-notifications key and an away key.

Built against `LoupixDeck.PluginSdk` 1.30.0 and targets .NET 10. Runs on macOS,
Windows and Linux, with the limits listed under [Platform support](#platform-support).

Made by [**web-engineer**](https://www.web-engineer.co.uk/)
([@web-engineer](https://github.com/web-engineer)).

This is an independent project and is not affiliated with or endorsed by Slack
Technologies or Salesforce. Slack is a trademark of Slack Technologies, LLC.

## Platform support

| Feature | macOS | Windows | Linux |
|---|---|---|---|
| Shortcut keys and dials | Yes | Yes | Yes. Slack is brought forward with `slack://open`, then the plugin waits a fixed 350 ms |
| Unread badge | Yes, read from the Dock; no token | Shows `macOS only` | Shows `macOS only` |
| Open conversation | Yes | Yes | Yes |
| Status, pause notifications, away | Yes (needs a token) | Yes (needs a token) | Yes (needs a token) |

Shortcut keys send key presses. On macOS they need the Accessibility
permission that LoupixDeck already asks for.

## Install

Copy the build output into the plugin folder:

| OS | Folder |
|---|---|
| macOS, Linux | `~/.config/LoupixDeck/plugins/slack/` |
| Windows | `%APPDATA%\LoupixDeck\plugins\slack\` |

Restart LoupixDeck, then enable the plugin for your device under
Plugins → Installed. The folder must contain `LoupixDeck.Plugin.Slack.dll`,
its `.deps.json`, `icon.png` and `plugin.json`.

The shortcut keys, dials and unread key work straight away. The status keys
need a token; see the next section.

## Connecting your Slack account

The status, pause-notifications and away keys use the Slack Web API with a
user token. You make the token yourself, with a small Slack app that only
you use.

You need about two minutes and a browser signed in to your Slack workspace.

1. **Copy the manifest.** Open LoupixDeck → Plugins → Slack → Settings and press
   **Copy app manifest and open Slack**. This puts the app's manifest on your
   clipboard and opens [api.slack.com/apps](https://api.slack.com/apps).
   The manifest is also in the plugin folder as `slack-app-manifest.json`,
   and in this repository: [slack-app-manifest.json](slack-app-manifest.json).
2. **Start a new app from the manifest.** On Slack's app page, press
   **Create New App**. In the *Create new app* dialog, under *Or start your own
   way*, choose **From a manifest** and press **Continue**. Do not pick *AI
   agent*, *Starter app* or *Blank app*.
3. **Pick your workspace** from the list and press **Next**.
4. **Paste the manifest.** Select the **JSON** tab, delete the example text,
   paste (Cmd+V or Ctrl+V) and press **Next**.
5. **Create it.** Slack shows a summary with six *User* scopes and no bot
   scopes. Press **Create**.
6. **Install it.** In the app's left sidebar, open **Install App**, press
   **Install to *your workspace*** and then **Allow**. Some workspaces need an
   admin to approve the app first. Slack then tells you it has asked them.
7. **Copy the token.** After installing, Slack shows a **User OAuth Token**
   starting with `xoxp-`. It is also on the app's **OAuth & Permissions** page.
   Paste it into the plugin's *User OAuth token* field and press **Save**.
   Ignore any *Bot User OAuth Token* (`xoxb-`). This app has no bot, so there
   should not be one.
8. **Check it.** Press **Test token**. It reports who you are signed in as and
   any missing scopes, and fills in *Workspace id*.

The manifest asks only for user scopes (below). The app has no bot, receives
no events and is never published.

| Scope | Used for |
|---|---|
| `users.profile:read`, `users.profile:write` | Reading and setting your status |
| `dnd:read`, `dnd:write` | Reading, starting and ending Do Not Disturb |
| `users:read`, `users:write` | Reading and setting away |

The token sits in this plugin's `settings.json` on your computer and is sent
only to `slack.com`. While an account key is on screen, the plugin asks Slack
for your status, Do Not Disturb and presence once a minute: three calls, well
inside Slack's rate limits. It also asks right after a key changes something.
When no account key is shown it makes no calls.

## Settings

| Setting | Key | Default | Notes |
|---|---|---|---|
| Bring Slack to the front first | `bring_to_front` | on | Off: shortcuts go to whichever app has focus |
| User OAuth token | `user_token` | empty | `xoxp-…`, see above |
| Workspace id (optional) | `team_id` | empty | `T…`; used by *Open conversation* with a bare channel id. *Test token* fills it in |

Buttons on the settings page:

- **Copy app manifest and open Slack**: puts the manifest on the clipboard and opens Slack's app page; see [Connecting your Slack account](#connecting-your-slack-account).
- **Test token**: runs `auth.test` and reports who you are and any missing scopes.
- **Get support**: opens [web-engineer.co.uk/contact](https://web-engineer.co.uk/contact).
- **Report a bug or request a feature**: opens this repository's issues.

## Commands

Command ids are permanent. Slack keys share one look: white icons and text on
Slack aubergine (`#4A154B`). Each key gets ordinary layers you can restyle in
the button editor.

### Slack (getting around)

| Command id | Key | macOS | Windows, Linux |
|---|---|---|---|
| `Slack.Open` | Open Slack | brings Slack forward | brings Slack forward |
| `Slack.Conversation` | Open conversation | `slack://channel?…` | `slack://channel?…` |
| `Slack.Unread` | Unread badge | Dock badge | `macOS only` |
| `Slack.JumpTo` | Jump to conversation | Cmd+K | Ctrl+K |
| `Slack.Search` | Search | Cmd+G | Ctrl+G |
| `Slack.Home` | Home | Ctrl+1 | Ctrl+Shift+1 |
| `Slack.DMs` | Direct messages | Cmd+Shift+K | Ctrl+Shift+2 |
| `Slack.Activity` | Activity | Cmd+Shift+M | Ctrl+Shift+M |
| `Slack.Threads` | Threads | Cmd+Shift+T | Ctrl+Shift+T |
| `Slack.Unreads` | All unreads | Cmd+Shift+A | Ctrl+Shift+A |
| `Slack.Channels` | Browse channels | Cmd+Shift+L | Ctrl+Shift+L |
| `Slack.People` | People and user groups | Cmd+Shift+E | Ctrl+Shift+E |
| `Slack.Downloads` | Downloads | Cmd+Shift+J | Ctrl+Shift+J |
| `Slack.PreviousConversation` / `Slack.NextConversation` | Previous / next conversation | Option+Up / Down | Alt+Up / Down |
| `Slack.PreviousUnread` / `Slack.NextUnread` | Previous / next unread | Option+Shift+Up / Down | Alt+Shift+Up / Down |
| `Slack.LatestUnread` | Jump to latest unread | Cmd+J | Ctrl+J |
| `Slack.Back` / `Slack.Forward` | History | Cmd+[ / Cmd+] | Alt+Left / Right |
| `Slack.WorkspaceSwitcher` | Switch workspace | Cmd+Shift+S | Ctrl+Shift+S |
| `Slack.Details` | Conversation details | Cmd+Shift+I | Ctrl+Shift+I |
| `Slack.RightSidebar` | Toggle right sidebar | Cmd+. | Ctrl+. |
| `Slack.FullScreen` | Full screen | Ctrl+Cmd+F | F11 |
| `Slack.ZoomIn` / `Slack.ZoomOut` / `Slack.ActualSize` | Zoom | Cmd+= / Cmd+- / Cmd+0 | Ctrl+= / Ctrl+- / Ctrl+0 |
| `Slack.Preferences` | Preferences | Cmd+, | Ctrl+, |

Dials (rotary encoders): turn left or right, press for the third action.

| Command id | Left | Right | Press |
|---|---|---|---|
| `Slack.ConversationDial` | Previous conversation | Next conversation | Jump to |
| `Slack.UnreadDial` | Previous unread | Next unread | Mark read |
| `Slack.HistoryDial` | Back | Forward | All unreads |
| `Slack.ZoomDial` | Zoom out | Zoom in | Actual size |

A fast spin sends at most 5 presses per turn event.

### Slack Messages

| Command id | Key | macOS | Windows, Linux |
|---|---|---|---|
| `Slack.MarkRead` | Mark conversation read | Esc | Esc |
| `Slack.MarkAllRead` | Mark all read | Shift+Esc | Shift+Esc |
| `Slack.NewMessage` | New message | Cmd+N | Ctrl+N |
| `Slack.EditLast` | Edit last message (empty message box) | Up | Up |
| `Slack.NewLine` | New line | Shift+Enter | Shift+Enter |
| `Slack.Upload` | Upload file | Cmd+O | Ctrl+O |
| `Slack.Snippet` | New snippet | Cmd+Shift+Enter | Ctrl+Shift+Enter |
| `Slack.Canvas` | New canvas | Cmd+Shift+N | Ctrl+Shift+N |
| `Slack.StatusDialog` | Set status dialog | Cmd+Shift+Y | Ctrl+Shift+Y |
| `Slack.Bold`, `Slack.Italic` | Bold, italic | Cmd+B, Cmd+I | Ctrl+B, Ctrl+I |
| `Slack.Strikethrough` | Strikethrough | Cmd+Shift+X | Ctrl+Shift+X |
| `Slack.Code`, `Slack.CodeBlock` | Code, code block | Cmd+Shift+C, Cmd+Option+Shift+C | Ctrl+Shift+C, Ctrl+Alt+Shift+C |
| `Slack.Quote` | Quote | Cmd+Shift+9 | Ctrl+Shift+9 |
| `Slack.BulletedList`, `Slack.NumberedList` | Lists | Cmd+Shift+8, Cmd+Shift+7 | Ctrl+Shift+8, Ctrl+Shift+7 |
| `Slack.Link` | Link | Cmd+Shift+U | Ctrl+Shift+U |

### Slack Huddles

| Command id | Key | macOS | Windows, Linux |
|---|---|---|---|
| `Slack.Huddle` | Start, join or leave a huddle | Cmd+Shift+H | Ctrl+Shift+H |
| `Slack.HuddleMute` | Mute or unmute in a huddle | Cmd+Shift+Space | Ctrl+Shift+Space |

Slack documents no shortcut for huddle video or screen sharing, so there are no
keys for them.

### Slack Status (needs a token)

| Command id | Parameters | What it does |
|---|---|---|
| `Slack.Status` | `Text`, `Emoji`, `Minutes` (e.g. `In a meeting`, `:calendar:`, `60`) | Sets that status; tap again while it is your status to clear it. `Minutes` 0 keeps it until cleared. A green ring round the key shows that this status is the one you have, and the key shows the time left |
| `Slack.ClearStatus` | none | Clears your status. The key shows your current status |
| `Slack.Focus` | `Minutes` (default 60) | Pauses notifications (Do Not Disturb) for that long; tap again to resume. States: `paused` (yellow, time left), `scheduled` (inside your Do Not Disturb schedule), `notifying` |
| `Slack.Away` | none | Sets you away; tap again for automatic. States: `away`, `active` |

Without a token these keys show `Set token`. When Slack refuses a change, the
key briefly shows why: `Missing scope`, `Bad token`, `Rate limited`, `Offline`
or `Failed`. The full error goes to the log.

### Unread badge

`Slack.Unread` has three states, each with its own layers:

| State | Meaning | Tap goes to |
|---|---|---|
| `mentions` | Red, with the count of mentions and DMs Slack shows on its icon | Activity |
| `unread` | A dot: unread messages, none of them for you | All unreads |
| `clear` | `All read`, or `Not running` / `No Slack` | Brings Slack forward |

The plugin reads the badge every 2 seconds, only while the key is on screen.
On macOS it comes from the Dock (`lsappinfo`), the label Slack itself puts on
its icon. It needs no token and no extra permission. What counts towards the
number follows your Slack notification preferences.

### Open conversation

`Slack.Conversation(Conversation,Caption)` opens one channel, DM or person
through Slack's deep link. Slack links take ids, not names. Paste any of these
as `Conversation`:

- a channel, DM or user id, such as `C0123ABCD`. This needs the *Workspace id* setting, which *Test token* fills in;
- `T0123ABCD/C0123ABCD`;
- a copied Slack link, such as `https://acme.slack.com/archives/C0123ABCD` or `https://app.slack.com/client/T…/C…`.

To get a conversation's link, right-click it in Slack and choose Copy → Copy link.
The channel id is also at the bottom of the channel's details. `Caption` is
only a label for you.

## How it differs from the old Loupedeck Slack profile

The original Loupedeck software shipped a Slack profile that was a set of
keyboard macros in three workspaces (Main, Messaging, Calls). This plugin keeps
that layout's ideas and redoes the details:

- Shortcuts follow current Slack. Saved items (old Cmd+Shift+S) is now the
  workspace switcher. Hide sidebar (Cmd+Shift+D) is gone. Upload is Cmd+O, not
  Cmd+U. History on macOS is Cmd+[ / Cmd+]. Calls are huddles, with
  Cmd+Shift+H and Cmd+Shift+Space instead of M and V.
- Keys bring Slack to the front before pressing anything, so a key works from
  any app.
- New: the unread badge key, conversation keys, and the status, Do Not Disturb
  and away keys.

## Build from source

Requires the .NET 10 SDK, and a LoupixDeck checkout next to this repository
with the SDK built once (`dotnet build ../LoupixDeck/LoupixDeck.PluginSdk -c Release`).
`nuget.config` points to that build.

```bash
dotnet build -c Release
dotnet test tests/LoupixDeck.Plugin.Slack.Tests
```

A post-build step copies the plugin into the host's plugin folder:

| Host build | Target folder |
|---|---|
| Release host (default) | `~/.config/LoupixDeck/plugins/slack/` |
| Debug host (`-p:LoupixDebugHost=true`) | `~/.config/LoupixDeck/debug/plugins/slack/` |
| Windows | `%AppData%\LoupixDeck\plugins\slack\` |

Pass `-p:LoupixSkipInstall=true` to build without copying. Stop LoupixDeck
before rebuilding; the host holds the DLL open.

## Licence

MIT. See [LICENSE](LICENSE).
