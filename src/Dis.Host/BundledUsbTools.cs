using System.Diagnostics;
using System.Reflection;
using Dis.Core;

namespace Dis.Host;

internal static class BundledUsbTools
{
    private static readonly Lazy<string?> bundled = new(() =>
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dis.UsbTools.zip");
        if (resource is null) return null; // Source-only diagnostic builds can use their SDK installation.
        using var archive = new MemoryStream(); resource.CopyTo(archive);
        return UsbToolBundle.Prepare(archive.ToArray(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dis", "UsbTools"));
    });

    public static string Resolve(string? legacyPath = null) => bundled.Value ?? AdbLocator.Resolve(legacyPath);

    public static void Verify()
    {
        if (bundled.Value is not string executable) throw new IOException("The embedded USB tool bundle is missing.");
        using var process = new Process { StartInfo = new(executable, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.Start();
        if (!process.WaitForExit(10000)) { process.Kill(true); throw new IOException("Bundled USB tool did not respond."); }
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0 || !output.Contains("Android Debug Bridge", StringComparison.Ordinal)) throw new IOException("Bundled USB tool verification failed. " + error);
    }
}
