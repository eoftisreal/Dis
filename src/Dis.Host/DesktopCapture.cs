using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Dis.Core;

namespace Dis.Host;

internal sealed class DesktopCapture : IDisposable
{
    private readonly Rectangle source;
    private readonly Bitmap desktop;
    private readonly Bitmap scaled;
    private readonly Graphics captureGraphics;
    private readonly Graphics scaleGraphics;
    private readonly ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().Single(c => c.FormatID == ImageFormat.Jpeg.Guid);
    private readonly EncoderParameters parameters = new(1);
    public FrameSize Size { get; }

    public DesktopCapture(Rectangle source, FrameSize size, int quality)
    {
        this.source = source;
        Size = size;
        // GDI BitBlt does not supply alpha. RGB prevents a zero alpha byte from making scaling transparent/black.
        desktop = new(source.Width, source.Height, PixelFormat.Format32bppRgb);
        scaled = new(size.Width, size.Height, PixelFormat.Format32bppRgb);
        captureGraphics = Graphics.FromImage(desktop);
        scaleGraphics = Graphics.FromImage(scaled);
        scaleGraphics.CompositingMode = CompositingMode.SourceCopy;
        scaleGraphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
    }

    public byte[] Capture()
    {
        CaptureScaled();
        using var buffer = new MemoryStream();
        scaled.Save(buffer, jpeg, parameters);
        if (buffer.Length > Wire.MaxFrameBytes - 12) throw new InvalidDataException("Encoded desktop exceeds frame limit.");
        return buffer.ToArray();
    }

    public async Task<EncodedFrame?> CaptureH264Async(NativeEncoder encoder, long timestamp)
    {
        CaptureScaled();
        var bits = scaled.LockBits(new Rectangle(0, 0, Size.Width, Size.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try { return await encoder.EncodeAsync(bits.Scan0, bits.Stride, timestamp); }
        finally { scaled.UnlockBits(bits); }
    }

    private void CaptureScaled()
    {
        captureGraphics.CopyFromScreen(source.Location, Point.Empty, source.Size, CopyPixelOperation.SourceCopy);
        DrawCursor();
        scaleGraphics.DrawImage(desktop, new Rectangle(0, 0, Size.Width, Size.Height));
    }

    private void DrawCursor()
    {
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref cursor) || (cursor.Flags & 1) == 0 || !source.Contains(cursor.Position)) return;
        if (!GetIconInfo(cursor.Cursor, out var icon)) return;
        try
        {
            nint dc = captureGraphics.GetHdc();
            try { DrawIconEx(dc, cursor.Position.X - source.X - (int)icon.HotspotX,
                cursor.Position.Y - source.Y - (int)icon.HotspotY, cursor.Cursor, 0, 0, 0, 0, 3); }
            finally { captureGraphics.ReleaseHdc(dc); }
        }
        finally
        {
            if (icon.Mask != 0) DeleteObject(icon.Mask);
            if (icon.Color != 0) DeleteObject(icon.Color);
        }
    }

    public void Dispose()
    {
        parameters.Dispose();
        scaleGraphics.Dispose();
        captureGraphics.Dispose();
        scaled.Dispose();
        desktop.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size; public uint Flags; public nint Cursor; public Point Position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public int IsIcon; public uint HotspotX, HotspotY; public nint Mask, Color; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIconInfo(nint icon, out IconInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint obj);
}
