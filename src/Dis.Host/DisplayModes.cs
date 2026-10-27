using System.Runtime.InteropServices;

namespace Dis.Host;

internal static class DisplayModes
{
    internal sealed record Adapter(string Name, string Description, bool Active, bool Virtual);
    public static IReadOnlyList<Adapter> Enumerate()
    {
        var result = new List<Adapter>();
        for (uint i = 0; ; i++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.Flags & 8) != 0) continue;
            bool virtualDisplay = device.Description.Contains("Virtual Display Driver", StringComparison.OrdinalIgnoreCase)
                || device.Description.Contains("Dis Virtual Display", StringComparison.OrdinalIgnoreCase)
                || device.Description.Contains("VirtualDisplayDriver", StringComparison.OrdinalIgnoreCase)
                || device.Description.Contains("MttVDD", StringComparison.OrdinalIgnoreCase);
            result.Add(new(device.Name, device.Description, (device.Flags & 1) != 0, virtualDisplay));
        }
        return result;
    }

    public static async Task<string> EnsureExtendedAsync()
    {
        Adapter adapter = Enumerate().FirstOrDefault(a => a.Virtual)
            ?? throw new IOException("Install the second-display driver first using Set up second display.");
        if (adapter.Active) return adapter.Name;
        // Use physical display coordinates, not potentially DPI-scaled Screen bounds.
        // The new monitor must touch an existing monitor, including layouts above/below primary.
        var edge = Enumerate().Where(a => a.Active).Select(a => Bounds(a.Name))
            .OrderByDescending(b => b.Right).FirstOrDefault();
        int result = -2;
        foreach (var supported in SupportedModes(adapter.Name))
        {
            var mode = supported;
            mode.X = edge.Right; mode.Y = edge.Top;
            mode.Fields = 0x20 | 0x80 | 0x40000 | 0x80000 | 0x100000 | 0x200000 | 0x400000;
            result = ChangeDisplaySettingsEx(adapter.Name, ref mode, 0, 1, 0);
            if (result == 0)
            {
                if (await WaitForActiveAsync(adapter.Name)) return adapter.Name;
                break;
            }
            if (result == 1) throw new IOException("Restart Windows to finish activating the second display.");
        }

        // Some indirect display drivers reject legacy GDI attachment even for advertised
        // modes. Let Windows choose the extended topology and its source/target modes.
        // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setdisplayconfig
        int topologyResult = SetDisplayConfig(0, 0, 0, 0, 0x80 | 0x4); // SDC_APPLY | SDC_TOPOLOGY_EXTEND
        if (topologyResult == 0)
        {
            // Topology changes can renumber GDI display names.
            for (int attempt = 0; attempt < 50; attempt++)
            {
                var active = Enumerate().FirstOrDefault(a => a.Virtual && a.Active);
                if (active is not null) return active.Name;
                await Task.Delay(100);
            }
        }
        throw new IOException($"Windows could not activate the second display (display mode code {result}, extend code {topologyResult}). Open Windows display settings and choose Extend these displays; if that also fails, restart Windows and run second-display setup again.");
    }

    private static IEnumerable<Mode> SupportedModes(string name)
    {
        var modes = new List<Mode>();
        for (int index = 0; ; index++)
        {
            var mode = new Mode { Size = (ushort)Marshal.SizeOf<Mode>() };
            if (!EnumDisplaySettingsEx(name, index, ref mode, 0)) break;
            if (mode.Width > 0 && mode.Height > 0 && mode.Bits == 32) modes.Add(mode);
        }
        // Prefer a modest 60 Hz desktop, then try the other driver-advertised modes.
        // Never invent a resolution/refresh rate or reuse a detached registry mode.
        return modes.OrderByDescending(m => m.Width == 1920 && m.Height == 1080 && m.Frequency == 60)
            .ThenBy(m => (ulong)m.Width * m.Height).ThenBy(m => m.Frequency)
            .DistinctBy(m => (m.Width, m.Height, m.Frequency, m.Orientation, m.Flags));
    }

    private static async Task<bool> WaitForActiveAsync(string name)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            if (Enumerate().Any(a => a.Name == name && a.Active)) return true;
            await Task.Delay(100);
        }
        return false;
    }

    public static void Disconnect(string deviceName)
    {
        var adapter = Enumerate().FirstOrDefault(a => a.Name == deviceName && a.Virtual);
        if (adapter is null || !adapter.Active) return;
        if (!Enumerate().Any(a => a.Active && a.Name != deviceName))
            throw new IOException("The virtual display is the only active monitor. Activate your physical monitor before disconnecting it.");
        // Detach only this virtual monitor; do not change other monitors' topology.
        var mode = new Mode { Size = (ushort)Marshal.SizeOf<Mode>(), Fields = 0x20 | 0x80000 | 0x100000 };
        Apply(deviceName, ref mode);
    }

    public static void Rotate(string deviceName, int orientation)
    {
        if (orientation is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(orientation));
        var mode = Read(deviceName, -1);
        if ((mode.Orientation & 1) != (orientation & 1)) (mode.Width, mode.Height) = (mode.Height, mode.Width);
        mode.Orientation = orientation;
        mode.Fields = 0x80 | 0x80000 | 0x100000;
        Apply(deviceName, ref mode);
    }
    public static void SetDesktopSize(string deviceName, int width, int height, int orientation)
    {
        if (orientation is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(orientation));
        var candidates = SupportedModes(deviceName).Select(m =>
        {
            if ((m.Orientation & 1) != (orientation & 1)) (m.Width, m.Height) = (m.Height, m.Width);
            m.Orientation = orientation;
            return m;
        }).Where(m => m.Frequency == 60).ToArray();
        if (candidates.Length == 0) throw new IOException("The virtual driver does not advertise a supported 60 Hz desktop mode.");
        var mode = candidates.OrderBy(m => Math.Abs(Math.Log((double)m.Width / m.Height / ((double)width / height))) * 8
            + Math.Abs(Math.Log((double)m.Width / width))).First();
        mode.Fields = 0x80 | 0x40000 | 0x80000 | 0x100000 | 0x400000;
        Apply(deviceName, ref mode);
    }
    public static Rectangle Bounds(string deviceName)
    {
        var mode = Read(deviceName, -1);
        return new(mode.X, mode.Y, (int)mode.Width, (int)mode.Height);
    }

    private static Mode Read(string deviceName, int index)
    {
        var mode = new Mode { Size = (ushort)Marshal.SizeOf<Mode>() };
        if (!EnumDisplaySettingsEx(deviceName, index, ref mode, 0)) throw new IOException("Windows could not read that monitor's display mode.");
        return mode;
    }
    private static void Apply(string deviceName, ref Mode mode)
    {
        int result = ChangeDisplaySettingsEx(deviceName, ref mode, 0, 1, 0);
        if (result != 0) throw new IOException($"Windows could not apply this display mode (code {result}). Choose a supported mode in Display settings.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Mode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        public ushort SpecVersion, DriverVersion, Size, Extra;
        public uint Fields;
        public int X, Y, Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Form;
        public ushort LogPixels;
        public uint Bits, Width, Height, Flags, Frequency, IcmMethod, IcmIntent, Media, Dither, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevices(string? name, uint index, ref DisplayDevice device, uint flags);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettingsEx(string name, int index, ref Mode mode, uint flags);
    [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsEx(string name, ref Mode mode, nint window, uint flags, nint parameter);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int SetDisplayConfig(uint pathCount, nint paths, uint modeCount, nint modes, uint flags);
}
