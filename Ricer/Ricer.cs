using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Ricer;

/// <summary>Port of ricer.ps1 to C#. Same commands, selection syntax, repo args and guards.</summary>
public sealed class Ricer
{
    public static string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string LocalAppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string AppData =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public List<string> Rest { get; set; }

    public string RepoUrl { get; private set; } =
        Environment.GetEnvironmentVariable("RICER_REPO") is { Length: > 0 } custom
            ? custom
            : "https://github.com/rdepa29/config";

    public string RepoDir { get; private set; } =
        Path.Combine(LocalAppData, "rdepa29-config");

    public string BinDir => Path.Combine(UserProfile, "bin");
    public string CfgRoot => Path.Combine(UserProfile, ".config");
    public string KomorebiCfgDir => Path.Combine(CfgRoot, "komorebi");
    public string AhkScript => Path.Combine(KomorebiCfgDir, "komorebi.ahk");
    public string ScoopDir => Path.Combine(UserProfile, "scoop");
    public string ScoopShim => Path.Combine(ScoopDir, "shims");
    public string FishLauncher => @"C:\msys64\fish.cmd";
    public const string WtGuid = "{33D44DF6-71E9-46FE-AB19-316CCBB5C965}";
    public static string WtSettings =>
        Path.Combine(LocalAppData, @"Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json");

    public static readonly string[] Apps =
    [
        "7zip", "git", "komorebi", "whkd", "autohotkey", "micro", "wezterm",
        "zoxide", "fastfetch", "btop", "JetBrainsMono-NF", "zed", "zen-browser",
        "wsddm", "vision-cursor"
    ];

    public static readonly string[] CfgDirs =
    [
        "accent-theme", "cava", "fish", "komorebi", "micro", "wezterm", "whkd"
    ];

    /// <summary>Set by a --elevated flag on install/update/uninstall.</summary>
    public bool Elevated { get; private set; }

