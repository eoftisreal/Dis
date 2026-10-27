using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Dis.Core;

namespace Dis.Host;

internal sealed record ServerOptions(IPAddress Address, int Port, string DeviceName, Rectangle Bounds,
    int MaxWidth, int MaxHeight, int Fps, int Quality, bool Input, int MaxPixels = 1280 * 720);
internal sealed record StreamStatistics(string Encoder, int Width, int Height, double Fps, double AckMs, double Mbps,
    double CaptureEncodeMs, double FrameWaitMs, double WriteMs, string CaptureBackend);

internal sealed class DisplayServer : IAsyncDisposable
{
    private readonly ServerOptions options;
    private readonly CancellationTokenSource stop = new();
    private readonly TcpListener listener;
    private readonly PairingSession pairing;
    private readonly bool ownsPairing;
    private readonly SemaphoreSlim handshakeSlots = new(4);
    private readonly List<Task> sessions = [];
    private readonly Task acceptTask;
    private int active;
    private long lastFrameAcknowledged;
    public long LastFrameAcknowledged => Interlocked.Read(ref lastFrameAcknowledged);
    public bool HasReceiver => Volatile.Read(ref active) != 0;
    public event Action<string>? Status;
    public event Action<StreamStatistics>? Statistics;
    public string Fingerprint => pairing.Identity.Fingerprint;

    public DisplayServer(ServerOptions options, PairingSession? session = null)
    {
        this.options = options;
        pairing = session ?? new(); ownsPairing = session is null;
        listener = new(options.Address, options.Port);
        try { listener.Start(4); }
        catch { if (ownsPairing) pairing.Dispose(); throw; }
        acceptTask = AcceptAsync();
    }

