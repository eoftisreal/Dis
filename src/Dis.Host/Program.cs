namespace Dis.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--display-smoke"))
        {
            string output = Path.GetFullPath("artifacts/display-smoke.json");
            try { Task.Run(() => DisplaySmoke.RunAsync(output)).GetAwaiter().GetResult(); Environment.ExitCode = 0; }
            catch (Exception e) { File.WriteAllText(output, e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        Application.Run(args.Contains("--install-display-driver") ? new DriverSetupForm() : new MainForm());
    }
}
