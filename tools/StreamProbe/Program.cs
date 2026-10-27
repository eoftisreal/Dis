using System.Net;
using System.Text.Json;
using Dis.Host;
using System.Diagnostics;
using Dis.Core;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;

if (args.Contains("--liveness-test")) { await TestLivenessAsync(); return; }

// Real-device mirror probe: no preferences, virtual driver, or Windows layout changes.
// It opens Dis on the one authorized USB device and records credential-free host metrics.
string adb = Path.GetFullPath(".tools/android-sdk/platform-tools/adb.exe");
string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/stream-probe.json");
int duration = args.Length > 1 ? int.Parse(args[1]) : 30;
var screen = Screen.PrimaryScreen ?? throw new IOException("No primary Windows display.");
bool virtualProbe = args.Contains("--virtual");
string? attachedVirtual = null;
if (virtualProbe)
{
    bool alreadyActive = DisplayModes.Enumerate().Any(a => a.Virtual && a.Active);
    string name = await DisplayModes.EnsureExtendedAsync();
    if (!alreadyActive) attachedVirtual = name;
    screen = Screen.AllScreens.First(s => s.DeviceName == name);
}
try
{
var samples = new List<StreamStatistics>();
var phases = new List<string>();
string phase = "fullscreen";
await using var tunnel = await AdbBridge.ConnectAsync(adb, "", 49831);
await using var server = new DisplayServer(new(IPAddress.Loopback, 49831, screen.DeviceName, screen.Bounds, 1920, 1920, 60, 75, false));
server.Status += status => Console.WriteLine(status);
server.Statistics += sample => { lock (samples) { samples.Add(sample); phases.Add(phase); } };
await tunnel.OpenReceiverAsync(server.ConnectionUri("127.0.0.1"));
if (args.Contains("--panel"))
{
    await Task.Delay(TimeSpan.FromSeconds(10));
    await Android("input", "tap", "640", "360");
    await Android("input", "tap", "1210", "60");
    phase = "controls"; Console.WriteLine("Opening phone controls");
    await Task.Delay(TimeSpan.FromSeconds(10));
    await Android("input", "keyevent", "KEYCODE_BACK");
    phase = "fullscreen-again"; Console.WriteLine("Closing phone controls");
    await Task.Delay(TimeSpan.FromSeconds(10));
}
else await Task.Delay(TimeSpan.FromSeconds(duration));
StreamStatistics[] captured;
lock (samples) captured = samples.ToArray();
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new { device = tunnel.Serial, durationSeconds = duration, phases, samples = captured }, new JsonSerializerOptions { WriteIndented = true }));
if (captured.Length < 3) throw new IOException("Too few streaming samples; check the phone and its Dis app.");
Console.WriteLine($"Measured {captured.Average(s => s.Fps):F1} FPS over {captured.Length} host samples; saved {output}");
}
finally { if (attachedVirtual is not null) DisplayModes.Disconnect(attachedVirtual); }

async Task Android(params string[] command)
{
    var start = new ProcessStartInfo(adb) { UseShellExecute = false, CreateNoWindow = true };
    foreach (string argument in new[] { "shell" }.Concat(command)) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException("ADB command failed to start.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new IOException("ADB command failed.");
}

static async Task TestLivenessAsync()
{
    var screen = Screen.PrimaryScreen ?? throw new IOException("No primary display for capture test.");
    await using var server = new DisplayServer(new(IPAddress.Loopback, 49832, screen.DeviceName, screen.Bounds, 320, 240, 30, 75, false));
    var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    server.Status += value => { if (value.StartsWith("Connected", StringComparison.Ordinal)) confirmed.TrySetResult(); };
    var uri = new Uri(server.ConnectionUri("127.0.0.1"));
    string token = uri.Query.Split('&').Single(p => p.StartsWith("token=", StringComparison.Ordinal))[6..];
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    using (var client = new TcpClient())
    {
        await client.ConnectAsync(IPAddress.Loopback, 49832, deadline.Token);
        using var tls = new SslStream(client.GetStream(), false, (_, certificate, _, _) => certificate is not null
            && Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).Equals(server.Fingerprint, StringComparison.OrdinalIgnoreCase));
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "Dis liveness test" }, deadline.Token);
        await Wire.WriteAsync(tls, Wire.Json(MessageType.Hello, new ClientHello(2, token, 320, 240, 30, "jpeg")), deadline.Token);
        var config = await Wire.ReadAsync(tls, deadline.Token);
        var first = await Wire.ReadAsync(tls, deadline.Token);
        var second = await Wire.ReadAsync(tls, deadline.Token);
        if (config.Type != MessageType.Config || first.Type != MessageType.Frame || second.Type != MessageType.Frame
            || server.LastFrameAcknowledged != 0 || confirmed.Task.IsCompleted) throw new IOException("An unacknowledged stream was reported as ready.");
        await Wire.WriteAsync(tls, new(MessageType.Ack, first.Sequence, []), deadline.Token);
        await confirmed.Task.WaitAsync(deadline.Token);
        if (server.LastFrameAcknowledged == 0) throw new IOException("Frame acknowledgement did not confirm playback.");
    }
    for (int i = 0; i < 50 && server.HasReceiver; i++) await Task.Delay(100, deadline.Token);
    if (server.HasReceiver) throw new IOException("Closed receiver was not detected.");
    Console.WriteLine("PASS receiver stays unconfirmed until frame ACK, confirms on ACK, and detects receiver disconnect.");
}
