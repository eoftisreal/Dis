using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Dis.Core;

int passed = 0;
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
    await Throws<InvalidDataException>(() => { FrameSize.Validate(new(2, "token", 1920, 1080, 30, "jpeg")); return Task.CompletedTask; });
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
await Check("TLS loopback transports real framed messages with a pinned certificate", async () =>
{
    using var rsa = RSA.Create(2048);
    var req = new CertificateRequest("CN=Dis test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
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
Console.WriteLine($"{passed} protocol and security checks passed.");

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
