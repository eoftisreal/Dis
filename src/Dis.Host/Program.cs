namespace Dis.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--verify-usb-tools"))
        {
            try { BundledUsbTools.Verify(); }
            catch (Exception e) { Environment.ExitCode = 1; Console.Error.WriteLine(e.Message); }
            return;
        }
        if (args.FirstOrDefault() == "--display-device-session")
        {
            try { DriverSession.RunHelperAsync(args).GetAwaiter().GetResult(); }
            catch (Exception e) { Environment.ExitCode = e is System.ComponentModel.Win32Exception win32 ? win32.NativeErrorCode : e.HResult; }
            return;
        }
        ApplicationConfiguration.Initialize();
        if (args.Contains("--display-lifecycle-check") || args.Contains("--display-capture-check"))
        {
            bool capture = args.Contains("--display-capture-check");
            string output = Path.GetFullPath(capture ? "artifacts/display-capture.json" : "artifacts/display-lifecycle.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            try { Task.Run(() => capture ? DisplaySmoke.CheckCaptureAsync(output) : DisplaySmoke.CheckLifecycleAsync(output)).GetAwaiter().GetResult(); Environment.ExitCode = 0; }
            catch (Exception e) { File.WriteAllText(output, e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--display-smoke"))
        {
            string output = Path.GetFullPath("artifacts/display-smoke.json");
            try { Task.Run(() => DisplaySmoke.RunAsync(output)).GetAwaiter().GetResult(); Environment.ExitCode = 0; }
            catch (Exception e) { File.WriteAllText(output, e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--install-display-driver")) { Application.Run(new DriverSetupForm()); return; }
        using var instance = new Mutex(false, "Local\\Dis.Host.SingleInstance");
        bool owns;
        try { owns = instance.WaitOne(0); }
        catch (AbandonedMutexException) { owns = true; }
        if (!owns) { MessageBox.Show("Dis is already running. Use the existing window.", "Dis"); return; }
        try { Application.Run(args.Contains("--qt-engine") ? new QtEngineContext(!args.Contains("--no-auto-connect")) : new ApplicationContext(new MainForm())); }
        finally { instance.ReleaseMutex(); }
    }
}
