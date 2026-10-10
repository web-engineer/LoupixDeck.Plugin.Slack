namespace LoupixDeck.Plugin.Slack.Shortcuts;

/// <summary>
/// One Slack keyboard shortcut, as the host's macro engine spells it on each OS. Key names follow
/// <c>System.KeyCombination</c>: <c>Cmd</c> is Command on macOS, <c>Alt</c> is Option.
/// </summary>
/// <param name="Id">Command id suffix (<c>Slack.&lt;Id&gt;</c>). Permanent once shipped.</param>
/// <param name="Mac">Combination on macOS.</param>
/// <param name="Other">Combination on Windows and Linux.</param>
/// <param name="Caption">Text on the key.</param>
/// <param name="Glyph">Material Design Icons code point for the key and the picker.</param>
internal sealed record Shortcut(
    string Id,
    string Group,
    string DisplayName,
    string Mac,
    string Other,
    string Caption,
    string Glyph,
    string Description)
{
    public string CommandName => "Slack." + Id;

    public string ForThisOs => OperatingSystem.IsMacOS() ? Mac : Other;
}

/// <summary>
/// Every shortcut the plugin offers. Checked against Slack's help page
/// (slack.com/help/articles/201374536) and the macOS app's own menu bar, Slack 4.52, October 2026.
/// </summary>
internal static class ShortcutCatalog
{
    public const string Navigate = "Slack";
    public const string Messages = "Slack Messages";
    public const string Huddles = "Slack Huddles";

