using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Dis.Host;

internal sealed class MainForm : Form
{
    private sealed record Connection(string Label, IPAddress Address)
    {
        public override string ToString() => Label;
    }
    private sealed record DisplayChoice(string Name, Rectangle Bounds)
    {
        public override string ToString() => $"{Name} · {Bounds.Width} × {Bounds.Height}";
    }

    private readonly ComboBox connection = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox display = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown port = new() { Minimum = 1024, Maximum = 65535, Value = 49830, Dock = DockStyle.Fill };
    private readonly NumericUpDown fps = new() { Minimum = 1, Maximum = 30, Value = 30, Dock = DockStyle.Fill };
    private readonly NumericUpDown quality = new() { Minimum = 40, Maximum = 95, Value = 85, Dock = DockStyle.Fill };
    private readonly CheckBox remoteInput = new() { Text = "Allow touch and pen", Checked = true, AutoSize = true };
    private readonly TextBox adb = new() { Text = "adb.exe", Dock = DockStyle.Fill };
    private readonly TextBox serial = new() { PlaceholderText = "Automatic for one connected device", Dock = DockStyle.Fill };
    private readonly TextBox pairing = new() { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, Height = 76, ScrollBars = ScrollBars.Vertical };
    private readonly Label status = new() { Text = "Ready", Dock = DockStyle.Fill, AutoSize = true };
    private readonly Button start = new() { Text = "Start display", AutoSize = true };
    private readonly Button copy = new() { Text = "Copy connection", AutoSize = true, Enabled = false };
    private readonly Button virtualButton = new() { Text = "Add virtual monitor (admin)", AutoSize = true };
    private readonly List<Control> settings = [];
    private DisplayServer? server;
    private AdbBridge? tunnel;
    private VirtualDisplay? virtualDisplay;
    private bool busy, closing;

    public MainForm()
    {
        Text = "Dis · Android Display";
        MinimumSize = new(700, 680); Size = new(790, 790);
        Font = new("Segoe UI", 10);
        BackColor = Color.FromArgb(244, 246, 250);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(24), ColumnCount = 2, AutoScroll = true };
        root.ColumnStyles.Add(new(SizeType.Absolute, 180)); root.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(root);
        var title = new Label { Text = "Your next screen.", Font = new("Segoe UI", 24, FontStyle.Bold), AutoSize = true, Padding = new(0, 0, 0, 12) };
        root.Controls.Add(title, 0, 0); root.SetColumnSpan(title, 2);
        root.RowCount = 1;
        Add("Connection", connection);
        Add("Display to stream", display);
        Add("Port", port);
        Add("Frames per second", fps);
        Add("JPEG quality", quality);
        Add("ADB executable", adb);
        Add("Device serial", serial);
        Add("Input", remoteInput);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([start, copy]); Add("", buttons);
        Add("Connection link", pairing);
        Add("Status", status);
        var virtualButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var refresh = new Button { Text = "Refresh displays", AutoSize = true };
        var windowsSettings = new Button { Text = "Display settings", AutoSize = true };
        virtualButtons.Controls.AddRange([virtualButton, refresh, windowsSettings]); Add("Extended desktop", virtualButtons);
        var help = new Label { AutoSize = true, MaximumSize = new(520, 0), Dock = DockStyle.Fill,
            Text = "Paste the connection link into the Dis Android app. USB mode needs authorized USB debugging and Android Platform Tools. Network mode uses the chosen adapter, including USB tethering. An extended desktop needs an installed, signed Dis display driver. Preview: software JPEG, up to 1920 × 1080; performance depends on your hardware." };
        Add("", help);
        start.Click += async (_, _) => await ToggleAsync();
        copy.Click += (_, _) => { if (pairing.Text.Length > 0) Clipboard.SetText(pairing.Text); };
        refresh.Click += (_, _) => RefreshDisplays();
        windowsSettings.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
        virtualButton.Click += async (_, _) => await ToggleVirtualAsync();
        settings.AddRange([connection, display, port, fps, quality, remoteInput, adb, serial, refresh, virtualButton]);
        connection.Items.Add(new Connection("USB cable (ADB) · keeps networking available", IPAddress.Loopback));
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        foreach (var address in adapter.GetIPProperties().UnicastAddresses)
            if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
                connection.Items.Add(new Connection($"{adapter.Name} · {address.Address}", address.Address));
        connection.SelectedIndex = 0;
        RefreshDisplays();
        FormClosing += OnClosing;

