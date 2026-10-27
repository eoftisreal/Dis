using System.Text.Json;

namespace Dis.Host;

internal sealed record HostPreferences(int Mode = 0, int Quality = 0, int Desktop = 0, int Orientation = 0,
    int Fps = 60, bool Input = true, bool HideIdle = true, string Adb = "", string Serial = "", bool AutoConnect = true,
    string ConnectionAddress = "127.0.0.1", string MirrorDisplay = "", int Port = 49830, int Version = 3)
{
    // Retain legacy fields for old preference files; these are no longer user choices.
    public HostPreferences EnforceConnectionBehavior() => this with { Mode = 0, HideIdle = true, MirrorDisplay = "", Adb = "", Version = 3 };
    private static string FileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dis", "preferences.json");
    public static HostPreferences Load()
    {
        try
        {
            string json = File.ReadAllText(FileName);
            var preferences = JsonSerializer.Deserialize<HostPreferences>(json) ?? new();
            return preferences.EnforceConnectionBehavior();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);
        string temporary = FileName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(EnforceConnectionBehavior()));
        File.Move(temporary, FileName, true);
    }
}
