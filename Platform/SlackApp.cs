using System.Diagnostics;
using System.Runtime.InteropServices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.Slack.Platform;

/// <summary>
/// What this machine has of Slack and how to reach it: detecting the desktop app, bringing it to
/// the front before a shortcut is sent, and opening <c>slack://</c> deep links. The per-OS
/// branches live here so the commands stay platform-neutral.
/// </summary>
internal static class SlackApp
{
    public const string MacBundleId = "com.tinyspeck.slackmacgap";

    /// <summary>How long to wait for Slack to become the frontmost app before a shortcut is sent.</summary>
    private static readonly TimeSpan ActivateTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Windows and Linux cannot cheaply tell when Slack has taken focus, so they wait this long
    /// after asking for it. A window that is already in front skips the wait.
    /// </summary>
    private static readonly TimeSpan BlindSettle = TimeSpan.FromMilliseconds(350);

    /// <summary>Extra pause after Slack came to the front, so its window has keyboard focus before the keys arrive.</summary>
    private static readonly TimeSpan FocusSettle = TimeSpan.FromMilliseconds(120);

    private static readonly Lazy<bool> Installed = new(DetectInstalled);

    public static bool IsInstalled => Installed.Value;

    /// <summary>
    /// Brings Slack to the front, launching it when it is not running, and returns once it is the
    /// frontmost app (or after a timeout). Returns false when Slack could not be reached at all.
    /// </summary>
    public static async Task<bool> ActivateAsync(IPluginHost host)
    {
        if (IsFrontmost()) return true;

        if (OperatingSystem.IsMacOS())
        {
            if (Capture("open", "-b", MacBundleId) is null) return false;
            var deadline = DateTime.UtcNow + ActivateTimeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(PollInterval).ConfigureAwait(false);
                if (IsFrontmost())
                {
                    await Task.Delay(FocusSettle).ConfigureAwait(false);
                    return true;
                }
            }

            host.Logger.Warn("Slack did not come to the front in time; sending the shortcut anyway");
            return true;
        }

        // slack://open focuses the running app (or starts it) on Windows and Linux.
        if (!host.OpenBrowser("slack://open")) return false;
        if (OperatingSystem.IsWindows())
        {
            var deadline = DateTime.UtcNow + ActivateTimeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(PollInterval).ConfigureAwait(false);
                if (IsFrontmost())
                {
                    await Task.Delay(FocusSettle).ConfigureAwait(false);
                    return true;
                }
            }

            return true;
        }

        await Task.Delay(BlindSettle).ConfigureAwait(false);
        return true;
    }

    /// <summary>Opens a <c>slack://</c> deep link, e.g. <c>slack://channel?team=T…&amp;id=C…</c>.</summary>
    public static bool OpenLink(IPluginHost host, string url) => host.OpenBrowser(url);

    /// <summary>True when Slack owns the frontmost window. Linux always answers false.</summary>
    public static bool IsFrontmost()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                // lsappinfo front prints the ASN of the frontmost app; ask for its bundle id.
                var asn = Capture("lsappinfo", "front");
                if (string.IsNullOrEmpty(asn)) return false;
                var info = Capture("lsappinfo", "info", "-only", "bundleid", asn);
                return info is not null && info.Contains(MacBundleId, StringComparison.OrdinalIgnoreCase);
            }

            if (OperatingSystem.IsWindows()) return WindowsForegroundIsSlack();
        }
        catch
        {
            // best effort: an unknown answer only means we activate again
        }

        return false;
    }

    private static bool DetectInstalled()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return Directory.Exists("/Applications/Slack.app")
                       || Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "Slack.app"));
            }

            if (OperatingSystem.IsWindows())
            {
                // Per-user install (the default), the machine-wide MSI and the Microsoft Store build.
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Directory.Exists(Path.Combine(local, "slack"))
                       || Directory.Exists(Path.Combine(programs, "Slack"))
                       || Directory.Exists(Path.Combine(local, "Packages")) &&
                          Directory.EnumerateDirectories(Path.Combine(local, "Packages"), "*SlackTechnologies*").Any();
            }

            if (OperatingSystem.IsLinux())
            {
                return File.Exists("/usr/bin/slack") || File.Exists("/snap/bin/slack") ||
                       Directory.Exists("/var/lib/flatpak/app/com.slack.Slack");
            }
        }
        catch
        {
            // detection is best effort
        }

        return false;
    }

    // ---- Windows foreground window ----

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static bool WindowsForegroundIsSlack()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return false;
        using var p = Process.GetProcessById((int)pid);
        return string.Equals(p.ProcessName, "slack", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs a command and returns its stdout (trimmed), or null on failure / timeout.</summary>
    internal static string? Capture(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return null; }
            return p.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
