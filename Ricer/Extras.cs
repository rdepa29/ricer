using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Ricer;

/// <summary>
/// Managed items that are not Scoop packages. They live in Ricer.Apps so the
/// selection syntax (... , 1-4, ^name) picks them up like anything else, but
/// ricer owns their install and uninstall end to end instead of shelling out to
/// scoop.
/// </summary>
public static class Extras
{
    public const string VisionCursor = "vision-cursor";

    private static readonly HashSet<string> Known =
        new(StringComparer.OrdinalIgnoreCase) { VisionCursor };

    public static bool IsExtra(string name) => Known.Contains(name);

    // ---- Vision-Cursor -------------------------------------------------
    // github.com/zDyant/Vision-Cursor has no releases and its .Install.inf only
    // copies into %SystemRoot%\Cursors, which needs admin. The registry half of
    // that INF is already HKCU-only, so we skip the INF entirely: fetch the
    // .cur/.ani files, keep them under %LOCALAPPDATA% and write the same HKCU
    // keys ourselves. That is a true per-user install - no UAC, no machine state.

    private const string VisionRaw = "https://raw.githubusercontent.com/zDyant/Vision-Cursor/main/src/";
    private const string CursorKey = @"Control Panel\Cursors";
    private const string SchemesKey = @"Control Panel\Cursors\Schemes";
    private const int RegExpandSz = 2;

    /// <summary>Scheme name and repo folder per variant, taken from each .Install.inf.</summary>
    public static readonly (string Scheme, string Folder)[] VisionVariants =
    [
        ("Vision Cursor White", "Vision-White"),
        ("Vision Cursor Black", "Vision-Black"),
    ];

    /// <summary>The variant ricer applies after installing. Keep in VisionVariants.</summary>
    public const string DefaultVisionScheme = "Vision Cursor White";

    /// <summary>
    /// The 17 files an .Install.inf ships, in the order its scheme value uses.
    /// The INF's [Strings] swaps the last two (%person% = pin.cur, %pin% =
    /// person.cur) because that is how Windows 11's Person/Pin roles are meant
    /// to line up; the list below already reflects the resolved file names.
    /// </summary>
    private static readonly string[] VisionFiles =
    [
        "pointer.cur", "help.cur", "work.ani", "busy.ani", "cross.cur", "text.cur",
        "handwriting.cur", "unavailiable.cur", "vert.cur", "horz.cur", "dgn1.cur",
        "dgn2.cur", "move.cur", "alternate.cur", "link.cur", "pin.cur", "person.cur",
    ];

    /// <summary>HKCU\Control Panel\Cursors role name -> file, straight out of [Wreg].</summary>
    private static readonly (string Role, string File)[] VisionRoles =
    [
        ("Arrow", "pointer.cur"),
        ("Help", "help.cur"),
        ("AppStarting", "work.ani"),
        ("Wait", "busy.ani"),
        ("Crosshair", "cross.cur"),
        ("IBeam", "text.cur"),
        ("NWPen", "handwriting.cur"),
        ("No", "unavailiable.cur"),
        ("SizeNS", "vert.cur"),
        ("SizeWE", "horz.cur"),
        ("SizeNWSE", "dgn1.cur"),
        ("SizeNESW", "dgn2.cur"),
        ("SizeAll", "move.cur"),
        ("UpArrow", "alternate.cur"),
        ("Hand", "link.cur"),
        ("Person", "pin.cur"),
        ("Pin", "person.cur"),
    ];

