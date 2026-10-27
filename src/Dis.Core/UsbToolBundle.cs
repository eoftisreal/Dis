using System.IO.Compression;
using System.Security.Cryptography;

namespace Dis.Core;

/// <summary>Restore the application's embedded USB tools without external file selection.</summary>
public static class UsbToolBundle
{
    public static readonly string[] RequiredFiles = ["adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll", "NOTICE.txt"];

    public static string Prepare(byte[] archive, string cacheRoot)
    {
        string directory = Path.Combine(Path.GetFullPath(cacheRoot), Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant());
        for (string? ancestor = directory; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if (Directory.Exists(ancestor) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The USB tool cache must not contain directory links.");
        using var zip = new ZipArchive(new MemoryStream(archive, false));
        var entries = RequiredFiles.Select(name => zip.GetEntry(name) ?? throw new IOException("Bundled USB tool is missing: " + name)).ToArray();
        if (entries.Any(entry => entry.Length is < 1 or > 32 * 1024 * 1024)) throw new IOException("Invalid bundled USB tools.");
        Directory.CreateDirectory(directory);
        foreach (var entry in entries)
        {
            string destination = Path.Combine(directory, entry.Name);
            using var source = entry.Open();
            byte[] expected = SHA256.HashData(source);
            if (File.Exists(destination))
            {
                if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new IOException("USB tool files must not be links.");
                using var existing = File.OpenRead(destination);
                if (CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(existing))) continue;
            }
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var input = entry.Open())
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                File.Move(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return Path.Combine(directory, "adb.exe");
    }
}
