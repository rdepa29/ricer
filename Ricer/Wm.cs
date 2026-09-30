using System.Diagnostics;
using Microsoft.Win32;

namespace Ricer;

public enum WmAction { Help, Start, Stop, Restart, Reload, Autostart, Status, Check }

public enum AutostartAction { Show, On, Off }

/// <summary>
/// Window-manager control: komorebi plus the komorebi.ahk hotkey daemon.
/// whkd is deliberately not used anywhere in here - komorebi.ahk replaces it
/// (whkd drops/leaks win-key combos).
/// </summary>
public sealed class Wm
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AhkRunValue = "KomorebiAhkOnLogin";
    private const string KomorebiLnk = "komorebi.lnk";

    private readonly Ricer ricer;

    public Wm(Ricer ricer) => this.ricer = ricer;

    private string ConfigDir => ricer.KomorebiCfgDir;
    private string AhkScript => ricer.AhkScript;
    private string Komorebic =>
        Path.Combine(ricer.ScoopDir, "apps", "komorebi", "current", "komorebic.exe");
    private string AutoHotkey =>
        Path.Combine(ricer.ScoopDir, "apps", "autohotkey", "current", "v2", "AutoHotkey64.exe");
    private string StartupDir => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    /// <summary>
    /// Map a subcommand token (including its short alias) to an action. Pure, so
    /// the selftest can cover the whole alias table without touching komorebi.
    /// </summary>
    public static WmAction Resolve(string? sub) =>
        (sub ?? "").ToLowerInvariant() switch
        {
            "start" => WmAction.Start,
            "stop" => WmAction.Stop,
            "restart" or "rs" => WmAction.Restart,
            "reload" or "r" => WmAction.Reload,
            "autostart" or "as" => WmAction.Autostart,
            "status" or "st" => WmAction.Status,
            "check" => WmAction.Check,
            "" or "help" or "-h" or "--help" => WmAction.Help,
            _ => throw new InvalidOperationException($"Unknown wm subcommand: {sub}"),
        };

    /// <summary>Map the argument of `wm autostart` to an action. Pure, like Resolve.</summary>
    public static AutostartAction ResolveAutostart(string? arg) =>
        (arg ?? "").ToLowerInvariant() switch
        {
            "on" or "enable" => AutostartAction.On,
            "off" or "disable" => AutostartAction.Off,
            "" or "status" or "show" => AutostartAction.Show,
            _ => throw new InvalidOperationException($"Expected 'on' or 'off', got: {arg}"),
        };

    public void Invoke(List<string> args)
    {
        string[] rest = args.Skip(1).ToArray();
        WmAction action;
        try
        {
            action = Resolve(args.Count > 0 ? args[0] : "");
        }
        catch (InvalidOperationException e)
        {
            Ricer.Err(e.Message);
            Console.Out.WriteLine(Usage());
            Environment.Exit(1);
            return;
        }

        switch (action)
        {
            case WmAction.Start:
                Start();
                break;
            case WmAction.Stop:
                Stop();
                break;
            case WmAction.Restart:
                Stop();
                Start();
                break;
            case WmAction.Reload:
                Reload();
                break;
            case WmAction.Autostart:
                Autostart(rest);
                break;
            case WmAction.Status:
                Status();
                break;
            case WmAction.Check:
                Check();
                break;
            default:
                Console.Out.WriteLine(Usage());
                break;
        }
    }

    private string Usage() =>
