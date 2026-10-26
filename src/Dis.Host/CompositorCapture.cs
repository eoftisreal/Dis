using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Dis.Host;

internal sealed class CompositorCapture : IDisposable
{
    private sealed record Request(nint Data, int Stride, int Width, int Height, TaskCompletionSource<bool> Completion);
    private readonly BlockingCollection<Request> requests = new(1);
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread worker;
    private Exception? failure;
    private bool disposed;
    public CompositorCapture(Rectangle monitor)
    {
        worker = new Thread(() => Run(monitor)) { IsBackground = true, Name = "Dis compositor capture" };
        worker.Start();
        try { ready.Task.WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult(); }
        catch { Dispose(); throw; }
    }
    public bool Copy(nint data, int stride, int width, int height)
    {
        if (failure is not null) throw new IOException("Compositor capture stopped.", failure);
        ObjectDisposedException.ThrowIf(disposed, this);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        requests.Add(new(data, stride, width, height, completion));
        return completion.Task.GetAwaiter().GetResult();
    }
    private void Run(Rectangle monitor)
    {
        nint context = 0; Request? current = null;
        try
        {
            Marshal.ThrowExceptionForHR(dis_capture_create(monitor.Left + monitor.Width / 2, monitor.Top + monitor.Height / 2, out context));
            ready.TrySetResult();
            foreach (var request in requests.GetConsumingEnumerable())
            {
                current = request;
                int result = dis_capture_copy(context, request.Data, request.Stride, request.Width, request.Height);
                Marshal.ThrowExceptionForHR(result);
                current = null; request.Completion.TrySetResult(result == 0);
            }
        }
        catch (Exception e) { failure = e; ready.TrySetException(e); current?.Completion.TrySetException(e); }
        finally
        {
            requests.CompleteAdding();
            while (requests.TryTake(out var pending)) pending.Completion.TrySetException(new IOException("Capture stopped.", failure));
            if (context != 0) dis_capture_destroy(context);
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; requests.CompleteAdding();
        if (worker.Join(3000)) requests.Dispose();
    }
    [DllImport("Dis.Codec.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int dis_capture_create(int x, int y, out nint context);
    [DllImport("Dis.Codec.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int dis_capture_copy(nint context, nint data, int stride, int width, int height);
    [DllImport("Dis.Codec.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void dis_capture_destroy(nint context);
}
