using System.Text.Json;

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
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            virtualDisplay = name, landscape.Width, landscape.Height,
            portraitWidth = portrait.Width, portraitHeight = portrait.Height,
            adapters = DisplayModes.Enumerate()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
