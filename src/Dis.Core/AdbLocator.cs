namespace Dis.Core;

public sealed record AdbSearchLocations(string AppDirectory, string WorkingDirectory, string LocalAppData,
    string? AndroidHome, string? AndroidSdkRoot, string? SearchPath)
{
    public static AdbSearchLocations Current() => new(AppContext.BaseDirectory, Environment.CurrentDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("ANDROID_HOME"), Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
        Environment.GetEnvironmentVariable("PATH"));
}

/// <summary>Resolve an explicit executable path before CreateProcess; no shell or PATH mutation is needed.</summary>
public static class AdbLocator
{
    public static string Resolve(string? configuredPath, AdbSearchLocations? locations = null) =>
        Find(configuredPath, locations) ?? throw new IOException(
            "ADB was not found. Click Browse and select adb.exe from Android Platform Tools, " +
            "or extract the complete platform-tools folder beside Dis.exe. " +
            "You can also enter the full path in ADB executable. Ethernet/Wi-Fi/USB-tether network mode does not need ADB.");

    public static string? Find(string? configuredPath, AdbSearchLocations? locations = null)
    {
        locations ??= AdbSearchLocations.Current();
        string requested = Clean(configuredPath);
        bool automatic = requested.Length == 0 || requested.Equals("adb.exe", StringComparison.OrdinalIgnoreCase)
            || requested.Equals("adb", StringComparison.OrdinalIgnoreCase);
        if (!automatic)
        {
            // Respect explicit choices: never silently replace a missing configured ADB with another installation.
            return Existing(requested, locations.AppDirectory) ?? Existing(requested, locations.WorkingDirectory);
        }

        foreach (string candidate in Candidates(locations))
        {
            string? found = Existing(candidate, locations.WorkingDirectory);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<string> Candidates(AdbSearchLocations locations)
    {
        yield return Path.Combine(locations.AppDirectory, "adb.exe");
        yield return Path.Combine(locations.AppDirectory, "platform-tools", "adb.exe");
        yield return Path.Combine(locations.AppDirectory, "adb", "adb.exe");
        foreach (string? sdk in new[] { locations.AndroidHome, locations.AndroidSdkRoot })
            if (!string.IsNullOrWhiteSpace(sdk)) yield return Path.Combine(Clean(sdk), "platform-tools", "adb.exe");
        if (!string.IsNullOrWhiteSpace(locations.LocalAppData))
            yield return Path.Combine(locations.LocalAppData, "Android", "Sdk", "platform-tools", "adb.exe");
        foreach (string entry in (locations.SearchPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string directory = Clean(entry);
            if (directory.Length != 0) yield return Path.Combine(directory, "adb.exe");
        }
    }

    private static string Clean(string? path) => Environment.ExpandEnvironmentVariables((path ?? "").Trim().Trim('"'));

    private static string? Existing(string candidate, string baseDirectory)
    {
        try
        {
            string full = Path.GetFullPath(candidate, baseDirectory);
            if (Directory.Exists(full)) full = Path.Combine(full, "adb.exe");
            return File.Exists(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
