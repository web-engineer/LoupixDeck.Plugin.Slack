using System.Diagnostics;

namespace LoupixDeck.Plugin.Slack.Platform;

/// <summary>Puts text on the system clipboard through the OS's own command-line tool.</summary>
internal static class Clipboard
{
    /// <summary>Returns false when no clipboard tool was found or it failed.</summary>
    public static bool SetText(string text)
    {
        if (OperatingSystem.IsMacOS()) return Pipe("pbcopy", [], text);
        if (OperatingSystem.IsWindows()) return Pipe("clip", [], text);
        return Pipe("wl-copy", [], text) || Pipe("xclip", ["-selection", "clipboard"], text) || Pipe("xsel", ["--clipboard", "--input"], text);
    }

    private static bool Pipe(string file, string[] args, string text)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.StandardInput.Write(text);
            p.StandardInput.Close();
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