        void Add(string label, Control control)
        {
            int row = root.RowCount++;
            root.RowStyles.Add(new(SizeType.AutoSize));
            root.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new(0, 9, 0, 9) }, 0, row);
            control.Margin = new(0, 6, 0, 6);
            root.Controls.Add(control, 1, row);
        }
    }

    private void RefreshDisplays()
    {
        string? old = (display.SelectedItem as DisplayChoice)?.Name;
        display.Items.Clear();
        foreach (var screen in Screen.AllScreens) display.Items.Add(new DisplayChoice(screen.DeviceName, screen.Bounds));
        display.SelectedIndex = Math.Max(0, display.Items.Cast<DisplayChoice>().ToList().FindIndex(s => s.Name == old));
    }

    private async Task ToggleAsync()
    {
        if (busy) return;
        busy = true; start.Enabled = false;
        try
        {
            if (server is not null) { await StopAsync(); return; }
            if (connection.SelectedItem is not Connection selected || display.SelectedItem is not DisplayChoice screen)
                throw new IOException("Select a connection and an active display.");
            foreach (var setting in settings) setting.Enabled = false;
            status.Text = "Starting…";
            if (selected.Address.Equals(IPAddress.Loopback))
                tunnel = await AdbBridge.ConnectAsync(adb.Text.Trim(), serial.Text, (int)port.Value);
            server = new(new(selected.Address, (int)port.Value, screen.Name, screen.Bounds, 1920, 1080,
                (int)fps.Value, (int)quality.Value, remoteInput.Checked));
            server.Status += UpdateStatus;
            pairing.Text = server.ConnectionUri(selected.Address.ToString());
            copy.Enabled = true;
            start.Text = "Stop display";
            status.Text = "Waiting for Android · copy the connection link";
        }
        catch (Exception e) when (e is IOException or SocketException or System.ComponentModel.Win32Exception or
            System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
        {
            await StopAsync(); status.Text = e.Message;
        }
        finally
        {
            busy = false; start.Enabled = true;
            if (server is null) foreach (var setting in settings) setting.Enabled = true;
        }
    }

    private void UpdateStatus(string value)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!IsDisposed) status.Text = value; }); }
        catch (InvalidOperationException) when (IsDisposed || !IsHandleCreated) { }
    }

    private async Task StopAsync()
    {
        if (server is not null)
        {
            var previous = server; server = null;
            previous.Status -= UpdateStatus;
            await previous.DisposeAsync();
        }
        if (tunnel is not null)
        {
            var previous = tunnel; tunnel = null;
            try { await previous.DisposeAsync(); }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { status.Text = e.Message; }
        }
        pairing.Clear(); copy.Enabled = false; start.Text = "Start display"; status.Text = "Stopped";
        foreach (var setting in settings) setting.Enabled = true;
    }

    private async Task ToggleVirtualAsync()
    {
        virtualButton.Enabled = false;
        try
        {
            if (virtualDisplay is not null) { virtualDisplay.Dispose(); virtualDisplay = null; virtualButton.Text = "Add virtual monitor (admin)"; }
            else
            {
                virtualDisplay = new();
                await virtualDisplay.WaitAsync();
                virtualButton.Text = "Remove virtual monitor";
                status.Text = "Use Windows Display settings to extend to the Dis monitor, then refresh displays.";
            }
            RefreshDisplays();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException or TimeoutException)
        {
            virtualDisplay?.Dispose(); virtualDisplay = null;
            status.Text = $"Virtual display: {e.Message}. Run Dis as administrator for this action and check the signed driver is installed.";
        }
        finally { virtualButton.Enabled = true; }
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        if (busy) { status.Text = "Wait for the current connection operation to finish."; return; }
        closing = true; Enabled = false;
        try { await StopAsync(); }
        finally { virtualDisplay?.Dispose(); Close(); }
    }
}
