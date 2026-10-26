namespace Dis.Core;

/// <summary>A small sliding window removes per-frame round trips without allowing an old-frame backlog.</summary>
public sealed class FrameWindow : IDisposable
{
    private readonly SemaphoreSlim slots;
    private readonly HashSet<uint> pending = [];
    private readonly object gate = new();
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
            if (!pending.Add(sequence)) { slots.Release(); throw new InvalidDataException("Duplicate frame sequence."); }
        }
    }
    public bool Acknowledge(uint sequence)
    {
        lock (gate)
        {
            if (!pending.Remove(sequence)) return false;
            slots.Release(); return true;
        }
    }
    public void Dispose() => slots.Dispose();
}
