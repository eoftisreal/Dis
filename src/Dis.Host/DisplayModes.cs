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

    public static string EnsureExtended()
    {
        Adapter adapter = Enumerate().FirstOrDefault(a => a.Virtual)
            ?? throw new IOException("Install the second-display driver first using Set up second display.");
        if (adapter.Active) return adapter.Name;
        Mode mode;
        try { mode = Read(adapter.Name, -2); }
        catch (IOException) { mode = Read(adapter.Name, 0); mode.Width = 1920; mode.Height = 1080; mode.Frequency = 60; }
        if (mode.Width == 0 || mode.Height == 0) { mode.Width = 1920; mode.Height = 1080; mode.Bits = 32; mode.Frequency = 60; }
        mode.X = Screen.AllScreens.Max(s => s.Bounds.Right); mode.Y = 0;
        mode.Fields = 0x20 | 0x80000 | 0x100000 | 0x400000; // position, width, height, frequency
        Apply(adapter.Name, ref mode);
        return adapter.Name;
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
}
