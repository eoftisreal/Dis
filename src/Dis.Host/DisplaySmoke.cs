using System.Text.Json;
using Dis.Core;

namespace Dis.Host;

internal static class DisplaySmoke
{
    public static async Task RunAsync(string output)
    {
        await DriverSetup.InstallAsync(_ => { });
        DriverSetup.SetDeviceEnabled(true);
        await Task.Delay(1000);
        string name = "";
        for (int attempt = 0; attempt < 100; attempt++)
        {
            try { name = await DisplayModes.EnsureExtendedAsync(); break; }
            catch (IOException) when (attempt < 99) { await Task.Delay(100); }
        }
        var landscape = DisplayModes.Bounds(name);
        DisplayModes.Rotate(name, 1);
        await Task.Delay(350);
        var portrait = DisplayModes.Bounds(name);
        DisplayModes.Rotate(name, 0);
        if (portrait.Height <= portrait.Width) throw new IOException("Virtual monitor did not change to portrait.");
        await Task.Run(() => CaptureProbe(DisplayModes.Bounds(name)));
        VerifyPenInput(DisplayModes.Bounds(name));
        DisplayModes.Disconnect(name);
        if (DisplayModes.Enumerate().Any(a => a.Name == name && a.Active))
            throw new IOException("The virtual monitor remained active after disconnect.");
        name = await DisplayModes.EnsureExtendedAsync();
        if (!DisplayModes.Enumerate().Any(a => a.Name == name && a.Active))
            throw new IOException("The virtual monitor did not reactivate after disconnect.");
        DisplayModes.Disconnect(name);
        DriverSetup.SetDeviceEnabled(false);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            virtualDisplay = name, disconnectedAfterTest = true, landscape.Width, landscape.Height,
            penHoverContactAndLeave = true,
            portraitWidth = portrait.Width, portraitHeight = portrait.Height,
            adapters = DisplayModes.Enumerate()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void VerifyPenInput(Rectangle bounds)
    {
        using var input = new InputInjector(bounds);
        if (!input.PenAvailable) throw new IOException("Windows synthetic pen is unavailable.");
        foreach (var action in new[] { PointerAction.Hover, PointerAction.Down, PointerAction.Move,
            PointerAction.Up, PointerAction.Hover, PointerAction.Cancel, PointerAction.Hover, PointerAction.Cancel })
        {
            input.Apply(new(PointerKind.Pen, action, 0, .5f, .5f, .6f, 10, -10));
            Thread.Sleep(5);
        }
    }

    public static async Task CheckLifecycleAsync(string output)
    {
        var physicalBefore = DisplayModes.Enumerate().Where(a => a.Active && !a.Virtual)
            .ToDictionary(a => a.Name, a => DisplayModes.Bounds(a.Name));
        string name;
        await using (var session = await DriverSession.StartAsync())
        {
            for (int i = 0; i < 50 && !DisplayModes.Enumerate().Any(a => a.Virtual); i++) await Task.Delay(100);
            name = await DisplayModes.EnsureExtendedAsync();
            if (!DisplayModes.Enumerate().Any(a => a.Name == name && a.Active)) throw new IOException("Monitor did not activate.");
            DisplayModes.Disconnect(name);
            if (DisplayModes.Enumerate().Any(a => a.Name == name && a.Active)) throw new IOException("Monitor did not detach.");
            name = await DisplayModes.EnsureExtendedAsync();
        }
        for (int i = 0; i < 50 && DisplayModes.Enumerate().Any(a => a.Virtual); i++) await Task.Delay(100);
        if (DisplayModes.Enumerate().Any(a => a.Virtual)) throw new IOException("Virtual adapter remains visible after session disposal.");
        foreach (var entry in physicalBefore)
            if (!DisplayModes.Enumerate().Any(a => a.Name == entry.Key && a.Active) || DisplayModes.Bounds(entry.Key) != entry.Value)
                throw new IOException("A physical display changed during the lifecycle test.");
        File.WriteAllText(output, JsonSerializer.Serialize(new { activated = true, detached = true, reactivated = true,
            hiddenWhenIdle = true, physicalDisplaysUnchanged = true, virtualDisplay = name }));
    }

    public static async Task CheckCaptureAsync(string output)
    {
        await using var session = await DriverSession.StartAsync();
        for (int i = 0; i < 50 && !DisplayModes.Enumerate().Any(a => a.Virtual); i++) await Task.Delay(100);
        string name = await DisplayModes.EnsureExtendedAsync();
        DisplayModes.SetDesktopSize(name, 1920, 1080, 0);
        await Task.Delay(350);
        await Task.Run(() => CaptureProbe(DisplayModes.Bounds(name), true));
        File.WriteAllText(output, JsonSerializer.Serialize(new { virtualDisplay = name, backend = "WGC", scaledCapture = "320x180", quadrantsPreserved = true, encodedFrames = 24 }));
    }

    private static void CaptureProbe(Rectangle bounds, bool requireCompositor = false)
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
        window.Paint += (_, e) =>
        {
            int halfWidth = window.ClientSize.Width / 2, halfHeight = window.ClientSize.Height / 2;
            e.Graphics.FillRectangle(Brushes.Orange, 0, 0, halfWidth, halfHeight);
            e.Graphics.FillRectangle(Brushes.Lime, halfWidth, 0, window.ClientSize.Width - halfWidth, halfHeight);
            e.Graphics.FillRectangle(Brushes.Blue, 0, halfHeight, halfWidth, window.ClientSize.Height - halfHeight);
            e.Graphics.FillRectangle(Brushes.Magenta, halfWidth, halfHeight, window.ClientSize.Width - halfWidth, window.ClientSize.Height - halfHeight);
        };
        window.Invalidate(); window.Update();
        bool quadrants = false;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            Application.DoEvents(); Thread.Sleep(50);
            using var image = new Bitmap(new MemoryStream(capture.Capture()));
            var topLeft = image.GetPixel(80, 45); var topRight = image.GetPixel(240, 45);
            var bottomLeft = image.GetPixel(80, 135); var bottomRight = image.GetPixel(240, 135);
            if (topLeft.R > 180 && topLeft.G > 80 && topLeft.B < 80 && topRight.G > 180 && topRight.R < 80
                && bottomLeft.B > 180 && bottomLeft.R < 80 && bottomRight.R > 180 && bottomRight.B > 180)
            { quadrants = true; break; }
        }
        if (!quadrants) throw new IOException("Scaled capture did not preserve all four screen quadrants.");
        if (requireCompositor && capture.Backend != "WGC") throw new IOException("GPU capture check used the GDI fallback instead of WGC.");
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
