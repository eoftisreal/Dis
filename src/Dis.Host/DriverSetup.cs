using System.ComponentModel;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;

namespace Dis.Host;

internal static class DriverSetup
{
    private const string Hash = "e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a";
    private const string Download = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip";
    private static readonly Guid DisplayClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private const string HardwareId = "Root\\MttVDD";

    public static async Task InstallAsync(Action<string> progress)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new IOException("Display driver setup requires administrator access.");
        progress("Preparing signed Virtual Display Driver 25.7.23…");
        string bundled = Path.Combine(AppContext.BaseDirectory, "driver", "vdd-25.7.23.zip");
        byte[] zip;
        if (File.Exists(bundled)) zip = await File.ReadAllBytesAsync(bundled);
        else
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var response = await http.GetAsync(Download, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > 1_000_000) throw new IOException("Unexpected driver download size.");
            using var body = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            byte[] block = new byte[16384]; int count;
            while ((count = await body.ReadAsync(block)) > 0)
            {
                if (buffer.Length + count > 1_000_000) throw new IOException("Driver download exceeds its limit.");
                buffer.Write(block, 0, count);
            }
            zip = buffer.ToArray();
        }
        if (!Convert.ToHexString(SHA256.HashData(zip)).Equals(Hash, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Driver package checksum mismatch. No driver was installed.");
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Dis", "Drivers", "25.7.23");
        ProtectDriverFolder(root);
        using (var archive = new ZipArchive(new MemoryStream(zip)))
        {
            foreach (string name in new[] { "MttVDD.inf", "MttVDD.dll", "mttvdd.cat", "vdd_settings.xml" })
            {
                var entry = archive.GetEntry("VirtualDisplayDriver/" + name) ?? throw new IOException("Driver package is incomplete.");
                string destination = Path.Combine(root, name);
                if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Driver files must not be links.");
                if (File.Exists(destination)) File.Delete(destination);
                using var source = entry.Open(); using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(target);
            }
        }
        progress("Checking the driver's trusted catalog signature…");
        await Task.Run(() => VerifySignature(Path.Combine(root, "mttvdd.cat")));
        string settingsRoot = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "VirtualDisplayDriver");
        Directory.CreateDirectory(settingsRoot);
        if (!File.Exists(Path.Combine(settingsRoot, "vdd_settings.xml")))
            File.Copy(Path.Combine(root, "vdd_settings.xml"), Path.Combine(settingsRoot, "vdd_settings.xml"));
        progress("Installing the second-monitor device. Accept the Windows publisher prompt if shown…");
        await Task.Run(() => RegisterDevice(Path.Combine(root, "MttVDD.inf")));
        await Task.Run(() => SetDeviceEnabled(false));
        progress("Driver installed. Return to Dis and choose Second display.");
    }

    private static void ProtectDriverFolder(string root)
    {
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string relative = Path.GetRelativePath(basePath, root);
        foreach (string segment in relative.Split(Path.DirectorySeparatorChar))
        {
            basePath = Path.Combine(basePath, segment);
            Directory.CreateDirectory(basePath);
            if ((File.GetAttributes(basePath) & FileAttributes.ReparsePoint) != 0) throw new IOException("Driver folder must not be a link.");
            var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false); security.SetOwner(admin);
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new(admin, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(basePath).SetAccessControl(security);
        }
    }

    private static void RegisterDevice(string inf)
    {
        Guid classId = DisplayClass;
        nint set = SetupDiCreateDeviceInfoList(ref classId, 0);
        if (set == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        var device = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
        bool created = false;
        try
        {
            if (!HasDevice())
            {
                if (!SetupDiCreateDeviceInfo(set, "Display", ref classId, "Dis second monitor", 0, 1, ref device)) throw new Win32Exception(Marshal.GetLastWin32Error());
                byte[] id = System.Text.Encoding.Unicode.GetBytes(HardwareId + "\0\0");
                if (!SetupDiSetDeviceRegistryProperty(set, ref device, 1, id, (uint)id.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!SetupDiCallClassInstaller(0x19, set, ref device)) throw new Win32Exception(Marshal.GetLastWin32Error());
                created = true;
            }
            if (!UpdateDriverForPlugAndPlayDevices(0, HardwareId, inf, 0, out bool reboot)) throw new Win32Exception(Marshal.GetLastWin32Error());
            created = false;
            if (reboot) throw new IOException("The driver installed. Restart Windows before connecting the second display.");
        }
        finally
        {
            if (created) SetupDiCallClassInstaller(5, set, ref device);
            SetupDiDestroyDeviceInfoList(set);
        }
    }
    internal static bool HasDevice()
    {
        Guid classId = DisplayClass;
        nint set = SetupDiGetClassDevs(ref classId, null, 0, 2);
        if (set == -1) return false;
        try
        {
            for (uint i = 0; ; i++)
            {
                var device = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref device)) return false;
                byte[] value = new byte[4096];
                if (SetupDiGetDeviceRegistryProperty(set, ref device, 1, out _, value, (uint)value.Length, out _)
                    && System.Text.Encoding.Unicode.GetString(value).Split('\0').Contains(HardwareId, StringComparer.OrdinalIgnoreCase)) return true;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    public static void SetDeviceEnabled(bool enabled)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new IOException("Hiding or restoring the virtual adapter requires administrator access.");
        var adapters = DisplayModes.Enumerate();
        if (!enabled && adapters.Any(a => a.Virtual && a.Active) && !adapters.Any(a => !a.Virtual && a.Active))
            throw new IOException("Activate a physical monitor before hiding the only active display.");
        Guid classId = DisplayClass;
        nint set = SetupDiGetClassDevs(ref classId, null, 0, 2);
        if (set == -1) throw new Win32Exception(Marshal.GetLastWin32Error());
        bool found = false;
        try
        {
            for (uint i = 0; ; i++)
            {
                var device = new DeviceInfo { Size = Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref device)) break;
                byte[] value = new byte[4096];
                if (!SetupDiGetDeviceRegistryProperty(set, ref device, 1, out _, value, (uint)value.Length, out _)
                    || !System.Text.Encoding.Unicode.GetString(value).Split('\0').Contains(HardwareId, StringComparer.OrdinalIgnoreCase)) continue;
                var change = new PropertyChange { HeaderSize = 8, Function = 0x12, State = enabled ? 1u : 2u, Scope = 1 };
                if (!SetupDiSetClassInstallParams(set, ref device, ref change, Marshal.SizeOf<PropertyChange>())
                    || !SetupDiCallClassInstaller(0x12, set, ref device)) throw new Win32Exception(Marshal.GetLastWin32Error());
                found = true;
            }
            if (!found && enabled) throw new IOException("Install the second-display driver first using Set up second display.");
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
    private static void VerifySignature(string file)
    {
        var info = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = file };
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        Marshal.StructureToPtr(info, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = pointer, Action = 1, Flags = 0x1010 };
        Guid action = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try
        {
            int status = WinVerifyTrust(-1, ref action, ref data);
            if (status != 0) throw new IOException($"Windows could not verify the driver's signature (0x{status:X8}). No device was installed.");
        }
        finally
        {
            data.Action = 2; WinVerifyTrust(-1, ref action, ref data);
            Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfo { public int Size; public Guid Class; public uint Instance; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyChange { public uint HeaderSize, Function, State, Scope, Profile; }
    [DllImport("setupapi.dll", EntryPoint = "SetupDiSetClassInstallParamsW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiSetClassInstallParams(nint set, ref DeviceInfo device, ref PropertyChange change, int size);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public nint Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData { public uint Size; public nint Policy, Sip; public uint Ui, Revocation, Choice; public nint File; public uint Action; public nint State, Url; public uint Flags, Context; public nint Signature; }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern nint SetupDiCreateDeviceInfoList(ref Guid classId, nint window);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiCreateDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiCreateDeviceInfo(nint set, string name, ref Guid classId, string description, nint window, uint flags, ref DeviceInfo data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiSetDeviceRegistryPropertyW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiSetDeviceRegistryProperty(nint set, ref DeviceInfo data, uint property, byte[] value, uint bytes);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiCallClassInstaller(uint function, nint set, ref DeviceInfo data);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid classId, string? enumerator, nint window, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref DeviceInfo data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceRegistryProperty(nint set, ref DeviceInfo data, uint property, out uint type, byte[] buffer, uint bytes, out uint needed);
    [DllImport("newdev.dll", EntryPoint = "UpdateDriverForPlugAndPlayDevicesW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateDriverForPlugAndPlayDevices(nint window, string hardware, string inf, uint flags, [MarshalAs(UnmanagedType.Bool)] out bool reboot);
}

internal sealed class DriverSetupForm : Form
{
    private bool completed;
    private readonly Label message = new() { Dock = DockStyle.Fill, Padding = new(24), AutoSize = true };
    public DriverSetupForm()
    {
        Text = "Dis · Second display setup"; Size = new(620, 220); Controls.Add(message);
        Shown += async (_, _) =>
        {
            try { await DriverSetup.InstallAsync(text => message.Text = text); Environment.ExitCode = 0; }
            catch (Exception e) { Environment.ExitCode = 1; message.Text = e.Message; }
            completed = true;
            var close = new Button { Text = "Close", Dock = DockStyle.Bottom, Height = 40 };
            close.Click += (_, _) => Close(); Controls.Add(close);
        };
        FormClosing += (_, e) => { if (!completed) e.Cancel = true; };
    }
}