    public static readonly IReadOnlyList<Shortcut> All =
    [
        // ---- getting around ----
        new("JumpTo", Navigate, "Jump to conversation", "Cmd+K", "Ctrl+K", "Jump to", "\U000F0349", // mdi-magnify
            "Open the quick switcher to jump to a channel, DM or person."),
        new("Search", Navigate, "Search", "Cmd+G", "Ctrl+G", "Search", "\U000F13B8", // mdi-text-search
            "Search all of Slack."),
        new("Home", Navigate, "Home", "Ctrl+1", "Ctrl+Shift+1", "Home", "\U000F02DC", // mdi-home
            "Show the Home view."),
        new("DMs", Navigate, "Direct messages", "Cmd+Shift+K", "Ctrl+Shift+2", "DMs", "\U000F0369", // mdi-message-text
            "Show all direct messages."),
        new("Activity", Navigate, "Activity", "Cmd+Shift+M", "Ctrl+Shift+M", "Activity", "\U000F0065", // mdi-at
            "Show Activity: mentions, reactions and replies."),
        new("Threads", Navigate, "Threads", "Cmd+Shift+T", "Ctrl+Shift+T", "Threads", "\U000F0822", // mdi-forum-outline
            "Show the threads you follow."),
        new("Unreads", Navigate, "All unreads", "Cmd+Shift+A", "Ctrl+Shift+A", "Unreads", "\U000F0B92", // mdi-email-mark-as-unread
            "Show All unreads."),
        new("Channels", Navigate, "Browse channels", "Cmd+Shift+L", "Ctrl+Shift+L", "Channels", "\U000F0423", // mdi-pound
            "Open the channel browser."),
        new("People", Navigate, "People and user groups", "Cmd+Shift+E", "Ctrl+Shift+E", "People", "\U000F000F", // mdi-account-multiple-outline
            "Show People and user groups."),
        new("Downloads", Navigate, "Downloads", "Cmd+Shift+J", "Ctrl+Shift+J", "Downloads", "\U000F01DA", // mdi-download
            "Show your downloads."),
        new("PreviousConversation", Navigate, "Previous conversation", "Alt+Up", "Alt+Up", "Prev chat", "\U000F005D", // mdi-arrow-up
            "Move to the conversation above in the sidebar."),
        new("NextConversation", Navigate, "Next conversation", "Alt+Down", "Alt+Down", "Next chat", "\U000F0045", // mdi-arrow-down
            "Move to the conversation below in the sidebar."),
        new("PreviousUnread", Navigate, "Previous unread conversation", "Alt+Shift+Up", "Alt+Shift+Up", "Prev unread", "\U000F0737", // mdi-arrow-up-bold
            "Move to the previous conversation with unread messages."),
        new("NextUnread", Navigate, "Next unread conversation", "Alt+Shift+Down", "Alt+Shift+Down", "Next unread", "\U000F072E", // mdi-arrow-down-bold
            "Move to the next conversation with unread messages."),
        new("LatestUnread", Navigate, "Jump to latest unread", "Cmd+J", "Ctrl+J", "Latest", "\U000F0DB9", // mdi-chevron-triple-down
            "Jump to the newest unread message in this conversation."),
        new("Back", Navigate, "Back", "Cmd+LeftBracket", "Alt+Left", "Back", "\U000F004D", // mdi-arrow-left
            "Go back in Slack's history."),
        new("Forward", Navigate, "Forward", "Cmd+RightBracket", "Alt+Right", "Forward", "\U000F0054", // mdi-arrow-right
            "Go forward in Slack's history."),
        new("WorkspaceSwitcher", Navigate, "Switch workspace", "Cmd+Shift+S", "Ctrl+Shift+S", "Workspace", "\U000F0BCD", // mdi-swap-horizontal-bold
            "Open the workspace switcher."),
        new("Details", Navigate, "Conversation details", "Cmd+Shift+I", "Ctrl+Shift+I", "Details", "\U000F02FD", // mdi-information-outline
            "Show details for the open conversation."),
        new("RightSidebar", Navigate, "Toggle right sidebar", "Cmd+Period", "Ctrl+Period", "Sidebar", "\U000F10AB", // mdi-dock-right
            "Show or hide the right sidebar."),
        new("FullScreen", Navigate, "Full screen", "Ctrl+Cmd+F", "F11", "Full screen", "\U000F0293", // mdi-fullscreen
            "Toggle full screen."),
        new("ZoomIn", Navigate, "Zoom in", "Cmd+Equals", "Ctrl+Equals", "Zoom in", "\U000F06ED", // mdi-magnify-plus-outline
            "Make Slack's text bigger."),
        new("ZoomOut", Navigate, "Zoom out", "Cmd+Minus", "Ctrl+Minus", "Zoom out", "\U000F06EC", // mdi-magnify-minus-outline
            "Make Slack's text smaller."),
        new("ActualSize", Navigate, "Actual size", "Cmd+0", "Ctrl+0", "100%", "\U000F0349", // mdi-magnify
            "Reset Slack's zoom."),
        new("Preferences", Navigate, "Preferences", "Cmd+Comma", "Ctrl+Comma", "Prefs", "\U000F0493", // mdi-cog
            "Open Slack's preferences."),

        // ---- reading and writing ----
        new("MarkRead", Messages, "Mark conversation read", "Esc", "Esc", "Read", "\U000F012C", // mdi-check
            "Mark every message in the open conversation as read."),
        new("MarkAllRead", Messages, "Mark all read", "Shift+Esc", "Shift+Esc", "All read", "\U000F012D", // mdi-check-all
            "Mark every message in every conversation as read."),
        new("NewMessage", Messages, "New message", "Cmd+N", "Ctrl+N", "New msg", "\U000F03EB", // mdi-pencil
            "Start a new message."),
        new("EditLast", Messages, "Edit last message", "Up", "Up", "Edit last", "\U000F03EB", // mdi-pencil
            "Edit your last message (works when the message box is empty)."),
        new("NewLine", Messages, "New line", "Shift+Enter", "Shift+Enter", "New line", "\U000F0311", // mdi-keyboard-return
            "Start a new line without sending."),
        new("Upload", Messages, "Upload file", "Cmd+O", "Ctrl+O", "Upload", "\U000F0552", // mdi-upload
            "Upload a file to the open conversation."),
        new("Snippet", Messages, "New snippet", "Cmd+Shift+Enter", "Ctrl+Shift+Enter", "Snippet", "\U000F0169", // mdi-code-braces
            "Create a code snippet."),
        new("Canvas", Messages, "New canvas", "Cmd+Shift+N", "Ctrl+Shift+N", "Canvas", "\U000F09EE", // mdi-file-document-outline
            "Create a canvas."),
        new("StatusDialog", Messages, "Set status", "Cmd+Shift+Y", "Ctrl+Shift+Y", "Status", "\U000F0FFB", // mdi-account-edit-outline
            "Open the Set a status dialog."),
        new("Bold", Messages, "Bold", "Cmd+B", "Ctrl+B", "Bold", "\U000F0264", // mdi-format-bold
            "Bold the selected text."),
        new("Italic", Messages, "Italic", "Cmd+I", "Ctrl+I", "Italic", "\U000F0277", // mdi-format-italic
            "Italicise the selected text."),
        new("Strikethrough", Messages, "Strikethrough", "Cmd+Shift+X", "Ctrl+Shift+X", "Strike", "\U000F0281", // mdi-format-strikethrough-variant
            "Strike through the selected text."),
        new("Code", Messages, "Code", "Cmd+Shift+C", "Ctrl+Shift+C", "Code", "\U000F0174", // mdi-code-tags
            "Format the selected text as inline code."),
        new("CodeBlock", Messages, "Code block", "Cmd+Alt+Shift+C", "Ctrl+Alt+Shift+C", "Code block", "\U000F0169", // mdi-code-braces
            "Format the selected text as a code block."),
        new("Quote", Messages, "Quote", "Cmd+Shift+9", "Ctrl+Shift+9", "Quote", "\U000F027E", // mdi-format-quote-close
            "Quote the selected text."),
        new("BulletedList", Messages, "Bulleted list", "Cmd+Shift+8", "Ctrl+Shift+8", "Bullets", "\U000F0279", // mdi-format-list-bulleted
            "Turn the selection into a bulleted list."),
        new("NumberedList", Messages, "Numbered list", "Cmd+Shift+7", "Ctrl+Shift+7", "Numbers", "\U000F027B", // mdi-format-list-numbered
            "Turn the selection into a numbered list."),
        new("Link", Messages, "Link", "Cmd+Shift+U", "Ctrl+Shift+U", "Link", "\U000F0337", // mdi-link
            "Turn the selected text into a link."),

        // ---- huddles ----
        new("Huddle", Huddles, "Start or leave huddle", "Cmd+Shift+H", "Ctrl+Shift+H", "Huddle", "\U000F02CB", // mdi-headphones
            "Start or join a huddle in the open conversation, or leave the one you are in."),
        new("HuddleMute", Huddles, "Mute in huddle", "Cmd+Shift+Space", "Ctrl+Shift+Space", "Mute", "\U000F036D", // mdi-microphone-off
            "Mute or unmute yourself in the current huddle.")
    ];
}
