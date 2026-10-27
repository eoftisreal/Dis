using System.ComponentModel;
using System.Runtime.InteropServices;
using Dis.Core;

namespace Dis.Host;

internal sealed class InputInjector : IDisposable
{
    private readonly Rectangle target;
    private readonly nint pen = CreateSyntheticPointerDevice(3, 1, 3);
    private bool mouseDown, penDown, penInRange;
    private Point lastPen;
    public bool PenAvailable => pen != 0;

    public InputInjector(Rectangle target) => this.target = target;

    public void Apply(InputEvent e)
    {
        var p = new Point(target.Left + (int)Math.Round(e.X * (target.Width - 1)), target.Top + (int)Math.Round(e.Y * (target.Height - 1)));
        if (e.Kind == PointerKind.Pen && PenAvailable) { InjectPen(e, p); return; }
        if (e.Kind == PointerKind.Pen) throw new IOException("Windows pen injection is unavailable.");
        Rectangle desktop = SystemInformation.VirtualScreen;
        uint flags = 0x8000 | 0x4000 | 0x0001; // absolute, virtual desktop, move
        switch (e.Action)
        {
            case PointerAction.Down:
                if (mouseDown) return;
                mouseDown = true; flags |= 0x0002; break;
            case PointerAction.Up:
            case PointerAction.Cancel:
                if (mouseDown) flags |= 0x0004;
                mouseDown = false; break;
        }
        var input = new NativeInput
        {
            Type = 0,
            Mouse = new MouseInput
            {
                X = (int)Math.Round((p.X - desktop.Left) * 65535.0 / Math.Max(1, desktop.Width - 1)),
                Y = (int)Math.Round((p.Y - desktop.Top) * 65535.0 / Math.Max(1, desktop.Height - 1)), Flags = flags
            }
        };
        if (SendInput(1, [input], Marshal.SizeOf<NativeInput>()) != 1) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private void InjectPen(InputEvent e, Point p)
    {
        uint flags = 0x20000 | 0x2; // UPDATE, INRANGE
        switch (e.Action)
        {
            case PointerAction.Down:
                if (penDown) return;
                flags = 0x10000 | 0x2 | 0x4 | 0x10; penDown = true; penInRange = true; break;
            case PointerAction.Move:
                if (penDown) flags |= 0x4 | 0x10;
                penInRange = true;
                break;
            case PointerAction.Hover:
                if (penDown) return;
                penInRange = true;
                break;
            case PointerAction.Up:
                if (!penDown) return;
                flags = 0x40000 | 0x2;
                penDown = false; penInRange = true;
                break;
            case PointerAction.Cancel:
                if (!penDown && !penInRange) return;
                flags = penDown ? 0x40000u | 0x8000u : 0x20000u;
                penDown = false; penInRange = false;
                break;
        }
        lastPen = p;
        var pointer = new PointerTypeInfo
        {
            Type = 3,
            Pen = new PointerPenInfo
            {
                Pointer = new PointerInfo { Type = 3, Id = 1, Flags = flags, Pixel = p, RawPixel = p },
                Flags = (uint)(((e.Buttons & 1) != 0 ? 1 : 0) | ((e.Buttons & 2) != 0 ? 6 : 0)),
                Mask = 1 | 4 | 8, Pressure = penDown ? (uint)Math.Round(e.Pressure * 1024) : 0,
                TiltX = (int)Math.Round(e.TiltX), TiltY = (int)Math.Round(e.TiltY)
            }
        };
        if (!InjectSyntheticPointerInput(pen, [pointer], 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (mouseDown)
        {
            var release = new NativeInput { Type = 0, Mouse = new MouseInput { Flags = 0x0004 } };
            SendInput(1, [release], Marshal.SizeOf<NativeInput>());
            mouseDown = false;
        }
        if (pen != 0)
        {
            try { if (penDown || penInRange) InjectPen(new(PointerKind.Pen, PointerAction.Cancel, 0, 0, 0, 0, 0, 0), lastPen); }
            catch (Win32Exception) { }
            DestroySyntheticPointerDevice(pen);
        }
    }

    // Win64 ABI: INPUT=40, POINTER_INFO=96, POINTER_PEN_INFO=120, POINTER_TYPE_INFO=152.
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct NativeInput { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct PointerInfo
    {
        public uint Type, Id, FrameId, Flags;
        public nint Source, Window;
        public Point Pixel, Himetric, RawPixel, RawHimetric;
        public uint Time, History;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public int ButtonChange;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PointerPenInfo { public PointerInfo Pointer; public uint Flags, Mask, Pressure, Rotation; public int TiltX, TiltY; }
    [StructLayout(LayoutKind.Explicit, Size = 152)] private struct PointerTypeInfo { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public PointerPenInfo Pen; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreateSyntheticPointerDevice(uint type, uint maxCount, uint mode);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InjectSyntheticPointerInput(nint device, PointerTypeInfo[] info, uint count);
    [DllImport("user32.dll")] private static extern void DestroySyntheticPointerDevice(nint device);
}
