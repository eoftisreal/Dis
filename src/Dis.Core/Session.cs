using System.Security.Cryptography;

namespace Dis.Core;

public sealed record ClientHello(int Version, string Token, int MaxWidth, int MaxHeight, int Fps, string Codec);
public sealed record StreamConfig(int Version, string Codec, int Width, int Height, int Fps, bool Input, bool Pen);

public readonly record struct FrameSize(int Width, int Height)
{
    public static FrameSize Fit(int width, int height, int maxWidth, int maxHeight)
    {
        if (width <= 0 || height <= 0 || maxWidth <= 0 || maxHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        double scale = Math.Min(1.0, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        return new(Math.Max(1, (int)Math.Floor(width * scale)), Math.Max(1, (int)Math.Floor(height * scale)));
    }

    public static void Validate(ClientHello hello)
    {
        if (hello.Version != 1 || hello.Codec != "jpeg" || hello.MaxWidth is < 320 or > 4096 ||
            hello.MaxHeight is < 240 or > 4096 || hello.Fps is < 1 or > 60)
            throw new InvalidDataException("Unsupported stream capabilities.");
    }
}

public sealed class PairingToken
{
    private readonly byte[] secret = RandomNumberGenerator.GetBytes(32);
    public string Value => Convert.ToHexString(secret).ToLowerInvariant();

    public bool Matches(string? token)
    {
        if (token is null || token.Length != 64) return false;
        try { return CryptographicOperations.FixedTimeEquals(secret, Convert.FromHexString(token)); }
        catch (FormatException) { return false; }
    }
}

/// <summary>Only one complete frame may be unacknowledged; never queue old desktop frames.</summary>
public sealed class AckWindow
{
    private readonly object gate = new();
    private uint expected;
    private TaskCompletionSource? pending;

    public Task Expect(uint sequence)
    {
        lock (gate)
        {
            if (pending is not null) throw new InvalidOperationException("Frame already in flight.");
            expected = sequence;
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return pending.Task;
        }
    }

    public bool Acknowledge(uint sequence)
    {
        lock (gate)
        {
            if (pending is null || expected != sequence) return false;
            var done = pending;
            pending = null;
            done.TrySetResult();
            return true;
        }
    }
}
