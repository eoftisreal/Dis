using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Dis.Core;

namespace Dis.Host;

internal sealed record EncodedFrame(byte[] Data, byte[] Configuration, long TimestampUs, bool Keyframe);

/// <summary>MF transforms stay on one MTA worker; a bitmap remains locked until the native read completes.</summary>
internal sealed class NativeEncoder : IDisposable
{
    private sealed record Request(nint Data, int Stride, long Timestamp, TaskCompletionSource<EncodedFrame?> Completion);
    private readonly BlockingCollection<Request> requests = new(1);
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread worker;
    private Exception? failure;
    private int disposed;
    public bool Hardware { get; private set; }

    private NativeEncoder(int width, int height, int fps, int bitrate, bool preferHardware)
    {
        worker = new Thread(() => Run(width, height, fps, bitrate, preferHardware)) { IsBackground = true, Name = "Dis H.264 encoder" };
        worker.Start();
    }
    public static async Task<NativeEncoder> CreateAsync(int width, int height, int fps, int bitrate, bool preferHardware)
    {
        var encoder = new NativeEncoder(width, height, fps, bitrate, preferHardware);
        try { await encoder.ready.Task.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false); return encoder; }
        catch { encoder.Dispose(); throw; }
    }
    public Task<EncodedFrame?> EncodeAsync(nint data, int stride, long pts)
    {
        if (failure is not null) throw new IOException("H.264 encoder stopped.", failure);
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        var completion = new TaskCompletionSource<EncodedFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { requests.Add(new(data, stride, pts, completion)); }
        catch (InvalidOperationException) { throw new IOException("H.264 encoder is unavailable.", failure); }
        return completion.Task; // Never cancel while native code may still reference the locked bitmap.
    }
    private void Run(int width, int height, int fps, int bitrate, bool preferHardware)
    {
        nint context = 0;
        Request? current = null;
        try
        {
            Marshal.ThrowExceptionForHR(dis_encoder_create(width, height, fps, bitrate, preferHardware ? 1 : 0, out context));
            Hardware = dis_encoder_hardware(context) != 0;
            ready.TrySetResult(true);
            foreach (Request request in requests.GetConsumingEnumerable())
            {
                current = request;
                Marshal.ThrowExceptionForHR(dis_encoder_encode(context, request.Data, request.Stride, request.Timestamp,
                    out nint output, out int bytes, out long timestamp, out int key));
                EncodedFrame? frame = null;
                if (bytes > 0)
                {
                    if (bytes > Wire.MaxFrameBytes - 12) throw new InvalidDataException("H.264 frame exceeds protocol limit.");
                    byte[] data = new byte[bytes]; Marshal.Copy(output, data, 0, bytes);
                    Marshal.ThrowExceptionForHR(dis_encoder_config(context, out nint configuration, out int length));
                    if (length is < 0 or > 65536) throw new InvalidDataException("H.264 configuration exceeds limit.");
                    byte[] config = new byte[length]; if (length > 0) Marshal.Copy(configuration, config, 0, length);
                    frame = new(H264.AnnexB(data), config, timestamp, key != 0);
                }
                current = null; request.Completion.TrySetResult(frame);
            }
        }
        catch (Exception e)
        {
            failure = e; ready.TrySetException(e); current?.Completion.TrySetException(e);
        }
        finally
        {
            requests.CompleteAdding();
            while (requests.TryTake(out var remaining)) remaining.Completion.TrySetException(new IOException("Encoder stopped.", failure));
            if (context != 0) dis_encoder_destroy(context);
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        requests.CompleteAdding();
        if (worker.Join(TimeSpan.FromSeconds(5))) requests.Dispose();
    }
    [DllImport("Dis.Codec.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int dis_encoder_create(int width, int height, int fps, int bitrate, int preferHardware, out nint context);
    [DllImport("Dis.Codec.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int dis_encoder_encode(nint context, nint data, int stride, long pts, out nint output, out int bytes, out long timestamp, out int key);
    [DllImport("Dis.Codec.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int dis_encoder_config(nint context, out nint output, out int bytes);
    [DllImport("Dis.Codec.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int dis_encoder_hardware(nint context);
    [DllImport("Dis.Codec.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void dis_encoder_destroy(nint context);
}
