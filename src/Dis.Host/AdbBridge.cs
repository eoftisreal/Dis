using System.Diagnostics;
using System.ComponentModel;
using Dis.Core;
using System.Text;

namespace Dis.Host;

internal sealed class AdbBridge(string executable, string serial, int port, bool owned) : IAsyncDisposable
{
    public static async Task<AdbBridge> ConnectAsync(string executable, string requestedSerial, int port)
    {
        executable = AdbLocator.Resolve(executable);
        string serial = requestedSerial.Trim();
        if (serial.Length == 0)
        {
            string devices = await RunAsync(executable, "devices");
            var ready = devices.Split('\n').Select(l => l.Trim().Split('\t'))
                .Where(p => p.Length == 2 && p[1] == "device").Select(p => p[0]).ToArray();
            if (ready.Length != 1) throw new IOException(ready.Length == 0
                ? "No authorized Android device. Enable USB debugging and accept the phone's authorization prompt."
                : "Several Android devices are connected. Enter the intended device serial.");
            serial = ready[0];
        }
        string endpoint = $"tcp:{port}";
        string mappings = await RunAsync(executable, "-s", serial, "reverse", "--list");
        foreach (string line in mappings.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[^2] != endpoint) continue;
            if (parts[^1] != endpoint) throw new IOException("That Android port belongs to another tunnel. Choose a different port.");
            return new(executable, serial, port, false);
        }
        await RunAsync(executable, "-s", serial, "reverse", "--no-rebind", endpoint, endpoint);
        return new(executable, serial, port, true);
    }

    private static Task<string> RunAsync(string executable, params string[] args) => RunCommandAsync(executable, TimeSpan.FromSeconds(10), args);

    private static async Task<string> RunCommandAsync(string executable, TimeSpan timeout, string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = StartProcess(start);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new IOException($"ADB did not respond within {timeout.TotalSeconds:0} seconds.");
        }
        string output = await stdout;
        string error = await stderr;
        if (process.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
            if (detail.Length == 0) detail = $"exit code 0x{process.ExitCode:X8}. Keep the entire platform-tools folder, including its DLLs, together.";
            throw new IOException($"ADB: {detail}");
        }
        return output;
    }

    public async Task OpenReceiverAsync(string uri)
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(uri)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        // Base64url contains no shell metacharacters; the raw pairing URI never enters Android shell parsing.
        string output = await RunCommandAsync(executable, TimeSpan.FromSeconds(30), ["-s", serial, "shell", "am", "start", "-W", "-n",
            "io.github.eoftisreal.dis/.UsbConnectActivity", "--es", "dis_pairing_b64", encoded]);
        if (output.Contains("Error", StringComparison.OrdinalIgnoreCase) || output.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Install the updated Dis Android app first, then connect USB again.");
    }

    public static async Task InstallReceiverAsync(string executable, string requestedSerial, string apk)
    {
        executable = AdbLocator.Resolve(executable);
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedSerial)) { args.Add("-s"); args.Add(requestedSerial.Trim()); }
        args.AddRange(["install", "-r", apk]);
        string output;
        try { output = await RunCommandAsync(executable, TimeSpan.FromSeconds(90), [.. args]); }
        catch (IOException e) when (e.Message.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.Ordinal))
        { throw new IOException("Uninstall the old Dis preview on the phone once, then press Install/update Android app again.", e); }
        if (!output.Contains("Success", StringComparison.Ordinal))
            throw new IOException("Android installation failed. If this is an older preview signed with a different key, uninstall that preview on the phone once, then install again. " + output.Trim());
    }

    private static Process StartProcess(ProcessStartInfo start)
    {
        try { return Process.Start(start) ?? throw new IOException("Cannot start adb.exe."); }
        catch (Win32Exception e)
        {
            throw new IOException("ADB could not be started. Use Browse to select adb.exe and keep the complete " +
                $"Android Platform Tools folder together. Windows reported: {e.Message}", e);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (owned) await RunAsync(executable, "-s", serial, "reverse", "--remove", $"tcp:{port}");
    }
}
