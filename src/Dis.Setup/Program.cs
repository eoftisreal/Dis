using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Dis.Setup;

internal static class Program
{
    internal static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dis", "Application");
    internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Dis";
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(false, @"Local\Dis.Setup");
        bool owns;
        try { owns = instance.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
        if (!owns) { MessageBox.Show("Dis setup is already open.", "Dis"); return; }
        try
        {
            if (args.Contains("--uninstall")) Uninstall();
            else if (args.Contains("--verify-payload")) VerifyPayload();
            else Application.Run(new SetupForm());
        }
        catch (Exception e) { Environment.ExitCode = 1; if (!args.Contains("--verify-payload")) MessageBox.Show(e.Message, "Dis setup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { instance.ReleaseMutex(); }
    }

    internal static Stream Payload() => Assembly.GetExecutingAssembly().GetManifestResourceStream("Dis.Payload.zip")
        ?? throw new IOException("The bundled application is missing. Rebuild with BUILD_ALL.cmd.");

    private static void VerifyPayload()
    {
        using var payload = Payload();
        using var zip = new ZipArchive(payload);
        var names = zip.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        foreach (string name in new[] { "Dis.exe", "Dis.Engine.exe", "Dis.apk", "Dis.Codec.dll", "platforms/qwindows.dll", "driver/vdd-25.7.23.zip", "licenses/Android-Platform-Tools-NOTICE.txt" })
            if (!names.Contains(name)) throw new IOException("Missing payload file: " + name);
        foreach (var entry in zip.Entries)
        {
            using var source = entry.Open();
            source.CopyTo(Stream.Null);
        }
    }

    internal static void CheckNotRunning()
    {
        foreach (string name in new[] { "Dis", "Dis.Engine" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    string? path = process.MainModule?.FileName;
                    if (path is not null && path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Choose Quit Dis from the system tray, then try again.");
                }
                catch (System.ComponentModel.Win32Exception) { throw new IOException("Close Dis from its tray menu before continuing."); }
            }
        }
    }

    internal static string Install(bool startup)
    {
        CheckNotRunning();
        using var payload = Payload();
        string version = "0.4-" + Convert.ToHexString(SHA256.HashData(payload))[..16];
        payload.Position = 0;
        string destination = Path.Combine(Root, version);
        for (string? ancestor = destination; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if (Directory.Exists(ancestor) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The installation path must not contain directory links.");
        Directory.CreateDirectory(destination);
        using (var zip = new ZipArchive(payload)) zip.ExtractToDirectory(destination, true);
        string executable = Path.Combine(destination, "Dis.exe");
        string uninstaller = Path.Combine(Root, "Dis-Setup.exe");
        if (!string.Equals(Environment.ProcessPath, uninstaller, StringComparison.OrdinalIgnoreCase)) File.Copy(Environment.ProcessPath!, uninstaller, true);
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "Dis Virtual Monitor"); key.SetValue("DisplayVersion", "0.4.4");
            key.SetValue("Publisher", "Dis"); key.SetValue("InstallLocation", destination);
            key.SetValue("DisplayIcon", executable); key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall");
            key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
        }
        using (var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        {
            if (startup) run.SetValue("Dis", $"\"{executable}\" --background"); else run.DeleteValue("Dis", false);
        }
        // Shell links use Windows Script Host COM directly; no shell command parsing.
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcuts are unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Dis");
        Directory.CreateDirectory(menu);
        dynamic shortcut = shell.CreateShortcut(Path.Combine(menu, "Dis.lnk"));
        shortcut.TargetPath = executable; shortcut.WorkingDirectory = destination; shortcut.IconLocation = executable; shortcut.Save();
        return executable;
    }

    private static void Uninstall()
    {
        if (MessageBox.Show("Remove Dis for this Windows account? Android and the separately installed display driver are kept.", "Uninstall Dis", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        CheckNotRunning();
        // Root is a fixed per-user application directory. Refuse junctions before recursive cleanup.
        for (string? ancestor = Root; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if (Directory.Exists(ancestor) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The application path contains a directory link; remove Dis manually.");
        if (Directory.Exists(Root))
        {
            var pending = new Stack<string>(); pending.Push(Root);
            while (pending.TryPop(out var path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Application folder contains a link; remove it manually.");
                foreach (string child in Directory.GetFileSystemEntries(path))
                {
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Application folder contains a link; remove it manually.");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(child);
                }
            }
            foreach (string directory in Directory.GetDirectories(Root)) Directory.Delete(directory, true);
        }
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
        {
            if (run?.GetValue("Dis") is string value && value.Contains(Root, StringComparison.OrdinalIgnoreCase)) run.DeleteValue("Dis", false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Dis", "Dis.lnk");
        if (File.Exists(shortcut)) File.Delete(shortcut);
        MessageBox.Show("Dis removed. Your preferences are kept. The running uninstaller remains in the Application folder and can be deleted after this window closes.", "Dis");
    }
}

internal sealed class SetupForm : Form
{
    private readonly CheckBox startup = new() { Text = "Start Dis in the tray when I sign in", Checked = true, AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(480, 0) };
    private readonly Button install = new() { Text = "Install Dis", AutoSize = true, Height = 38 };
    public SetupForm()
    {
        Text = "Dis Setup"; ClientSize = new(540, 300); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(26), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(new Label { Text = "Your Android. A second Windows display.", AutoSize = true, Font = new(Font.FontFamily, 16), Margin = new(0, 0, 0, 20) });
        layout.Controls.Add(new Label { Text = "Install Dis for your account, including the Android receiver and USB tools.\nThen open Devices to install the phone app and set up the display driver once.", AutoSize = true, MaximumSize = new(480, 0), Margin = new(0, 0, 0, 18) });
        layout.Controls.Add(startup); layout.Controls.Add(install); layout.Controls.Add(status); Controls.Add(layout);
        install.Click += async (_, _) =>
        {
            install.Enabled = false; startup.Enabled = false; ControlBox = false; status.Text = "Installing Dis…";
            bool startAtLogin = startup.Checked;
            try
            {
                string executable = await Task.Run(() => Program.Install(startAtLogin));
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)!, Arguments = "--ui-page 1" });
                Close();
            }
            catch (Exception e) { status.Text = e.Message; install.Enabled = true; startup.Enabled = true; ControlBox = true; }
        };
    }
}
