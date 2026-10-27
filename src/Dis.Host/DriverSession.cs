using System.Diagnostics;

namespace Dis.Host;

// A separate elevated helper survives a crash of the streaming app and hides only
// the pinned Root\MttVDD adapter when the session ends. It never installs a driver.
internal sealed class DriverSession : IAsyncDisposable
{
    private readonly EventWaitHandle ready;
    private readonly EventWaitHandle stop;
    private readonly EventWaitHandle heartbeat;
    private Process? helper;

    private DriverSession(string name)
    {
        ready = new(false, EventResetMode.ManualReset, name + ".ready");
        stop = new(false, EventResetMode.ManualReset, name + ".stop");
        heartbeat = new(false, EventResetMode.AutoReset, name + ".heartbeat");
    }
    public void KeepAlive() => heartbeat.Set();

    public static async Task<DriverSession> StartAsync()
    {
        string name = "Local\\Dis.Display." + Guid.NewGuid().ToString("N");
        var session = new DriverSession(name);
        try
        {
            using var parent = Process.GetCurrentProcess();
            string executable = Environment.ProcessPath ?? throw new IOException("Cannot locate Dis.exe.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, typeof(Program).Assembly.GetName().Name + ".dll"));
            foreach (string arg in new[] { "--display-device-session", parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString(), name })
                start.ArgumentList.Add(arg);
            session.helper = Process.Start(start) ?? throw new IOException("Could not start virtual-display management.");
            var started = Stopwatch.GetTimestamp();
            while (!session.ready.WaitOne(0))
            {
                if (session.helper.HasExited || Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(30))
                    throw new IOException("The virtual adapter could not be enabled. Run second-display setup and retry.");
                await Task.Delay(100);
            }
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            stop.Set();
            if (helper is not null)
            {
                await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                if (helper.ExitCode != 0) throw new IOException($"Windows could not hide the virtual adapter (code 0x{helper.ExitCode:X8}). Disable Virtual Display Driver in Device Manager.");
            }
        }
        finally { helper?.Dispose(); helper = null; ready.Dispose(); stop.Dispose(); heartbeat.Dispose(); }
    }

    public static async Task RunHelperAsync(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[1], out int pid) || !long.TryParse(args[2], out long ticks)
            || !args[3].StartsWith("Local\\Dis.Display.", StringComparison.Ordinal))
            throw new IOException("Invalid display-session arguments.");
        using var parent = Process.GetProcessById(pid);
        if (parent.StartTime.ToUniversalTime().Ticks != ticks
            || !string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The display-session owner no longer exists.");
        using var ready = EventWaitHandle.OpenExisting(args[3] + ".ready");
        using var stop = EventWaitHandle.OpenExisting(args[3] + ".stop");
        using var heartbeat = EventWaitHandle.OpenExisting(args[3] + ".heartbeat");
        try
        {
            DriverSetup.SetDeviceEnabled(true);
            ready.Set();
            // The engine must prove it is still processing receiver frames. An
            // abandoned or hung engine must not leave an enabled adapter behind.
            await Task.Run(() =>
            {
                long renewed = Stopwatch.GetTimestamp();
                TimeSpan lease = TimeSpan.FromSeconds(75); // Covers manual network pairing.
                while (!parent.HasExited)
                {
                    int signaled = WaitHandle.WaitAny([stop, heartbeat], 1000);
                    if (signaled == 0) return;
                    if (signaled == 1) { renewed = Stopwatch.GetTimestamp(); lease = TimeSpan.FromSeconds(15); }
                    if (Stopwatch.GetElapsedTime(renewed) >= lease) return;
                }
            });
        }
        finally
        {
            // Give Windows time to release capture/display resources before disabling.
            foreach (var adapter in DisplayModes.Enumerate().Where(a => a.Virtual && a.Active
                && (a.Description.Contains("Virtual Display Driver", StringComparison.OrdinalIgnoreCase)
                    || a.Description.Contains("MttVDD", StringComparison.OrdinalIgnoreCase))))
            {
                try { DisplayModes.Disconnect(adapter.Name); }
                catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { }
            }
            for (int attempt = 0; ; attempt++)
            {
                try { DriverSetup.SetDeviceEnabled(false); break; }
                catch (Exception e) when (attempt < 19 && (e is IOException or System.ComponentModel.Win32Exception))
                { await Task.Delay(250); }
            }
        }
    }
}
