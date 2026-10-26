using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dis.Host;

/// <summary>The software device lives only while its handle is open. Does not install or trust a driver.</summary>
internal sealed class VirtualDisplay : IDisposable
{
    private nint handle;
    private readonly DeviceCreated callback;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public VirtualDisplay()
    {
        callback = OnCreated;
        var info = new CreateInfo
        {
            Size = (uint)Marshal.SizeOf<CreateInfo>(), InstanceId = "DisDisplay-0",
            HardwareIds = "DisDisplay\0\0", CompatibleIds = "DisDisplay\0\0",
            Description = "Dis Virtual Display", Capabilities = 1 | 2 | 8 // removable, silent install, driver required
        };
        int hr = SwDeviceCreate("Dis", "HTREE\\ROOT\\0", ref info, 0, 0, callback, 0, out handle);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private void OnCreated(nint device, int result, nint context, nint instanceId)
    {
        if (result < 0) ready.TrySetException(new Win32Exception(result));
        else ready.TrySetResult();
    }

    public async Task WaitAsync() => await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));

    public void Dispose()
    {
        if (handle != 0) { SwDeviceClose(handle); handle = 0; }
        GC.KeepAlive(callback);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct CreateInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string InstanceId, HardwareIds, CompatibleIds;
        public nint ContainerId;
        public uint Capabilities;
        [MarshalAs(UnmanagedType.LPWStr)] public string Description;
        public nint Location, SecurityDescriptor;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void DeviceCreated(nint device, int result, nint context, nint instanceId);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int SwDeviceCreate(string enumerator, string parent, ref CreateInfo info, uint count, nint properties, DeviceCreated callback, nint context, out nint device);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)] private static extern void SwDeviceClose(nint device);
}