$"""
ricer wm - window manager control (komorebi + komorebi.ahk, NOT whkd)

  ricer wm start             komorebic start, then launch {AhkScript}
  ricer wm stop              stop komorebi and the komorebi.ahk process
  ricer wm restart           wm stop, then wm start
  ricer wm reload            komorebic reload-configuration + reload komorebi.ahk
  ricer wm autostart on      start komorebi and komorebi.ahk at login
  ricer wm autostart off     remove the login entries
  ricer wm autostart         print the current login entries
  ricer wm status            processes, config paths, KOMOREBI_CONFIG_HOME
  ricer wm check             komorebic check
""";

    // ---- config home ----

    /// <summary>
    /// Point this process (and anything it spawns) at ~/.config/komorebi. The
    /// user-level env var is set by EnsureEnv; setting it here too means `ricer
    /// wm start` works in a shell that predates that.
    /// </summary>
    private void EnsureConfigHome()
    {
        _ = Directory.CreateDirectory(ConfigDir);
        Environment.SetEnvironmentVariable("KOMOREBI_CONFIG_HOME", ConfigDir);
    }

    private int Kore(string args)
    {
        EnsureConfigHome();
        return File.Exists(Komorebic)
            ? Tool.Run(Komorebic, args)
            : Tool.RunCmd("komorebic " + args);
    }

    private string KoreCapture(string args) => Tool.RunCapture(
        File.Exists(Komorebic) ? Komorebic : "komorebic.exe", args);

    // ---- komorebi.ahk ----

    private bool AhkStart()
    {
        if (!File.Exists(AhkScript))
        {
            Ricer.Err($"missing {AhkScript} - run 'ricer config' to sync the komorebi configs");
            return false;
        }
        if (!File.Exists(AutoHotkey))
        {
            Ricer.Err($"AutoHotkey v2 not found at {AutoHotkey} - run 'ricer install autohotkey'");
            return false;
        }
        // The script is #SingleInstance Force, so this reloads it if it is already up.
        if (!Tool.RunDetached(AutoHotkey, $"\"{AhkScript}\""))
        {
            Ricer.Err("failed to launch komorebi.ahk");
            return false;
        }
        return true;
    }

    private int AhkStop()
    {
        int killed = 0;
        foreach (Process p in AhkProcesses())
        {
            try
            {
                p.Kill();
                killed++;
            }
            catch
            {
                // already gone, or not ours to kill
            }
            finally
            {
                p.Dispose();
            }
        }
        return killed;
    }

    /// <summary>Every AutoHotkey v2 process running the interpreter we launch the script with.</summary>
    private List<Process> AhkProcesses()
    {
        var found = new List<Process>();
        foreach (Process p in Process.GetProcesses())
        {
            bool keep = false;
            try
            {
                keep = p.ProcessName.StartsWith("AutoHotkey", StringComparison.OrdinalIgnoreCase) &&
                       SamePath(p.MainModule?.FileName, AutoHotkey);
            }
            catch
            {
                keep = false;
            }
            if (keep) found.Add(p); else p.Dispose();
        }
        return found;
    }

    private static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int CountProcesses(string name)
    {
        try
        {
            return Process.GetProcessesByName(name).Length;
        }
        catch
        {
            return 0;
        }
    }

    // ---- subcommands ----

    private void Start()
    {
        Ricer.Step("Starting komorebi");
        if (Kore("start") != 0) Ricer.Warn("komorebic start returned a non-zero exit code");
        if (AhkStart()) Ricer.Ok("komorebi.ahk running (hotkeys via AutoHotkey, not whkd)");
    }

    private void Stop()
    {
        Ricer.Step("Stopping komorebi");
        if (AhkStop() > 0) Ricer.Ok("komorebi.ahk stopped"); else Ricer.Ok("komorebi.ahk was not running");
        if (Kore("stop") != 0) Ricer.Warn("komorebic stop returned a non-zero exit code");
    }

    private void Reload()
    {
        Ricer.Step("Reloading komorebi configuration");
        if (Kore("reload-configuration") != 0)
            Ricer.Warn("komorebic reload-configuration returned a non-zero exit code");
        if (AhkStart()) Ricer.Ok("komorebi.ahk reloaded");
    }

    private void Check()
    {
        Ricer.Step("komorebic check");
        if (Kore("check") != 0) Ricer.Warn("komorebic check reported problems");
    }

    // ---- autostart ----

    private void Autostart(string[] rest)
    {
        AutostartAction action;
        try
        {
            action = ResolveAutostart(rest.Length > 0 ? rest[0] : "");
        }
        catch (InvalidOperationException e)
        {
            Ricer.Err(e.Message);
            Environment.Exit(1);
            return;
        }

        switch (action)
        {
            case AutostartAction.On:
                EnableAutostart();
                break;
            case AutostartAction.Off:
                DisableAutostart();
                break;
            default:
                AutostartStatus();
                break;
        }
    }

    private void EnableAutostart()
    {
        if (File.Exists(AhkScript) && File.Exists(AutoHotkey))
        {
            using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey, true)!;
            run.SetValue(AhkRunValue, $"\"{AutoHotkey}\" \"{AhkScript}\"", RegistryValueKind.String);
            Ricer.Ok($"login entry set: {AhkRunValue}");
        }
        else
        {
            Ricer.Err($"need both {AutoHotkey} and {AhkScript} - run 'ricer config' and 'ricer install autohotkey'");
        }

        if (File.Exists(Komorebic))
        {
            if (Kore("enable-autostart") == 0)
                Ricer.Ok($"komorebi login shortcut: {Path.Combine(StartupDir, KomorebiLnk)}");
            else
                Ricer.Warn("komorebic enable-autostart returned a non-zero exit code");
        }
        else
        {
            Ricer.Err($"komorebic not found at {Komorebic} - run 'ricer install komorebi'");
        }
    }

    private void DisableAutostart()
    {
        using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey, true)!;
        if (run.GetValue(AhkRunValue) is not null)
        {
            run.DeleteValue(AhkRunValue, false);
            Ricer.Ok($"login entry removed: {AhkRunValue}");
        }
        else
        {
            Ricer.Ok($"no login entry: {AhkRunValue}");
        }
        if (Kore("disable-autostart") == 0) Ricer.Ok("komorebi login shortcut removed");
        else Ricer.Warn("komorebic disable-autostart returned a non-zero exit code");
    }

    private void AutostartStatus()
    {
        Ricer.Step("wm autostart");
        string? ahk = null;
        using (RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey))
            ahk = run?.GetValue(AhkRunValue) as string;
        Line(AhkRunValue, ahk ?? "(not set)", ahk is not null);

        string lnk = Path.Combine(StartupDir, KomorebiLnk);
        Line(KomorebiLnk, lnk, File.Exists(lnk));
    }

    // ---- status ----

    private void Status()
    {
        EnsureConfigHome();
        Ricer.Step("wm status (komorebi + komorebi.ahk, not whkd)");

        string configJson = KoreCapture("configuration");
        Line("KOMOREBI_CONFIG_HOME", ConfigDir, Directory.Exists(ConfigDir));
        Line("komorebi.json", configJson, configJson.Length > 0);
        Line("komorebi.ahk", AhkScript, File.Exists(AhkScript));
        Line("AutoHotkey v2", AutoHotkey, File.Exists(AutoHotkey));

        int komorebi = CountProcesses("komorebi");
        int ahk = AhkProcesses().Count;
        Line("komorebi running", $"{komorebi} process(es)", komorebi > 0);
        Line("komorebi.ahk running", $"{ahk} process(es)", ahk > 0);

        int whkd = CountProcesses("whkd");
        Line("whkd running", $"{whkd} process(es)", whkd == 0);
    }

    private static void Line(string name, string value, bool ok)
    {
        Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.Out.Write($"{(ok ? "PASS " : "WARN ")} {name,-24}");
        Console.ResetColor();
        Console.Out.WriteLine(value);
    }
}
