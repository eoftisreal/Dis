using System.Runtime.InteropServices;

namespace Dis.Host;

internal sealed class StreamTimer : IDisposable
{
    private readonly bool active = timeBeginPeriod(1) == 0;
    public void Dispose() { if (active) timeEndPeriod(1); }
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);
}
