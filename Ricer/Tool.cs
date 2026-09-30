using System.ComponentModel;
using System.Diagnostics;

namespace Ricer;

public static class Tool
{
    /// <summary>UAC "cancel" exit code, so callers can tell a decline from a failure.</summary>
    public const int UacDeclined = 1223;

    /// <summary>Run a native exe. Returns exit code; robocopy codes 0-7 count as success.</summary>
    public static int Run(string file, string args, bool silent = false)
    {
        using var p = Start(file, args, silent);
        return p.ExitCode;
    }

    /// <summary>Run cmd.exe so bat-based shims (scoop) work.</summary>
    public static int RunCmd(string args, bool silent = false)
    {
        using var p = Start("cmd.exe", $"/d /c {args}", silent);
        return p.ExitCode;
    }

    /// <summary>
    /// Run an exe through the UAC "runas" verb so it gets an admin token. Windows
    /// shows the consent prompt; if the user declines we get ERROR_CANCELLED (1223)
    /// rather than an exception. The window is left visible on purpose - an elevated
    /// scoop install is long-running and the user should see it work.
    /// </summary>
    public static int RunElevated(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception e)
        {
            return e.NativeErrorCode == UacDeclined ? UacDeclined : -1;
        }
    }

    /// <summary>Run cmd.exe through the UAC "runas" verb so bat-based shims work.</summary>
    public static int RunElevatedCmd(string args) => RunElevated("cmd.exe", $"/d /c {args}");

    public static bool Success(int exitCode) => exitCode is >= 0 and < 8;

    /// <summary>Launch a background GUI process and return without waiting for it.</summary>
    public static bool RunDetached(string file, string args)
    {
        try
        {
            // ShellExecute so the child inherits no handle on our console.
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            _ = Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Run an exe and capture stdout (trimmed).</summary>
    public static string RunCapture(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        _ = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return output.Trim();
    }

    private static Process Start(string file, string args, bool silent)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = false
        };

        if (silent)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
        }

        var p = Process.Start(psi)!;
        if (silent)
        {
            _ = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd();
        }
        p.WaitForExit();
        return p;
    }
}