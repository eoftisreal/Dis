using System.Text;
using System.Text.Json;

namespace Dis.Host;

// JSON lines travel only through inherited process pipes. No HTTP server, open
// listening control port, shared credential file or third-party service is used.
internal sealed class QtEngineContext : ApplicationContext
{
    private readonly Control dispatcher = new();
    private readonly StreamWriter output = new(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 3000 };
    private readonly HostController controller;

    public QtEngineContext(bool allowAutoConnect = true)
    {
        _ = dispatcher.Handle;
        controller = new(action => { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke(action); });
        controller.StateChanged += state => { try { output.WriteLine(JsonSerializer.Serialize(state, HostController.Json)); } catch (IOException) { /* stdin EOF performs shutdown if the shell exits. */ } };
        timer.Tick += async (_, _) => { if (allowAutoConnect) await controller.ProbeAsync(); };
        dispatcher.BeginInvoke(async () =>
        {
            controller.Publish(); await controller.ProbeAsync(allowAutoConnect); timer.Start();
            if (allowAutoConnect && controller.Preferences.AutoConnect && controller.Preferences.ConnectionAddress != "127.0.0.1")
                await controller.ExecuteAsync(JsonSerializer.SerializeToElement(new { command = "connect" }));
        });
        _ = Task.Run(ReadAsync);
    }

    private async Task OnUiAsync(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.BeginInvoke(async () => { try { await action(); done.SetResult(); } catch (Exception e) { done.SetException(e); } });
        await done.Task;
    }

    private async Task ReadAsync()
    {
        try
        {
            using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            while (await input.ReadLineAsync() is string line)
            {
                if (line.Length > 32768) continue;
                using var document = JsonDocument.Parse(line);
                var command = document.RootElement.Clone();
                if (command.GetProperty("command").GetString() == "quit") break;
                await OnUiAsync(() => controller.ExecuteAsync(command));
            }
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException) { }
        finally
        {
            await OnUiAsync(async () =>
            {
                timer.Stop();
                try { await controller.ShutdownAsync(); }
                finally { ExitThread(); }
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); dispatcher.Dispose(); output.Dispose(); }
        base.Dispose(disposing);
    }
}
