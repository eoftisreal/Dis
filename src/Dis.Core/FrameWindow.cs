using System.Diagnostics;

namespace Dis.Core;

/// <summary>A small sliding window removes per-frame round trips without allowing an old-frame backlog.</summary>
public sealed class FrameWindow : IDisposable
{
    private readonly SemaphoreSlim slots;
    private readonly Dictionary<uint, long> pending = [];
    private readonly object gate = new();
    private double acknowledgementMs;
    public double AcknowledgementMs { get { lock (gate) return acknowledgementMs; } }
    public FrameWindow(int capacity)
    {
        if (capacity is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(capacity));
        slots = new(capacity, capacity);
    }
    public async Task ReserveAsync(uint sequence, CancellationToken ct)
    {
        await slots.WaitAsync(ct);
        lock (gate)
        {
            if (!pending.TryAdd(sequence, Stopwatch.GetTimestamp())) { slots.Release(); throw new InvalidDataException("Duplicate frame sequence."); }
        }
    }
    public bool Acknowledge(uint sequence)
    {
        lock (gate)
        {
            if (!pending.Remove(sequence, out long sent)) return false;
            double elapsed = Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
            acknowledgementMs = acknowledgementMs == 0 ? elapsed : acknowledgementMs * 0.8 + elapsed * 0.2;
            slots.Release(); return true;
        }
    }
    // An encoder can buffer input without producing an access unit. Release the
    // unused slot without claiming a remote acknowledgement or consuming sequence.
    public void CancelReservation(uint sequence)
    {
        lock (gate) { if (pending.Remove(sequence)) slots.Release(); }
    }
    public void Dispose() => slots.Dispose();
}
