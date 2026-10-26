using System.Text.Json;
using Dis.Core;

namespace Dis.Host;

internal static class DisplaySmoke
{
    public static async Task RunAsync(string output)
    {
        await DriverSetup.InstallAsync(_ => { });
        string name = "";
        for (int attempt = 0; attempt < 100; attempt++)
        {
            try { name = DisplayModes.EnsureExtended(); break; }
            catch (IOException) when (attempt < 99) { await Task.Delay(100); }
        }
        var landscape = DisplayModes.Bounds(name);
        DisplayModes.Rotate(name, 1);
        await Task.Delay(350);
        var portrait = DisplayModes.Bounds(name);
        DisplayModes.Rotate(name, 0);
        if (portrait.Height <= portrait.Width) throw new IOException("Virtual monitor did not change to portrait.");
        await Task.Run(() => CaptureProbe(DisplayModes.Bounds(name)));
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            virtualDisplay = name, landscape.Width, landscape.Height,
            portraitWidth = portrait.Width, portraitHeight = portrait.Height,
            adapters = DisplayModes.Enumerate()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CaptureProbe(Rectangle bounds)
    {
        using var encoder = NativeEncoder.CreateAsync(320, 180, 30, 2_000_000, false).GetAwaiter().GetResult();
        using var window = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
            Bounds = bounds, BackColor = Color.Orange, TopMost = true, ShowInTaskbar = false };
        window.Show(); window.Bounds = bounds; window.BringToFront(); window.Refresh(); Application.DoEvents();
        using var capture = new DesktopCapture(bounds, new FrameSize(320, 180), 85);
        bool painted = false; Color pixel = Color.Black;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            Application.DoEvents(); Thread.Sleep(100);
            using var image = new Bitmap(new MemoryStream(capture.Capture()));
            pixel = image.GetPixel(160, 90);
            if (pixel.R >= 180 && pixel.G >= 80 && pixel.B <= 80) { painted = true; break; }
        }
        if (!painted)
        {
            File.WriteAllBytes("artifacts/capture-probe.jpg", capture.Capture());
            throw new IOException($"Virtual capture {capture.Backend} returned ({pixel.R},{pixel.G},{pixel.B}); source={bounds}; window={window.Bounds}; session={System.Diagnostics.Process.GetCurrentProcess().SessionId}.");
        }
        using var output = File.Create("artifacts/capture-smoke.h264");
        int frames = 0;
        for (int attempt = 0; attempt < 100 && frames < 24; attempt++)
        {
            var frame = capture.CaptureH264Async(encoder, 1_000_000L + attempt * 33333L).GetAwaiter().GetResult();
            if (frame is null) continue;
            if (frames == 0) output.Write(H264.ParameterSets(frame.Configuration, frame.Data));
            output.Write(frame.Data); frames++;
        }
        if (frames != 24) throw new IOException("Virtual display capture did not produce 24 H.264 pictures.");
    }
}