    public static string CursorDir(string scheme) =>
        Path.Combine(Ricer.LocalAppData, "ricer", "cursors", scheme);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ricer");
        return c;
    }

    public static void Install(string name)
    {
        if (string.Equals(name, VisionCursor, StringComparison.OrdinalIgnoreCase))
        {
            InstallVisionCursor();
            return;
        }
        throw new InvalidOperationException($"no ricer-managed installer for '{name}'");
    }

    public static void Uninstall(string name)
    {
        if (string.Equals(name, VisionCursor, StringComparison.OrdinalIgnoreCase))
        {
            UninstallVisionCursor();
            return;
        }
        throw new InvalidOperationException($"no ricer-managed uninstaller for '{name}'");
    }

    /// <summary>Scheme currently applied under HKCU\Control Panel\Cursors, or null.</summary>
    public static string? AppliedScheme()
    {
        using RegistryKey? k = Registry.CurrentUser.OpenSubKey(CursorKey);
        return k?.GetValue("Cursor") as string;
    }

    public static bool VisionCursorInstalled()
    {
        foreach (var (scheme, _) in VisionVariants)
        {
            if (!Directory.Exists(CursorDir(scheme))) return false;
            foreach (string f in VisionFiles)
                if (!File.Exists(Path.Combine(CursorDir(scheme), f)))
                    return false;
        }
        return true;
    }

    public static bool VisionCursorApplied() =>
        VisionVariants.Any(v => string.Equals(AppliedScheme(), v.Scheme, StringComparison.OrdinalIgnoreCase));

    private static void InstallVisionCursor()
    {
        Ricer.Step("vision-cursor (per-user, no admin needed)");

        foreach (var (scheme, folder) in VisionVariants)
        {
            string dir = CursorDir(scheme);
            _ = Directory.CreateDirectory(dir);
            foreach (string f in VisionFiles)
            {
                string dest = Path.Combine(dir, f);
                string url = VisionRaw + folder + "/CUR/" + f;
                if (File.Exists(dest) && new FileInfo(dest).Length > 0) continue;
                try
                {
                    using var res = Http.GetAsync(url).GetAwaiter().GetResult();
                    res.EnsureSuccessStatusCode();
                    File.WriteAllBytes(dest, res.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException($"could not fetch {url}: {e.Message}");
                }
            }
            WriteScheme(scheme, dir);
            Ricer.Ok($"registered scheme '{scheme}' -> {dir}");
        }

        Apply(DefaultVisionScheme);
        Ricer.Ok($"applied '{DefaultVisionScheme}'; the other variant stays in Mouse settings");
    }

    private static void UninstallVisionCursor()
    {
        Ricer.Step("Removing vision-cursor");

        // Only touch the roles we wrote, and only while Vision is the active
        // scheme - a user who switched to another cursor keeps theirs.
        string? applied = AppliedScheme();
        bool ours = VisionVariants.Any(v => string.Equals(applied, v.Scheme, StringComparison.OrdinalIgnoreCase));

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(CursorKey, true)!)
        {
            if (ours)
            {
                k.DeleteValue("Cursor", false);
                foreach (var (role, _) in VisionRoles) k.DeleteValue(role, false);
                k.SetValue("", "Windows Default", RegistryValueKind.String);
            }
        }
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SchemesKey, true)!)
        {
            foreach (var (scheme, _) in VisionVariants) k.DeleteValue(scheme, false);
        }

        foreach (var (scheme, _) in VisionVariants)
        {
            string dir = CursorDir(scheme);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        Refresh();
        Ricer.Ok(ours
            ? "vision-cursor removed; cursor reset to the Windows default"
            : "vision-cursor removed; the scheme you had selected was left alone");
    }

    /// <summary>Register the scheme and point every role at the files we just fetched.</summary>
    private static void WriteScheme(string scheme, string dir)
    {
        string joined = string.Join(",", VisionFiles.Select(f => Path.Combine(dir, f)));

        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(SchemesKey, true)!)
            k.SetValue(scheme, joined, RegistryValueKind.String);

        using RegistryKey c = Registry.CurrentUser.CreateSubKey(CursorKey, true)!;
        foreach (var (role, file) in VisionRoles)
            c.SetValue(role, Path.Combine(dir, file), RegistryValueKind.String);
        c.SetValue("Cursor", scheme, RegistryValueKind.String);
        // [Wreg] also points the key's *default* value at the scheme.
        c.SetValue("", scheme, RegistryValueKind.String);
    }

    /// <summary>Select a registered scheme and tell every already-running app about it.</summary>
    public static void Apply(string scheme)
    {
        using RegistryKey c = Registry.CurrentUser.CreateSubKey(CursorKey, true)!;
        c.SetValue("Cursor", scheme, RegistryValueKind.String);
        c.SetValue("", scheme, RegistryValueKind.String);
        using RegistryKey s = Registry.CurrentUser.CreateSubKey(SchemesKey, true)!;
        if (s.GetValue(scheme) is string files)
        {
            string[] parts = files.Split(',');
            // The scheme list is positional; VisionFiles is in the same order.
            for (int i = 0; i < VisionFiles.Length && i < parts.Length; i++)
            {
                string role = VisionRoles[i].Role;
                c.SetValue(role, parts[i], RegistryValueKind.String);
            }
        }
        Refresh();
    }

    private const uint SpiSetCursors = 0x0057;
    private const uint SpiSendChange = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(
        uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    /// <summary>SPI_SETCURSORS: make every running window reload the cursor set.</summary>
    private static void Refresh() =>
        _ = SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, SpiSendChange);
}
