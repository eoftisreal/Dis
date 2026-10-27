using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Dis.Core;

namespace Dis.Host;

// The Qt shell owns presentation; this controller exclusively owns the tested
// Windows capture, pairing, USB tunnel and virtual-adapter session lifetimes.
internal sealed class HostController
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Action<Action> post;
    private DisplayServer? server;
    private AdbBridge? tunnel;
    private DriverSession? driver;
    private CancellationTokenSource? receiverWatch;
    private PairingSession? pairing;
    private string? virtualName;
    private bool probing, stopped;
    private readonly UsbAutoconnect autoconnect = new();
    private string? sessionSerial;
    private string status = "ready", message = "Your workspace, extended.", pairingUri = "";
    private string probeError = "";
    private AdbBridge.Device[] devices = [];
    private StreamStatistics? metrics;
    public HostPreferences Preferences { get; private set; }
    public bool Busy { get; private set; }
    public event Action<object>? StateChanged;

    public HostController(Action<Action> post)
    {
        this.post = post;
        Preferences = Normalize(HostPreferences.Load());
    }

    private static HostPreferences Normalize(HostPreferences p) => p.EnforceConnectionBehavior() with
    {
        Quality = Math.Clamp(p.Quality, 0, 2), Desktop = Math.Clamp(p.Desktop, 0, 2),
        Orientation = Math.Clamp(p.Orientation, 0, 3), Fps = Math.Clamp(p.Fps, 10, 60), Port = Math.Clamp(p.Port, 1024, 65535),
        Adb = "", Serial = p.Serial ?? "", ConnectionAddress = p.ConnectionAddress ?? "127.0.0.1", MirrorDisplay = ""
    };

    public void Publish()
    {
        var addresses = new List<object> { new { label = "USB cable", address = "127.0.0.1" } };
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a => a.OperationalStatus == OperationalStatus.Up))
        foreach (var ip in adapter.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)))
            addresses.Add(new { label = $"{adapter.Name} · {ip.Address}", address = ip.Address.ToString() });
        var screens = Screen.AllScreens.Select(s => new { name = s.DeviceName, label = $"{(s.Primary ? "Main display" : "Display")} · {s.Bounds.Width} × {s.Bounds.Height}", width = s.Bounds.Width, height = s.Bounds.Height, primary = s.Primary }).ToArray();
        StateChanged?.Invoke(new { type = "state", status, message, busy = Busy, active = server is not null,
            preferences = Preferences, devices, phoneOnline = devices.Any(d => d.State == "device"), probeError,
            driverInstalled = DriverSetup.HasDevice(), connections = addresses, displays = screens, metrics,
            pairingUri, version = "0.4.4" });
    }

    public async Task ProbeAsync(bool allowAutoConnect = true)
    {
        if (probing || Busy || stopped) return;
        probing = true;
        try
        {
            try { devices = await AdbBridge.InspectAsync(Preferences.Adb); probeError = ""; }
            catch (Exception e) when (Handled(e)) { devices = []; probeError = e.Message; }
            if (!stopped) Publish();
            if (!allowAutoConnect || stopped || Busy || Preferences.ConnectionAddress != "127.0.0.1") return;
            var ready = devices.Where(d => d.State == "device" && (Preferences.Serial.Length == 0 || d.Serial == Preferences.Serial)).ToArray();
            autoconnect.Observe(ready.Length == 1 ? ready[0].Serial : null);
            if (server is not null && sessionSerial is not null && !devices.Any(d => d.Serial == sessionSerial && d.State == "device"))
            {
                await ExecuteAsync(JsonSerializer.SerializeToElement(new { command = "disconnect", unplugged = true }));
                return;
            }
            if (ready.Length != 1 || !autoconnect.TryBegin(Preferences.AutoConnect, server is not null)) return;
            var preferences = Preferences;
            try
            {
                if (await AdbBridge.UsbAutostartEnabledAsync(Preferences.Adb, ready[0].Serial))
                {
                    if (stopped || Busy || server is not null || preferences != Preferences || !autoconnect.CanContinue(ready[0].Serial)) return;
                    await ExecuteAsync(JsonSerializer.SerializeToElement(new { command = "connect", automatic = true }));
                }
                else autoconnect.PreferenceDisabled(); // Allow the phone switch to take effect without replugging.
            }
            catch (Exception e) when (Handled(e)) { probeError = e.Message; Publish(); }
        }
        finally { probing = false; }
    }

    public async Task ExecuteAsync(JsonElement command)
    {
        string action = command.GetProperty("command").GetString() ?? "";
        if (action == "snapshot") { Publish(); return; }
        if (action == "refresh") { await ProbeAsync(); return; }
        if (Busy) return;
        Busy = true;
        try
        {
            if (action == "configure")
            {
                if (server is not null) throw new IOException("Disconnect before changing your display preferences.");
                var merged = JsonSerializer.SerializeToNode(Preferences, Json)!.AsObject();
                foreach (var property in command.GetProperty("settings").EnumerateObject()) merged[property.Name] = System.Text.Json.Nodes.JsonNode.Parse(property.Value.GetRawText());
                Preferences = Normalize(merged.Deserialize<HostPreferences>(Json) ?? new());
                Preferences.Save();
                autoconnect.Reset();
            }
            else if (action == "connect") { if (server is null) await StartAsync(); }
            else if (action == "disconnect") { if (!command.TryGetProperty("unplugged", out _)) autoconnect.Suppress(); await StopAsync(); status = "ready"; message = "Disconnected. Ready when you are."; }
            else if (action == "rotate")
            {
                Preferences = Preferences with { Orientation = Math.Clamp(command.GetProperty("orientation").GetInt32(), 0, 3) };
                Preferences.Save();
                bool restart = server is not null;
                if (restart) await StopAsync(false);
                if (restart) await StartAsync();
                else { status = "ready"; message = "Orientation saved for your next connection."; }
            }
            else if (action == "setup")
            {
                status = "working"; message = "Setting up your virtual display…"; Publish();
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
                start.ArgumentList.Add("--install-display-driver");
                using var helper = Process.Start(start) ?? throw new IOException("Could not open display setup.");
                await helper.WaitForExitAsync();
                if (helper.ExitCode != 0) throw new IOException("Display setup did not finish. Check the setup window for details.");
                status = "ready"; message = "Virtual display installed. You're ready to connect.";
            }
            else if (action == "install")
            {
                status = "working"; message = "Installing the Android receiver…"; Publish();
                string apk = command.TryGetProperty("path", out var path) ? path.GetString() ?? "" : Path.Combine(AppContext.BaseDirectory, "Dis.apk");
                if (!File.Exists(apk)) throw new IOException("Select the Dis Android APK to install.");
                await AdbBridge.InstallReceiverAsync(Preferences.Adb, Preferences.Serial, apk);
                autoconnect.Reset();
                status = "ready"; message = "Android receiver installed. Connect your display.";
            }
            else if (action == "refresh") await ProbeAsync();
        }
        catch (Exception e) when (Handled(e) || e is JsonException or ArgumentException or InvalidOperationException)
        {
            string reason = e.Message;
            if (action is "connect" or "rotate")
                try { await StopAsync(); } catch (Exception cleanup) when (Handled(cleanup)) { reason += " Cleanup: " + cleanup.Message; }
            status = "error"; message = reason;
        }
        finally { Busy = false; if (!stopped) Publish(); }
    }

    private async Task StartAsync()
    {
        var p = Preferences;
        if (!IPAddress.TryParse(p.ConnectionAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new IOException("Select an available USB or local-network connection.");
        status = "connecting"; message = "Preparing your display…"; metrics = null; Publish();
        if (IPAddress.IsLoopback(address))
        {
            tunnel = await AdbBridge.ConnectAsync(p.Adb, p.Serial, p.Port);
            sessionSerial = tunnel.Serial;
        }
        string deviceName;
        Rectangle bounds;
        {
            if (driver is null)
            {
                message = "Approve Windows' administrator prompt to enable the virtual monitor."; Publish();
                driver = await DriverSession.StartAsync();
                for (int i = 0; i < 50 && !DisplayModes.Enumerate().Any(a => a.Virtual); i++) await Task.Delay(100);
            }
            virtualName = DisplayModes.Enumerate().FirstOrDefault(a => a.Virtual)?.Name;
            deviceName = virtualName = await DisplayModes.EnsureExtendedAsync();
            int width = p.Desktop == 2 ? 800 : 1920, height = p.Desktop == 2 ? 600 : 1080;
            if (p.Desktop == 0 && tunnel is not null) { var phone = await tunnel.ScreenSizeAsync(); width = Math.Max(phone.Width, phone.Height); height = Math.Min(phone.Width, phone.Height); }
            if ((p.Orientation & 1) != 0) (width, height) = (height, width);
            DisplayModes.SetDesktopSize(deviceName, width, height, p.Orientation);
            bounds = DisplayModes.Bounds(deviceName);
        }
        pairing ??= new();
        server = new(new(address, p.Port, deviceName, bounds, 1920, 1920, p.Fps, 75, p.Input, p.Quality == 0 ? 1280 * 720 : 1920 * 1080), pairing);
        var current = server;
        current.Status += value => post(() => { if (server != current || stopped) return; if (value.Contains(" fps", StringComparison.Ordinal)) return; message = value; if (value.StartsWith("Connected", StringComparison.Ordinal)) status = "connected"; else if (value.StartsWith("Disconnected", StringComparison.Ordinal)) status = "waiting"; Publish(); });
        current.Statistics += value => post(() => { if (server != current || stopped) return; metrics = value; if (current.LastFrameAcknowledged != 0) { driver?.KeepAlive(); status = "connected"; message = "Your Android display is connected."; } Publish(); });
        receiverWatch = new();
        _ = WatchReceiverAsync(current, IPAddress.IsLoopback(address), receiverWatch.Token);
        string uri = current.ConnectionUri(address.ToString());
        if (tunnel is not null) { await tunnel.OpenReceiverAsync(uri); if (status != "connected") { status = "waiting"; message = "Opening Dis on your Android device…"; } }
        else { pairingUri = uri; status = "waiting"; message = "Open Dis on Android and use the network pairing link."; }
    }

    private async Task StopAsync(bool revoke = true)
    {
        receiverWatch?.Cancel(); receiverWatch?.Dispose(); receiverWatch = null;
        try { if (server is not null) { var old = server; server = null; await old.DisposeAsync(); } }
        finally
        {
            try { if (tunnel is not null) { var old = tunnel; tunnel = null; await old.DisposeAsync(); } }
            finally
            {
                if (revoke)
                {
                    pairing?.Dispose(); pairing = null;
                    try { if (virtualName is not null) DisplayModes.Disconnect(virtualName); }
                    catch (Exception e) when (Handled(e)) { message = e.Message; }
                    finally
                    {
                        virtualName = null;
                        if (driver is not null) { var old = driver; driver = null; await old.DisposeAsync(); }
                    }
                }
                pairingUri = ""; metrics = null; sessionSerial = null;
            }
        }
    }

    private async Task WatchReceiverAsync(DisplayServer current, bool usb, CancellationToken token)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            while (true)
            {
                await Task.Delay(1000, token);
                if (server != current || stopped) return;
                long acknowledged = current.LastFrameAcknowledged;
                TimeSpan? age = acknowledged == 0 ? null : Stopwatch.GetElapsedTime(acknowledged);
                if (age is not null && age < TimeSpan.FromSeconds(5)) driver?.KeepAlive();
                if (Busy || !ReceiverWatchdog.Expired(Stopwatch.GetElapsedTime(started), age, current.HasReceiver, usb)) continue;
                Busy = true; autoconnect.Suppress();
                string reason = acknowledged == 0
                    ? "The phone did not start displaying video. The virtual display was removed. Unlock the phone, open Dis, then reconnect."
                    : "The phone stopped receiving video. The virtual display was removed. Reconnect when your phone is ready.";
                try { await StopAsync(); }
                catch (Exception e) when (Handled(e)) { reason += " Cleanup: " + e.Message; }
                finally { Busy = false; status = "error"; message = reason; if (!stopped) Publish(); }
                return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public async Task ShutdownAsync() { stopped = true; await StopAsync(); }
    private static bool Handled(Exception e) => e is IOException or TimeoutException or SocketException or UnauthorizedAccessException
        or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or System.Security.Cryptography.CryptographicException;
}
