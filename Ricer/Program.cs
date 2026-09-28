namespace Ricer;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            string cmd = args.Length == 0 ? "help" : args[0];
            var rest = args.Length > 1 ? args[1..].ToList() : [];

            var ricer = new Ricer(rest);

            string? repoArg = rest
                .Where(a => a.Contains(':') || a.Contains('/') ||
                            System.IO.Directory.Exists(a))
                .LastOrDefault();
            if (repoArg is not null)
            {
                ricer.HasRepoArg = true;
                ricer.ResolveRepoArg(repoArg);
                ricer.Rest.Remove(repoArg);
            }

            switch (cmd.ToLowerInvariant())
            {
                case "":
                case "help":
                case "-h":
                case "--help":
                    ricer.ShowHelp();
                    break;
                case "install":
                    ricer.InvokeInstall();
                    break;
                case "update":
                    ricer.InvokeUpdate();
                    break;
                case "uninstall":
                case "rm":
                    ricer.InvokeUninstall();
                    break;
                case "list":
                case "ls":
                    ricer.InvokeList();
                    break;
                case "status":
                case "doctor":
                    ricer.InvokeStatus();
                    break;
                case "config":
                case "cfg":
                    ricer.InvokeConfig();
                    break;
                case "selftest":
                    ricer.SelfTest();
                    break;
                default:
                    Ricer.Err($"Unknown command: {cmd}");
                    ricer.ShowHelp();
                    return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Ricer.Err(ex.Message);
            return 1;
        }
    }
}