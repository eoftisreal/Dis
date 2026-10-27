using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dis.Core;

int passed = 0;
await Check("touch and scrolling packets match Java and reject invalid gesture combinations", async () =>
{
    var scrollBytes = Convert.FromHexString("000500003f0000003e8000000000000041f00000c2700000");
    var scroll = InputEvent.Parse(scrollBytes);
    Require(scroll.Kind == PointerKind.Mouse && scroll.Action == PointerAction.Scroll && scroll.TiltX == 30 && scroll.TiltY == -60);
    var touch = InputEvent.Parse(Convert.FromHexString("020000003f0000003e8000003f4000000000000000000000"));
    Require(touch.Kind == PointerKind.Touch && touch.Action == PointerAction.Down && touch.Pressure == .75f);
    scrollBytes[0] = 2;
    await Throws<InvalidDataException>(() => { InputEvent.Parse(scrollBytes); return Task.CompletedTask; });
    scrollBytes[0] = 0; scrollBytes[3] = 1;
    await Throws<InvalidDataException>(() => { InputEvent.Parse(scrollBytes); return Task.CompletedTask; });
});
await Check("embedded USB tools restore missing and damaged files without an external ADB path", () =>
{
    using var files = new AdbTestDirectory();
    using var data = new MemoryStream();
    using (var archive = new System.IO.Compression.ZipArchive(data, System.IO.Compression.ZipArchiveMode.Create, true))
        foreach (string name in UsbToolBundle.RequiredFiles)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(System.Text.Encoding.UTF8.GetBytes("bundled-test-" + name));
        }
    byte[] bytes = data.ToArray();
    string cache = Path.Combine(files.Locations.AppDirectory, "private-usb-cache");
    string executable = UsbToolBundle.Prepare(bytes, cache);
    Require(File.ReadAllText(executable) == "bundled-test-adb.exe");
    string library = Path.Combine(Path.GetDirectoryName(executable)!, "AdbWinApi.dll");
    File.Delete(executable); File.WriteAllText(library, "corrupted");
    Require(UsbToolBundle.Prepare(bytes, cache) == executable);
    Require(File.ReadAllText(executable) == "bundled-test-adb.exe" && File.ReadAllText(library) == "bundled-test-AdbWinApi.dll");
    return Task.CompletedTask;
});
await Check("permanent adapter cleanup overrides legacy mirror and idle preferences", () =>
{
    var legacy = new Dis.Host.HostPreferences(Mode: 1, HideIdle: false, MirrorDisplay: "old display", Adb: "deleted-old-build/adb.exe", Quality: 2);
    var current = legacy.EnforceConnectionBehavior();
    Require(current.Mode == 0 && current.HideIdle && current.MirrorDisplay == "" && current.Adb == "" && current.Version == 3 && current.Quality == 2);
    Require(new Dis.Host.HostPreferences().HideIdle);
    return Task.CompletedTask;
});
await Check("receiver watchdog cleans failed startup, disconnect, and stalled video", () =>
{
    Require(!ReceiverWatchdog.Expired(TimeSpan.FromSeconds(29), null, false, true));
    Require(ReceiverWatchdog.Expired(TimeSpan.FromSeconds(30), null, true, true));
    Require(!ReceiverWatchdog.Expired(TimeSpan.FromSeconds(59), null, false, false));
    Require(ReceiverWatchdog.Expired(TimeSpan.FromSeconds(60), null, false, false));
    Require(!ReceiverWatchdog.Expired(TimeSpan.FromHours(1), TimeSpan.FromSeconds(2), false, true));
    Require(ReceiverWatchdog.Expired(TimeSpan.FromHours(1), TimeSpan.FromSeconds(3), false, true));
    Require(!ReceiverWatchdog.Expired(TimeSpan.FromHours(1), TimeSpan.FromSeconds(9), true, true));
    Require(ReceiverWatchdog.Expired(TimeSpan.FromHours(1), TimeSpan.FromSeconds(10), true, true));
    return Task.CompletedTask;
});
await Check("cross-language golden packet", async () =>
{
    using var output = new MemoryStream();
    await Wire.WriteAsync(output, new(MessageType.Hello, 7, "abc"u8.ToArray()));
    Require(Convert.ToHexString(output.ToArray()) == "44495331010000000000000300000007616263");
});
await Check("fragmented headers and bodies", async () =>
{
    byte[] golden = Convert.FromHexString("44495331010000000000000300000007616263");
    using var stream = new FragmentedStream(golden);
    Packet p = await Wire.ReadAsync(stream);
    Require(p.Type == MessageType.Hello && p.Sequence == 7 && p.Payload.SequenceEqual("abc"u8.ToArray()));
});
await Check("truncation at every byte boundary", async () =>
{
    byte[] golden = Convert.FromHexString("44495331010000000000000300000007616263");
    for (int i = 0; i < golden.Length; i++)
    {
        using var stream = new MemoryStream(golden[..i]);
        await Throws<EndOfStreamException>(async () => { await Wire.ReadAsync(stream); });
    }
});
await Check("reject huge lengths before payload allocation", async () =>
{
    foreach (uint length in new uint[] { 8 * 1024 * 1024 + 1, int.MaxValue, uint.MaxValue })
    {
        byte[] header = Convert.FromHexString("44495331030000000000000000000001");
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), length);
        using var stream = new MemoryStream(header);
        await Throws<InvalidDataException>(async () => { await Wire.ReadAsync(stream); });
    }
});
await Check("reject future flags, types, and incompatible versions", async () =>
{
    foreach (int index in new[] { 0, 4, 5, 6, 7 })
    {
        byte[] bytes = Convert.FromHexString("44495331050000000000000000000001"); bytes[index] = 255;
        using var stream = new MemoryStream(bytes);
        await Throws<InvalidDataException>(async () => { await Wire.ReadAsync(stream); });
    }
    await Throws<InvalidDataException>(() => { FrameSize.Validate(new(3, "token", 1920, 1080, 30, "jpeg")); return Task.CompletedTask; });
});
await Check("read cancellation unblocks a silent peer", async () =>
{
    using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    using var client = new TcpClient();
    var connect = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
    using var peer = await listener.AcceptTcpClientAsync(); await connect;
    using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
    await Throws<OperationCanceledException>(async () => { await Wire.ReadAsync(peer.GetStream(), timeout.Token); });
});
await Check("pointer golden vector and non-finite rejection", async () =>
{
    byte[] bytes = Convert.FromHexString("010100003f0000003e8000003f400000c170000041f00000");
    var e = InputEvent.Parse(bytes);
    Require(e.Kind == PointerKind.Pen && e.Action == PointerAction.Move && e.X == .5f && e.Y == .25f && e.Pressure == .75f && e.TiltX == -15 && e.TiltY == 30);
    foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -.1f, 1.1f })
    {
        var bad = bytes.ToArray(); BinaryPrimitives.WriteSingleBigEndian(bad.AsSpan(4), value);
        await Throws<InvalidDataException>(() => { InputEvent.Parse(bad); return Task.CompletedTask; });
    }
});
await Check("portrait and odd-size geometry keeps aspect ratio", () =>
{
    Require(FrameSize.Fit(1080, 2400, 1920, 1080) == new FrameSize(486, 1080));
    Require(FrameSize.Fit(3840, 2160, 1920, 1080) == new FrameSize(1920, 1080));
    Require(FrameSize.Fit(852, 478, 1920, 1080) == new FrameSize(852, 478));
    return Task.CompletedTask;
});
await Check("ack window rejects stale and future acknowledgements", async () =>
{
    var window = new AckWindow(); var first = window.Expect(uint.MaxValue);
    Require(!window.Acknowledge(0) && !first.IsCompleted);
    await Throws<InvalidOperationException>(() => { window.Expect(0); return Task.CompletedTask; });
    Require(window.Acknowledge(uint.MaxValue)); await first;
    Require(!window.Acknowledge(uint.MaxValue));
    var next = window.Expect(0); Require(!window.Acknowledge(uint.MaxValue)); Require(window.Acknowledge(0)); await next;
});
await Check("pairing token rejects malformed and unrelated secrets", () =>
{
    var token = new PairingToken();
    Require(token.Matches(token.Value) && token.Matches(token.Value.ToUpperInvariant()));
    Require(!token.Matches(null) && !token.Matches("short") && !token.Matches(new string('z', 64)) && !token.Matches(new PairingToken().Value));
    return Task.CompletedTask;
});
await Check("USB autoconnect respects manual disconnect, replug, preferences, and failed attempts", () =>
{
    var usb = new UsbAutoconnect();
    Require(!usb.TryBegin(true, false));
    usb.Observe("phone");
    Require(!usb.TryBegin(false, false) && !usb.TryBegin(true, true));
    Require(usb.TryBegin(true, false) && !usb.TryBegin(true, false));
    usb.PreferenceDisabled(); Require(usb.TryBegin(true, false));
    usb.Suppress(); usb.PreferenceDisabled(); Require(!usb.TryBegin(true, false));
    usb.Observe("phone"); Require(!usb.TryBegin(true, false));
    usb.Observe(null); usb.Observe("phone"); Require(usb.TryBegin(true, false));
    usb.Observe("other"); Require(usb.TryBegin(true, false));
    usb.Reset(); Require(usb.TryBegin(true, false));
    return Task.CompletedTask;
});
await Check("TLS loopback transports real framed messages with a pinned certificate", async () =>
{
    using var identity = new SessionIdentity();
    var cert = identity.Certificate;
    byte[] pin = SHA256.HashData(cert.RawData);
    using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
    using var peer = await listener.AcceptTcpClientAsync();
    using var serverTls = new SslStream(peer.GetStream());
    using var clientTls = new SslStream(client.GetStream(), false, (_, certificate, _, _) =>
        certificate is not null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(certificate.GetRawCertData()), pin));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await Task.WhenAll(serverTls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert }, timeout.Token),
        clientTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "Dis test" }, timeout.Token));
    await Wire.WriteAsync(clientTls, new(MessageType.Hello, 1, "abc"u8.ToArray()), timeout.Token);
    Packet packet = await Wire.ReadAsync(serverTls, timeout.Token);
    Require(packet.Sequence == 1 && packet.Payload.SequenceEqual("abc"u8.ToArray()));
});
await Check("portable ADB discovery is independent of the working directory", () =>
{
    using var files = new AdbTestDirectory();
    string expected = files.Create("app", "platform-tools", "adb.exe");
    files.Create("working", "adb.exe");
    Require(AdbLocator.Find("adb.exe", files.Locations) == expected);
    return Task.CompletedTask;
});
await Check("explicit quoted, relative, and folder ADB selections", () =>
{
    using var files = new AdbTestDirectory();
    string selected = files.Create("Android SDK", "platform-tools", "adb.exe");
    files.Create("app", "adb.exe");
    Require(AdbLocator.Find($"\"{selected}\"", files.Locations) == selected);
    Require(AdbLocator.Find(Path.GetDirectoryName(selected), files.Locations) == selected);
    string relative = files.Create("app", "my-tools", "adb.exe");
    Require(AdbLocator.Find(Path.Combine("my-tools", "adb.exe"), files.Locations) == relative);
    return Task.CompletedTask;
});
await Check("SDK, Android Studio, and PATH ADB fallback order", () =>
{
    using var files = new AdbTestDirectory();
    string sdkHome = files.Create("home-sdk", "platform-tools", "adb.exe");
    string sdkRoot = files.Create("root-sdk", "platform-tools", "adb.exe");
    string studio = files.Create("local", "Android", "Sdk", "platform-tools", "adb.exe");
    string onPath = files.Create("path-tools", "adb.exe");
    var locations = files.Locations with
    {
        AndroidHome = Path.GetDirectoryName(Path.GetDirectoryName(sdkHome)),
        AndroidSdkRoot = Path.GetDirectoryName(Path.GetDirectoryName(sdkRoot)),
        SearchPath = $"\"{Path.GetDirectoryName(onPath)}\""
    };
    Require(AdbLocator.Find(null, locations) == sdkHome); File.Delete(sdkHome);
    Require(AdbLocator.Find(null, locations) == sdkRoot); File.Delete(sdkRoot);
    Require(AdbLocator.Find(null, locations) == studio); File.Delete(studio);
    Require(AdbLocator.Find(null, locations) == onPath);
    return Task.CompletedTask;
});
await Check("missing explicit ADB does not silently switch installations", async () =>
{
    using var files = new AdbTestDirectory();
    string fallback = files.Create("app", "platform-tools", "adb.exe");
    string missing = Path.Combine(files.Locations.WorkingDirectory, "missing", "adb.exe");
    Require(AdbLocator.Find(missing, files.Locations) is null);
    await Throws<IOException>(() => { AdbLocator.Resolve(missing, files.Locations); return Task.CompletedTask; });
    File.Delete(fallback);
    Require(AdbLocator.Find(null, files.Locations with { AndroidHome = "\0invalid" }) is null);
    await Throws<IOException>(() => { AdbLocator.Resolve(null, files.Locations); return Task.CompletedTask; });
});
await Check("two-frame window pipelines without unbounded buffering", async () =>
{
    using var window = new FrameWindow(2);
    await window.ReserveAsync(uint.MaxValue, default); await window.ReserveAsync(0, default);
    Task blocked = window.ReserveAsync(1, default);
    Require(!blocked.IsCompleted && !window.Acknowledge(99));
    Require(window.Acknowledge(0)); await blocked.WaitAsync(TimeSpan.FromSeconds(1));
    Require(!window.Acknowledge(0) && window.Acknowledge(uint.MaxValue) && window.Acknowledge(1));
});
await Check("cancelling a full frame window leaves no phantom frame", async () =>
{
    using var window = new FrameWindow(1); await window.ReserveAsync(1, default);
    using var cancel = new CancellationTokenSource(40);
    await Throws<OperationCanceledException>(() => window.ReserveAsync(2, cancel.Token));
    Require(!window.Acknowledge(2) && window.Acknowledge(1));
    await window.ReserveAsync(2, default); Require(window.Acknowledge(2));
});
await Check("v2 H.264 negotiation and full-height portrait geometry", async () =>
{
    FrameSize.Validate(new(2, "token", 1920, 1920, 60, "h264"));
    Require(FrameSize.FitVideo(1080, 1920, 1920, 1920, 1280 * 720) == new FrameSize(720, 1280));
    Require(FrameSize.FitVideo(1080, 1920, 1920, 1920, 1920 * 1080) == new FrameSize(1080, 1920));
    await Throws<InvalidDataException>(() => { FrameSize.Validate(new(1, "token", 1920, 1080, 30, "h264")); return Task.CompletedTask; });
});
await Check("H.264 parameter-set framing and malformed lengths", async () =>
{
    byte[] annex = Convert.FromHexString("000000016742001e0000000168ce06e2");
    Require(H264.ParameterSets(annex, []).SequenceEqual(annex));
    byte[] avcc = Convert.FromHexString("0142001effe100046742001e01000468ce06e2");
    Require(H264.ParameterSets(avcc, []).SequenceEqual(annex));
    Require(H264.AnnexB(Convert.FromHexString("000000046742001e0000000468ce06e2")).SequenceEqual(annex));
    await Throws<InvalidDataException>(() => { H264.AnnexB(Convert.FromHexString("000000ff01")); return Task.CompletedTask; });
    await Throws<InvalidDataException>(() => { H264.ParameterSets(avcc[..^1], []); return Task.CompletedTask; });
    await Throws<InvalidDataException>(() => { H264.ParameterSets([], new byte[] { 0, 0, 1, 0x65, 1 }); return Task.CompletedTask; });
});
await Check("buffered encoder input releases its slot without accepting a phantom ACK", async () =>
{
    using var window = new FrameWindow(1);
    await window.ReserveAsync(1, default);
    window.CancelReservation(1);
    window.CancelReservation(1);
    Require(!window.Acknowledge(1) && window.AcknowledgementMs == 0);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    await window.ReserveAsync(1, timeout.Token);
    Require(window.Acknowledge(1) && window.AcknowledgementMs >= 0);
});
if (args.Contains("--check-installed-adb"))
{
    await Check("installed Windows ADB launches from an unrelated working directory", async () =>
    {
        Require(OperatingSystem.IsWindows());
        using var files = new AdbTestDirectory();
        string executable = AdbLocator.Resolve(null);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = files.Locations.WorkingDirectory,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("version"); // No server restart, USB operation, device command, or tunnel mutation.
        using var process = Process.Start(info) ?? throw new IOException("ADB process did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Require(process.ExitCode == 0 && (await output).Contains("Android Debug Bridge", StringComparison.Ordinal));
        await error;
    });
}
Console.WriteLine($"{passed} protocol, security, and ADB checks passed.");

async Task Check(string name, Func<Task> test) { await test(); passed++; Console.WriteLine($"PASS {name}"); }
static void Require(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], ct);
}

sealed class AdbTestDirectory : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"Dis-adb-test-{Guid.NewGuid():N}");
    public AdbSearchLocations Locations { get; }
    public AdbTestDirectory()
    {
        Locations = new(Path.Combine(root, "app"), Path.Combine(root, "working"), Path.Combine(root, "local"), null, null, null);
        Directory.CreateDirectory(Locations.AppDirectory);
        Directory.CreateDirectory(Locations.WorkingDirectory);
        Directory.CreateDirectory(Locations.LocalAppData);
    }
    public string Create(params string[] parts)
    {
        string path = Path.Combine([root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Path-resolution fixture; never executed.");
        return path;
    }
    public void Dispose() => Directory.Delete(root, true);
}
