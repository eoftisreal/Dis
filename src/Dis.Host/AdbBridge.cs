using System.Diagnostics;

namespace Dis.Host;

internal sealed class AdbBridge(string executable, string serial, int port, bool owned) : IAsyncDisposable
{
    public static async Task<AdbBridge> ConnectAsync(string executable, string requestedSerial, int port)
    {
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

    private static async Task<string> RunAsync(string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start adb.exe.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new IOException("ADB did not respond within 10 seconds.");
        }
        string output = await stdout;
        string error = await stderr;
        if (process.ExitCode != 0) throw new IOException($"ADB: {error.Trim()}");
        return output;
    }

    public async ValueTask DisposeAsync()
    {
        if (owned) await RunAsync(executable, "-s", serial, "reverse", "--remove", $"tcp:{port}");
    }
}