    public string ConnectionUri(string host) => $"dis://{host}:{options.Port}?fp={Fingerprint}&token={pairing.Token.Value}";

    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                if (!await handshakeSlots.WaitAsync(0, stop.Token)) { client.Dispose(); continue; }
                sessions.RemoveAll(t => t.IsCompleted);
                sessions.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        bool acquired = false;
        using (client)
        using (var life = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        using (life.Token.Register(client.Dispose))
        {
            try
            {
                client.NoDelay = true;
                client.SendBufferSize = 64 * 1024;
                using var tls = new SslStream(client.GetStream(), false);
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(life.Token))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = pairing.Identity.Certificate,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        ClientCertificateRequired = false,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    }, deadline.Token);
                    Packet helloPacket = await Wire.ReadAsync(tls, deadline.Token);
                    if (helloPacket.Type != MessageType.Hello || helloPacket.Sequence != 0)
                        throw new InvalidDataException("Expected client hello.");
                    var hello = Wire.ParseJson<ClientHello>(helloPacket);
                    if (!pairing.Token.Matches(hello.Token)) throw new AuthenticationException("Pairing rejected.");
                    FrameSize.Validate(hello);
                    if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
                    {
                        await Wire.WriteAsync(tls, Wire.Json(MessageType.Error, new { message = "A device is already connected." }), deadline.Token);
                        return;
                    }
                    acquired = true;
                    await StreamAsync(tls, client, hello, life);
                }
            }
            catch (Exception e) when (e is IOException or SocketException or AuthenticationException or
                OperationCanceledException or ObjectDisposedException or InvalidDataException or
                System.Text.Json.JsonException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException or TimeoutException)
            {
                if (acquired && !stop.IsCancellationRequested) Status?.Invoke($"Disconnected: {e.Message}");
            }
            finally
            {
                if (acquired) Interlocked.Exchange(ref active, 0);
                handshakeSlots.Release();
            }
        }
    }

    private async Task StreamAsync(SslStream tls, TcpClient client, ClientHello hello, CancellationTokenSource life)
    {
        using var timer = new StreamTimer();
        var size = FrameSize.FitVideo(options.Bounds.Width, options.Bounds.Height,
            Math.Min(options.MaxWidth, hello.MaxWidth), Math.Min(options.MaxHeight, hello.MaxHeight), options.MaxPixels);
        int fps = Math.Min(options.Fps, hello.Fps);
        using var capture = new DesktopCapture(options.Bounds, size, options.Quality);
        using var input = new InputInjector(options.Bounds);
        using var writer = new SemaphoreSlim(1);
        using var ack = new FrameWindow(hello.Version >= 2 ? 2 : 1);
        NativeEncoder? encoder = null;
        EncodedFrame? first = null;
        string codec = "jpeg", encoderName = "JPEG fallback";
        byte[]? configuration = null;
        if (hello.Version >= 2 && hello.Codec == "h264")
        {
            try
            {
                encoder = await NativeEncoder.CreateAsync(size.Width, size.Height, fps, Math.Clamp(size.Width * size.Height * 5, 2_000_000, 12_000_000), true);
                var started = Stopwatch.GetTimestamp();
                while (first is null && Stopwatch.GetElapsedTime(started).TotalSeconds < 2)
                {
                    life.Token.ThrowIfCancellationRequested();
                    first = await capture.CaptureH264Async(encoder, TimestampUs());
                    if (first is null) await Task.Delay(4, life.Token);
                }
                if (first is null) throw new IOException("Encoder produced no frame during startup.");
                configuration = H264.ParameterSets(first.Configuration, first.Data);
                codec = "h264"; encoderName = encoder.Hardware ? "H.264 hardware" : "H.264 software";
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or
                System.Runtime.InteropServices.COMException or IOException or TimeoutException)
            {
                encoder?.Dispose(); encoder = null; first = null;
                Status?.Invoke($"H.264 unavailable ({e.Message}); using JPEG fallback.");
            }
        }
        using var encoderLifetime = encoder;
        if (codec == "jpeg") fps = Math.Min(fps, 30);
        await Wire.WriteAsync(tls, Wire.Json(MessageType.Config,
            new StreamConfig(hello.Version, codec, size.Width, size.Height, fps, options.Input, options.Input && input.PenAvailable, encoderName,
                options.Input && input.TouchAvailable, options.Input)), life.Token);
        if (configuration is not null) await Wire.WriteAsync(tls, new(MessageType.CodecConfig, 0, configuration), life.Token);
        Status?.Invoke("Waiting for Android to decode the first frame…");
        var receive = ReceiveAsync();
        var send = SendAsync();
        try
        {
            Task done = await Task.WhenAny(receive, send);
            await done;
        }
        finally
        {
            life.Cancel();
            client.Dispose();
            try { await Task.WhenAll(receive, send); }
            catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or SocketException or TimeoutException or InvalidDataException) { }
        }

        async Task WriteAsync(Packet packet, CancellationToken ct)
        {
            await writer.WaitAsync(ct);
            try { await Wire.WriteAsync(tls, packet, ct); }
            finally { writer.Release(); }
        }

        async Task ReceiveAsync()
        {
            bool confirmed = false;
            long rateStart = Stopwatch.GetTimestamp();
            int count = 0;
            while (true)
            {
                var packet = await Wire.ReadAsync(tls, life.Token);
                if (Stopwatch.GetElapsedTime(rateStart).TotalSeconds >= 1) { count = 0; rateStart = Stopwatch.GetTimestamp(); }
                if (++count > 2000) throw new InvalidDataException("Input rate exceeded.");
                switch (packet.Type)
                {
                    case MessageType.Ack:
                        if (!ack.Acknowledge(packet.Sequence)) throw new InvalidDataException("Unexpected frame acknowledgement.");
                        Interlocked.Exchange(ref lastFrameAcknowledged, Stopwatch.GetTimestamp());
                        if (!confirmed) { confirmed = true; Status?.Invoke($"Connected · {size.Width} × {size.Height} · {encoderName}"); }
                        break;
                    case MessageType.Input:
                        var pointer = InputEvent.Parse(packet.Payload);
                        if (options.Input) input.Apply(pointer);
                        break;
                    case MessageType.Ping:
                        await WriteAsync(new(MessageType.Pong, packet.Sequence, packet.Payload), life.Token);
                        break;
                    default: throw new InvalidDataException("Unexpected client message.");
                }
            }
        }

        async Task SendAsync()
        {
            uint sequence = 0;
            long geometryChecked = 0;
            long stats = Stopwatch.GetTimestamp();
            int sent = 0;
            long bytes = 0;
            double captureMs = 0, frameWaitMs = 0, writeMs = 0;
            while (true)
            {
                life.Token.ThrowIfCancellationRequested();
                long start = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(geometryChecked).TotalSeconds >= 1)
                {
                    if (!Screen.AllScreens.Any(s => s.DeviceName == options.DeviceName && s.Bounds == options.Bounds))
                        throw new IOException("Display layout changed. Select the display and reconnect.");
                    geometryChecked = start;
                }
                long timestamp = TimestampUs();
                // Wait before capture/encode, so backpressure cannot age a freshly
                // captured desktop while it waits for an older frame to be consumed.
                using var frameDeadline = CancellationTokenSource.CreateLinkedTokenSource(life.Token);
                frameDeadline.CancelAfter(TimeSpan.FromSeconds(8));
                uint nextSequence = sequence + 1;
                long stage = Stopwatch.GetTimestamp();
                await ack.ReserveAsync(nextSequence, frameDeadline.Token);
                frameWaitMs += Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
                stage = Stopwatch.GetTimestamp();
                byte[] data;
                if (encoder is not null)
                {
                    EncodedFrame? frame = first ?? await capture.CaptureH264Async(encoder, timestamp);
                    captureMs += Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
                    first = null;
                    if (frame is null)
                    {
                        ack.CancelReservation(nextSequence);
                        var wait = TimeSpan.FromSeconds(1.0 / fps) - Stopwatch.GetElapsedTime(start);
                        await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(2), life.Token); continue;
                    }
                    data = frame.Data; timestamp = frame.TimestampUs;
                }
                else { data = capture.Capture(); captureMs += Stopwatch.GetElapsedTime(stage).TotalMilliseconds; }
                byte[] payload = new byte[data.Length + 12];
                BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)size.Width);
                BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), (ushort)size.Height);
                BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(4), timestamp);
                data.CopyTo(payload, 12);
                sequence = nextSequence;
                stage = Stopwatch.GetTimestamp();
                await WriteAsync(new(MessageType.Frame, sequence, payload), frameDeadline.Token);
                writeMs += Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
                sent++;
                bytes += payload.Length;
                double seconds = Stopwatch.GetElapsedTime(stats).TotalSeconds;
                if (seconds >= 2)
                {
                    Statistics?.Invoke(new(encoderName, size.Width, size.Height, sent / seconds, ack.AcknowledgementMs, bytes * 8 / seconds / 1_000_000,
                        captureMs / sent, frameWaitMs / sent, writeMs / sent, capture.Backend));
                    Status?.Invoke($"{encoderName} · {sent / seconds:F1} fps · {ack.AcknowledgementMs:F0} ms frame ACK · {bytes * 8 / seconds / 1_000_000:F1} Mbps · {size.Width} × {size.Height}");
                    stats = Stopwatch.GetTimestamp(); sent = 0; bytes = 0;
                    captureMs = frameWaitMs = writeMs = 0;
                }
                var delay = TimeSpan.FromSeconds(1.0 / fps) - Stopwatch.GetElapsedTime(start);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, life.Token);
            }
        }
    }

    private static long TimestampUs() => (long)(Stopwatch.GetTimestamp() * (1_000_000.0 / Stopwatch.Frequency));

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        listener.Stop();
        try { await acceptTask; }
        finally
        {
            try { await Task.WhenAll(sessions); }
            finally { handshakeSlots.Dispose(); if (ownsPairing) pairing.Dispose(); stop.Dispose(); }
        }
    }
}
