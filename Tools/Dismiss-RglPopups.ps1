# Auto-dismisses the Bannerlord editor's startup error popups:
#   1. "RGL CONTENT WARNING" dialogs (e.g. "Unable to find texture: ui_...")  -> closed.
#   2. The follow-up "Faced a problem, would you like to proceed?" dialog     -> clicks NO
#      (declining log collection).
#   3. The follow-up "Always ignore?" style dialog                            -> clicks OK.
#
# The follow-up auto-answers (2 and 3) are ONLY performed during INITIAL LAUNCH - while the
# newest TaleWorlds/Bannerlord process is younger than 5 minutes - so a genuine mid-session
# engine problem still surfaces to the user instead of being silently swallowed.
#
# WHY EXTERNAL: these popups fire during NATIVE editor startup, before any module DLL loads
# (proven 2026-08-23 by timeline), so no mod code can preempt them. Only dialogs of the
# standard #32770 class are touched, matched by title (RGL) or content text, so nothing
# else on the desktop can be affected. Exits on its own 10 minutes after the last
# TaleWorlds process disappears (6 hour hard cap).

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
public static class RglDismiss {
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr hWnd, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    public const uint WM_CLOSE = 0x0010;
    public const uint BM_CLICK = 0x00F5;
    public static int Closed = 0, AnsweredNo = 0, AnsweredOk = 0;

    static string Text(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, 512); return sb.ToString(); }
    static string Cls(IntPtr h)  { var sb = new StringBuilder(64);  GetClassName(h, sb, 64);  return sb.ToString(); }

    // All static text + the button handles of one dialog.
    static void Inspect(IntPtr dlg, out string content, out Dictionary<string, IntPtr> buttons) {
        var text = new StringBuilder();
        var btns = new Dictionary<string, IntPtr>();
        EnumChildWindows(dlg, (h, l) => {
            var c = Cls(h);
            if (c == "Static") text.Append(Text(h)).Append(' ');
            else if (c == "Button") {
                var caption = Text(h).Replace("&", "").Trim();
                if (caption.Length > 0 && !btns.ContainsKey(caption)) btns[caption] = h;
            }
            return true;
        }, IntPtr.Zero);
        content = text.ToString();
        buttons = btns;
    }

    public static void Sweep(bool allowFollowUps) {
        EnumWindows((hWnd, lParam) => {
            if (!IsWindowVisible(hWnd) || Cls(hWnd) != "#32770") return true;
            var title = Text(hWnd);

            // 1. The RGL content warning itself - close it, any time.
            if (title.IndexOf("RGL", StringComparison.OrdinalIgnoreCase) >= 0) {
                PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                Closed++;
                return true;
            }

            if (!allowFollowUps) return true;   // launch window over - leave real dialogs alone

            string content; Dictionary<string, IntPtr> buttons;
            Inspect(hWnd, out content, out buttons);

            // 2. "Faced a problem, would you like to proceed?" -> NO (decline log collection).
            if (content.IndexOf("Faced a problem", StringComparison.OrdinalIgnoreCase) >= 0
                || (content.IndexOf("would you like to proceed", StringComparison.OrdinalIgnoreCase) >= 0
                    && content.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0)) {
                foreach (var kv in buttons)
                    if (kv.Key.Equals("No", StringComparison.OrdinalIgnoreCase)) {
                        SendMessage(kv.Value, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        AnsweredNo++;
                        return true;
                    }
                return true;
            }

            // 3. "Always ignore" -> OK.
            if (content.IndexOf("Always ignore", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Always ignore", StringComparison.OrdinalIgnoreCase) >= 0) {
                foreach (var kv in buttons)
                    if (kv.Key.Equals("OK", StringComparison.OrdinalIgnoreCase)
                        || kv.Key.Equals("Yes", StringComparison.OrdinalIgnoreCase)) {
                        SendMessage(kv.Value, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        AnsweredOk++;
                        return true;
                    }
            }
            return true;
        }, IntPtr.Zero);
    }
}
"@

$deadline = (Get-Date).AddHours(6)
$lastSeen = Get-Date
Write-Host "RGL popup dismisser v2 running (Ctrl+C to stop)..."
while ((Get-Date) -lt $deadline) {
    $proc = Get-Process | Where-Object { $_.ProcessName -match 'TaleWorlds|Bannerlord' } | Sort-Object StartTime -Descending | Select-Object -First 1
    $inLaunchWindow = $false
    if ($proc) {
        $lastSeen = Get-Date
        try { $inLaunchWindow = ((Get-Date) - $proc.StartTime).TotalSeconds -lt 300 } catch { }
    }
    elseif (((Get-Date) - $lastSeen).TotalMinutes -gt 10) { break }

    [RglDismiss]::Sweep($inLaunchWindow)
    Start-Sleep -Milliseconds 200
}
Write-Host "Dismisser exiting. Closed $([RglDismiss]::Closed) RGL warning(s), answered No x$([RglDismiss]::AnsweredNo), OK x$([RglDismiss]::AnsweredOk)."
