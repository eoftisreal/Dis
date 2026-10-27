namespace Dis.Core;

/// <summary>One connection attempt per attachment, with explicit disconnect suppression.</summary>
public sealed class UsbAutoconnect
{
    private string? observed, attempted;
    private bool suppressed;
    public void Observe(string? serial)
    {
        if (observed == serial) return;
        observed = serial; attempted = null; suppressed = false;
    }
    public bool TryBegin(bool enabled, bool active)
    {
        if (!enabled || active || suppressed || observed is null || attempted == observed) return false;
        attempted = observed; return true;
    }
    public void PreferenceDisabled() { attempted = null; }
    public bool CanContinue(string serial) => observed == serial && !suppressed;
    public void Suppress() { suppressed = true; }
    public void Reset() { attempted = null; suppressed = false; }
}
