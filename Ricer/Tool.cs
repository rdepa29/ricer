using System.Diagnostics;

namespace Ricer;

public static class Tool
{
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

    public static bool Success(int exitCode) => exitCode is >= 0 and < 8;

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