    /// <summary>True when this process already holds an admin token.</summary>
    public static bool IsAdmin
    {
        get
        {
            try
            {
                using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                return new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public Ricer(List<string> rest)
    {
        // Pull --elevated out before anything else looks at the tokens: it is a
        // flag, not a selector, and a stray one would blow up Selection.Expand.
        Elevated = rest.RemoveAll(
            a => a is "--elevated" or "-e" or "--admin") > 0;
        Rest = rest;
    }

    // ---- output helpers ----
    public static void Step(string m) { Write("[ricer] =========> ", ConsoleColor.Cyan, m); }
    public static void Ok(string m) { Write("[ricer] =========> ", ConsoleColor.Green, m); }
    public static void Warn(string m) { Write("[ricer::WARN] ===> ", ConsoleColor.Yellow, m); }
    public static void Err(string m) { Write("[ricer::ERROR] ==> ", ConsoleColor.Red, m); }

    private static void Write(string prefix, ConsoleColor color, string m)
    {
        Console.ForegroundColor = color;
        Console.Out.Write(prefix);
        Console.ResetColor();
        Console.Out.WriteLine(m);
    }

    public static bool CmdExists(string name)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("where.exe", name)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            _ = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ---- setup ----
    public void EnsureScoop()
    {
        if (File.Exists(Path.Combine(ScoopShim, "scoop.cmd"))) return;
        Step("Installing scoop");
        if (!CmdExists("git"))
        {
            Err("git not found. Install git from https://git-scm.com or winget install git.git then re-run.");
            throw new InvalidOperationException("git required");
        }
        int code = Tool.Run("powershell.exe",
            "-NoProfile -ExecutionPolicy Bypass -Command \"iex (irm https://get.scoop.sh)\"");
        if (!File.Exists(Path.Combine(ScoopShim, "scoop.cmd")))
            throw new InvalidOperationException("Scoop install failed");
        Ok("scoop installed");
    }

    public void EnsureBuckets()
    {
        Step("Adding scoop buckets");
        _ = Tool.RunCmd("scoop bucket add extras", silent: true);
        _ = Tool.RunCmd("scoop bucket add nerd-fonts", silent: true);
        _ = Tool.RunCmd("scoop bucket add wsddm https://github.com/rdepa29/wsddm", silent: true);
        _ = Tool.RunCmd("scoop update", silent: true);
    }

    private void PutScoopOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!path.Contains(ScoopShim, StringComparison.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PATH", ScoopShim + ";" + path);
    }

    // ---- MSYS2 + fish ----
    public void EnsureMsysAndFish()
    {
        if (!File.Exists(@"C:\msys64\usr\bin\bash.exe"))
        {
            Step("Installing MSYS2 via winget");
            if (!CmdExists("winget"))
                throw new InvalidOperationException(
                    "winget not available; install MSYS2 from https://www.msys2.org manually");
            _ = Tool.Run("winget.exe",
                "install --id MSYS2.MSYS2 --silent --accept-package-agreements --accept-source-agreements");
        }
        if (!File.Exists(@"C:\msys64\usr\bin\bash.exe"))
        {
            Err("MSYS2 not found at C:\\msys64");
            return;
        }
        Step("Installing fish via MSYS2 pacman");
        _ = Tool.Run(@"C:\msys64\usr\bin\bash.exe", "-lc \"pacman -Sy --noconfirm --needed fish\"");
        if (File.Exists(@"C:\msys64\usr\bin\fish.exe"))
            Ok($"fish installed ({Tool.RunCapture(@"C:\msys64\usr\bin\fish.exe", "--version")})");
    }

    public void EnsureFishLauncher()
    {
        bool existed = File.Exists(FishLauncher);
        if (!existed) Step($"Creating {FishLauncher}");
        if (!Directory.Exists(@"C:\msys64"))
            _ = System.IO.Directory.CreateDirectory(@"C:\msys64");
        // CHERE_INVOKING keeps fish in the directory it was launched from instead of
        // MSYS2's default of $HOME (~ = C:/Users/%User%).
        File.WriteAllText(FishLauncher,
            "@echo off\r\n" +
            "set \"HOME=%USERPROFILE%\"\r\n" +
            "if not defined XDG_CONFIG_HOME set \"XDG_CONFIG_HOME=%USERPROFILE%\\.config\"\r\n" +
            "set \"PATH=C:\\msys64\\usr\\bin;%PATH%\"\r\n" +
            "set \"CHERE_INVOKING=1\"\r\n" +
            "C:\\msys64\\usr\\bin\\fish.exe %*\r\n");
        if (existed) Ok("fish launcher refreshed");
        else Ok("fish launcher created");
    }

    public void EnsureGitBashAlias()
    {
        Step("Adding fish alias to git bash");
        string bc = Path.Combine(UserProfile, ".bashrc");
        if (!File.Exists(bc)) File.WriteAllText(bc, "");
        if (!File.ReadAllText(bc).Contains("fish", StringComparison.Ordinal))
            File.AppendAllText(bc, "alias fish='/c/msys64/usr/bin/fish'\r\n");
        string bp = Path.Combine(UserProfile, ".bash_profile");
        if (!File.Exists(bp))
            File.WriteAllText(bp,
                "# source bashrc for interactive login shells\r\n" +
                "if [ -f ~/.bashrc ]; then\r\n" +
                "  source ~/.bashrc\r\n" +
                "fi\r\n");
        Ok("git bash alias ready");
    }

    public void EnsureWTProfile()
    {
        Step("Syncing Windows Terminal fish profile");
        if (!File.Exists(WtSettings))
        {
            Warn("Windows Terminal settings.json not found");
            return;
        }
        string raw = File.ReadAllText(WtSettings);
        JsonNode root;
        try
        {
            root = JsonNode.Parse(raw)!;
        }
        catch (JsonException)
        {
            Warn("Windows Terminal settings.json has comments; skipping fish profile add");
            return;
        }
        var list = root?["profiles"]?["list"] as JsonArray;
        if (list is null)
        {
            Warn("Windows Terminal settings.json has no profiles.list; skipping");
            return;
        }
        foreach (var node in list)
        {
            if (node?["name"]?.GetValue<string>() is { } name &&
                string.Equals(name, "fish", StringComparison.OrdinalIgnoreCase))
            {
                Ok("fish profile already present");
                return;
            }
        }
        list.Add(new JsonObject
        {
            ["commandline"] = @"C:\msys64\fish.cmd",
            ["env"] = new JsonObject { ["HOME"] = UserProfile },
            ["guid"] = WtGuid,
            ["hidden"] = false,
            ["name"] = "fish"
        });
        File.WriteAllText(WtSettings,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Ok("fish profile added");
    }

    // ---- config repo ----
    public void EnsureRepo()
    {
        if (RepoUrl.Length == 0)
        {
            Step($"Using local config repo: {RepoDir}");
            return;
        }
        if (!Directory.Exists(Path.Combine(RepoDir, ".git")))
        {
            Step($"Cloning {RepoUrl} -> {RepoDir}");
            if (!CmdExists("git")) throw new InvalidOperationException("git not available");
            _ = Tool.Run("git.exe", $"clone --depth 1 \"{RepoUrl}\" \"{RepoDir}\"");
            if (!System.IO.Directory.Exists(Path.Combine(RepoDir, ".git")))
                throw new InvalidOperationException("git clone failed");
        }
        else
        {
            Step($"Updating {RepoDir}");
            _ = Tool.Run("git.exe", $"-C \"{RepoDir}\" pull --ff-only");
        }
    }

    public bool TestConfigsFresh()
    {
        string fish = Path.Combine(RepoDir, "fish");
        if (!File.Exists(Path.Combine(fish, "functions", "ricer.fish"))) return false;
        if (!File.Exists(Path.Combine(fish, "conf.d", "50-windows-paths.fish"))) return false;
        string accent = Path.Combine(fish, "conf.d", "99-accent.fish");
        if (!File.Exists(accent)) return false;
        return File.ReadAllText(accent).Contains("powershell -NoProfile", StringComparison.Ordinal);
    }

    public void SyncConfigs()
    {
        if (RepoUrl.Length > 0 && !TestConfigsFresh())
        {
            Warn($"config repo ({RepoDir}) is stale - it lacks the current fish configs. Push the config repo, then run 'ricer config'. Skipping mirror so live configs aren't reverted.");
            return;
        }
        var missing = CfgDirs.Where(d => !System.IO.Directory.Exists(Path.Combine(RepoDir, d))).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"config repo {RepoDir} is missing required dir(s): {string.Join(", ", missing)}. " +
                $"Custom config repos must use the same layout as the base repo ({string.Join(", ", CfgDirs)}).");
        }
        Step($"Mirroring configs into {CfgRoot}");
        foreach (string d in CfgDirs)
        {
            string src = Path.Combine(RepoDir, d);
            if (System.IO.Directory.Exists(src))
                _ = Tool.Run("robocopy.exe",
                    $"\"{src}\" \"{Path.Combine(CfgRoot, d)}\" /E /NFL /NDL /NJH /NJS", silent: true);
        }
        string scoopCfg = Path.Combine(CfgRoot, "scoop");
        _ = System.IO.Directory.CreateDirectory(scoopCfg);
        string scoopSrc = Path.Combine(RepoDir, "scoop", "config.json");
        if (File.Exists(scoopSrc))
            File.Copy(scoopSrc, Path.Combine(scoopCfg, "config.json"), true);
        string microApp = Path.Combine(AppData, "micro");
        if (!System.IO.Directory.Exists(microApp))
            _ = Tool.Run("robocopy.exe",
                $"\"{Path.Combine(CfgRoot, "micro")}\" \"{microApp}\" /E /NFL /NDL /NJH /NJS", silent: true);
        string cavaApp = Path.Combine(AppData, "cava");
        if (!System.IO.Directory.Exists(cavaApp))
            _ = Tool.Run("robocopy.exe",
                $"\"{Path.Combine(CfgRoot, "cava")}\" \"{cavaApp}\" /E /NFL /NDL /NJH /NJS", silent: true);
        Ok("configs synced");
    }

    public void ResolveRepoArg(string spec)
    {
        if (System.IO.Directory.Exists(spec))
        {
            RepoUrl = "";
            RepoDir = Path.GetFullPath(spec);
            return;
        }
        string url = System.Text.RegularExpressions.Regex.IsMatch(spec, @"^[\w.-]+/[\w.-]+$")
            ? $"https://github.com/{spec}"
            : spec;
        var name = System.Text.RegularExpressions.Regex.Replace(
                       url, @"^(https?://[^/]+/|git@[^:]+:|ssh://[^/]+/|)", "")
                   .Replace(".git", "", StringComparison.Ordinal);
        foreach (char c in name)
            if (!char.IsLetterOrDigit(c) && c is not '.' and not '_' and not '-')
                name = name.Replace(c, '-');
        RepoUrl = url;
        RepoDir = Path.Combine(Path.Combine(LocalAppData, "ricer-repos"), name);
        Step($"Config repo: {url} -> {RepoDir}");
    }

    // ---- shims + env ----
    public void EnsureBinShims()
    {
        Step($"Installing ricer shims to {BinDir}");
        _ = System.IO.Directory.CreateDirectory(BinDir);

        string self = Environment.ProcessPath!;
        string destExe = Path.Combine(BinDir, "ricer.exe");
        if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(destExe), StringComparison.OrdinalIgnoreCase))
            File.Copy(self, destExe, true);
        else
            Ok("ricer already installed in bin");

        string fishCmd = Path.Combine(BinDir, "fish.cmd");
        if (!File.Exists(fishCmd))
            File.WriteAllText(fishCmd, "@echo off\r\ncall C:\\msys64\\fish.cmd %*\r\n");
        string bashCmd = Path.Combine(BinDir, "bash.cmd");
        if (!File.Exists(bashCmd))
            File.WriteAllText(bashCmd, "@echo off\r\nC:\\msys64\\usr\\bin\\bash.exe %*\r\n");
        // Bare `wm` for cmd/PowerShell. MSYS2 fish will not exec a .cmd at all, so
        // the fish side is a wm.fish function in the config repo's fish/functions.
        string wmCmd = Path.Combine(BinDir, "wm.cmd");
        if (!File.Exists(wmCmd))
            File.WriteAllText(wmCmd, "@echo off\r\n\"%~dp0ricer.exe\" wm %*\r\n");
        if (File.Exists(Path.Combine(CfgRoot, "fish", "functions", "wm.fish")))
            Ok("wm available as 'wm' (wm.cmd) and 'wm' (fish function)");
        else
            Warn("wm.fish not found in the synced fish config; 'wm' only works in cmd/PowerShell");

        string userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
        if (!userPath.Contains(BinDir, StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable(
                "Path", userPath.TrimEnd(';') + ";" + BinDir, EnvironmentVariableTarget.User);
            Ok("added to user PATH (new shells will see fish/bash/ricer/wm)");
        }
        else
        {
            Ok("already on user PATH");
        }
    }

    public void EnsureEnv()
    {
        Step("Setting user environment variables");
        Environment.SetEnvironmentVariable(
            "WEZTERM_CONFIG_FILE", Path.Combine(CfgRoot, "wezterm", "wezterm.lua"), EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(
            "XDG_CONFIG_HOME", CfgRoot, EnvironmentVariableTarget.User);
        _ = System.IO.Directory.CreateDirectory(KomorebiCfgDir);
        Environment.SetEnvironmentVariable(
            "KOMOREBI_CONFIG_HOME", KomorebiCfgDir, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable("KOMOREBI_CONFIG_HOME", KomorebiCfgDir);
        Ok("env vars set");
    }

    // ---- app install/update/uninstall ----
    private void InstallApps()
    {
        if (Rest.Count > 0)
        {
            var targets = Selection.Expand([.. Apps], Rest);
            if (targets.Count == 0)
            {
                Warn("no packages selected");
                return;
            }
            Step($"Installing apps: {string.Join(", ", targets)}");
            foreach (string app in targets) InstallOne(app);
            return;
        }
        Step($"Installing apps: {string.Join(", ", Apps)}");
        foreach (string app in Apps) InstallOne(app);
        Step("Installing cava (best effort, manifest may be missing outside extra Scoop buckets)");
        _ = Tool.RunCmd("scoop install cava 2>&1", silent: true);
    }

    /// <summary>
    /// Install one entry from Apps. Scoop packages go through scoop (elevated
    /// when --elevated was passed); ricer-managed extras install themselves.
    /// </summary>
    private void InstallOne(string app)
    {
        if (Extras.IsExtra(app))
        {
            if (Elevated)
                Ricer.Warn($"{app} installs per-user; --elevated not needed");
            try
            {
                Extras.Install(app);
            }
            catch (Exception e)
            {
                Ricer.Err($"{app}: {e.Message}");
            }
            return;
        }

        if (!Elevated)
        {
            _ = Tool.RunCmd($"scoop install {app}");
            return;
        }
        if (IsAdmin)
        {
            _ = Tool.RunCmd($"scoop install {app}");
            return;
        }
        Ricer.Step($"scoop install {app} (elevated)");
        int code = Tool.RunElevatedCmd($"scoop install {app}");
        if (code == Tool.UacDeclined)
            Ricer.Err($"UAC declined; {app} was not installed");
        else
            Ricer.Ok($"scoop install {app} exited {code}");
    }

    public void InvokeInstall()
    {
        EnsureScoop();
        PutScoopOnPath();
        EnsureBuckets();
        InstallApps();

        if (Rest.Count > 0)
        {
            if (HasRepoArg) { EnsureRepo(); SyncConfigs(); }
            return;
        }

        EnsureMsysAndFish();
        EnsureFishLauncher();
        EnsureGitBashAlias();
        EnsureWTProfile();
        EnsureRepo();
        SyncConfigs();
        EnsureBinShims();
        EnsureEnv();
    }

    public void InvokeUpdate()
    {
        if (Rest.Count > 0)
        {
            var names = Selection.Expand([.. Apps], Rest);
            if (names.Count == 0)
            {
                Warn("no packages selected");
                return;
            }
            Step($"Updating apps: {string.Join(", ", names)}");
            foreach (string app in names)
            {
                if (Extras.IsExtra(app)) InstallOne(app);
                else _ = Tool.RunCmd($"scoop update {app}");
            }
            return;
        }
        Step("Updating Scoop apps");
        _ = Tool.RunCmd("scoop update 2>&1", silent: true);
        foreach (string app in Apps.Where(Extras.IsExtra)) InstallOne(app);

        // self-update: if running from a git checkout, pull it
        string baseDir = AppContext.BaseDirectory;
        if (System.IO.Directory.Exists(Path.Combine(baseDir, ".git")))
        {
            Step($"Self-updating ricer ({baseDir})");
            _ = Tool.Run("git.exe", $"-C \"{baseDir}\" pull --ff-only");
        }

        EnsureMsysAndFish();
        EnsureRepo();
        SyncConfigs();
        EnsureBinShims();
        EnsureFishLauncher();
        Ok("ricer update complete");
    }

    public void InvokeUninstall()
    {
        var specials = Rest.Where(a => a is "fish" or "ricer" or "wm").ToList();
        var selects = Rest.Where(a => a is not "fish" and not "ricer" and not "wm").ToList();

        var names = new List<string>();
        if (Rest.Count == 0) names = [.. Apps];
        else if (selects.Count > 0) names = Selection.Expand([.. Apps], selects);

        if (names.Count == 0 && Rest.Count > 0)
            Warn("nothing uninstalled (nothing selected)");

        foreach (string n in names)
        {
            if (Extras.IsExtra(n))
            {
                Extras.Uninstall(n);
                continue;
            }
            if (Elevated && !IsAdmin)
            {
                Ricer.Step($"scoop uninstall {n} (elevated)");
                int code = Tool.RunElevatedCmd($"scoop uninstall {n}");
                if (code == Tool.UacDeclined) Ricer.Err($"UAC declined; {n} was not uninstalled");
                continue;
            }
            _ = Tool.RunCmd($"scoop uninstall {n}");
            Ok($"{n} uninstalled");
        }

        foreach (string pkg in specials)
        {
            if (pkg == "fish")
            {
                Step("Removing MSYS2 fish");
                _ = Tool.Run(@"C:\msys64\usr\bin\bash.exe", "-lc \"pacman -Rn --noconfirm fish\"");
                if (File.Exists(FishLauncher)) File.Delete(FishLauncher);
                if (File.Exists(Path.Combine(BinDir, "fish.cmd"))) File.Delete(Path.Combine(BinDir, "fish.cmd"));
                Ok("fish removed");
                continue;
            }
            if (pkg == "ricer")
            {
                Step("Removing ricer shims");
                if (File.Exists(Path.Combine(BinDir, "ricer.exe"))) File.Delete(Path.Combine(BinDir, "ricer.exe"));
                Ok("ricer shims removed (PATH entry left in place)");
                continue;
            }
            if (pkg == "wm")
            {
                Step("Removing the bare 'wm' shims");
                new Wm(this).Invoke(["autostart", "off"]);
                if (File.Exists(Path.Combine(BinDir, "wm.cmd"))) File.Delete(Path.Combine(BinDir, "wm.cmd"));
                Ok("wm.cmd removed");
                // Only the mirrored copy: the config repo is the source of truth, so
                // 'ricer config' will put wm.fish back if the repo still has it.
                string wmFish = Path.Combine(CfgRoot, "fish", "functions", "wm.fish");
                if (File.Exists(wmFish))
                {
                    File.Delete(wmFish);
                    Ok("wm.fish removed (from " + CfgRoot + ", not from the config repo)");
                }
                Ok("wm shims removed");
                continue;
            }
            Warn($"{pkg} is not a ricer-managed package");
        }
    }

    public void InvokeList()
    {
        Step("Installed Scoop apps");
        _ = Tool.RunCmd("scoop list");
        Step($"Managed configs in {CfgRoot}");
        foreach (string d in CfgDirs)
        {
            bool ok = System.IO.Directory.Exists(Path.Combine(CfgRoot, d));
            Console.Out.WriteLine($"{(ok ? "OK " : "-- ")} {d}");
        }
    }

    public void InvokeStatus()
    {
        Step("ricer health check");
        var checks = new Dictionary<string, bool>
        {
            ["scoop"] = File.Exists(Path.Combine(ScoopShim, "scoop.cmd")),
            ["git (git for windows)"] = CmdExists("git"),
            ["MSYS2 bash"] = File.Exists(@"C:\msys64\usr\bin\bash.exe"),
            ["fish"] = File.Exists(@"C:\msys64\usr\bin\fish.exe"),
            ["fish launcher"] = File.Exists(FishLauncher),
            ["configs synced"] =
                System.IO.Directory.Exists(Path.Combine(CfgRoot, "fish")) &&
                System.IO.Directory.Exists(Path.Combine(CfgRoot, "wezterm")),
            ["bin shims"] =
                File.Exists(Path.Combine(BinDir, "ricer.exe")) &&
                File.Exists(Path.Combine(BinDir, "fish.cmd")) &&
                File.Exists(Path.Combine(BinDir, "bash.cmd")) &&
                File.Exists(Path.Combine(BinDir, "wm.cmd")),
            ["komorebi config home"] =
                System.IO.Directory.Exists(KomorebiCfgDir) &&
                string.Equals(
                    Environment.GetEnvironmentVariable("KOMOREBI_CONFIG_HOME", EnvironmentVariableTarget.User),
                    KomorebiCfgDir, StringComparison.OrdinalIgnoreCase),
            ["komorebi.ahk"] = File.Exists(AhkScript),
            ["vision-cursor"] = Extras.VisionCursorInstalled() && Extras.VisionCursorApplied(),
            ["repo clone"] = System.IO.Directory.Exists(Path.Combine(RepoDir, ".git"))
        };
        foreach (var kv in checks)
            Console.Out.WriteLine($"{(kv.Value ? "PASS " : "FAIL ")} {kv.Key}");
        if (checks["configs synced"])
        {
            string accent = Path.Combine(CfgRoot, "fish", "conf.d", "99-accent.fish");
            if (File.Exists(accent))
            {
                bool hasContinuations = System.Text.RegularExpressions.Regex.IsMatch(
                    File.ReadAllText(accent), @"\\\r?$", System.Text.RegularExpressions.RegexOptions.Multiline);
                if (hasContinuations)
                    Warn("99-accent.fish still has fish-4-incompatible line continuations");
                else
                    Ok("99-accent.fish parsed clean for fish 4.x");
            }
        }
    }

    public void InvokeConfig()
    {
        EnsureRepo();
        SyncConfigs();
    }

    // ---- help ----
    public void ShowHelp()
    {
        var appLine = string.Join("   ", Apps.Select((a, i) => $"{i + 1}.{a}"));
        string help =
$"""
ricer - my dotfiles package manager

USAGE
  ricer <command> [args...]

COMMANDS
  install                full bootstrap: ALL apps + env (msys2/fish, WT, shims), configs from the base repo
  install <selection>    install just the selected apps (no env/config bootstrap)
  update                 update ALL scoop apps + re-sync configs/shims from the base repo
  update <selection>     scoop update the selected apps
  uninstall              uninstall ALL managed apps (specials fish/ricer need explicit names)
  uninstall <selection>  scoop uninstall the selection; special: fish, ricer
  list                   installed apps + synced configs
  status                 health checks
  config [repo]          re-clone + re-sync configs from the base repo (default) or [repo]
  wm <sub>               window manager: start | stop | restart | reload | autostart | status | check
  help                   this output

FLAGS
  --elevated             run the install/uninstall with a UAC admin prompt (also -e/--admin).
                         Not needed for vision-cursor, which installs per-user.

WM (komorebi + komorebi.ahk, not whkd)
  wm start               komorebic start, then launch {AhkScript}
  wm stop                stop komorebi and the komorebi.ahk process
  wm restart             wm stop + wm start
  wm reload              komorebic reload-configuration + reload komorebi.ahk
  wm autostart on|off    login entry for komorebi + komorebi.ahk (no status prints current state)
  wm status              running processes, config paths, KOMOREBI_CONFIG_HOME
  wm check               komorebic check

SELECTION
  ...    all packages      1,3,5    those            1-4    range
  2-     from 2 onwards    -3       up to 3          ^2     exclude 2
  app names work too (e.g. btop), and ^name excludes an app.
  no selection = ALL packages.
  note: in cmd.exe escape the caret as ^^2

RICER-MANAGED (not scoop)
  vision-cursor          github.com/zDyant/Vision-Cursor, installed per-user:
                         files in {Extras.CursorDir("<scheme>")}, schemes in
                         HKCU\Control Panel\Cursors\Schemes. No admin needed.

REPO ARG (optional, for config and install)
  default base repo: {RepoUrl}   (override earlier with `$env:RICER_REPO`)
  [repo] may be:  owner/repo (github shorthand) | https://... | git@host:owner/repo | a local folder path
  custom repos must use the same layout as the base repo; ricer ERRORS if a required dir is missing.

MANAGED APPS
  {appLine}

CONFIGS:      {string.Join(" ", CfgDirs)} (repo: {RepoUrl})
""";
        Console.Out.WriteLine(help);
    }

    public bool HasRepoArg { get; set; }

    /// <summary>
    /// Side-effect-free selection harness (port of the old PS seltest.ps1).
    /// Bare `ricer selftest` runs the known sample set; with args it just
    /// echoes the expansion of those selectors.
    /// </summary>
    public void SelfTest()
    {
        if (Rest.Count > 0)
        {
            Console.Out.WriteLine(string.Join(", ", Selection.Expand([.. Apps], Rest)));
            return;
        }

        string all = string.Join(", ", Apps);
        (string[] Sel, string Expect)[] samples =
        [
            (["1,3"], "7zip, komorebi"),
            (["1-4"], "7zip, git, komorebi, whkd"),
            (["..."], all),
            (["...", "^13"], string.Join(", ", Apps.Where((_, i) => i != 12))),
            (["2-"], string.Join(", ", Apps.Skip(1))),
            (["-3"], "7zip, git, komorebi"),
            (["^2 5"], "autohotkey"),
            (["zed"], "zed"),
            (["...", "^9"], string.Join(", ", Apps.Where(a => a != "fastfetch"))),
            (["12 13"], "zed, zen-browser"),
            (["4 ^4"], ""),
            (["99"], ""),
            (["zzz"], "THROW"),
        ];

        int pass = 0, fail = 0;
        void Report(string label, string got, string expect)
        {
            bool ok = got == expect;
            if (ok) pass++; else fail++;
            Console.Out.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label}  =>  {got}");
            if (!ok) Console.Out.WriteLine($"      expected: {expect}");
        }

        foreach (var (sel, expect) in samples)
        {
            string got;
            try
            {
                got = string.Join(", ", Selection.Expand([.. Apps], [.. sel]));
            }
            catch (InvalidOperationException)
            {
                got = "THROW";
            }
            Report($"ricer selftest {string.Join(' ', sel)}", got, expect);
        }

        (string Token, string Expect)[] wmCases =
        [
            ("start", "start"), ("stop", "stop"),
            ("restart", "restart"), ("rs", "restart"),
            ("reload", "reload"), ("r", "reload"),
            ("autostart", "autostart"), ("as", "autostart"),
            ("status", "status"), ("st", "status"),
            ("check", "check"),
            ("", "help"), ("help", "help"), ("-h", "help"), ("--help", "help"),
            ("START", "start"), ("St", "status"),
            ("bogus", "THROW"), ("stat", "THROW"),
        ];
        foreach (var (token, expect) in wmCases)
        {
            string got;
            try
            {
                got = Wm.Resolve(token).ToString().ToLowerInvariant();
            }
            catch (InvalidOperationException)
            {
                got = "THROW";
            }
            Report($"ricer selftest wm {(token == "" ? "<none>" : token)}", got, expect);
        }

        (string Token, string Expect)[] autostartCases =
        [
            ("on", "on"), ("enable", "on"), ("ON", "on"),
            ("off", "off"), ("disable", "off"),
            ("", "show"), ("status", "show"), ("show", "show"),
            ("enabled", "THROW"), ("yes", "THROW"),
        ];
        foreach (var (token, expect) in autostartCases)
        {
            string got;
            try
            {
                got = Wm.ResolveAutostart(token).ToString().ToLowerInvariant();
            }
            catch (InvalidOperationException)
            {
                got = "THROW";
            }
            Report($"ricer selftest wm autostart {(token == "" ? "<none>" : token)}", got, expect);
        }

        Console.Out.WriteLine($"\n{pass} passed, {fail} failed");
        if (fail > 0) Environment.Exit(1);
    }
}